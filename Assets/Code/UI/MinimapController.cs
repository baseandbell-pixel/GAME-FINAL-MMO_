using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class MinimapController : MonoBehaviour
{
    [Header("Target & Camera References")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private Camera minimapCamera;

    [Header("UI References")]
    [SerializeField] private TMP_Text zoneCoordText;
    [SerializeField] private string zoneName = "Starter Field";

    [Header("Settings")]
    [SerializeField] private float cameraHeight = 20f;
    [SerializeField] private bool rotateWithPlayer = false;
    [SerializeField] private float zoomStep = 2f;
    [SerializeField] private float minZoom = 6f;
    [SerializeField] private float maxZoom = 24f;

    private void LateUpdate()
    {
        if (playerTarget == null || minimapCamera == null) return;

        // 1. บังคับขยับเฉพาะตัวกล้อง Minimap_Camera เท่านั้น (ไม่ไปยุ่งกับตำแหน่ง UI)
        Vector3 newPosition = playerTarget.position;
        newPosition.y += cameraHeight;
        minimapCamera.transform.position = newPosition;

        // 2. บังคับหมุนเฉพาะตัวกล้อง Minimap_Camera
        if (rotateWithPlayer)
        {
            minimapCamera.transform.rotation = Quaternion.Euler(90f, playerTarget.eulerAngles.y, 0f);
        }
        else
        {
            minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // 3. อัปเดตข้อความชื่อโซนและพิกัด (X, Z)
        if (zoneCoordText != null)
        {
            int posX = Mathf.RoundToInt(playerTarget.position.x);
            int posZ = Mathf.RoundToInt(playerTarget.position.z);
            zoneCoordText.text = $"{zoneName} ({posX}, {posZ})";
        }

        // 4. กดปุ่ม M / N เพื่อซูมเข้า-ซูมออก
        if (!ChatBoxUI.IsTyping && Keyboard.current != null)
        {
            if (Keyboard.current.mKey.wasPressedThisFrame) ZoomIn();
            if (Keyboard.current.nKey.wasPressedThisFrame) ZoomOut();
        }
    }

    public void ZoomIn()
    {
        if (minimapCamera == null) return;
        minimapCamera.orthographicSize = Mathf.Max(minZoom, minimapCamera.orthographicSize - zoomStep);
    }

    public void ZoomOut()
    {
        if (minimapCamera == null) return;
        minimapCamera.orthographicSize = Mathf.Min(maxZoom, minimapCamera.orthographicSize + zoomStep);
    }
}