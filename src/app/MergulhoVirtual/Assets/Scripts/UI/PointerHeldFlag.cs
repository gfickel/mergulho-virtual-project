using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Exposes whether a pointer is currently pressed on this UI element. Sits
/// alongside a Slider so a driver can pause programmatic value updates while
/// the user is dragging (otherwise per-frame updates fight the drag). Multiple
/// handlers on one GameObject all receive the event, so this coexists with the
/// Slider's own drag handling.
/// </summary>
public class PointerHeldFlag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool Held { get; private set; }

    public void OnPointerDown(PointerEventData eventData) => Held = true;
    public void OnPointerUp(PointerEventData eventData) => Held = false;

    void OnDisable() => Held = false;
}
