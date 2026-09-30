using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// Sparkline chart (the tide curve). Not an M3 component — custom
    /// Painter2D drawing, but strictly token-colored: each visual concern
    /// (fill, line, baseline, high/low extremum dots) is its own internal
    /// layer element drawn with its USS-resolved `color`, the same pattern as
    /// MdCircularProgress.Arc. Data-driven from code: <see cref="SetSamples"/>
    /// takes evenly-spaced values (e.g. hourly tide heights); the curve is
    /// Catmull-Rom smoothed for rendering. Optional dashed <see cref="Baseline"/>
    /// reference (drawn only when it falls inside the padded sample range) and
    /// per-extremum labels along the bottom strip via
    /// <see cref="ExtremumLabelFormatter"/>. Non-interactive (picking ignored).
    /// </summary>
    public sealed class MdSparkline : VisualElement
    {
        public const string UssClassName = "md-sparkline";
        public const string PlotClassName = "md-sparkline__plot";
        public const string FillClassName = "md-sparkline__fill";
        public const string LineClassName = "md-sparkline__line";
        public const string BaselineClassName = "md-sparkline__baseline";
        public const string HighDotsClassName = "md-sparkline__high-dots";
        public const string LowDotsClassName = "md-sparkline__low-dots";
        public const string LabelStripClassName = "md-sparkline__labels";
        public const string LabelClassName = "md-sparkline__label";

        const float VerticalPaddingFraction = 0.18f;
        const int MinCurveSamples = 128;
        const float LineWidth = 2f;
        const float BaselineWidth = 1f;
        const float DashLength = 4f;
        const float DashGap = 4f;
        const float MarkerRadius = 3.5f;

        readonly VisualElement _plot;
        readonly VisualElement _labelStrip;
        readonly Layer[] _layers;
        readonly List<Label> _labelPool = new();
        // Each pooled label's x as a fraction of the strip, parallel to _labelPool.
        // Kept because the clamp below needs re-applying on every layout change.
        readonly List<float> _labelFractions = new();
        readonly List<int> _highs = new();
        readonly List<int> _lows = new();

        float[] _samples = Array.Empty<float>();
        float _baseline = float.NaN;
        Func<int, bool, string> _labelFormatter;

        /// <summary>Horizontal dashed reference in sample units (e.g. the
        /// published Nível Médio, 1.28 m LAT). NaN (the default) hides it; it
        /// also stays hidden while outside the padded sample range.</summary>
        public float Baseline
        {
            get => _baseline;
            set
            {
                _baseline = value;
                MarkLayersDirty();
            }
        }

        /// <summary>Optional label per extremum along the bottom strip:
        /// (sampleIndex, isHigh) → text; null/empty text skips that extremum.
        /// Null formatter (the default) = no labels.</summary>
        public Func<int, bool, string> ExtremumLabelFormatter
        {
            get => _labelFormatter;
            set
            {
                _labelFormatter = value;
                RebuildLabels();
            }
        }

        /// <summary>The current samples — exposed for tests.</summary>
        public IReadOnlyList<float> Samples => _samples;

        public MdSparkline()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;

            _plot = new VisualElement { name = "plot", pickingMode = PickingMode.Ignore };
            _plot.AddToClassList(PlotClassName);
            Add(_plot);

            // Paint order = child order: fill under baseline under line under dots.
            _layers = new[]
            {
                MakeLayer("fill", FillClassName, LayerKind.Fill),
                MakeLayer("baseline", BaselineClassName, LayerKind.Baseline),
                MakeLayer("line", LineClassName, LayerKind.Line),
                MakeLayer("high-dots", HighDotsClassName, LayerKind.HighDots),
                MakeLayer("low-dots", LowDotsClassName, LayerKind.LowDots),
            };

            _labelStrip = new VisualElement { name = "labels", pickingMode = PickingMode.Ignore };
            _labelStrip.AddToClassList(LabelStripClassName);
            // The clamp is a function of the strip's width, so it has to re-run
            // whenever the strip is resized (rotation, a different card width).
            _labelStrip.RegisterCallback<GeometryChangedEvent>(_ => PositionAllLabels());
            Add(_labelStrip);
        }

        Layer MakeLayer(string name, string className, LayerKind kind)
        {
            var layer = new Layer(this, kind) { name = name };
            layer.AddToClassList(className);
            MdOverlay.FillParent(layer);
            _plot.Add(layer);
            return layer;
        }

        /// <summary>Set the evenly-spaced samples (null or &lt;2 clears the
        /// chart). Recomputes extrema and labels, repaints all layers.</summary>
        public void SetSamples(IReadOnlyList<float> samples)
        {
            if (samples == null || samples.Count < 2)
            {
                _samples = Array.Empty<float>();
            }
            else
            {
                _samples = new float[samples.Count];
                for (int i = 0; i < samples.Count; i++)
                    _samples[i] = samples[i];
            }
            FindExtremes(_samples, _highs, _lows);
            MarkLayersDirty();
            RebuildLabels();
        }

        void MarkLayersDirty()
        {
            foreach (var layer in _layers)
                layer.MarkDirtyRepaint();
        }

        /// <summary>Interior local maxima/minima, strict on at least one side —
        /// same rule as the uGUI TideSparkline (flat plateaus mark both ends).</summary>
        internal static void FindExtremes(IReadOnlyList<float> h, List<int> highs, List<int> lows)
        {
            highs.Clear();
            lows.Clear();
            for (int i = 1; i < h.Count - 1; i++)
            {
                bool isHigh = h[i] >= h[i - 1] && h[i] >= h[i + 1] && (h[i] > h[i - 1] || h[i] > h[i + 1]);
                bool isLow = h[i] <= h[i - 1] && h[i] <= h[i + 1] && (h[i] < h[i - 1] || h[i] < h[i + 1]);
                if (isHigh)
                    highs.Add(i);
                else if (isLow)
                    lows.Add(i);
            }
        }

        void RebuildLabels()
        {
            int active = 0;
            if (_labelFormatter != null && _samples.Length >= 2)
            {
                foreach (var (indices, isHigh) in new[] { (_highs, true), (_lows, false) })
                {
                    foreach (int i in indices)
                    {
                        string text = _labelFormatter(i, isHigh);
                        if (string.IsNullOrEmpty(text))
                            continue;
                        int slot = active++;
                        var label = GetOrCreateLabel(slot);
                        label.text = text;
                        label.style.display = DisplayStyle.Flex;
                        _labelFractions[slot] = (float)i / (_samples.Length - 1);
                        PositionLabel(slot);
                    }
                }
            }
            for (int i = active; i < _labelPool.Count; i++)
                _labelPool[i].style.display = DisplayStyle.None;
        }

        Label GetOrCreateLabel(int index)
        {
            while (_labelPool.Count <= index)
            {
                int slot = _labelPool.Count;
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList(LabelClassName);
                // Centered on its extremum's x, then clamped into the strip;
                // `left` is written by PositionLabel.
                label.style.position = Position.Absolute;
                label.style.translate = new Translate(Length.Percent(-50f), 0f);
                // A label's own width only resolves after layout and changes with
                // its text, so it re-clamps itself whenever its box changes. This
                // converges: the second pass computes the same x and writes it back.
                label.RegisterCallback<GeometryChangedEvent>(_ => PositionLabel(slot));
                _labelStrip.Add(label);
                _labelPool.Add(label);
                _labelFractions.Add(0f);
            }
            return _labelPool[index];
        }

        /// <summary>
        /// Where a label's CENTRE may sit along the strip so its whole box stays
        /// inside it, given the strip's width and the label's own.
        /// <para>
        /// Percent positioning plus a -50% translate does not clamp anything: an
        /// extremum near either end hangs half a label past the plot, and nothing
        /// on this path sets `overflow: hidden`, so on the Home card it escaped the
        /// padding box outright. An extremum AT index 0 or n-1 would put half the
        /// label outside the card. (FindExtremes only returns interior indices
        /// today — this does not rely on that staying true.)
        /// </para>
        /// <para>A label wider than the strip cannot satisfy both edges; it is
        /// centred, which spills equally instead of hard left.</para>
        /// </summary>
        internal static float ClampLabelCenter(float fraction, float stripWidth, float labelWidth)
        {
            float half = labelWidth * 0.5f;
            float max = stripWidth - half;
            if (max < half)
                return stripWidth * 0.5f;
            return Mathf.Clamp(fraction * stripWidth, half, max);
        }

        /// <summary>Applies one pooled label's x. Falls back to the unclamped
        /// percent while layout is unresolved (no panel, zero-width strip, hidden
        /// label); the geometry callbacks re-run it once the widths are real.</summary>
        void PositionLabel(int index)
        {
            if (index < 0 || index >= _labelPool.Count)
                return;
            var label = _labelPool[index];
            float fraction = _labelFractions[index];
            float strip = _labelStrip.contentRect.width;
            float width = label.resolvedStyle.width;
            if (float.IsNaN(strip) || strip <= 0f || float.IsNaN(width) || width <= 0f)
            {
                label.style.left = Length.Percent(100f * fraction);
                return;
            }
            label.style.left = ClampLabelCenter(fraction, strip, width);
        }

        void PositionAllLabels()
        {
            for (int i = 0; i < _labelPool.Count; i++)
                PositionLabel(i);
        }

        void ComputeRange(out float min, out float range)
        {
            min = float.MaxValue;
            float max = float.MinValue;
            foreach (float h in _samples)
            {
                if (h < min) min = h;
                if (h > max) max = h;
            }
            float pad = (max - min) * VerticalPaddingFraction + 0.001f;
            min -= pad;
            max += pad;
            range = Mathf.Max(0.001f, max - min);
        }

        Vector2[] BuildCurve(Rect rect, float min, float range)
        {
            int n = _samples.Length;
            int count = Mathf.Max(n, MinCurveSamples);
            var pts = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                float h = SampleCatmullRom(_samples, t * (n - 1));
                float normalized = Mathf.Clamp01((h - min) / range);
                // UI Toolkit y grows downward: min value sits at yMax.
                pts[i] = new Vector2(
                    Mathf.Lerp(rect.xMin, rect.xMax, t),
                    Mathf.Lerp(rect.yMax, rect.yMin, normalized));
            }
            return pts;
        }

        static float SampleCatmullRom(float[] arr, float u)
        {
            int n = arr.Length;
            int i1 = Mathf.Clamp(Mathf.FloorToInt(u), 0, n - 1);
            int i0 = Mathf.Clamp(i1 - 1, 0, n - 1);
            int i2 = Mathf.Clamp(i1 + 1, 0, n - 1);
            int i3 = Mathf.Clamp(i1 + 2, 0, n - 1);
            float t = u - i1;
            float a = -0.5f * arr[i0] + 1.5f * arr[i1] - 1.5f * arr[i2] + 0.5f * arr[i3];
            float b = arr[i0] - 2.5f * arr[i1] + 2f * arr[i2] - 0.5f * arr[i3];
            float c = -0.5f * arr[i0] + 0.5f * arr[i2];
            float d = arr[i1];
            return ((a * t + b) * t + c) * t + d;
        }

        void DrawLayer(MeshGenerationContext ctx, LayerKind kind, Color color, Rect rect)
        {
            if (_samples.Length < 2 || rect.width < 4f || rect.height < 4f)
                return;
            ComputeRange(out float min, out float range);
            var painter = ctx.painter2D;

            switch (kind)
            {
                case LayerKind.Fill:
                {
                    var pts = BuildCurve(rect, min, range);
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(rect.xMin, rect.yMax));
                    foreach (var p in pts)
                        painter.LineTo(p);
                    painter.LineTo(new Vector2(rect.xMax, rect.yMax));
                    painter.ClosePath();
                    painter.Fill();
                    break;
                }
                case LayerKind.Line:
                {
                    var pts = BuildCurve(rect, min, range);
                    painter.strokeColor = color;
                    painter.lineWidth = LineWidth;
                    painter.lineJoin = LineJoin.Round;
                    painter.lineCap = LineCap.Round;
                    painter.BeginPath();
                    painter.MoveTo(pts[0]);
                    for (int i = 1; i < pts.Length; i++)
                        painter.LineTo(pts[i]);
                    painter.Stroke();
                    break;
                }
                case LayerKind.Baseline:
                {
                    if (float.IsNaN(_baseline))
                        return;
                    float normalized = (_baseline - min) / range;
                    if (normalized <= 0f || normalized >= 1f)
                        return;
                    float y = Mathf.Lerp(rect.yMax, rect.yMin, normalized);
                    painter.strokeColor = color;
                    painter.lineWidth = BaselineWidth;
                    painter.BeginPath();
                    for (float x = rect.xMin; x < rect.xMax; x += DashLength + DashGap)
                    {
                        painter.MoveTo(new Vector2(x, y));
                        painter.LineTo(new Vector2(Mathf.Min(x + DashLength, rect.xMax), y));
                    }
                    painter.Stroke();
                    break;
                }
                case LayerKind.HighDots:
                case LayerKind.LowDots:
                {
                    var indices = kind == LayerKind.HighDots ? _highs : _lows;
                    painter.fillColor = color;
                    foreach (int i in indices)
                    {
                        float t = (float)i / (_samples.Length - 1);
                        float normalized = Mathf.Clamp01((_samples[i] - min) / range);
                        var center = new Vector2(
                            Mathf.Lerp(rect.xMin, rect.xMax, t),
                            Mathf.Lerp(rect.yMax, rect.yMin, normalized));
                        painter.BeginPath();
                        painter.Arc(center, MarkerRadius, 0f, 360f);
                        painter.Fill();
                    }
                    break;
                }
            }
        }

        internal enum LayerKind
        {
            Fill,
            Line,
            Baseline,
            HighDots,
            LowDots,
        }

        /// <summary>A drawing layer painted with its own USS-resolved `color`
        /// — the MdCircularProgress.Arc pattern, keeping every color in USS.</summary>
        internal sealed class Layer : VisualElement
        {
            readonly MdSparkline _owner;
            readonly LayerKind _kind;

            public Layer(MdSparkline owner, LayerKind kind)
            {
                _owner = owner;
                _kind = kind;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx =>
                    _owner.DrawLayer(ctx, _kind, resolvedStyle.color, contentRect);
            }
        }
    }
}
