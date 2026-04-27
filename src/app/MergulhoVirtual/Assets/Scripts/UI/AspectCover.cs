using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Image))]
[RequireComponent(typeof(AspectRatioFitter))]
public class AspectCover : MonoBehaviour
{
    void OnEnable() { Sync(); }

    void Sync()
    {
        var image = GetComponent<Image>();
        var fitter = GetComponent<AspectRatioFitter>();
        if (image.sprite == null) return;
        var r = image.sprite.rect;
        if (r.height <= 0f) return;
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = r.width / r.height;
    }

#if UNITY_EDITOR
    void OnValidate() { Sync(); }
#endif
}
