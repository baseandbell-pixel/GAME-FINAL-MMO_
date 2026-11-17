using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Collectible 3D ground loot drop supporting Gold, Materials, Consumables, and Rarity Gear.
/// - Gold & basic materials auto-loot cleanly when walking near.
/// - Gear, Boss Cores, and Potions show a Rarity-colored Loot Beam + floating name label and are picked up via [F] (or AutoLootAll).
/// </summary>
[DisallowMultipleComponent]
public sealed class MMOLootPickup : MonoBehaviour
{
    public enum LootType
    {
        Gold = 0,
        Item = 1
    }

    private static readonly List<MMOLootPickup> ActiveDrops = new List<MMOLootPickup>();

    private LootType _type;
    private int _amount;
    private MMOItemInstance _itemInstance;
    private MMOPlayerCombat _ownerCombat;
    private MMOPlayerInventory _ownerInventory;
    private Material _runtimeMaterial;
    private Material _beamMaterial;
    private Transform _labelTransform;

    private Vector3 _launchVelocity;
    private float _spawnedAt;
    private float _settledAt = -1f;
    private float _groundY = 0.48f;
    private bool _settled;
    private bool _collected;
    private bool _autoMagnetize;

    private const float InteractRadius = 3.4f;
    private const float MagnetGraceAfterSettle = 0.70f;
    private const float MagnetRadius = 2.9f;
    private const float CollectRadius = 1.45f;
    private const float AutoCollectAfterSeconds = 25f;
    private const float DespawnAfterSeconds = 55f;

    public string DisplayPromptText
    {
        get
        {
            if (_type == LootType.Gold) return $"+{_amount} Gold";
            if (_itemInstance == null) return "Item";
            MMOItemTemplate tpl = MMOItemCatalog.Get(_itemInstance.itemId);
            string name = tpl != null ? tpl.displayName : _itemInstance.itemId;
            string qty = _itemInstance.stackCount > 1 ? $" x{_itemInstance.stackCount}" : "";
            string hex = MMOItemCatalog.GetRarityHex(_itemInstance.rarity);
            return $"<color={hex}>[{_itemInstance.rarity}] {name}{qty}</color>";
        }
    }

    public static MMOLootPickup FindNearestInteractable(Vector3 playerWorldPos)
    {
        MMOLootPickup best = null;
        float bestDist = InteractRadius;
        for (int i = ActiveDrops.Count - 1; i >= 0; i--)
        {
            MMOLootPickup drop = ActiveDrops[i];
            if (drop == null || drop._collected || !drop._settled) continue;

            float d = Vector3.Distance(playerWorldPos, drop.transform.position);
            if (d <= bestDist)
            {
                bestDist = d;
                best = drop;
            }
        }
        return best;
    }

    public static bool TryPickupNearest(Vector3 playerWorldPos)
    {
        MMOLootPickup drop = FindNearestInteractable(playerWorldPos);
        if (drop == null) return false;
        drop.Collect();
        return true;
    }

    public static void SpawnGoldDrop(Vector3 monsterPos, int goldAmount, MMOPlayerCombat ownerCombat, MMOPlayerInventory ownerInventory)
    {
        SpawnInternal(monsterPos, LootType.Gold, goldAmount, null, ownerCombat, ownerInventory);
    }

    public static void SpawnItemDrop(Vector3 monsterPos, MMOItemInstance item, MMOPlayerCombat ownerCombat, MMOPlayerInventory ownerInventory)
    {
        if (item == null) return;
        SpawnInternal(monsterPos, LootType.Item, item.stackCount, item, ownerCombat, ownerInventory);
    }

