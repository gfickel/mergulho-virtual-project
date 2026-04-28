using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(RectTransform))]
public class AnimalViewerInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [SerializeField] private Transform turntable;
    [SerializeField] private Transform viewerCamera;

    [Header("Rotate")]
    [SerializeField] private float rotateDegreesPerPixel = 0.4f;

    [Header("Zoom")]
    [SerializeField] private float minDistance = 0.5f;
    [SerializeField] private float maxDistance = 6f;
    [SerializeField] private float pinchMetersPerPixel = 0.005f;
    [SerializeField] private float scrollMetersPerNotch = 0.3f;

    private readonly Dictionary<int, Vector2> activePointers = new Dictionary<int, Vector2>();
    private float lastPinchDistance = -1f;

    public void OnPointerDown(PointerEventData e)
    {
        activePointers[e.pointerId] = e.position;
        if (activePointers.Count == 2)
        {
            lastPinchDistance = CurrentPinchDistance();
        }
    }

    public void OnPointerUp(PointerEventData e)
    {
        activePointers.Remove(e.pointerId);
        if (activePointers.Count < 2) lastPinchDistance = -1f;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!activePointers.ContainsKey(e.pointerId)) return;
        activePointers[e.pointerId] = e.position;

        if (activePointers.Count >= 2)
        {
            float distance = CurrentPinchDistance();
            if (lastPinchDistance > 0f)
            {
                float deltaPixels = distance - lastPinchDistance;
                ZoomBy(-deltaPixels * pinchMetersPerPixel);
            }
            lastPinchDistance = distance;
        }
        else if (activePointers.Count == 1)
        {
            RotateBy(e.delta.x);
        }
    }

    void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                ZoomBy(-Mathf.Sign(scroll) * scrollMetersPerNotch);
            }
        }
#endif
    }

    void RotateBy(float pixelsX)
    {
        if (turntable == null) return;
        turntable.Rotate(0f, -pixelsX * rotateDegreesPerPixel, 0f, Space.World);
    }

    void ZoomBy(float meters)
    {
        if (viewerCamera == null || turntable == null) return;
        Vector3 toCam = viewerCamera.position - turntable.position;
        float current = toCam.magnitude;
        if (current < 0.0001f) return;
        float next = Mathf.Clamp(current + meters, minDistance, maxDistance);
        viewerCamera.position = turntable.position + toCam.normalized * next;
        viewerCamera.LookAt(turntable.position);
    }

    float CurrentPinchDistance()
    {
        if (activePointers.Count < 2) return 0f;
        var enumerator = activePointers.GetEnumerator();
        enumerator.MoveNext();
        Vector2 a = enumerator.Current.Value;
        enumerator.MoveNext();
        Vector2 b = enumerator.Current.Value;
        return Vector2.Distance(a, b);
    }
}
