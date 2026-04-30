using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class BeachNameView : MonoBehaviour
{
    [SerializeField] GPSHandler gps;

    TMP_Text label;

    void Awake() => label = GetComponent<TMP_Text>();

    void OnEnable()
    {
        if (gps == null) return;
        gps.PlaceChanged += OnPlaceChanged;
        OnPlaceChanged(gps.CurrentPlaceName);
    }

    void OnDisable()
    {
        if (gps == null) return;
        gps.PlaceChanged -= OnPlaceChanged;
    }

    void OnPlaceChanged(string placeName)
    {
        label.text = placeName ?? "";
    }
}
