using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// An item lying in the world (drop or chest stand-in). Shared Mode flow:
///
///  1. Local player is within pickupRadius and presses E (only the peer that controls a player reads input).
///  2. That peer checks it has inventory room, then sends RPC_RequestPickup (any peer -> this item's State Authority).
///  3. The State Authority verifies the sender (Fusion's RpcInfo, not a parameter), their distance and that the
///     item is not already claimed, then sets the networked Claimed flag - first request wins.
///  4. RPC_ConfirmPickup (SA -> All) tells everyone who won; ONLY the winner's peer adds the item to its inventory.
///  5. The authority despawns the object shortly after (driven from FixedUpdateNetwork so it survives an authority hand-off).
///
/// What the item IS travels as a string id + quantity; each peer resolves the ItemData from Resources/Items.
/// </summary>
public class WorldItem : NetworkBehaviour
{
    public static readonly List<WorldItem> All = new List<WorldItem>();

    [Networked] public NetworkString<_32> ItemId { get; set; }
    [Networked] public int Quantity { get; set; } = 1;
    [Networked, OnChangedRender(nameof(OnClaimedChanged))] public NetworkBool Claimed { get; set; }

    [Header("Interaction")]
    [Min(0.5f)] [SerializeField] private float pickupRadius = 2.5f;
    [Tooltip("Extra distance the authority tolerates (proxy lag) when validating a request.")]
    [SerializeField] private float validationSlack = 2f;
    [SerializeField] private float despawnDelay = 0.75f;

    [Header("Visuals")]
    [SerializeField] private Transform visual;
    [SerializeField] private Renderer visualRenderer;
    [SerializeField] private float bobHeight = 0.15f;
    [SerializeField] private float spinSpeed = 90f;

    public bool IsSpawned { get; private set; }
    public ItemData Data => ItemRegistry.Get(ItemId.ToString());

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _mpb;
    private Vector3 _visualBase;
    private float _claimedAt = -1f;
    private float _lastRequestTime = -10f;

    // one "nearest item" computation per frame, shared by all instances
    private static int s_frame = -1;
    private static WorldItem s_promptItem;

    // ------------------------------------------------------------------ spawn helper

