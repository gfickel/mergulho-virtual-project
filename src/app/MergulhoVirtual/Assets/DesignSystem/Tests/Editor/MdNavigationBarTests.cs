using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdNavigationBarTests
    {
        /// <summary>
        /// The V2 bottom bar (DESIGN_IMPLEMENTATION.md §4). "help" on Avistamentos is the
        /// placeholder for the custom shark fin that is blocked on the designer (D7).
        /// </summary>
        static readonly MdNavDestination[] Destinations =
        {
            new MdNavDestination("home", "Início"),
            new MdNavDestination("explore", "Mergulho"),
            new MdNavDestination("location_on", "Praias"),
            new MdNavDestination("help", "Avistamentos"),
        };

        static MdNavigationBar Bar()
        {
            var bar = new MdNavigationBar();
            bar.SetDestinations(Destinations);
            return bar;
        }

        [Test]
        public void SetDestinations_BuildsOneItemPerDestinationWithIconAndLabel()
        {
            var bar = Bar();
            Assert.That(bar.DestinationCount, Is.EqualTo(4));
            var items = bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList();
            Assert.That(items.Count, Is.EqualTo(4));

            var first = items[0];
            var indicator = first.Q<VisualElement>(className: MdNavigationBar.IndicatorClassName);
            Assert.That(indicator, Is.Not.Null);
            Assert.That(indicator.Q<VisualElement>(className: "md-state-layer"), Is.Not.Null);
            Assert.That(indicator.Q<MdIcon>(className: MdNavigationBar.IconClassName).Icon,
                Is.EqualTo("home"));
            Assert.That(first.Q<Label>(className: MdNavigationBar.LabelClassName).text,
                Is.EqualTo("Início"));
        }

        [Test]
        public void SelectedIndex_MovesActiveClassAndRaises()
        {
            var bar = Bar();
            Assert.That(bar.SelectedIndex, Is.EqualTo(-1));

            int lastIndex = -2;
            bar.SelectionChanged += i => lastIndex = i;
            bar.SelectedIndex = 1;
            Assert.That(lastIndex, Is.EqualTo(1));

            var items = bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList();
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName));

            bar.SelectedIndex = 3;
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName), Is.False);
            Assert.That(items[3].ClassListContains(MdNavigationBar.ItemActiveClassName));
        }

        [Test]
        public void SelectedIndex_SameValueDoesNotRaise()
        {
            var bar = Bar();
            bar.SelectedIndex = 2;
            int raised = 0;
            bar.SelectionChanged += _ => raised++;
            bar.SelectedIndex = 2;
            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void SelectedIndex_OutOfRangeClearsToMinusOne()
        {
            var bar = Bar();
            bar.SelectedIndex = 2;
            bar.SelectedIndex = 99;
            Assert.That(bar.SelectedIndex, Is.EqualTo(-1));
            Assert.That(bar.Query<VisualElement>(className: MdNavigationBar.ItemActiveClassName).ToList(),
                Is.Empty);
        }

        [Test]
        public void SetDestinations_KeepsSelectionInRangeClearsWhenShrunk()
        {
            var bar = Bar();
            bar.SelectedIndex = 1;

            bar.SetDestinations(new List<MdNavDestination>(Destinations));
            Assert.That(bar.SelectedIndex, Is.EqualTo(1), "same-size rebuild keeps selection");
            var items = bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList();
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName));

            bar.SetDestinations(new[] { Destinations[0] });
            Assert.That(bar.SelectedIndex, Is.EqualTo(-1), "stale selection cleared");

            bar.SetDestinations(null);
            Assert.That(bar.DestinationCount, Is.Zero);
        }

        [Test]
        public void Items_AreFocusableTouchTargetsWithInertVisuals()
        {
            var bar = Bar();
            var item = bar.Q<VisualElement>(className: MdNavigationBar.ItemClassName);
            Assert.That(item.focusable, Is.True);
            Assert.That(item.pickingMode, Is.EqualTo(PickingMode.Position));
            Assert.That(item.Q<VisualElement>(className: MdNavigationBar.IndicatorClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
            Assert.That(item.Q<Label>(className: MdNavigationBar.LabelClassName).pickingMode,
                Is.EqualTo(PickingMode.Ignore));
        }

        // ---- V2 restyle (DESIGN_IMPLEMENTATION.md §4 "Bar spec") -------------------
        // The restyle is USS-only, so the structural guarantees the USS relies on are
        // asserted here and the declarations themselves are asserted from the file
        // (below). Neither needs a panel, both fail loudly if someone reshapes the tree
        // or swaps a token for a literal.

        [Test]
        public void Item_HasIndicatorThenLabel_WithIconInsideTheIndicator()
        {
            // The USS styles the pill as `.md-nav-bar__indicator` and the caption as a
            // following SIBLING (margin-top: 4px is V2's `gap: 4`); the icon must be a
            // DESCENDANT of the indicator or the pill would not enclose it.
            var item = Bar().Q<VisualElement>(className: MdNavigationBar.ItemClassName);
            Assert.That(item.childCount, Is.EqualTo(2));
            Assert.That(item.ElementAt(0).ClassListContains(MdNavigationBar.IndicatorClassName));
            Assert.That(item.ElementAt(1).ClassListContains(MdNavigationBar.LabelClassName));

            var icon = item.Q<MdIcon>(className: MdNavigationBar.IconClassName);
            Assert.That(icon.parent.ClassListContains(MdNavigationBar.IndicatorClassName));
            Assert.That(item.Q<Label>(className: MdNavigationBar.LabelClassName).parent, Is.SameAs(item));
        }

        [Test]
        public void Items_CarryNoInlineGeometry_SoTheBarIsRestyledFromUssAlone()
        {
            // Every dimension in the bar spec (64dp tab, 56x32 pill, 24dp icon) comes
            // from the stylesheet. Inline styles set from C# would be unthemeable.
            var item = Bar().Q<VisualElement>(className: MdNavigationBar.ItemClassName);
            Assert.That(item.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(item.style.height.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(item.style.flexGrow.keyword, Is.EqualTo(StyleKeyword.Null));

            var indicator = item.Q<VisualElement>(className: MdNavigationBar.IndicatorClassName);
            Assert.That(indicator.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(indicator.style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void SetDestinations_StillBuildsThreeAndFiveDestinations()
        {
            // V2 uses four; M3 allows 3-5 and the USS (space-between + fixed 64dp tabs)
            // must not be four-specific. Nothing in the component may assume a count.
            var bar = new MdNavigationBar();

            bar.SetDestinations(Destinations.Take(3).ToList());
            Assert.That(bar.DestinationCount, Is.EqualTo(3));
            bar.SelectedIndex = 2;
            Assert.That(bar.SelectedIndex, Is.EqualTo(2));

            var five = Destinations.Concat(new[] { new MdNavDestination("info", "Sobre") }).ToList();
            bar.SetDestinations(five);
            Assert.That(bar.DestinationCount, Is.EqualTo(5));
            Assert.That(bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList().Count,
                Is.EqualTo(5));
            bar.SelectedIndex = 4;
            Assert.That(bar.Query<VisualElement>(className: MdNavigationBar.ItemActiveClassName).ToList().Count,
                Is.EqualTo(1), "exactly one destination is ever active");
        }

        // ---- Stylesheet contract ---------------------------------------------------

        static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex Rule = new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled);

        /// <summary>
        /// selector (whitespace-normalized) -> its declarations (whitespace-normalized).
        /// Comments are stripped FIRST: the file's header prose names the same tokens the
        /// rules do, and matching it would make these assertions vacuous.
        /// </summary>
        static Dictionary<string, string> Rules()
        {
            string path = Path.Combine(Application.dataPath,
                "DesignSystem", "Components", "MdNavigationBar", "MdNavigationBar.uss");
            string text = BlockComment.Replace(File.ReadAllText(path), " ");
            var map = new Dictionary<string, string>();
            foreach (Match m in Rule.Matches(text))
            {
                string selector = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
                string body = Regex.Replace(m.Groups[2].Value, @"\s+", " ").Trim();
                map[selector] = map.TryGetValue(selector, out var existing) ? existing + " " + body : body;
            }
            return map;
        }

        static void AssertDeclares(Dictionary<string, string> rules, string selector, params string[] declarations)
        {
            Assert.That(rules.ContainsKey(selector), Is.True,
                $"MdNavigationBar.uss has no rule for `{selector}`");
            foreach (var declaration in declarations)
                Assert.That(rules[selector], Does.Contain(declaration),
                    $"`{selector}` should declare `{declaration}` but has: {rules[selector]}");
        }

        [Test]
        public void Uss_Container_IsTheDarkBarWithANavyTopBorder()
        {
            var rules = Rules();
            AssertDeclares(rules, ".md-nav-bar",
                "background-color: var(--md-sys-color-inverse-surface)",
                "border-top-width: 1px",
                "border-top-color: var(--md-sys-color-primary)",
                "flex-direction: row",
                "justify-content: space-between",
                "padding-left: 24px",
                "padding-right: 24px",
                // A floor, not a fixed height: the host adds the 34dp home-indicator
                // safe-area padding BELOW this row (§1, Appendix B).
                "min-height: 64px");
            // ...which also means it must not pin a fixed height.
            Assert.That(Regex.IsMatch(rules[".md-nav-bar"], @"(^|[^-])height:"), Is.False,
                "the bar must not set `height` — the host adds safe-area padding below the row");
        }

        [Test]
        public void Uss_Tabs_AreFixedWidthSoThreeToFiveDestinationsFit()
        {
            var rules = Rules();
            AssertDeclares(rules, ".md-nav-bar__item",
                "width: 64px",       // 5 x 64 = 320 <= 342dp of content width at 390dp
                "height: 64px",      // the 64dp tab row
                "flex-shrink: 0",    // ...and it stays 64 at the five-destination end
                "align-items: center",
                "justify-content: center");
        }

        [Test]
        public void Uss_InactiveDestination_IsWhiteAtFiftyPercentWithNoPill()
        {
            var rules = Rules();
            // "white at 50%" = inverse-on-surface at 50% opacity (Appendix A.1), never a literal.
            AssertDeclares(rules, ".md-nav-bar__icon",
                "font-size: 24px",
                "color: var(--md-sys-color-inverse-on-surface)",
                "opacity: 0.5");
            AssertDeclares(rules, ".md-nav-bar__label",
                "font-size: 11px",                 // label-small metrics...
                "letter-spacing: 0",
                "Inter-Medium.asset",              // ...at the 500 face (V2 inactive)
                "color: var(--md-sys-color-inverse-on-surface)",
                "opacity: 0.5",
                "margin-top: 4px");                // V2 `gap: 4`; USS has no `gap`
            // No pill when inactive: the indicator box is there (unconditionally — see the
            // deviation note in the USS header) but transparent, so nothing is drawn.
            AssertDeclares(rules, ".md-nav-bar__indicator",
                "background-color: rgba(0, 0, 0, 0)");
        }

        [Test]
        public void Uss_ActiveDestination_IsTheAmberPillWithNavyIconAndSixHundredLabel()
        {
            var rules = Rules();
            // The 56x32 pill geometry is unconditional; selecting a destination only
            // repaints it (no layout jump under the background-color transition).
            AssertDeclares(rules, ".md-nav-bar__indicator",
                "width: 56px",
                "height: 32px",
                "padding: 4px 16px",
                "border-radius: var(--md-sys-shape-corner-large)");   // = 16dp after Slice 0
            AssertDeclares(rules, ".md-nav-bar__item--active .md-nav-bar__indicator",
                "background-color: var(--md-sys-color-secondary)");
            AssertDeclares(rules, ".md-nav-bar__item--active .md-nav-bar__icon",
                "color: var(--md-sys-color-on-secondary)",
                "opacity: 1");
            AssertDeclares(rules, ".md-nav-bar__item--active .md-nav-bar__label",
                "Inter-SemiBold.asset",   // label-small's own 600 face
                "opacity: 1");
            // The ripple on the amber pill must be the on-color, not the white one.
            AssertDeclares(rules, ".md-nav-bar__item--active .md-state-layer",
                "background-color: var(--md-sys-color-on-secondary)");
        }

        [Test]
        public void Uss_KeepsTheStateLayerHooks()
        {
            var rules = Rules();
            AssertDeclares(rules, ".md-nav-bar__indicator .md-state-layer",
                "background-color: var(--md-sys-color-inverse-on-surface)");
            AssertDeclares(rules, ".md-nav-bar__item:hover .md-state-layer",
                "opacity: var(--md-sys-state-hover-opacity)");
            AssertDeclares(rules, ".md-nav-bar__item:focus .md-state-layer",
                "opacity: var(--md-sys-state-focus-opacity)");
            AssertDeclares(rules, ".md-nav-bar__item:active .md-state-layer",
                "opacity: var(--md-sys-state-pressed-opacity)");
        }
    }
}
