using UnityEngine;
using UnityEngine.InputSystem;

public class ShopNPC : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject interactPromptUI;
    [SerializeField] private ShopUIController shopUI;

    [Header("Settings")]
    [SerializeField] private float interactRange = 3.5f;

    private void Start()
    {
        if (interactPromptUI != null)
        {
            interactPromptUI.SetActive(false);
        }
    }

    private void Update()
    {
        var combat = PlayerCombat.Local;
        if (combat == null || !combat.IsSpawned)
        {
            if (interactPromptUI != null && interactPromptUI.activeSelf)
                interactPromptUI.SetActive(false);
            return;
        }

        ShopUIController targetShop = shopUI != null ? shopUI : ShopUIController.Instance;
        bool isShopOpen = targetShop != null && targetShop.IsOpen;

        float distance = Vector3.Distance(transform.position, combat.transform.position);
        bool inRange = distance <= interactRange;

        // โชว์ป้ายกด [E] เฉพาะเมื่ออยู่ในระยะ และไม่มีหน้าต่าง UI ใดเปิดบล็อกอยู่
        if (interactPromptUI != null)
        {
            interactPromptUI.SetActive(inRange && !isShopOpen && !GameplayInput.IsBlocked);
        }

        if (Keyboard.current == null) return;

        // กด E เปิดร้าน (ถ้าอยู่ในระยะและไม่ติด Blocker) หรือกด E ปิดร้านถ้าเปิดอยู่แล้ว
        if (Keyboard.current.eKey.wasPressedThisFrame && targetShop != null)
        {
            if (isShopOpen)
            {
                targetShop.SetShopOpen(false);
            }
            else if (inRange && !GameplayInput.IsBlocked)
            {
                targetShop.SetShopOpen(true);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}