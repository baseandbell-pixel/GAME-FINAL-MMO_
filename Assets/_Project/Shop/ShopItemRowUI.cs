using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopItemRowUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text actionButtonText;

    public void Setup(string itemName, Color nameColor, string priceLabel, Sprite iconSprite,
                      string buttonLabel, Color buttonColor, Action onClickAction)
    {
        if (nameText != null)
        {
            nameText.text = itemName;
            nameText.color = nameColor;
        }

        if (priceText != null) priceText.text = priceLabel;

        if (iconImage != null)
        {
            iconImage.sprite = iconSprite;
            iconImage.color = Color.white;
            iconImage.enabled = iconSprite != null;
        }

        if (actionButtonText != null) actionButtonText.text = buttonLabel;

        if (actionButton != null)
        {
            if (actionButton.image != null) actionButton.image.color = buttonColor;
            actionButton.onClick.RemoveAllListeners();
            actionButton.onClick.AddListener(() => onClickAction?.Invoke());
        }
    }
}