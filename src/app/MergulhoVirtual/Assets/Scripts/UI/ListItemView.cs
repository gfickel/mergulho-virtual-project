using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ListItemView : MonoBehaviour
{
    [SerializeField] private Image thumbnailImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private Button button;

    private Action onClick;

    void Awake()
    {
        if (button != null) button.onClick.AddListener(HandleClick);
    }

    void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    public void Bind(Sprite thumbnail, string title, string subtitle, Action clickHandler)
    {
        if (thumbnailImage != null)
        {
            thumbnailImage.sprite = thumbnail;
            thumbnailImage.enabled = thumbnail != null;
            var cover = thumbnailImage.GetComponent<AspectCover>();
            if (cover != null) cover.Refresh();
        }
        if (titleText != null) titleText.text = title;
        if (subtitleText != null)
        {
            subtitleText.text = subtitle;
            subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
        }
        onClick = clickHandler;
    }

    void HandleClick()
    {
        onClick?.Invoke();
    }
}