    /// <summary>Spawns a drop from this peer (which becomes its State Authority).</summary>
    public static WorldItem Spawn(NetworkRunner runner, WorldItem prefab, Vector3 position, ItemData item, int quantity)
    {
        if (runner == null || !runner.IsRunning) { Debug.LogWarning("[WorldItem] Spawn failed: runner not running."); return null; }
        if (prefab == null) { Debug.LogWarning("[WorldItem] Spawn failed: no prefab."); return null; }
        if (item == null) { Debug.LogWarning("[WorldItem] Spawn failed: no item."); return null; }

        string id = item.itemId;
        int qty = Mathf.Max(1, quantity);
        return runner.Spawn(prefab, position, Quaternion.identity, null, (r, o) =>
        {
            var wi = o.GetComponent<WorldItem>();
            wi.ItemId = id;
            wi.Quantity = qty;
        });
    }

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        if (visual == null && visualRenderer != null) visual = visualRenderer.transform;
        if (visualRenderer == null) visualRenderer = GetComponentInChildren<Renderer>();
        if (visual == null && visualRenderer != null) visual = visualRenderer.transform;
        if (visual != null) _visualBase = visual.localPosition;
    }

    public override void Spawned()
    {
        IsSpawned = true;
        if (!All.Contains(this)) All.Add(this);

        var data = Data;
        Debug.Log($"[WorldItem] Spawned {Quantity}x {(data != null ? data.displayName : ItemId.ToString())} at {transform.position} (authority {Object.StateAuthority}).");
        ApplyVisual();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        IsSpawned = false;
        All.Remove(this);
        if (s_promptItem == this) s_promptItem = null;
    }

    private void ApplyVisual()
    {
        if (visualRenderer == null) return;
        var data = Data;
        visualRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorId, data != null ? data.worldColor : Color.magenta);
        visualRenderer.SetPropertyBlock(_mpb);
        visualRenderer.enabled = !Claimed;
    }

    private void OnClaimedChanged()
    {
        if (!IsSpawned) return;
        Debug.Log($"[WorldItem] {ItemId} claimed={Claimed}.");
        if (visualRenderer != null) visualRenderer.enabled = !Claimed;
    }

    // ------------------------------------------------------------------ authority housekeeping

    public override void FixedUpdateNetwork()
    {
        if (!IsSpawned || !Object.HasStateAuthority || !Claimed) return;

        if (_claimedAt < 0f) _claimedAt = Time.time;
        if (Time.time - _claimedAt >= despawnDelay)
        {
            Debug.Log($"[WorldItem] Despawning claimed item {ItemId}.");
            Runner.Despawn(Object);
        }
    }

    // ------------------------------------------------------------------ local interaction

    private void Update()
    {
        if (!IsSpawned) return;

        if (visual != null && !Claimed)
        {
            visual.localPosition = _visualBase + Vector3.up * (Mathf.Sin(Time.time * 2f + transform.position.x) * bobHeight);
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        RefreshPromptTarget();
        if (s_promptItem != this) return;

        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) RequestPickup();
    }

    private static void RefreshPromptTarget()
    {
        if (s_frame == Time.frameCount) return;
        s_frame = Time.frameCount;
        s_promptItem = null;

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked) return;

        float best = float.MaxValue;
        Vector3 p = player.transform.position;
        for (int i = 0; i < All.Count; i++)
        {
            var it = All[i];
            if (it == null || !it.IsSpawned || it.Claimed) continue;
            float d = Planar(it.transform.position, p);
            if (d <= it.pickupRadius && d < best) { best = d; s_promptItem = it; }
        }
    }

    private void RequestPickup()
    {
        if (Time.time - _lastRequestTime < 0.5f) return;      // local spam guard
        _lastRequestTime = Time.time;

        var data = Data;
        var inv = InventoryManager.Local;
        if (data == null) { Debug.LogWarning($"[WorldItem] Cannot pick up: unknown item '{ItemId}'."); return; }
        if (inv == null) { Debug.LogWarning("[WorldItem] Cannot pick up: no local InventoryManager."); return; }
        if (!inv.CanAdd(data, Quantity))
        {
            Debug.LogWarning($"[WorldItem] Inventory full - cannot pick up {Quantity}x {data.displayName}.");
            return;
        }

        Debug.Log($"[WorldItem] Requesting pickup of {Quantity}x {data.displayName}.");
        RPC_RequestPickup();
    }

    // ------------------------------------------------------------------ RPCs

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPickup(RpcInfo info = default)
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        if (Claimed)
        {
            Debug.Log($"[WorldItem] Pickup by {info.Source} rejected: already claimed.");
            return;
        }

        PlayerCombat picker = null;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p != null && p.IsSpawned && p.Object.StateAuthority == info.Source) { picker = p; break; }
        }
        if (picker == null || picker.IsDead)
        {
            Debug.LogWarning($"[WorldItem] Pickup rejected: no living player object for {info.Source}.");
            return;
        }

        float d = Planar(picker.transform.position, transform.position);
        if (d > pickupRadius + validationSlack)
        {
            Debug.LogWarning($"[WorldItem] Pickup rejected: {info.Source} is {d:0.0}m away (max {pickupRadius + validationSlack:0.0}).");
            return;
        }

        Claimed = true;
        Debug.Log($"[WorldItem] {ItemId} x{Quantity} claimed by {info.Source}.");
        RPC_ConfirmPickup(info.Source);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ConfirmPickup(PlayerRef winner)
    {
        if (Runner == null || Runner.LocalPlayer != winner) return;     // only the winner's peer gets the item

        var data = Data;
        var inv = InventoryManager.Local;
        if (data == null || inv == null)
        {
            Debug.LogWarning("[WorldItem] Pickup confirmed but data/inventory missing - item lost.");
            return;
        }

        int remainder = inv.AddItem(data, Quantity);
        Debug.Log($"[WorldItem] Picked up {Quantity - remainder}x {data.displayName}.");

        if (remainder > 0)
        {
            // Inventory filled between the check and the confirmation: put the leftovers back into the world.
            Debug.LogWarning($"[WorldItem] Inventory filled meanwhile - dropping {remainder}x {data.displayName} back.");
            inv.SpawnWorldItem(data, remainder, transform.position);
        }
    }

    // ------------------------------------------------------------------ prompt UI

    private void OnGUI()
    {
        if (!IsSpawned || s_promptItem != this || Event.current.type != EventType.Repaint) return;
        var data = Data;
        string text = $"[E] Pick up  {(data != null ? data.displayName : ItemId.ToString())}  x{Quantity}";
        var rect = new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.72f, 280f, 26f);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, 22f), text);
        GUI.color = prev;
    }

    private static float Planar(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
