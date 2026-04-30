using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ConditionsPillView : MonoBehaviour
{
    [SerializeField] TMP_Text label;
    [SerializeField] ConditionsService conditions;
    [SerializeField] TideService tides;
    [SerializeField] GPSHandler gps;
    [SerializeField] ScreenManager screenManager;
    [SerializeField] BeachesScreenController beachesController;

    Button button;

    void Awake()
    {
        button = GetComponent<Button>();
        if (label == null) label = GetComponentInChildren<TMP_Text>();
    }

    void OnEnable()
    {
        if (conditions != null)
        {
            conditions.ConditionsChanged += OnDataChanged;
        }
        if (tides != null)
        {
            tides.TideChanged += OnTideChanged;
        }
        if (button != null) button.onClick.AddListener(OnTap);
        Render();
    }

    void OnDisable()
    {
        if (conditions != null) conditions.ConditionsChanged -= OnDataChanged;
        if (tides != null) tides.TideChanged -= OnTideChanged;
        if (button != null) button.onClick.RemoveListener(OnTap);
    }

    void OnDataChanged(ConditionsSnapshot _) => Render();
    void OnTideChanged(TideSnapshot _) => Render();

    void Render()
    {
        if (label == null) return;
        label.text = $"{FormatWave()}  ·  {FormatMoon()}  ·  {FormatTide()}";
    }

    string FormatWave()
    {
        var snap = conditions != null ? conditions.CurrentConditions : null;
        if (snap == null || !snap.hasWaveHeight) return "—";
        return string.Format(CultureInfo.InvariantCulture, "\U0001F30A {0:0.0} m", snap.waveHeightM);
    }

    string FormatMoon()
    {
        return MoonPhaseGlyph(MoonPhase.Name(MoonPhase.Phase(DateTime.UtcNow)));
    }

    string FormatTide()
    {
        if (tides == null) return "—";
        var t = tides.CurrentTide;
        if (!t.valid) return "—";
        return t.rising ? "↑ subindo" : "↓ descendo";
    }

    static string MoonPhaseGlyph(MoonPhaseName n)
    {
        switch (n)
        {
            case MoonPhaseName.New:            return "\U0001F311";
            case MoonPhaseName.WaxingCrescent: return "\U0001F312";
            case MoonPhaseName.FirstQuarter:   return "\U0001F313";
            case MoonPhaseName.WaxingGibbous:  return "\U0001F314";
            case MoonPhaseName.Full:           return "\U0001F315";
            case MoonPhaseName.WaningGibbous:  return "\U0001F316";
            case MoonPhaseName.LastQuarter:    return "\U0001F317";
            case MoonPhaseName.WaningCrescent: return "\U0001F318";
            default: return "—";
        }
    }

    void OnTap()
    {
        string beach = gps != null ? gps.CurrentPlaceName : null;
        if (!string.IsNullOrEmpty(beach) && beachesController != null)
        {
            beachesController.ShowDetailFor(beach);
        }
        if (screenManager != null) screenManager.ShowBeaches();
    }
}
