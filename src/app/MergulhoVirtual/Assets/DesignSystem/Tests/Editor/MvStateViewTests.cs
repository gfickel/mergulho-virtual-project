using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MvStateViewTests
    {
        static MdIcon IconOf(MvStateView v) => v.Q<MdIcon>(className: MvStateView.IconClassName);
        static Label TitleOf(MvStateView v) => v.Q<Label>(className: MvStateView.TitleClassName);
        static Label BodyOf(MvStateView v) => v.Q<Label>(className: MvStateView.BodyClassName);
        static VisualElement FooterOf(MvStateView v) => v.Q(className: MvStateView.FooterClassName);
        static MdButton ActionOf(MvStateView v) => v.Q<MdButton>(className: MvStateView.ActionClassName);

        [Test]
        public void Defaults_AreTheErrorVariantWithItsGlyph_AndNoCopy()
        {
            var view = new MvStateView();

            Assert.That(view.Variant, Is.EqualTo(MvStateViewVariant.Error));
            Assert.That(view.ClassListContains(MvStateView.UssClassName));
            Assert.That(view.ClassListContains("mv-state-view--error"));

            // No copy of its own: the pt-BR strings live in the app's formatter.
            Assert.That(view.Title, Is.Empty);
            Assert.That(view.Body, Is.Empty);
            Assert.That(view.ActionText, Is.Empty);

            Assert.That(IconOf(view).text, Is.EqualTo(MdIconGlyphs.Map[MvStateView.DefaultIconNames[0]]));
            Assert.That(IconOf(view).style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void Structure_IsContentThenFooter_WithTheGlyphAboveTheCopy()
        {
            var view = new MvStateView();

            Assert.That(view.childCount, Is.EqualTo(2));
            Assert.That(view[0].ClassListContains(MvStateView.ContentClassName));
            Assert.That(view[1].ClassListContains(MvStateView.FooterClassName));

            var content = view[0];
            Assert.That(content.childCount, Is.EqualTo(4));
            Assert.That(content[0].ClassListContains(MvStateView.IconClassName));
            Assert.That(content[1].ClassListContains(MvStateView.IllustrationClassName));
            Assert.That(content[2].ClassListContains(MvStateView.TitleClassName));
            Assert.That(content[3].ClassListContains(MvStateView.BodyClassName));
        }

        [Test]
        public void Variant_SwapsExactlyOneClass_AndItsDefaultGlyph()
        {
            var view = new MvStateView();

            view.Variant = MvStateViewVariant.Offline;
            Assert.That(view.ClassListContains("mv-state-view--offline"));
            Assert.That(view.ClassListContains("mv-state-view--error"), Is.False);
            Assert.That(IconOf(view).text, Is.EqualTo(MdIconGlyphs.Map[MvStateView.DefaultIconNames[1]]));

            view.Variant = MvStateViewVariant.Empty;
            Assert.That(view.ClassListContains("mv-state-view--empty"));
            Assert.That(view.ClassListContains("mv-state-view--offline"), Is.False);
            Assert.That(IconOf(view).text, Is.EqualTo(MdIconGlyphs.Map[MvStateView.DefaultIconNames[2]]));
        }

        [Test]
        public void EveryVariantsDefaultGlyph_IsInTheShippedSubset()
        {
            foreach (var name in MvStateView.DefaultIconNames)
                Assert.That(MdIconGlyphs.Map.ContainsKey(name), Is.True,
                    $"'{name}' is not in the Material Symbols subset — add it to " +
                    "tools/design_system/material_symbols_icons.txt and rerun make ds-icons.");
        }

        [Test]
        public void AnExplicitIcon_SurvivesALaterVariantChange()
        {
            var view = new MvStateView { Icon = "cloud_upload" };
            Assert.That(IconOf(view).text, Is.EqualTo(MdIconGlyphs.Map["cloud_upload"]));

            view.Variant = MvStateViewVariant.Offline;
            Assert.That(IconOf(view).text, Is.EqualTo(MdIconGlyphs.Map["cloud_upload"]),
                "the caller's glyph must win over the variant default");
        }

        [Test]
        public void EmptyIcon_HidesTheSlot()
        {
            var view = new MvStateView { Icon = "" };
            Assert.That(IconOf(view).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void TitleAndBody_AreHiddenUntilSet_AndNullBecomesEmpty()
        {
            var view = new MvStateView();
            Assert.That(TitleOf(view).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(BodyOf(view).style.display.value, Is.EqualTo(DisplayStyle.None));

            view.Title = "Algo deu errado por aqui";
            view.Body = "Não conseguimos carregar as informações.";
            Assert.That(TitleOf(view).text, Is.EqualTo("Algo deu errado por aqui"));
            Assert.That(BodyOf(view).text, Is.EqualTo("Não conseguimos carregar as informações."));
            Assert.That(TitleOf(view).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(BodyOf(view).style.display.value, Is.EqualTo(DisplayStyle.Flex));

            view.Title = null;
            view.Body = null;
            Assert.That(view.Title, Is.Empty);
            Assert.That(view.Body, Is.Empty);
            Assert.That(TitleOf(view).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(BodyOf(view).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        /// <summary>
        /// A state with no honest retry path must not draw a button that does
        /// nothing, so the empty label hides the whole footer rather than
        /// rendering an unlabelled bar.
        /// </summary>
        [Test]
        public void NoActionText_HidesTheWholeFooter()
        {
            var view = new MvStateView();
            Assert.That(FooterOf(view).style.display.value, Is.EqualTo(DisplayStyle.None));

            view.ActionText = "Tentar novamente";
            Assert.That(FooterOf(view).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(ActionOf(view).Text, Is.EqualTo("Tentar novamente"));

            view.ActionText = "";
            Assert.That(FooterOf(view).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        /// <summary>
        /// The action is a filled MdButton and nothing else is interactive: the
        /// root ignores picking so a full-screen state cannot swallow taps meant
        /// for whatever it is laid over. The click itself is a PlayMode test
        /// (<c>PointerInteractionTests.MvStateView_ActionClick_RaisesActionInvoked</c>) —
        /// Clickable needs a real panel.
        /// </summary>
        [Test]
        public void TheActionIsAFilledButton_AndNothingElseIsPickable()
        {
            var view = new MvStateView { ActionText = "Tentar novamente" };

            Assert.That(ActionOf(view).Variant, Is.EqualTo(MdButtonVariant.Filled));
            Assert.That(view.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(view.Q(className: MvStateView.ContentClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
            Assert.That(TitleOf(view).pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(BodyOf(view).pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        /// <summary>
        /// A retry that is in flight disables its button rather than removing it —
        /// a button that vanishes reads as the state having changed.
        /// </summary>
        [Test]
        public void ActionEnabled_TogglesTheButtonWithoutHidingIt()
        {
            var view = new MvStateView { ActionText = "Tentar novamente" };
            Assert.That(view.ActionEnabled, Is.True);

            view.ActionEnabled = false;
            Assert.That(view.ActionEnabled, Is.False);
            Assert.That(ActionOf(view).enabledSelf, Is.False);
            Assert.That(FooterOf(view).style.display.value, Is.EqualTo(DisplayStyle.Flex),
                "disabled, not gone");

            view.ActionEnabled = true;
            Assert.That(ActionOf(view).enabledSelf, Is.True);
        }

        [Test]
        public void Illustration_IsHiddenUntilOneIsSet_AndClearingHidesItAgain()
        {
            var view = new MvStateView();
            var slot = view.Q<Image>(className: MvStateView.IllustrationClassName);
            Assert.That(slot.style.display.value, Is.EqualTo(DisplayStyle.None));

            var texture = new UnityEngine.Texture2D(4, 4);
            try
            {
                view.SetIllustration(texture);
                Assert.That(slot.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(slot.image, Is.SameAs(texture));

                view.SetIllustration((UnityEngine.Texture2D)null);
                Assert.That(slot.style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
