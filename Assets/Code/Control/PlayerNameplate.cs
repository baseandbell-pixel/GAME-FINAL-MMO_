
using TMPro;
using UnityEngine;

public class PlayerNameplate : MonoBehaviour
{
    [Header("Name")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private string defaultName = "Player";

    [Header("Billboard")]
    [SerializeField] private bool faceCamera = true;

    private Camera mainCamera;

    private void Awake()
    {
        if (nameText == null)
        {
            nameText = GetComponentInChildren<TMP_Text>();
        }

        mainCamera = Camera.main;

        if (nameText != null)
        {
            nameText.text = defaultName;
        }
        else
        {
            Debug.LogError(
                "[PlayerNameplate] TextMeshPro component not found.",
                this
            );
        }
    }

    private void LateUpdate()
    {
        if (!faceCamera)
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            return;
        }

        Vector3 direction =
            mainCamera.transform.position - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation =
                Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    public void SetPlayerName(string playerName)
    {
        if (nameText == null)
        {
            return;
        }

        nameText.text = string.IsNullOrWhiteSpace(playerName)
            ? defaultName
            : playerName;
    }
}
