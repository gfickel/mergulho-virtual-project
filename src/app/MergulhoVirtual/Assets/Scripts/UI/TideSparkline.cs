using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class TideSparkline : MaskableGraphic
{
    [SerializeField] TideService tides;

    [Header("Curve")]
    [SerializeField, Range(0.05f, 0.4f)] float verticalPadding = 0.18f;
    [SerializeField, Min(8)] int curveSamples = 192;

    [Header("Colors")]
    [SerializeField] Color fillTopColor    = new Color(0.55f, 0.85f, 1.00f, 0.85f);
    [SerializeField] Color fillBottomColor = new Color(0.05f, 0.20f, 0.55f, 0.05f);
    [SerializeField] Color lineColor       = new Color(0.95f, 0.98f, 1.00f, 1.00f);
    [SerializeField] Color glowColor       = new Color(0.55f, 0.85f, 1.00f, 0.40f);
    [SerializeField] Color cursorColor     = new Color(1.00f, 1.00f, 1.00f, 0.85f);
    [SerializeField] Color tickColor       = new Color(1.00f, 1.00f, 1.00f, 0.10f);
    [SerializeField] Color baselineColor   = new Color(1.00f, 1.00f, 1.00f, 0.18f);
    [SerializeField] Color highMarkerColor = new Color(1.00f, 0.85f, 0.40f, 1.00f);
    [SerializeField] Color lowMarkerColor  = new Color(0.50f, 0.85f, 1.00f, 1.00f);

    [Header("Strokes")]
    [SerializeField] float lineThickness   = 2f;
    [SerializeField] float glowThickness   = 8f;
    [SerializeField] float cursorThickness = 2f;
    [SerializeField] float markerRadius    = 3.5f;
    [SerializeField] float aaFringePx      = 1.2f;

    [Header("Labels")]
    [SerializeField] float bottomStripPx   = 16f;
    [SerializeField] float labelFontSize   = 10f;

    TideSnapshot snap;
    bool hasData;
    readonly List<TMP_Text> labelPool = new();

    protected override void OnEnable()
    {
        base.OnEnable();
        if (tides == null) return;
        tides.TideChanged += OnTideChanged;
        if (tides.CurrentTide.valid) OnTideChanged(tides.CurrentTide);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (tides != null) tides.TideChanged -= OnTideChanged;
    }

    void OnTideChanged(TideSnapshot t)
    {
        snap = t;
        hasData = t.valid && t.next24hHeights != null && t.next24hHeights.Length >= 2;
        SetVerticesDirty();
        UpdateExtremumLabels();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!hasData) return;

        Rect r = rectTransform.rect;
        float left = r.xMin;
        float right = r.xMax;
        float bottom = r.yMin + bottomStripPx;
        float top = r.yMax;
        if (top - bottom < 4f || right - left < 4f) return;

        var heights = snap.next24hHeights;
        int N = heights.Length;
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < N; i++)
        {
            if (heights[i] < min) min = heights[i];
            if (heights[i] > max) max = heights[i];
        }
        float pad = (max - min) * verticalPadding + 0.001f;
        min -= pad; max += pad;
        float range = Mathf.Max(0.001f, max - min);

        int S = Mathf.Max(N, curveSamples);
        var pts = new Vector2[S];
        for (int i = 0; i < S; i++)
        {
            float t = (float)i / (S - 1);
            float u = t * (N - 1);
            float h = SampleCatmullRom(heights, u);
            float n = Mathf.Clamp01((h - min) / range);
            pts[i] = new Vector2(Mathf.Lerp(left, right, t), Mathf.Lerp(bottom, top, n));
        }

        for (int hr = 6; hr <= 18; hr += 6)
        {
            float tx = Mathf.Lerp(left, right, hr / (float)(N - 1));
            DrawRect(vh, tx - 0.5f, bottom, tx + 0.5f, top, tickColor);
        }

        float mslN = (0f - min) / range;
        if (mslN > 0f && mslN < 1f)
        {
            float y = Mathf.Lerp(bottom, top, mslN);
            DrawDashedHorizontal(vh, left, right, y, 4f, 4f, 1f, baselineColor);
        }

        for (int i = 0; i < S - 1; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[i + 1];
            Color cA = GradColor(a.y, bottom, top);
            Color cB = GradColor(b.y, bottom, top);
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x, bottom), fillBottomColor, Vector2.zero);
            vh.AddVert(new Vector3(a.x, a.y),    cA,              Vector2.zero);
            vh.AddVert(new Vector3(b.x, b.y),    cB,              Vector2.zero);
            vh.AddVert(new Vector3(b.x, bottom), fillBottomColor, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx, idx + 2, idx + 3);
        }

        DrawLineStrip(vh, pts, glowThickness, glowColor, aaFringePx);
        DrawLineStrip(vh, pts, lineThickness, lineColor, aaFringePx);

        DrawRect(vh, left, bottom, left + cursorThickness, top, cursorColor);
        DrawDot(vh, new Vector2(left + cursorThickness * 0.5f, top), cursorThickness * 1.6f, cursorColor, aaFringePx);

        DrawExtremumDots(vh, heights, min, range, left, right, bottom, top);
    }

    Color GradColor(float y, float bottom, float top)
    {
        float t = Mathf.Clamp01((y - bottom) / Mathf.Max(0.001f, top - bottom));
        return Color.Lerp(fillBottomColor, fillTopColor, t);
    }

    static float SampleCatmullRom(float[] arr, float u)
    {
        int N = arr.Length;
        int i1 = Mathf.Clamp(Mathf.FloorToInt(u), 0, N - 1);
        int i0 = Mathf.Clamp(i1 - 1, 0, N - 1);
        int i2 = Mathf.Clamp(i1 + 1, 0, N - 1);
        int i3 = Mathf.Clamp(i1 + 2, 0, N - 1);
        float t = u - i1;
        float a = -0.5f * arr[i0] + 1.5f * arr[i1] - 1.5f * arr[i2] + 0.5f * arr[i3];
        float b = arr[i0] - 2.5f * arr[i1] + 2f * arr[i2] - 0.5f * arr[i3];
        float c = -0.5f * arr[i0] + 0.5f * arr[i2];
        float d = arr[i1];
        return ((a * t + b) * t + c) * t + d;
    }

    static void DrawLineStrip(VertexHelper vh, Vector2[] pts, float thickness, Color color, float aa)
    {
        if (pts.Length < 2) return;
        float half = thickness * 0.5f;
        float halfAA = half + Mathf.Max(0f, aa);
        Color clear = new Color(color.r, color.g, color.b, 0f);

        var n = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 d;
            if (i == 0)                       d = (pts[1] - pts[0]).normalized;
            else if (i == pts.Length - 1)     d = (pts[i] - pts[i - 1]).normalized;
            else                              d = (pts[i + 1] - pts[i - 1]).normalized;
            n[i] = new Vector2(-d.y, d.x);
        }
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 a = pts[i],  na = n[i];
            Vector2 b = pts[i + 1], nb = n[i + 1];
            int v = vh.currentVertCount;
            vh.AddVert(a + na * halfAA, clear, Vector2.zero); // 0
            vh.AddVert(a + na * half,   color, Vector2.zero); // 1
            vh.AddVert(a - na * half,   color, Vector2.zero); // 2
            vh.AddVert(a - na * halfAA, clear, Vector2.zero); // 3
            vh.AddVert(b + nb * halfAA, clear, Vector2.zero); // 4
            vh.AddVert(b + nb * half,   color, Vector2.zero); // 5
            vh.AddVert(b - nb * half,   color, Vector2.zero); // 6
            vh.AddVert(b - nb * halfAA, clear, Vector2.zero); // 7
            vh.AddTriangle(v + 0, v + 1, v + 5); vh.AddTriangle(v + 0, v + 5, v + 4); // top fringe
            vh.AddTriangle(v + 1, v + 2, v + 6); vh.AddTriangle(v + 1, v + 6, v + 5); // core
            vh.AddTriangle(v + 2, v + 3, v + 7); vh.AddTriangle(v + 2, v + 7, v + 6); // bottom fringe
        }
    }

    static void DrawRect(VertexHelper vh, float x0, float y0, float x1, float y1, Color color)
    {
        int idx = vh.currentVertCount;
        vh.AddVert(new Vector3(x0, y0), color, Vector2.zero);
        vh.AddVert(new Vector3(x0, y1), color, Vector2.zero);
        vh.AddVert(new Vector3(x1, y1), color, Vector2.zero);
        vh.AddVert(new Vector3(x1, y0), color, Vector2.zero);
        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx, idx + 2, idx + 3);
    }

    static void DrawDashedHorizontal(VertexHelper vh, float x0, float x1, float y, float dash, float gap, float thick, Color color)
    {
        float half = thick * 0.5f;
        float x = x0;
        while (x < x1)
        {
            float xe = Mathf.Min(x + dash, x1);
            DrawRect(vh, x, y - half, xe, y + half, color);
            x = xe + gap;
        }
    }

    static void DrawDot(VertexHelper vh, Vector2 c, float radius, Color color, float aa, int sides = 24)
    {
        Color clear = new Color(color.r, color.g, color.b, 0f);
        float outer = radius + Mathf.Max(0f, aa);
        int center = vh.currentVertCount;
        vh.AddVert(c, color, Vector2.zero);
        for (int i = 0; i < sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            vh.AddVert(c + dir * radius, color, Vector2.zero); // inner ring
            vh.AddVert(c + dir * outer,  clear, Vector2.zero); // outer fringe
        }
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            int innerI = center + 1 + i * 2;
            int innerN = center + 1 + nxt * 2;
            int outerI = center + 2 + i * 2;
            int outerN = center + 2 + nxt * 2;
            vh.AddTriangle(center, innerI, innerN);                  // solid disk
            vh.AddTriangle(innerI, outerI, outerN);                  // fringe quad
            vh.AddTriangle(innerI, outerN, innerN);
        }
    }

    void DrawExtremumDots(VertexHelper vh, float[] h, float min, float range, float left, float right, float bottom, float top)
    {
        int N = h.Length;
        for (int i = 1; i < N - 1; i++)
        {
            bool isHigh = h[i] >= h[i - 1] && h[i] >= h[i + 1] && (h[i] > h[i - 1] || h[i] > h[i + 1]);
            bool isLow  = h[i] <= h[i - 1] && h[i] <= h[i + 1] && (h[i] < h[i - 1] || h[i] < h[i + 1]);
            if (!isHigh && !isLow) continue;
            float t = (float)i / (N - 1);
            float n = Mathf.Clamp01((h[i] - min) / range);
            Vector2 p = new Vector2(Mathf.Lerp(left, right, t), Mathf.Lerp(bottom, top, n));
            DrawDot(vh, p, markerRadius * 1.9f, new Color(1f, 1f, 1f, 0.22f), aaFringePx);
            DrawDot(vh, p, markerRadius, isHigh ? highMarkerColor : lowMarkerColor, aaFringePx);
        }
    }

    void UpdateExtremumLabels()
    {
        int active = 0;
        if (hasData)
        {
            var heights = snap.next24hHeights;
            int N = heights.Length;
            for (int i = 1; i < N - 1; i++)
            {
                bool isHigh = heights[i] >= heights[i - 1] && heights[i] >= heights[i + 1] && (heights[i] > heights[i - 1] || heights[i] > heights[i + 1]);
                bool isLow  = heights[i] <= heights[i - 1] && heights[i] <= heights[i + 1] && (heights[i] < heights[i - 1] || heights[i] < heights[i + 1]);
                if (!isHigh && !isLow) continue;

                var label = GetOrCreateLabel(active++);
                label.gameObject.SetActive(true);
                DateTime tLocal = snap.windowStart.AddHours(i).ToLocalTime();
                string arrow = isHigh ? "▲" : "▼";
                Color c = isHigh ? highMarkerColor : lowMarkerColor;
                label.text = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{arrow}</color> {tLocal:HH:mm}";

                float frac = (float)i / (N - 1);
                var rt = (RectTransform)label.transform;
                rt.anchorMin = new Vector2(frac, 0f);
                rt.anchorMax = new Vector2(frac, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(56f, bottomStripPx);
            }
        }
        for (int i = active; i < labelPool.Count; i++)
            labelPool[i].gameObject.SetActive(false);
    }

    TMP_Text GetOrCreateLabel(int idx)
    {
        while (labelPool.Count <= idx)
        {
            var go = new GameObject($"ExtremumLabel_{labelPool.Count}", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = labelFontSize;
            t.color = new Color(1f, 1f, 1f, 0.9f);
            t.alignment = TextAlignmentOptions.Bottom;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.richText = true;
            t.overflowMode = TextOverflowModes.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            labelPool.Add(t);
        }
        return labelPool[idx];
    }
}
