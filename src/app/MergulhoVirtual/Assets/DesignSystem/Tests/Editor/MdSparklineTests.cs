using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdSparklineTests
    {
        static List<Label> VisibleLabels(MdSparkline sparkline) =>
            sparkline.Query<Label>(className: MdSparkline.LabelClassName).ToList()
                .Where(l => l.style.display.value != DisplayStyle.None).ToList();

        [Test]
        public void Hierarchy_HasPlotLayersAndLabelStrip()
        {
            var sparkline = new MdSparkline();
            Assert.That(sparkline.ClassListContains(MdSparkline.UssClassName));
            Assert.That(sparkline.pickingMode, Is.EqualTo(PickingMode.Ignore), "non-interactive");

            var plot = sparkline.Q<VisualElement>(className: MdSparkline.PlotClassName);
            Assert.That(plot, Is.Not.Null);
            foreach (var layerClass in new[]
            {
                MdSparkline.FillClassName, MdSparkline.BaselineClassName, MdSparkline.LineClassName,
                MdSparkline.HighDotsClassName, MdSparkline.LowDotsClassName,
            })
            {
                var layer = plot.Q<VisualElement>(className: layerClass);
                Assert.That(layer, Is.Not.Null, layerClass);
                Assert.That(layer.pickingMode, Is.EqualTo(PickingMode.Ignore), layerClass);
            }
            // Paint order = child order: fill under the line.
            var fill = plot.Q<VisualElement>(className: MdSparkline.FillClassName);
            var line = plot.Q<VisualElement>(className: MdSparkline.LineClassName);
            Assert.That(plot.IndexOf(fill), Is.LessThan(plot.IndexOf(line)));

            Assert.That(sparkline.Q<VisualElement>(className: MdSparkline.LabelStripClassName), Is.Not.Null);
        }

        [Test]
        public void FindExtremes_FindsInteriorPeaksAndValleys()
        {
            var highs = new List<int>();
            var lows = new List<int>();

            MdSparkline.FindExtremes(new[] { 1f, 3f, 1f, 0f, 4f, 1f }, highs, lows);
            Assert.That(highs, Is.EqualTo(new[] { 1, 4 }));
            Assert.That(lows, Is.EqualTo(new[] { 3 }));

            // Endpoints are never extrema; monotonic data yields none.
            MdSparkline.FindExtremes(new[] { 0f, 1f, 2f, 3f }, highs, lows);
            Assert.That(highs, Is.Empty);
            Assert.That(lows, Is.Empty);

            // Flat plateau marks both ends (same rule as the uGUI TideSparkline).
            MdSparkline.FindExtremes(new[] { 1f, 2f, 2f, 1f }, highs, lows);
            Assert.That(highs, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void SetSamples_WithFormatter_CreatesLabelsAtExtremes()
        {
            var sparkline = new MdSparkline
            {
                ExtremumLabelFormatter = (i, isHigh) => (isHigh ? "▲" : "▼") + i,
            };
            sparkline.SetSamples(new[] { 1f, 3f, 1f, 0f, 4f, 1f });

            var labels = VisibleLabels(sparkline);
            Assert.That(labels.Select(l => l.text), Is.EquivalentTo(new[] { "▲1", "▲4", "▼3" }));
            // Each label is positioned at its extremum's fraction of the width.
            // Unattached, no width resolves, so this is the pre-clamp fallback;
            // ClampLabelCenter_* below pins what happens once one does.
            var high1 = labels.First(l => l.text == "▲1");
            Assert.That(high1.style.left.value.value, Is.EqualTo(100f * 1 / 5).Within(0.01f));
            Assert.That(high1.style.left.value.unit, Is.EqualTo(LengthUnit.Percent));

            // Monotonic data → all pooled labels hidden.
            sparkline.SetSamples(new[] { 0f, 1f, 2f, 3f });
            Assert.That(VisibleLabels(sparkline), Is.Empty);

            // Null formatter → no labels even with extrema present.
            sparkline.SetSamples(new[] { 1f, 3f, 1f });
            sparkline.ExtremumLabelFormatter = null;
            Assert.That(VisibleLabels(sparkline), Is.Empty);
        }

        /// <summary>
        /// The label strip sets no `overflow: hidden`, and percent + a -50%
        /// translate clamps nothing — so without this an extremum at or near an
        /// end hangs half a label outside the card. Pins the arithmetic; the
        /// resolved widths it runs against only exist under a real panel.
        /// </summary>
        [Test]
        public void ClampLabelCenter_KeepsTheWholeLabelInsideTheStrip()
        {
            const float strip = 300f;
            const float label = 40f;

            // An interior extremum is untouched.
            Assert.That(MdSparkline.ClampLabelCenter(0.5f, strip, label), Is.EqualTo(150f).Within(0.001f));

            // Both ends pull in by half a label instead of hanging over the edge.
            Assert.That(MdSparkline.ClampLabelCenter(0f, strip, label), Is.EqualTo(20f).Within(0.001f));
            Assert.That(MdSparkline.ClampLabelCenter(1f, strip, label), Is.EqualTo(280f).Within(0.001f));

            // …and so does one merely NEAR an end — the Home card's last low tide,
            // which measured 3.5dp past the card's padding box.
            Assert.That(MdSparkline.ClampLabelCenter(0.99f, strip, label), Is.EqualTo(280f).Within(0.001f));

            // A label wider than the strip cannot satisfy both edges: centre it,
            // so it spills equally rather than snapping hard left.
            Assert.That(MdSparkline.ClampLabelCenter(0f, 30f, 60f), Is.EqualTo(15f).Within(0.001f));
        }

        [Test]
        public void Baseline_DefaultsHiddenAndRoundTrips()
        {
            var sparkline = new MdSparkline();
            Assert.That(float.IsNaN(sparkline.Baseline), "hidden (NaN) by default");

            sparkline.Baseline = 1.28f;
            Assert.That(sparkline.Baseline, Is.EqualTo(1.28f));
        }

        [Test]
        public void SetSamples_NullOrShort_ClearsChart()
        {
            var sparkline = new MdSparkline
            {
                ExtremumLabelFormatter = (i, isHigh) => "x",
            };
            sparkline.SetSamples(new[] { 1f, 3f, 1f });
            Assert.That(VisibleLabels(sparkline), Is.Not.Empty);

            sparkline.SetSamples(null);
            Assert.That(sparkline.Samples, Is.Empty);
            Assert.That(VisibleLabels(sparkline), Is.Empty);

            sparkline.SetSamples(new[] { 1f });
            Assert.That(sparkline.Samples, Is.Empty, "fewer than 2 samples clears");
        }
    }
}
