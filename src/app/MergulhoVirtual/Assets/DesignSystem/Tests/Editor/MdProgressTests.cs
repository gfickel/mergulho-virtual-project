using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdProgressTests
    {
        // ---- Linear -------------------------------------------------------

        [Test]
        public void Linear_Hierarchy_HasIndicator()
        {
            var progress = new MdLinearProgress();
            Assert.That(progress.Q<VisualElement>(className: MdLinearProgress.IndicatorClassName), Is.Not.Null);
            Assert.That(progress.pickingMode, Is.EqualTo(PickingMode.Ignore), "not interactive");
        }

        [Test]
        public void Linear_Value_ClampsAndSetsIndicatorWidth()
        {
            var progress = new MdLinearProgress { Value = 0.7f };
            var indicator = progress.Q<VisualElement>(className: MdLinearProgress.IndicatorClassName);
            Assert.That(progress.Value, Is.EqualTo(0.7f).Within(1e-5));
            Assert.That(indicator.style.width.value.unit, Is.EqualTo(LengthUnit.Percent));
            Assert.That(indicator.style.width.value.value, Is.EqualTo(70f).Within(1e-3));

            progress.Value = 1.5f;
            Assert.That(progress.Value, Is.EqualTo(1f));
            Assert.That(indicator.style.width.value.value, Is.EqualTo(100f).Within(1e-3));

            progress.Value = -2f;
            Assert.That(progress.Value, Is.Zero);
            Assert.That(indicator.style.width.value.value, Is.Zero);
        }

        [Test]
        public void Linear_Indeterminate_TogglesClassAndRestoresValueOnExit()
        {
            var progress = new MdLinearProgress { Value = 0.4f };
            Assert.That(progress.ClassListContains(MdLinearProgress.IndeterminateClassName), Is.False);

            progress.Indeterminate = true;
            Assert.That(progress.ClassListContains(MdLinearProgress.IndeterminateClassName));

            progress.Indeterminate = false;
            Assert.That(progress.ClassListContains(MdLinearProgress.IndeterminateClassName), Is.False);
            var indicator = progress.Q<VisualElement>(className: MdLinearProgress.IndicatorClassName);
            Assert.That(indicator.style.width.value.value, Is.EqualTo(40f).Within(1e-3),
                "leaving indeterminate re-applies the determinate value");
        }

        // ---- Circular -----------------------------------------------------

        [Test]
        public void Circular_Hierarchy_HasTrackUnderIndicator()
        {
            var progress = new MdCircularProgress();
            var track = progress.Q<VisualElement>(className: MdCircularProgress.TrackClassName);
            var indicator = progress.Q<VisualElement>(className: MdCircularProgress.IndicatorClassName);
            Assert.That(track, Is.Not.Null);
            Assert.That(indicator, Is.Not.Null);
            Assert.That(progress.IndexOf(track), Is.LessThan(progress.IndexOf(indicator)),
                "track renders under the indicator");
            Assert.That(progress.pickingMode, Is.EqualTo(PickingMode.Ignore), "not interactive");
        }

        [Test]
        public void Circular_Value_ClampsAndSetsSweep()
        {
            var progress = new MdCircularProgress { Value = 0.25f };
            var indicator = (MdCircularProgress.Arc)progress.Q<VisualElement>(
                className: MdCircularProgress.IndicatorClassName);
            Assert.That(indicator.SweepAngle, Is.EqualTo(90f).Within(1e-3));

            progress.Value = 2f;
            Assert.That(progress.Value, Is.EqualTo(1f));
            Assert.That(indicator.SweepAngle, Is.EqualTo(360f).Within(1e-3));

            progress.Value = -1f;
            Assert.That(indicator.SweepAngle, Is.Zero);
        }

        [Test]
        public void Circular_Track_IsFullCircle()
        {
            var progress = new MdCircularProgress();
            var track = (MdCircularProgress.Arc)progress.Q<VisualElement>(
                className: MdCircularProgress.TrackClassName);
            Assert.That(track.SweepAngle, Is.EqualTo(360f));
        }

        [Test]
        public void Circular_Indeterminate_TogglesClassAndRestoresValueOnExit()
        {
            var progress = new MdCircularProgress { Value = 0.5f };
            progress.Indeterminate = true;
            Assert.That(progress.ClassListContains(MdCircularProgress.IndeterminateClassName));

            progress.Indeterminate = false;
            var indicator = (MdCircularProgress.Arc)progress.Q<VisualElement>(
                className: MdCircularProgress.IndicatorClassName);
            Assert.That(indicator.SweepAngle, Is.EqualTo(180f).Within(1e-3),
                "leaving indeterminate re-applies the determinate value");
        }
    }
}
