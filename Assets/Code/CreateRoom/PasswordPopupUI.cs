using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PasswordPopupUI : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("ตัว Panel ของ Popup นี้ (ปล่อยว่างไว้ได้ ระบบจะใช้ GameObject นี้เอง)")]
    public GameObject panelRoot;

    [Header("UI Elements")]
    public TMP_Text titleText;
    public TMP_InputField passwordInput;
    public Button confirmButton;
    public Button cancelButton;
    public TMP_Text errorText;

    string _expectedPassword;
    Action _onPasswordVerified;

    void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        panelRoot.SetActive(false);
    }

    void OnEnable()
    {
        if (confirmButton) confirmButton.onClick.AddListener(OnClickConfirm);
        if (cancelButton) cancelButton.onClick.AddListener(Close);
    }

    void OnDisable()
    {
        if (confirmButton) confirmButton.onClick.RemoveListener(OnClickConfirm);
        if (cancelButton) cancelButton.onClick.RemoveListener(Close);
    }

    /// <summary>เปิดหน้าต่างกรอกรหัสผ่านสำหรับห้อง Private</summary>
    public void Open(string roomDisplayName, string expectedPassword, Action onPasswordVerified)
    {
        _expectedPassword = expectedPassword ?? "";
        _onPasswordVerified = onPasswordVerified;

        if (titleText) titleText.text = $"Enter Password: {roomDisplayName}";
        if (passwordInput)
        {
            passwordInput.text = "";
            passwordInput.interactable = true;
        }
        if (errorText) errorText.text = "";

        if (panelRoot) panelRoot.SetActive(true);
        if (passwordInput) passwordInput.ActivateInputField();
    }

    public void Close()
    {
        if (panelRoot) panelRoot.SetActive(false);
    }

    void OnClickConfirm()
    {
        string typed = passwordInput ? passwordInput.text.Trim() : "";

        if (string.IsNullOrEmpty(typed))
        {
            if (errorText) errorText.text = "Please enter password.";
            return;
        }

        if (typed != _expectedPassword)
        {
            if (errorText) errorText.text = "Incorrect password!";
            if (passwordInput)
            {
                passwordInput.text = "";
                passwordInput.ActivateInputField();
            }
            return;
        }

        // รหัสผ่านถูกต้อง -> ปิด Popup แล้วสั่ง Join ห้อง
        var callback = _onPasswordVerified;
        Close();
        callback?.Invoke();
    }
}