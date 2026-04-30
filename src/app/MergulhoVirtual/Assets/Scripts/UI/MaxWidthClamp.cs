using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class MaxWidthClamp : MonoBehaviour
{
    [SerializeField] private float maxWidth = 720f;
    [SerializeField] private float horizontalMargin = 80f;

    private RectTransform rt;

    private void OnEnable()
    {
        rt = (RectTransform)transform;
        Apply();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled) return;
        Apply();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        Apply();
    }

    private void Apply()
    {
        if (rt == null) rt = (RectTransform)transform;
        var parentRt = rt.parent as RectTransform;
        if (parentRt == null) return;

        float parentWidth = parentRt.rect.width;
        float targetWidth = Mathf.Min(parentWidth - horizontalMargin, maxWidth);

        var anchorMin = rt.anchorMin;
        var anchorMax = rt.anchorMax;
        if (anchorMin.x != 0.5f || anchorMax.x != 0.5f)
        {
            rt.anchorMin = new Vector2(0.5f, anchorMin.y);
            rt.anchorMax = new Vector2(0.5f, anchorMax.y);
        }

        var pivot = rt.pivot;
        if (pivot.x != 0.5f)
        {
            rt.pivot = new Vector2(0.5f, pivot.y);
        }

        var size = rt.sizeDelta;
        if (!Mathf.Approximately(size.x, targetWidth))
        {
            size.x = targetWidth;
            rt.sizeDelta = size;
        }
    }
}
