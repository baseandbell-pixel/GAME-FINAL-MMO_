using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class GoogleTokenResponse
{
    public string access_token;
    public int expires_in;
    public string refresh_token;
    public string scope;
    public string token_type;
    public string id_token;
}

/// <summary>
/// Google OAuth "loopback" sign-in for desktop builds: we listen on a local port,
/// send the user to the browser, and read the authorization code out of the redirect.
///
/// The listener is a plain TcpListener rather than HttpListener, and the query string is
/// parsed by hand rather than with System.Web.HttpUtility. Both of those are available in
/// the Editor (full Mono BCL) but not guaranteed in a player build, which is why sign-in
/// could work in the Editor and kill the build at the redirect step.
/// </summary>
public class GoogleOAuthLoopback
{
    private readonly string clientId;
    private readonly string clientSecret;
    private readonly string[] scopes;

    /// <summary>How long we wait for the browser to come back before giving up.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    public GoogleOAuthLoopback(string clientId, string clientSecret, params string[] scopes)
    {
        this.clientId = clientId;
        this.clientSecret = clientSecret;
        this.scopes = (scopes != null && scopes.Length > 0) ? scopes : new[] { "openid", "email", "profile" };
    }

    public async Task<GoogleTokenResponse> SignInAsync(CancellationToken ct = default)
    {
        string codeVerifier = GenerateCodeVerifier();
        string codeChallenge = GenerateCodeChallenge(codeVerifier);
        string state = Base64Url(RandomBytes(16));

        int port = GetRandomUnusedPort();
        string redirectUri = $"http://127.0.0.1:{port}/";
        string scopeParam = Uri.EscapeDataString(string.Join(" ", scopes));

        string authUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            "?response_type=code" +
            "&client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
            "&scope=" + scopeParam +
            "&code_challenge=" + codeChallenge +
            "&code_challenge_method=S256" +
            "&access_type=offline" +   // optional: gets refresh_token on first consent
            "&prompt=select_account" + // optional: force account chooser
            "&state=" + state;

        string query = await WaitForRedirectAsync(port, authUrl, ct);

        var parameters = ParseQueryString(query);
        parameters.TryGetValue("code", out string code);
        parameters.TryGetValue("state", out string returnedState);
        parameters.TryGetValue("error", out string error);

        if (!string.IsNullOrEmpty(error))
            throw new Exception("OAuth error: " + error);
        if (returnedState != state)
            throw new Exception("State mismatch. Potential CSRF.");
        if (string.IsNullOrEmpty(code))
            throw new Exception("No authorization code returned.");

        return await ExchangeCodeAsync(code, codeVerifier, redirectUri);
    }

    /// <summary>
    /// Opens the browser and waits for Google to redirect back to our local port.
    /// Returns the query string of that redirect (without the leading '?').
    /// </summary>
    async Task<string> WaitForRedirectAsync(int port, string authUrl, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        // Stop the listener if the caller cancels or we run out of patience, which also
        // makes the pending AcceptTcpClientAsync fail instead of hanging forever.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        using var registration = timeoutCts.Token.Register(() =>
        {
            try { listener.Stop(); } catch { }
        });

        try
        {
            Application.OpenURL(authUrl);

            // The browser may also ask for /favicon.ico, so keep accepting until the
            // request that actually carries the OAuth answer shows up.
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();

                string requestLine = await ReadRequestLineAsync(stream);
                string query = ExtractQuery(requestLine);

                bool isAnswer = query.Contains("code=") || query.Contains("error=");
                await WriteResponseAsync(stream, isAnswer);

                if (isAnswer) return query;

                if (timeoutCts.IsCancellationRequested) break;
            }
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("Timed out waiting for the Google sign-in redirect.", ex);
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }

        throw new Exception("The sign-in redirect did not contain an authorization code.");
    }

    /// <summary>Reads just the first line of the HTTP request, e.g. "GET /?code=... HTTP/1.1".</summary>
    static async Task<string> ReadRequestLineAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var line = new StringBuilder();

        while (line.Length < 8192)
        {
            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) break;

            line.Append(Encoding.ASCII.GetString(buffer, 0, read));

            int newline = line.ToString().IndexOf('\n');
            if (newline >= 0) return line.ToString(0, newline).TrimEnd('\r');
        }

        return line.ToString();
    }

    static string ExtractQuery(string requestLine)
    {
        // "GET /?code=abc&state=xyz HTTP/1.1"
        int question = requestLine.IndexOf('?');
        if (question < 0) return string.Empty;

        int space = requestLine.IndexOf(' ', question);
        return space < 0
            ? requestLine.Substring(question + 1)
            : requestLine.Substring(question + 1, space - question - 1);
    }

    static async Task WriteResponseAsync(NetworkStream stream, bool isAnswer)
    {
        string html = isAnswer
            ? "<html><body><script>window.close && window.close();</script>" +
              "You may close this window and return to the game.</body></html>"
            : "<html><body></body></html>";

        byte[] body = Encoding.UTF8.GetBytes(html);
        byte[] header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(header, 0, header.Length);
        await stream.WriteAsync(body, 0, body.Length);
        await stream.FlushAsync();
    }

    async Task<GoogleTokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri)
    {
        using var http = new HttpClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "code", code },
            { "code_verifier", codeVerifier },
            { "redirect_uri", redirectUri },
            { "client_secret", clientSecret },
            { "grant_type", "authorization_code" }
        });

        HttpResponseMessage resp = await http.PostAsync("https://oauth2.googleapis.com/token", form);
        string json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Token endpoint error ({(int)resp.StatusCode}): {json}");

        // UnityEngine.JsonUtility expects field names to match exactly
        var tokens = JsonUtility.FromJson<GoogleTokenResponse>(json);
        if (tokens == null || string.IsNullOrEmpty(tokens.id_token))
            throw new Exception("Token response missing id_token. Check scopes and client type.");

        return tokens;
    }

    // --- Helpers ---

    /// <summary>Replacement for System.Web.HttpUtility.ParseQueryString, which is not in the player profile.</summary>
    static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query)) return result;

        if (query[0] == '?') query = query.Substring(1);

        foreach (string pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;

            int eq = pair.IndexOf('=');
            string key = eq < 0 ? pair : pair.Substring(0, eq);
            string value = eq < 0 ? string.Empty : pair.Substring(eq + 1);

            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace("+", "%20"));
        }

        return result;
    }

    private static string GenerateCodeVerifier()
    {
        // 43-128 chars URL-safe
        return Base64Url(RandomBytes(32));
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha = SHA256.Create();
        byte[] bytes = Encoding.ASCII.GetBytes(codeVerifier);
        byte[] hash = sha.ComputeHash(bytes);
        return Base64Url(hash);
    }

    private static byte[] RandomBytes(int len)
    {
        byte[] data = new byte[len];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);
        return data;
    }

    private static string Base64Url(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int GetRandomUnusedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
