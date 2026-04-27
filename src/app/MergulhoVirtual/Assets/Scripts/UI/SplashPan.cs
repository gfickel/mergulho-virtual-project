using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class SplashPan : MonoBehaviour
{
    [SerializeField] private float duration = 2f;

    private RectTransform rt;
    private Image image;
    private float startX;
    private float endX;
    private float elapsed;

    void Awake()
    {
        rt = (RectTransform)transform;
        image = GetComponent<Image>();
    }

    void OnEnable()
    {
        Canvas.ForceUpdateCanvases();

        var parent = rt.parent as RectTransform;
        float viewportWidth = parent != null ? parent.rect.width : rt.rect.width;

        // Use the drawn sprite width, not the RectTransform width — they only match when
        // the rect's aspect equals the sprite's aspect. Derive from sprite to be robust.
        float drawnWidth = rt.rect.width;
        if (image != null && image.sprite != null)
        {
            float spriteAspect = image.sprite.rect.width / image.sprite.rect.height;
            float aspectDrivenWidth = rt.rect.height * spriteAspect;
            drawnWidth = image.preserveAspect
                ? Mathf.Min(rt.rect.width, aspectDrivenWidth)
                : rt.rect.width;
        }

        float pan = Mathf.Max(0f, (drawnWidth - viewportWidth) * 0.5f);

        startX = +pan*.9f;
        endX = -pan*.9f;
        elapsed = 0f;
        rt.anchoredPosition = new Vector2(startX, rt.anchoredPosition.y);
    }

    void Update()
    {
        if (duration <= 0f) return;
        elapsed += Time.deltaTime;
        float u = Mathf.Clamp01(elapsed / duration);
        float x = Mathf.Lerp(startX, endX, u);
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    }
}