    private static void SpawnInternal(
        Vector3 monsterPos,
        LootType type,
        int amount,
        MMOItemInstance item,
        MMOPlayerCombat ownerCombat,
        MMOPlayerInventory ownerInventory)
    {
        if (!Application.isPlaying || ownerCombat == null) return;

        float angleRad = Random.Range(0f, Mathf.PI * 2f);
        float spawnOffsetDist = Random.Range(0.55f, 1.25f);
        Vector3 startPos = monsterPos + new Vector3(
            Mathf.Cos(angleRad) * spawnOffsetDist,
            0.95f,
            Mathf.Sin(angleRad) * spawnOffsetDist);

        MMOItemTemplate tpl = item != null ? MMOItemCatalog.Get(item.itemId) : null;
        ItemRarity rarity = item != null ? item.rarity : ItemRarity.Common;

        PrimitiveType shape = PrimitiveType.Cube;
        Vector3 scale = Vector3.one * 0.50f;
        Color color = new Color(1f, 0.82f, 0.14f, 1f);
        bool autoMag = true;

        if (type == LootType.Gold)
        {
            shape = PrimitiveType.Sphere;
            scale = Vector3.one * 0.48f;
            color = new Color(1f, 0.82f, 0.14f, 1f);
            autoMag = true;
        }
        else if (tpl != null)
        {
            color = MMOItemCatalog.GetRarityColor(rarity);
            if (tpl.category == ItemCategory.Consumable)
            {
                shape = PrimitiveType.Capsule;
                scale = new Vector3(0.36f, 0.34f, 0.36f);
                if (rarity == ItemRarity.Common) color = new Color(1f, 0.24f, 0.34f, 1f);
                autoMag = false;
            }
            else if (tpl.category == ItemCategory.Material)
            {
                shape = PrimitiveType.Cylinder;
                scale = new Vector3(0.40f, 0.25f, 0.40f);
                autoMag = tpl.itemId != "warlord_core";
            }
            else
            {
                shape = PrimitiveType.Cube;
                scale = new Vector3(0.58f, 0.44f, 0.44f);
                autoMag = false;
            }
        }

        GameObject go = GameObject.CreatePrimitive(shape);
        go.name = type == LootType.Gold ? "LootDrop_Gold" : $"LootDrop_{item.itemId}_{rarity}";
        go.transform.position = startPos;
        go.transform.localScale = scale;

        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * (1.5f + (int)rarity * 0.45f));

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        float beamHeight = type == LootType.Gold ? 1.6f : (2.1f + (int)rarity * 0.75f);
        Material beamMat = CreateBeamMaterial(color, type == LootType.Gold ? 0.28f : 0.38f + (int)rarity * 0.06f);
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "LootBeam";
        beam.transform.SetParent(go.transform, false);
        beam.transform.localPosition = new Vector3(0f, beamHeight, 0f);
        beam.transform.localScale = new Vector3(0.24f, beamHeight, 0.24f);
        Collider beamCol = beam.GetComponent<Collider>();
        if (beamCol != null) Destroy(beamCol);
        Renderer beamRenderer = beam.GetComponent<Renderer>();
        if (beamRenderer != null)
        {
            beamRenderer.sharedMaterial = beamMat;
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
        }

        Transform labelTf = null;
        if (!autoMag)
        {
            GameObject lblGo = new GameObject("LootLabel");
            lblGo.transform.position = startPos + Vector3.up * 1.15f;
            TextMeshPro tmp = lblGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.fontSize = 3.0f + (int)rarity * 0.35f;
            tmp.fontStyle = FontStyles.Bold;
            string qty = item != null && item.stackCount > 1 ? $" x{item.stackCount}" : "";
            string itemTitle = tpl != null ? tpl.displayName : "Item";
            tmp.text = $"<color={MMOItemCatalog.GetRarityHex(rarity)}>[{rarity}] {itemTitle}{qty}</color>\n<size=72%><color=#FFFFFF>[F] Pick Up</color></size>";
            lblGo.transform.SetParent(go.transform, true);
            labelTf = lblGo.transform;
        }

        MMOLootPickup pickup = go.AddComponent<MMOLootPickup>();
        pickup._type = type;
        pickup._amount = amount;
        pickup._itemInstance = item;
        pickup._ownerCombat = ownerCombat;
        pickup._ownerInventory = ownerInventory;
        pickup._runtimeMaterial = mat;
        pickup._beamMaterial = beamMat;
        pickup._labelTransform = labelTf;
        pickup._spawnedAt = Time.time;
        pickup._autoMagnetize = autoMag;

        float floorY = SampleFloorHeight(startPos, monsterPos.y);
        pickup._groundY = floorY + 0.48f;

        Vector3 outDir = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
        pickup._launchVelocity = outDir * Random.Range(2.4f, 3.6f) + Vector3.up * 4.2f;
        ActiveDrops.Add(pickup);
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

    private static Material CreateBeamMaterial(Color baseColor, float alpha)
    {
        Shader unlit = Shader.Find("Sprites/Default")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Standard");
        Material m = new Material(unlit);
        Color beamColor = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(alpha));
        m.SetColor("_BaseColor", beamColor);
        m.SetColor("_Color", beamColor);
        return m;
    }

    private void OnDestroy()
    {
        ActiveDrops.Remove(this);
        if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
        if (_beamMaterial != null) Destroy(_beamMaterial);
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

        transform.Rotate(0f, 125f * dt, 0f, Space.World);
        if (_labelTransform != null && Camera.main != null)
        {
            _labelTransform.rotation = Quaternion.LookRotation(_labelTransform.position - Camera.main.transform.position);
        }

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
        bool shouldAutoVacuum = _autoMagnetize || (_ownerInventory != null && _ownerInventory.AutoLootAll);

        if (age >= AutoCollectAfterSeconds || (graceElapsed && shouldAutoVacuum && dist <= MagnetRadius))
        {
            float pullSpeed = age >= AutoCollectAfterSeconds ? 16f : 11f;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, pullSpeed * dt);
            dist = Vector3.Distance(transform.position, targetPos);
            if (dist <= CollectRadius)
            {
                Collect();
                return;
            }
        }
        else
        {
            Vector3 bob = transform.position;
            bob.y = _groundY + Mathf.Sin(Time.time * 4.0f) * 0.12f;
            transform.position = bob;
        }
    }

    private void Collect()
    {
        if (_collected) return;
        _collected = true;

        if (_type == LootType.Gold)
        {
            _ownerCombat.CollectGoldDrop(_amount);
        }
        else if (_itemInstance != null)
        {
            _ownerCombat.CollectItemDrop(_itemInstance);
        }

        MMOAudioManager.PlayLootPickup(transform.position);
        Destroy(gameObject);
    }
}
