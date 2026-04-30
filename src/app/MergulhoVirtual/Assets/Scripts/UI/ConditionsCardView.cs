using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ConditionsCardView : MonoBehaviour
{
    [SerializeField] TMP_Text label;
    [SerializeField] ConditionsService conditions;
    [SerializeField] TideService tides;
    [Tooltip("Pixel column where the value text starts. Increase if a label name overflows.")]
    [SerializeField] int valueColumnPx = 88;

    const float FreshnessRefreshInterval = 60f;
    static readonly string[] CardinalDirections = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    float freshnessTimer;

    void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        if (conditions != null) conditions.ConditionsChanged += OnDataChanged;
        if (tides != null) tides.TideChanged += OnTideChanged;
        Render();
    }

    void OnDisable()
    {
        if (conditions != null) conditions.ConditionsChanged -= OnDataChanged;
        if (tides != null) tides.TideChanged -= OnTideChanged;
    }

    void Update()
    {
        freshnessTimer += Time.unscaledDeltaTime;
        if (freshnessTimer >= FreshnessRefreshInterval)
        {
            freshnessTimer = 0f;
            Render();
        }
    }

    void OnDataChanged(ConditionsSnapshot _) => Render();
    void OnTideChanged(TideSnapshot _) => Render();

    void Render()
    {
        if (label == null) return;

        var snap = conditions != null ? conditions.CurrentConditions : null;
        var tide = tides != null ? tides.CurrentTide : default;

        var sb = new StringBuilder();
        Row(sb, "Onda",  FormatWaveRow(snap));
        Row(sb, "Maré",  FormatTideRow(tide));
        Row(sb, "Lua",   FormatMoonRow());
        Row(sb, "Vento", FormatWindRow(snap));
        Row(sb, "Água",  FormatWaterRow(snap));
        sb.AppendLine();
        sb.Append("<size=85%><alpha=#80>").Append(FormatFreshness(snap)).Append("<alpha=#FF></size>");
        label.text = sb.ToString();

        // TMP doesn't always notify the parent VerticalLayoutGroup of its new
        // preferred height in the same frame the text changes — without this
        // rebuild, siblings below (e.g. TideSparkline) draw on top of the card.
        if (transform.parent is RectTransform parentRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }
    }

    void Row(StringBuilder sb, string name, string value)
    {
        sb.Append("<b>").Append(name).Append("</b>");
        sb.Append("<pos=").Append(valueColumnPx).Append("px>");
        sb.AppendLine(value);
    }

    static string FormatWaveRow(ConditionsSnapshot s)
    {
        if (s == null) return "—";
        var parts = new List<string>();
        if (s.hasWaveHeight)    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0} m", s.waveHeightM));
        if (s.hasWavePeriod)    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0} s", s.wavePeriodS));
        if (s.hasWaveDirection) parts.Add(DegToCardinal(s.waveDirectionDeg));
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    static string FormatTideRow(TideSnapshot t)
    {
        if (!t.valid) return "—";
        if (t.rising && t.nextHighAt != DateTime.MinValue)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "subindo, próxima alta {0:HH:mm} ({1:0.0} m)",
                t.nextHighAt.ToLocalTime(), t.nextHighM);
        }
        if (!t.rising && t.nextLowAt != DateTime.MinValue)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "descendo, próxima baixa {0:HH:mm} ({1:0.0} m)",
                t.nextLowAt.ToLocalTime(), t.nextLowM);
        }
        return t.rising ? "subindo" : "descendo";
    }

    static string FormatMoonRow()
    {
        DateTime now = DateTime.UtcNow;
        var name = MoonPhase.Name(MoonPhase.Phase(now));
        int illumPct = Mathf.RoundToInt(MoonPhase.Illumination(now) * 100f);
        return $"{MoonPhaseLabelPtBr(name)} · {illumPct}% iluminada";
    }

    static string FormatWindRow(ConditionsSnapshot s)
    {
        if (s == null) return "—";
        var parts = new List<string>();
        if (s.hasWindSpeed)     parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0} km/h", s.windSpeedKmh));
        if (s.hasWindDirection) parts.Add(DegToCardinal(s.windDirectionDeg));
        return parts.Count == 0 ? "—" : string.Join(" ", parts);
    }

    static string FormatWaterRow(ConditionsSnapshot s)
    {
        if (s == null || !s.hasSeaTemp) return "—";
        return string.Format(CultureInfo.InvariantCulture, "{0:0} °C", s.seaTempC);
    }

    static string FormatFreshness(ConditionsSnapshot s)
    {
        if (s == null || s.fetchedAtTicksUtc <= 0) return "Atualizado: —";
        TimeSpan age = DateTime.UtcNow - s.FetchedAtUtc;
        if (age.TotalSeconds < 60) return "Atualizado: agora";
        if (age.TotalMinutes < 60) return $"Atualizado: há {(int)age.TotalMinutes}m";
        if (age.TotalHours < 24)   return $"Atualizado: há {(int)age.TotalHours}h";
        return $"Atualizado: há {(int)age.TotalDays}d";
    }

    static string DegToCardinal(float deg)
    {
        deg = ((deg % 360f) + 360f) % 360f;
        int idx = Mathf.RoundToInt(deg / 45f) % 8;
        return CardinalDirections[idx];
    }

    static string MoonPhaseLabelPtBr(MoonPhaseName n)
    {
        switch (n)
        {
            case MoonPhaseName.New:            return "Nova";
            case MoonPhaseName.WaxingCrescent: return "Crescente";
            case MoonPhaseName.FirstQuarter:   return "Quarto Crescente";
            case MoonPhaseName.WaxingGibbous:  return "Gibosa Crescente";
            case MoonPhaseName.Full:           return "Cheia";
            case MoonPhaseName.WaningGibbous:  return "Gibosa Minguante";
            case MoonPhaseName.LastQuarter:    return "Quarto Minguante";
            case MoonPhaseName.WaningCrescent: return "Minguante";
            default: return "—";
        }
    }
}
