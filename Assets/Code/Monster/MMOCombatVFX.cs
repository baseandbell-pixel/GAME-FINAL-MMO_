using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Self-contained MMO Combat Visual Effects system (Hit sparks, Critical Hit shockwaves,
/// Floating Damage Numbers, Aggro Alerts, and Death/Respawn bursts).
/// Uses procedural meshes, ParticleSystems, and URP-compatible runtime materials so
/// it never suffers from missing prefabs or pink shaders.
/// </summary>
public sealed class MMOCombatVFX : MonoBehaviour
{
    private static MMOCombatVFX _instance;
    public static MMOCombatVFX Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("MMOCombatVFX_Manager");
                _instance = go.AddComponent<MMOCombatVFX>();
            }
            return _instance;
        }
    }

    private Material _particleMaterial;
    private Material _ringMaterial;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        EnsureMaterials();
    }

    private void EnsureMaterials()
    {
        if (_particleMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _particleMaterial = new Material(shader)
            {
                name = "Runtime_MMO_VFX_ParticleMat"
            };
        }

        if (_ringMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            _ringMaterial = new Material(shader)
            {
                name = "Runtime_MMO_VFX_RingMat"
            };
        }
    }

    // =========================================================
    // 1. FLOATING DAMAGE & CRITICAL HIT POPUPS
    // =========================================================

    public static void SpawnDamagePopup(Vector3 worldPosition, int damage, bool isCritical, bool isPlayerDamage = false)
    {
        if (!Application.isPlaying) return;
        Instance.CreateDamagePopupInternal(worldPosition, damage, isCritical, isPlayerDamage);
    }

    public static void SpawnStatusPopup(Vector3 worldPosition, string text, Color color, float scale = 1f)
    {
        if (!Application.isPlaying) return;
        Instance.CreateStatusPopupInternal(worldPosition, text, color, scale);
    }

    private void CreateDamagePopupInternal(Vector3 worldPos, int damage, bool isCrit, bool isPlayerDamage)
    {
        Vector3 jitter = new Vector3(
            Random.Range(-0.35f, 0.35f),
            Random.Range(0.05f, 0.25f),
            Random.Range(-0.35f, 0.35f));

        GameObject go = new GameObject(isCrit ? "CritDamagePopup" : "DamagePopup");
        go.transform.position = worldPos + jitter;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.fontStyle = FontStyles.Bold;

        if (isPlayerDamage)
        {
            tmp.text = $"-{damage}";
            tmp.fontSize = 4.2f;
            tmp.color = new Color(1f, 0.25f, 0.25f, 1f);
        }
        else if (isCrit)
        {
            tmp.text = $"<size=65%>CRITICAL!</size>\n{damage}!";
            tmp.fontSize = 6.8f;
            tmp.color = new Color(1f, 0.82f, 0.12f, 1f);
            tmp.enableVertexGradient = true;
            tmp.colorGradient = new VertexGradient(
                new Color(1f, 0.96f, 0.35f),
                new Color(1f, 0.96f, 0.35f),
                new Color(1f, 0.28f, 0.05f),
                new Color(1f, 0.28f, 0.05f));
        }
        else
        {
            tmp.text = damage.ToString();
            tmp.fontSize = 4.6f;
            tmp.color = new Color(1f, 0.96f, 0.88f, 1f);
        }

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingOrder = isCrit ? 50 : 30;
        }

        StartCoroutine(AnimatePopupRoutine(go.transform, tmp, isCrit ? 1.15f : 0.85f, isCrit));
    }

    private void CreateStatusPopupInternal(Vector3 worldPos, string message, Color color, float scaleMultiplier)
    {
        GameObject go = new GameObject("StatusPopup");
        go.transform.position = worldPos;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.fontStyle = FontStyles.Bold;
        tmp.text = message;
        tmp.fontSize = 5.2f * scaleMultiplier;
        tmp.color = color;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingOrder = 60;
        }

        StartCoroutine(AnimatePopupRoutine(go.transform, tmp, 1.25f, true));
    }

    private IEnumerator AnimatePopupRoutine(Transform tr, TextMeshPro tmp, float duration, bool punchScale)
    {
        Vector3 startPos = tr.position;
        Vector3 drift = new Vector3(Random.Range(-0.4f, 0.4f), punchScale ? 2.1f : 1.5f, Random.Range(-0.4f, 0.4f));
        Color baseColor = tmp.color;

        float elapsed = 0f;
        while (elapsed < duration && tr != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Ease-out upward float
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);
            tr.position = startPos + drift * easeOut;

            // Face main camera
            Camera cam = Camera.main;
            if (cam != null)
            {
                tr.rotation = cam.transform.rotation;
            }

            // Elastic scale punch for Critical Hits & Alerts
            if (punchScale)
            {
                float scale;
                if (t < 0.14f)
                {
                    scale = Mathf.Lerp(0.35f, 1.55f, t / 0.14f);
                }
                else if (t < 0.30f)
                {
                    scale = Mathf.Lerp(1.55f, 1.0f, (t - 0.14f) / 0.16f);
                }
                else
                {
                    scale = 1.0f;
                }
                tr.localScale = Vector3.one * scale;
            }

            // Fade out during last 35%
            if (t > 0.65f)
            {
                float alpha = Mathf.InverseLerp(1f, 0.65f, t);
                Color c = baseColor;
                c.a = alpha;
                tmp.color = c;
            }

            yield return null;
        }

        if (tr != null)
        {
            Destroy(tr.gameObject);
        }
    }

    // =========================================================
    // 2. HIT SPARKS, CRITICAL SHOCKWAVE & SLASH BURST
    // =========================================================

    public static void SpawnHitEffect(Vector3 hitPosition, bool isCritical, Color? customColor = null)
    {
        if (!Application.isPlaying) return;
        Instance.CreateHitEffectInternal(hitPosition, isCritical, customColor);
    }

    private void CreateHitEffectInternal(Vector3 hitPos, bool isCrit, Color? customColor)
    {
        if (!Application.isPlaying) return;
        EnsureMaterials();

        GameObject root = new GameObject(isCrit ? "VFX_CritHitBurst" : "VFX_NormalHitBurst");
        root.transform.position = hitPos;

        Color primaryColor = customColor ?? (isCrit
            ? new Color(1f, 0.78f, 0.12f, 1f)
            : new Color(0.45f, 0.88f, 1f, 1f));

        Color secondaryColor = isCrit
            ? new Color(1f, 0.22f, 0.08f, 1f)
            : new Color(1f, 0.95f, 0.8f, 1f);

        // 1. Particle Burst
        // Stop immediately after AddComponent because Unity auto-starts ParticleSystem on Awake,
        // which otherwise logs "Setting the duration while system is still playing is not supported".
        ParticleSystem ps = root.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystemRenderer psr = root.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = _particleMaterial;
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.lengthScale = isCrit ? 3.2f : 2.2f;
        psr.velocityScale = 0.08f;

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, isCrit ? 0.45f : 0.30f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(isCrit ? 5.5f : 3.5f, isCrit ? 12.5f : 7.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(isCrit ? 0.12f : 0.08f, isCrit ? 0.26f : 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(primaryColor, secondaryColor);
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)(isCrit ? 48 : 20))
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        ps.Play();

        // 2. Expanding Shockwave Ring (bigger and double-ringed on Critical Hit)
        StartCoroutine(AnimateShockwaveRingRoutine(
            hitPos,
            primaryColor,
            startRadius: 0.2f,
            endRadius: isCrit ? 2.4f : 1.15f,
            duration: isCrit ? 0.28f : 0.18f,
            width: isCrit ? 0.16f : 0.08f,
            tiltRandom: true));

        if (isCrit)
        {
            StartCoroutine(AnimateShockwaveRingRoutine(
                hitPos,
                secondaryColor,
                startRadius: 0.15f,
                endRadius: 3.1f,
                duration: 0.34f,
                width: 0.12f,
                tiltRandom: false));

            StartCoroutine(AnimateCritLightFlashRoutine(hitPos, primaryColor));
        }

        Destroy(root, 1.0f);
    }

    public static void SpawnAggroPulse(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.StartCoroutine(Instance.AnimateShockwaveRingRoutine(
            worldPos + Vector3.up * 0.15f,
            new Color(1f, 0.22f, 0.15f, 0.95f),
            startRadius: 0.4f,
            endRadius: 3.8f,
            duration: 0.45f,
            width: 0.14f,
            tiltRandom: false));
    }

    public static void SpawnRespawnEffect(Vector3 worldPos)
    {
        if (!Application.isPlaying) return;
        Instance.StartCoroutine(Instance.AnimateShockwaveRingRoutine(
            worldPos + Vector3.up * 0.15f,
            new Color(0.25f, 0.95f, 0.65f, 0.95f),
            startRadius: 0.2f,
            endRadius: 3.2f,
            duration: 0.55f,
            width: 0.15f,
            tiltRandom: false));
    }

    private IEnumerator AnimateShockwaveRingRoutine(
        Vector3 center,
        Color color,
        float startRadius,
        float endRadius,
        float duration,
        float width,
        bool tiltRandom)
    {
        EnsureMaterials();

        GameObject ringGo = new GameObject("VFX_ShockwaveRing");
        ringGo.transform.position = center;
        if (tiltRandom)
        {
            ringGo.transform.rotation = Quaternion.Euler(
                Random.Range(-55f, 55f),
                Random.Range(0f, 360f),
                Random.Range(-55f, 55f));
        }

        LineRenderer lr = ringGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = 36;
        lr.sharedMaterial = _ringMaterial;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        float elapsed = 0f;
        while (elapsed < duration && ringGo != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            float radius = Mathf.Lerp(startRadius, endRadius, ease);

            for (int i = 0; i < lr.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / lr.positionCount;
                lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            float currentWidth = Mathf.Lerp(width, 0.01f, t);
            lr.startWidth = currentWidth;
            lr.endWidth = currentWidth;

            Color c = color;
            c.a = Mathf.Lerp(color.a, 0f, t);
            lr.startColor = c;
            lr.endColor = c;

            yield return null;
        }

        if (ringGo != null)
        {
            Destroy(ringGo);
        }
    }

    private IEnumerator AnimateCritLightFlashRoutine(Vector3 pos, Color color)
    {
        GameObject lightGo = new GameObject("VFX_CritFlashLight");
        lightGo.transform.position = pos + Vector3.up * 0.5f;

        Light pointLight = lightGo.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = color;
        pointLight.range = 6.5f;
        pointLight.intensity = 6.0f;
        pointLight.shadows = LightShadows.None;

        float duration = 0.22f;
        float elapsed = 0f;
        while (elapsed < duration && lightGo != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            pointLight.intensity = Mathf.Lerp(6.0f, 0f, t);
            yield return null;
        }

        if (lightGo != null)
        {
            Destroy(lightGo);
        }
    }
}
