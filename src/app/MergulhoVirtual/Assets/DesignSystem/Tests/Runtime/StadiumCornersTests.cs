using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    /// <summary>
    /// PlayMode coverage for <see cref="MdShape.KeepStadium"/> — the runtime half
    /// of the corner fix, for pills whose height is content-driven.
    /// <para>
    /// This has to be a PlayMode test: the whole point of the helper is that it
    /// reacts to RESOLVED geometry, which only exists once a real panel has run a
    /// layout pass. The panel here is themeless (no component USS, no fonts), so
    /// every element gets an explicit height — that is fine, and in fact isolates
    /// the helper from the stylesheets.
    /// </para>
    /// </summary>
    public class StadiumCornersTests
    {
        GameObject _host;
        PanelSettings _panelSettings;
        UIDocument _document;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject("shape-test-host");
            _document = _host.AddComponent<UIDocument>();
            _document.panelSettings = _panelSettings;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_panelSettings);
        }

        IEnumerator Mount(VisualElement element, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.width = width;
            element.style.height = height;
            _document.rootVisualElement.Add(element);
            yield return null; // attach
            yield return null; // layout + GeometryChangedEvent reaction
        }

        static void AssertStadium(VisualElement element, float expectedHeight)
        {
            float expected = expectedHeight / 2f;
            var resolved = element.resolvedStyle;
            Assert.That(resolved.height, Is.EqualTo(expectedHeight).Within(0.01f), "height");
            Assert.That(resolved.borderTopLeftRadius, Is.EqualTo(expected).Within(0.01f), "top-left");
            Assert.That(resolved.borderTopRightRadius, Is.EqualTo(expected).Within(0.01f), "top-right");
            Assert.That(resolved.borderBottomLeftRadius, Is.EqualTo(expected).Within(0.01f), "bottom-left");
            Assert.That(resolved.borderBottomRightRadius, Is.EqualTo(expected).Within(0.01f), "bottom-right");
        }

        [UnityTest]
        public IEnumerator KeepStadium_SetsRadiusToHalfTheResolvedHeight()
        {
            var element = new VisualElement();
            MdShape.KeepStadium(element);
            yield return Mount(element, 280f, 42f);

            // 42dp tall and 280dp wide: the shape the beach selector pill wants.
            // corner-full's 1000px would have clamped to rx=140, ry=21 — a lens.
            AssertStadium(element, 42f);
        }

        [UnityTest]
        public IEnumerator KeepStadium_FollowsAHeightChange()
        {
            var element = new VisualElement();
            MdShape.KeepStadium(element);
            yield return Mount(element, 280f, 42f);
            AssertStadium(element, 42f);

            // This is the case the USS literal cannot serve: a pill that grew
            // (wrapped label, larger OS font scale) must re-round to the new half.
            element.style.height = 70f;
            yield return null;
            yield return null;
            AssertStadium(element, 70f);
        }

        [UnityTest]
        public IEnumerator KeepStadium_OnASquare_ProducesACircle()
        {
            var element = new VisualElement();
            MdShape.KeepStadium(element);
            yield return Mount(element, 40f, 40f);

            // Half of a square's height is also half its width, so the helper is
            // a superset of the circle case and cannot regress an icon button.
            AssertStadium(element, 40f);
        }

        [UnityTest]
        public IEnumerator MvTag_IsAStadium_NotALens()
        {
            var tag = new MvTag { Text = "Ambiente recifal", Variant = MvTagVariant.Success };
            yield return Mount(tag, 160f, 24f);

            AssertStadium(tag, 24f);
        }

        [UnityTest]
        public IEnumerator MvTag_StaysAStadium_WhenItsContentGrows()
        {
            var tag = new MvTag { Text = "Pendente", Size = MvTagSize.Small };
            yield return Mount(tag, 90f, 20f);
            AssertStadium(tag, 20f);

            // A tag's real height is padding + the label's line box; simulate the
            // size rung changing it.
            tag.style.height = 28f;
            yield return null;
            yield return null;
            AssertStadium(tag, 28f);
        }
    }
}
