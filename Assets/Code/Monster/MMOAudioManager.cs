using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Self-contained procedural 3D/2D audio manager for MMO combat, footsteps, skills, and UI feedback.
/// Synthesizes AudioClips at runtime via AudioClip.Create so no external WAV/MP3 assets are required.
/// </summary>
[DisallowMultipleComponent]
public sealed class MMOAudioManager : MonoBehaviour
{
    private static MMOAudioManager _instance;
    public static MMOAudioManager Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying)
            {
                GameObject go = new GameObject("MMOAudio_Manager");
                _instance = go.AddComponent<MMOAudioManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    public static float MasterSfxVolume { get; set; } = 0.65f;

    private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
    private readonly List<AudioSource> _pool = new List<AudioSource>();
    private int _poolCursor;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        BuildProceduralClips();
        WarmAudioSourcePool(12);
    }

    private void WarmAudioSourcePool(int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject child = new GameObject($"SFX_Voice_{i}");
            child.transform.SetParent(transform, false);
            AudioSource src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0.75f;
            src.minDistance = 2.5f;
            src.maxDistance = 28f;
            src.rolloffMode = AudioRolloffMode.Linear;
            _pool.Add(src);
        }
    }

    private void BuildProceduralClips()
    {
        _clips["swing1"] = CreateNoiseWhooshClip("SFX_Swing1", 0.18f, 650f, 220f, 0.45f);
        _clips["swing2"] = CreateNoiseWhooshClip("SFX_Swing2", 0.22f, 820f, 180f, 0.55f);
        _clips["hit_normal"] = CreateImpactClip("SFX_HitNormal", 0.14f, 170f, 55f, 0.55f, 0.35f);
        _clips["hit_crit"] = CreateCritImpactClip("SFX_HitCrit", 0.28f);
        _clips["stun"] = CreateStunThunderClip("SFX_StunStrike", 0.36f);
        _clips["heal"] = CreateChimeClip("SFX_Heal", 0.42f, new[] { 523.25f, 659.25f, 783.99f });
        _clips["levelup"] = CreateChimeClip("SFX_LevelUp", 0.62f, new[] { 523.25f, 659.25f, 783.99f, 1046.50f });
        _clips["aggro"] = CreateImpactClip("SFX_Aggro", 0.26f, 240f, 95f, 0.5f, 0.2f);
        _clips["death"] = CreateImpactClip("SFX_Death", 0.38f, 140f, 38f, 0.6f, 0.25f);
        _clips["footstep"] = CreateImpactClip("SFX_Footstep", 0.08f, 110f, 45f, 0.22f, 0.25f);
        _clips["boss_cast"] = CreateChimeClip("SFX_BossCast", 0.45f, new[] { 220f, 196f, 164.8f });
    }

    // =========================================================
    // PUBLIC STATIC PLAYBACK API
    // =========================================================

    public static void PlaySwordSwing(Vector3 worldPos, bool isComboFinisher)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt(isComboFinisher ? "swing2" : "swing1", worldPos, 0.55f, Random.Range(0.95f, 1.07f));
    }

    public static void PlayHitImpact(Vector3 worldPos, bool isCritical)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt(
            isCritical ? "hit_crit" : "hit_normal",
            worldPos,
            isCritical ? 0.85f : 0.60f,
            Random.Range(0.94f, 1.08f));
    }

    public static void PlayStunStrike(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt("stun", worldPos, 0.85f, Random.Range(0.96f, 1.04f));
    }

    public static void PlayHeal(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt("heal", worldPos, 0.65f, 1.0f);
    }

    public static void PlayLevelUp(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt("levelup", worldPos, 0.80f, 1.0f);
    }

    public static void PlayAggro(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt("aggro", worldPos, 0.55f, Random.Range(0.92f, 1.04f));
    }

    public static void PlayDeath(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt("death", worldPos, 0.70f, Random.Range(0.90f, 1.02f));
    }

    public static void PlayFootstep(Vector3 worldPos, float volume = 0.25f)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt("footstep", worldPos, volume, Random.Range(0.88f, 1.14f));
    }

    public static void PlayBossCast(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.PlayClipAt("boss_cast", worldPos, 0.70f, 0.95f);
    }

    private void PlayClipAt(string key, Vector3 worldPos, float volume, float pitch)
    {
        if (MasterSfxVolume <= 0.001f || !_clips.TryGetValue(key, out AudioClip clip) || clip == null)
        {
            return;
        }

        if (_pool.Count == 0)
        {
            return;
        }

        AudioSource src = _pool[_poolCursor];
        _poolCursor = (_poolCursor + 1) % _pool.Count;

        src.transform.position = worldPos;
        src.pitch = pitch;
        src.volume = Mathf.Clamp01(volume * MasterSfxVolume);
        src.clip = clip;
        src.Play();
    }

    // =========================================================
    // PROCEDURAL SYNTHESIS HELPERS
    // =========================================================

    private static AudioClip CreateNoiseWhooshClip(string name, float duration, float startFreq, float endFreq, float gain)
    {
        const int sampleRate = 22050;
        int samples = Mathf.Max(256, Mathf.RoundToInt(sampleRate * duration));
        float[] data = new float[samples];

        float phase = 0f;
        float lp = 0f;
        uint seed = 2463534242u;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float env = Mathf.Sin(t * Mathf.PI);
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += freq / sampleRate;

            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            float white = ((seed & 0xFFFF) / 32768f) - 1f;

            lp = Mathf.Lerp(lp, white, Mathf.Clamp01(freq / (sampleRate * 0.5f)));
            float tone = Mathf.Sin(phase * Mathf.PI * 2f) * 0.25f;
            data[i] = Mathf.Clamp((lp * 0.8f + tone) * env * gain, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateImpactClip(string name, float duration, float startFreq, float endFreq, float gain, float noiseMix)
    {
        const int sampleRate = 22050;
        int samples = Mathf.Max(256, Mathf.RoundToInt(sampleRate * duration));
        float[] data = new float[samples];

        float phase = 0f;
        uint seed = 135792468u;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float env = Mathf.Pow(1f - t, 2.4f);
            float freq = Mathf.Lerp(startFreq, endFreq, Mathf.Sqrt(t));
            phase += freq / sampleRate;

            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            float white = ((seed & 0xFFFF) / 32768f) - 1f;

            float body = Mathf.Sin(phase * Mathf.PI * 2f);
            data[i] = Mathf.Clamp((body * (1f - noiseMix) + white * noiseMix) * env * gain, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateCritImpactClip(string name, float duration)
    {
        const int sampleRate = 22050;
        int samples = Mathf.Max(256, Mathf.RoundToInt(sampleRate * duration));
        float[] data = new float[samples];

        float lowPhase = 0f;
        float ringPhase1 = 0f;
        float ringPhase2 = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float lowEnv = Mathf.Pow(1f - t, 3.0f);
            float ringEnv = Mathf.Pow(1f - t, 1.8f);

            lowPhase += Mathf.Lerp(210f, 55f, t) / sampleRate;
            ringPhase1 += 980f / sampleRate;
            ringPhase2 += 1470f / sampleRate;

            float punch = Mathf.Sin(lowPhase * Mathf.PI * 2f) * lowEnv * 0.65f;
            float metallic = (Mathf.Sin(ringPhase1 * Mathf.PI * 2f) * 0.55f +
                              Mathf.Sin(ringPhase2 * Mathf.PI * 2f) * 0.45f) * ringEnv * 0.35f;

            data[i] = Mathf.Clamp(punch + metallic, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateStunThunderClip(string name, float duration)
    {
        const int sampleRate = 22050;
        int samples = Mathf.Max(256, Mathf.RoundToInt(sampleRate * duration));
        float[] data = new float[samples];

        float subPhase = 0f;
        float zapPhase = 0f;
        uint seed = 987654321u;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float env = Mathf.Pow(1f - t, 2.0f);

            subPhase += Mathf.Lerp(150f, 38f, t) / sampleRate;
            zapPhase += Mathf.Lerp(1200f, 320f, t) / sampleRate;

            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            float crackle = (((seed & 0xFFFF) / 32768f) - 1f) * 0.3f;

            float sub = Mathf.Sin(subPhase * Mathf.PI * 2f) * 0.55f;
            float zap = Mathf.Sin(zapPhase * Mathf.PI * 2f) * 0.30f;

            data[i] = Mathf.Clamp((sub + zap + crackle) * env * 0.85f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateChimeClip(string name, float duration, float[] notes)
    {
        const int sampleRate = 22050;
        int samples = Mathf.Max(256, Mathf.RoundToInt(sampleRate * duration));
        float[] data = new float[samples];
        float[] phases = new float[notes.Length];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float sum = 0f;

            for (int n = 0; n < notes.Length; n++)
            {
                float noteStart = (float)n / notes.Length * 0.45f;
                if (t < noteStart) continue;

                float localT = Mathf.Clamp01((t - noteStart) / (1f - noteStart));
                float env = Mathf.Sin(Mathf.Min(localT * 6f, 1f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - localT, 1.6f);
                phases[n] += notes[n] / sampleRate;
                sum += Mathf.Sin(phases[n] * Mathf.PI * 2f) * env;
            }

            data[i] = Mathf.Clamp((sum / Mathf.Max(1, notes.Length)) * 0.8f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
