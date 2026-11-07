using System;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// The shape of one player document in Firestore: players/{firebase uid}.
/// Only used to carry the values in and out - the game reads GoogleAPI's own fields.
/// </summary>
[FirestoreData]
public class PlayerData
{
    [FirestoreProperty] public string name { get; set; }
    [FirestoreProperty] public int avatarId { get; set; }
}

public class GoogleAPI : MonoBehaviour
{
    /// <summary>Nick is a NetworkString<_16>, so anything longer would be truncated on the wire.</summary>
    public const int MaxNameLength = 16;

    public static GoogleAPI instance;
    public string uuid = "";
    public int stage = 0;
    public string username = "";
    
    //new
    public int avatarId = 0;


    public bool profileLoaded;

    static FirebaseFirestore _db;

    /// <summary>
    /// Firestore, with its local cache turned off.
    ///
    /// By default Firestore keeps an offline LevelDB cache in
    /// %LOCALAPPDATA%/firestore/__FIRAPP_DEFAULT/&lt;project&gt; and takes an exclusive lock on it.
    /// A second process on the same machine - the build while the Editor is open, or two test
    /// clients side by side - cannot take that lock, and Firestore answers by aborting the
    /// process with an internal assertion, which shows up as the Editor simply disappearing.
    /// Without the local cache every instance talks to the server only, so any number of them
    /// can run together.
    /// </summary>
    static FirebaseFirestore Db
    {
        get
        {
            if (_db == null)
            {
                _db = FirebaseFirestore.DefaultInstance;
                _db.Settings.PersistenceEnabled = false;
            }
            return _db;
        }
    }

    DocumentReference Doc => Db.Collection("players").Document(uuid);

    //end
    void Awake()
    {
        if(instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

    }

    //new
    public async Task LoadProfileAsync(string fallbackName)
    {
        bool loaded = false;

        if (!string.IsNullOrEmpty(uuid))
        {
            try
            {
                var snapshot = await Doc.GetSnapshotAsync();
                if (snapshot.Exists)
                {
                    PlayerData saved = snapshot.ConvertTo<PlayerData>();
                    if (saved != null && !string.IsNullOrEmpty(saved.name))
                    {
                        username = ClampName(saved.name);
                        avatarId = Mathf.Clamp(saved.avatarId, 0, LobbyNetwork.AvatarCount - 1);
                        loaded = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[GoogleAPI] Profile load failed: " + ex);
            }
        }

        if (!loaded)
        {
            username = ClampName(string.IsNullOrEmpty(fallbackName) ? "Player" : fallbackName);
            avatarId = UnityEngine.Random.Range(0, LobbyNetwork.AvatarCount);
            await SaveProfileAsync();
        }

        profileLoaded = true;
    }

    public async Task<bool> SaveProfileAsync()
    {
        if (string.IsNullOrEmpty(uuid)) return false;

        try
        {
            PlayerData toSave = new PlayerData
            {
                name = username,
                avatarId = avatarId,
            };
            await Doc.SetAsync(toSave, SetOptions.MergeAll);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[GoogleAPI] Profile save failed: " + ex);
            return false;
        }
    }

    public static string ClampName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Player";
        name = name.Trim();
        if (name.Length == 0) return "Player";
        return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
    }
    //end
}
