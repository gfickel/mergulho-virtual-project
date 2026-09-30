using System;
using System.Globalization;
using MergulhoVirtual.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ConditionsPillView : MonoBehaviour
{
    [SerializeField] TMP_Text label;
    [SerializeField] ConditionsService conditions;
    [SerializeField] TideService tides;

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

    /// <summary>
    /// Slice 6 note: this used to also drive the uGUI Beaches screen straight to the
    /// tapped beach's detail panel. That screen is gone, and the Praias tab already
    /// shows whatever beach GPS (or the override) resolved to, so the tap is now just
    /// the navigation.
    /// </summary>
    void OnTap() => AppUiHost.NavigateTo(AppRoutes.Praias);
}
