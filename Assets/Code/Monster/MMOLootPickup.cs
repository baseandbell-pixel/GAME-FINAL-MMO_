using UnityEngine;

/// <summary>
/// Collectible 3D ground loot drop spawned per contributor when a monster is defeated.
/// Features a bright vertical loot beam, clear ground visibility window, and magnet pickup.
/// </summary>
[DisallowMultipleComponent]
public sealed class MMOLootPickup : MonoBehaviour
{
    public enum LootType
    {
        Gold = 0,
        HealthPotion = 1,
        PowerRelic = 2
    }

    private LootType _type;
    private int _amount;
    private MMOPlayerCombat _ownerCombat;
    private Material _runtimeMaterial;
    private Material _beamMaterial;

    private Vector3 _launchVelocity;
    private float _spawnedAt;
    private float _settledAt = -1f;
    private float _groundY = 0.48f;
    private bool _settled;
    private bool _collected;

    private const float MagnetGraceAfterSettle = 0.85f;
    private const float MagnetRadius = 2.8f;
    private const float CollectRadius = 1.45f;
    private const float AutoCollectAfterSeconds = 20f;
    private const float DespawnAfterSeconds = 45f;

    public static void SpawnDrop(Vector3 monsterPos, LootType type, int amount, MMOPlayerCombat ownerCombat)
    {
        if (!Application.isPlaying || ownerCombat == null)
        {
            return;
        }

        float angleRad = Random.Range(0f, Mathf.PI * 2f);
        float spawnOffsetDist = Random.Range(0.55f, 1.15f);
        Vector3 startPos = monsterPos + new Vector3(
            Mathf.Cos(angleRad) * spawnOffsetDist,
            0.95f,
            Mathf.Sin(angleRad) * spawnOffsetDist);

        PrimitiveType shape = type switch
        {
            LootType.Gold => PrimitiveType.Sphere,
            LootType.HealthPotion => PrimitiveType.Capsule,
            _ => PrimitiveType.Cube
        };

        GameObject go = GameObject.CreatePrimitive(shape);
        go.name = $"LootDrop_{type}";
        go.transform.position = startPos;
        go.transform.localScale = type switch
        {
            LootType.Gold => Vector3.one * 0.55f,
            LootType.HealthPotion => new Vector3(0.42f, 0.38f, 0.42f),
            _ => Vector3.one * 0.56f
        };

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Color color = type switch
        {
            LootType.Gold => new Color(1f, 0.82f, 0.14f, 1f),
            LootType.HealthPotion => new Color(1f, 0.20f, 0.32f, 1f),
            _ => new Color(0.24f, 0.92f, 1f, 1f)
        };

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 2.0f);

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Add a tall translucent vertical loot beam so drops stand out clearly on the arena floor.
        Material beamMat = CreateBeamMaterial(color);
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "LootBeam";
        beam.transform.SetParent(go.transform, false);
        beam.transform.localPosition = new Vector3(0f, 2.2f, 0f);
        beam.transform.localScale = new Vector3(0.26f, 2.4f, 0.26f);
        Collider beamCol = beam.GetComponent<Collider>();
        if (beamCol != null) Destroy(beamCol);
        Renderer beamRenderer = beam.GetComponent<Renderer>();
        if (beamRenderer != null)
        {
            beamRenderer.sharedMaterial = beamMat;
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
        }

        MMOLootPickup pickup = go.AddComponent<MMOLootPickup>();
        pickup._type = type;
        pickup._amount = amount;
        pickup._ownerCombat = ownerCombat;
        pickup._runtimeMaterial = mat;
        pickup._beamMaterial = beamMat;
        pickup._spawnedAt = Time.time;

        float floorY = SampleFloorHeight(startPos, monsterPos.y);
        pickup._groundY = floorY + 0.48f;

        Vector3 outDir = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
        pickup._launchVelocity = outDir * Random.Range(2.4f, 3.6f) + Vector3.up * 4.2f;
    }

    private static float SampleFloorHeight(Vector3 nearPos, float fallbackY)
    {
        Vector3 origin = new Vector3(nearPos.x, nearPos.y + 4f, nearPos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.GetComponentInParent<MMOMonsterAI>() == null &&
                hit.transform.GetComponentInParent<MMOPlayerController>() == null)
            {
                return hit.point.y;
            }
        }
        return fallbackY;
    }

    private static Material CreateBeamMaterial(Color baseColor)
    {
        Shader unlit = Shader.Find("Sprites/Default")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Standard");
        Material m = new Material(unlit);
        Color beamColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.38f);
        m.SetColor("_BaseColor", beamColor);
        m.SetColor("_Color", beamColor);
        return m;
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
            _runtimeMaterial = null;
        }
        if (_beamMaterial != null)
        {
            Destroy(_beamMaterial);
            _beamMaterial = null;
        }
    }

    private void Update()
    {
        if (_collected || _ownerCombat == null)
        {
            Destroy(gameObject);
            return;
        }

        float dt = Time.deltaTime;
        float age = Time.time - _spawnedAt;
        if (age >= DespawnAfterSeconds)
        {
            Destroy(gameObject);
            return;
        }

        transform.Rotate(0f, 130f * dt, _type == LootType.PowerRelic ? 35f * dt : 0f, Space.World);

        if (!_settled)
        {
            _launchVelocity.y -= 13.5f * dt;
            transform.position += _launchVelocity * dt;
            if (transform.position.y <= _groundY)
            {
                Vector3 pos = transform.position;
                pos.y = _groundY;
                transform.position = pos;
                _settled = true;
                _settledAt = Time.time;
            }
            return;
        }

        Vector3 targetPos = _ownerCombat.transform.position + Vector3.up * 0.9f;
        float dist = Vector3.Distance(transform.position, targetPos);
        bool graceElapsed = Time.time - _settledAt >= MagnetGraceAfterSettle;

        if (age >= AutoCollectAfterSeconds || (graceElapsed && dist <= MagnetRadius))
        {
            float pullSpeed = age >= AutoCollectAfterSeconds ? 16f : 11f;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, pullSpeed * dt);
            dist = Vector3.Distance(transform.position, targetPos);
        }
        else
        {
            Vector3 bob = transform.position;
            bob.y = _groundY + Mathf.Sin(Time.time * 4.0f) * 0.14f;
            transform.position = bob;
        }

        if (graceElapsed && dist <= CollectRadius)
        {
            Collect();
        }
    }

    private void Collect()
    {
        if (_collected) return;
        _collected = true;

        _ownerCombat.CollectLoot(_type, _amount);
        MMOAudioManager.PlayLootPickup(transform.position);
        Destroy(gameObject);
    }
}
