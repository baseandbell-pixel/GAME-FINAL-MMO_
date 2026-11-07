using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

public static class FirebaseGoogleSignIn
{
    private static bool _initialized;

    public static async Task InitializeIfNeeded()
    {
        if (_initialized) return;

        var status = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (status != DependencyStatus.Available)
        {
            throw new Exception("Firebase dependencies are not resolved: " + status);
        }

        _initialized = true;
    }

    public static async Task<FirebaseUser> SignInWithIdTokenAsync(string idToken)
    {
        await InitializeIfNeeded();

        var auth = FirebaseAuth.DefaultInstance;
        // Only the ID token is required for Firebase sign-in
        var credential = GoogleAuthProvider.GetCredential(idToken, null);
        var user = await auth.SignInWithCredentialAsync(credential);

        return user;
    }
}