using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    /// <summary>
    /// PlayMode interaction tests: a real runtime panel (UIDocument), synthetic
    /// pointer events, assertions on component callbacks. This is the layer
    /// that catches picking/event bugs before any AR integration.
    /// </summary>
    public class PointerInteractionTests
    {
        GameObject _host;
        PanelSettings _panelSettings;
        UIDocument _document;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject("ui-test-host");
            _document = _host.AddComponent<UIDocument>();
            _document.panelSettings = _panelSettings;
            yield return null; // let the panel attach
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_panelSettings);
        }

        IEnumerator Mount(VisualElement element)
        {
            // The test panel is themeless: no component USS (min-heights) and no
            // fonts (text measures 0), so give the element an explicit rect —
            // picking needs a non-empty worldBound.
            element.style.position = Position.Absolute;
            element.style.left = 20;
            element.style.top = 20;
            element.style.width = 200;
            element.style.height = 48;
            _document.rootVisualElement.Add(element);
            // Two frames: one for attach, one for layout.
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator MdButton_Click_RaisesClicked()
        {
            var button = new MdButton { Text = "Enviar" };
            int clicks = 0;
            button.Clicked += () => clicks++;
            yield return Mount(button);

            TestPointer.Click(button);
            Assert.That(clicks, Is.EqualTo(1));

            TestPointer.Click(button);
            Assert.That(clicks, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator MdButton_Disabled_DoesNotRaiseClicked()
        {
            var button = new MdButton { Text = "Enviar" };
            int clicks = 0;
            button.Clicked += () => clicks++;
            button.SetEnabled(false);
            yield return Mount(button);

            TestPointer.Click(button);
            Assert.That(clicks, Is.Zero);

            button.SetEnabled(true);
            TestPointer.Click(button);
            Assert.That(clicks, Is.EqualTo(1), "re-enabling restores interactivity");
        }

        [UnityTest]
        public IEnumerator MdIconButton_Click_RaisesClicked()
        {
            var button = new MdIconButton { Icon = "close" };
            int clicks = 0;
            button.Clicked += () => clicks++;
            yield return Mount(button);

            TestPointer.Click(button);
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator MdFab_Click_RaisesClicked()
        {
            var fab = new MdFab { Icon = "photo_camera" };
            int clicks = 0;
            fab.Clicked += () => clicks++;
            yield return Mount(fab);

            TestPointer.Click(fab);
            Assert.That(clicks, Is.EqualTo(1));

            fab.SetEnabled(false);
            TestPointer.Click(fab);
            Assert.That(clicks, Is.EqualTo(1), "disabled FAB ignores clicks");
        }

        [UnityTest]
        public IEnumerator MdChip_Filter_ClickTogglesSelection()
        {
            var chip = new MdChip { Kind = MdChipKind.Filter, Text = "Sancho" };
            int clicked = 0;
            bool? lastSelection = null;
            chip.Clicked += () => clicked++;
            chip.SelectedChanged += v => lastSelection = v;
            yield return Mount(chip);

            TestPointer.Click(chip);
            Assert.That(chip.Selected, Is.True);
            Assert.That(clicked, Is.EqualTo(1));
            Assert.That(lastSelection, Is.True);

            TestPointer.Click(chip);
            Assert.That(chip.Selected, Is.False);
            Assert.That(lastSelection, Is.False);
        }

        [UnityTest]
        public IEnumerator MdListItem_Click_RaisesClicked()
        {
            var item = new MdListItem { Headline = "Sancho" };
            int clicks = 0;
            item.Clicked += () => clicks++;
            yield return Mount(item);

            TestPointer.Click(item);
            Assert.That(clicks, Is.EqualTo(1));

            item.SetEnabled(false);
            TestPointer.Click(item);
            Assert.That(clicks, Is.EqualTo(1), "disabled list item ignores clicks");
        }

        [UnityTest]
        public IEnumerator MdTextField_InnerEdit_RaisesValueChangedAndFloatsLabel()
        {
            var field = new MdTextField { LabelText = "Espécie" };
            string last = null;
            field.ValueChanged += v => last = v;
            yield return Mount(field);

            // Simulate the edit path: the inner TextField's ChangeEvent must
            // forward through the wrapper (this is what typing produces).
            field.Q<TextField>().value = "Galeocerdo cuvier";
            yield return null;

            Assert.That(last, Is.EqualTo("Galeocerdo cuvier"));
            Assert.That(field.Value, Is.EqualTo("Galeocerdo cuvier"));
            Assert.That(field.ClassListContains(MdTextField.FloatingClassName));
        }

        [UnityTest]
        public IEnumerator MdDropdown_OpenSelectAndScrimDismiss()
        {
            var dropdown = new MdDropdown { LabelText = "Praia" };
            dropdown.SetChoices(new[] { "Sancho", "Cacimba do Padre", "Sueste" });
            int lastSelection = -2;
            dropdown.SelectionChanged += i => lastSelection = i;
            yield return Mount(dropdown);

            // Open: click the field → menu + scrim appear on the overlay layer.
            TestPointer.Click(dropdown);
            yield return null;
            Assert.That(dropdown.OpenMenu, Is.Not.Null);
            Assert.That(dropdown.ClassListContains(MdDropdown.OpenClassName));
            var root = _document.rootVisualElement;
            var layer = root.Q<VisualElement>(className: MdOverlay.LayerClassName);
            Assert.That(layer, Is.Not.Null, "overlay layer created on demand");
            Assert.That(root.IndexOf(layer), Is.EqualTo(root.childCount - 1), "overlay is the last child (on top)");

            // Themeless panel: menu items have no USS height, so give them one
            // (same reason Mount() sets explicit sizes).
            var items = layer.Query<VisualElement>(className: MdMenu.ItemClassName).ToList();
            Assert.That(items.Count, Is.EqualTo(3));
            foreach (var item in items)
                item.style.height = 48;
            yield return null;

            // Select item 1 → event, value, closed.
            TestPointer.Click(items[1]);
            yield return null;
            Assert.That(lastSelection, Is.EqualTo(1));
            Assert.That(dropdown.Value, Is.EqualTo("Cacimba do Padre"));
            Assert.That(dropdown.OpenMenu, Is.Null, "menu closed after selection");
            Assert.That(dropdown.ClassListContains(MdDropdown.OpenClassName), Is.False);
            Assert.That(layer.Q<VisualElement>(className: MdMenu.UssClassName), Is.Null, "popup removed");

            // Re-open, then click the scrim → dismissed without changing selection.
            TestPointer.Click(dropdown);
            yield return null;
            Assert.That(dropdown.OpenMenu, Is.Not.Null);
            var scrim = layer.Q<VisualElement>(className: MdMenu.ScrimClassName);
            // Themeless panel again: the document root — and therefore the
            // parent-filling scrim — can be zero-sized, so give the scrim an
            // explicit rect and click a point inside it but clear of the
            // field (20..220 × 20..68) and the menu (docked at 0,0 here).
            scrim.style.right = StyleKeyword.Auto;
            scrim.style.bottom = StyleKeyword.Auto;
            scrim.style.width = 600;
            scrim.style.height = 600;
            yield return null;
            // Click just below the field. The point must sit inside the
            // panel's pickable viewport — tiny in this themeless panel (the
            // document root measured ~218×0) — and the item clicks above
            // already proved this band resolves position-picking correctly.
            // Guards make the geometric intent explicit: the tap lands on the
            // scrim, clear of both the field and the popup.
            var point = new UnityEngine.Vector2(dropdown.worldBound.center.x, dropdown.worldBound.yMax + 24);
            Assert.That(scrim.worldBound.Contains(point), "scrim covers the click point");
            Assert.That(dropdown.worldBound.Contains(point), Is.False, "field clear of the click point");
            Assert.That(dropdown.OpenMenu.Root.worldBound.Contains(point), Is.False, "popup clear of the click point");
            TestPointer.ClickAt(scrim, point);
            yield return null;
            Assert.That(dropdown.OpenMenu, Is.Null, "scrim tap dismisses");
            Assert.That(dropdown.Index, Is.EqualTo(1), "selection unchanged by dismissal");
        }

        [UnityTest]
        public IEnumerator MdTopAppBar_NavAndActionClicks_Raise()
        {
            var bar = new MdTopAppBar { Title = "Praias", NavigationIcon = "arrow_back" };
            int navClicks = 0, actionClicks = 0;
            bar.NavigationClicked += () => navClicks++;
            bar.AddAction("search", () => actionClicks++);
            yield return Mount(bar);

            // Themeless panel: no USS sizes, so the inner icon buttons measure
            // 0 — give them explicit rects (same reason Mount() sizes the root).
            var nav = bar.Q<MdIconButton>(className: MdTopAppBar.NavClassName);
            var action = bar.Q<MdIconButton>(className: MdTopAppBar.ActionClassName);
            foreach (var button in new[] { nav, action })
            {
                button.style.width = 48;
                button.style.height = 48;
            }
            yield return null;

            TestPointer.Click(nav);
            Assert.That(navClicks, Is.EqualTo(1));
            Assert.That(actionClicks, Is.Zero, "nav tap doesn't leak into actions");

            TestPointer.Click(action);
            Assert.That(actionClicks, Is.EqualTo(1));
            Assert.That(navClicks, Is.EqualTo(1), "action tap doesn't leak into nav");
        }

        [UnityTest]
        public IEnumerator MdNavigationBar_TapDestination_SelectsAndMovesIndicator()
        {
            var bar = new MdNavigationBar();
            bar.SetDestinations(new[]
            {
                new MdNavDestination("view_in_ar", "AR"),
                new MdNavDestination("beach_access", "Praias"),
                new MdNavDestination("info", "Sobre"),
            });
            bar.SelectedIndex = 0;
            int lastIndex = -2, raised = 0;
            bar.SelectionChanged += i => { lastIndex = i; raised++; };
            yield return Mount(bar);

            // Themeless panel: items have no USS height — size them explicitly.
            var items = bar.Query<VisualElement>(className: MdNavigationBar.ItemClassName).ToList();
            foreach (var item in items)
                item.style.height = 48;
            yield return null;

            TestPointer.Click(items[1]);
            Assert.That(bar.SelectedIndex, Is.EqualTo(1));
            Assert.That(lastIndex, Is.EqualTo(1));
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName));
            Assert.That(items[0].ClassListContains(MdNavigationBar.ItemActiveClassName), Is.False);

            // Re-tapping the selected destination is a no-op.
            TestPointer.Click(items[1]);
            Assert.That(raised, Is.EqualTo(1), "re-tap of the active destination raises nothing");

            TestPointer.Click(items[2]);
            Assert.That(bar.SelectedIndex, Is.EqualTo(2));
            Assert.That(items[2].ClassListContains(MdNavigationBar.ItemActiveClassName));
            Assert.That(items[1].ClassListContains(MdNavigationBar.ItemActiveClassName), Is.False);
        }

        [UnityTest]
        public IEnumerator MdDialog_ConfirmAndDismissActions()
        {
            var anchor = new MdButton { Text = "Abrir" };
            yield return Mount(anchor);

            int confirmed = 0, dismissed = 0, closed = 0;
            var dialog = MdDialog.Open(anchor, "Descartar registro?", "As informações serão perdidas.",
                "Descartar", () => confirmed++, "Cancelar", () => dismissed++);
            dialog.Closed += () => closed++;

            var root = _document.rootVisualElement;
            var layer = root.Q<VisualElement>(className: MdOverlay.LayerClassName);
            Assert.That(layer, Is.Not.Null, "overlay layer created on demand");
            Assert.That(root.IndexOf(layer), Is.EqualTo(root.childCount - 1), "overlay is the last child (on top)");
            Assert.That(layer.Q<VisualElement>(className: MdDialog.UssClassName), Is.EqualTo(dialog));
            Assert.That(layer.Q<VisualElement>(className: MdDialog.ScrimClassName), Is.Not.Null);

            // Themeless panel: no USS sizes, and the layer chain has DEFINITE
            // height 0 (docRoot is width×0), so Yoga's default flex-shrink: 1
            // collapses even explicit child heights to 0. Anchor the centering
            // holder to the top (the band the earlier tests prove pickable),
            // stop the shrink cascade, and give the buttons explicit rects.
            void MakeClickable(MdDialog d)
            {
                d.parent.style.justifyContent = Justify.FlexStart;
                d.style.flexShrink = 0;
                foreach (var button in new[] { d.DismissButton, d.ConfirmButton })
                {
                    button.style.width = 80;
                    button.style.height = 48;
                    button.style.flexShrink = 0;
                }
            }
            MakeClickable(dialog);
            yield return null;

            TestPointer.Click(dialog.ConfirmButton);
            Assert.That(confirmed, Is.EqualTo(1));
            Assert.That(dismissed, Is.Zero, "confirm tap doesn't leak into dismiss");
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(layer.Q<VisualElement>(className: MdDialog.UssClassName), Is.Null, "dialog removed");
            Assert.That(layer.Q<VisualElement>(className: MdDialog.ScrimClassName), Is.Null, "scrim removed");

            // Second dialog: the dismiss path.
            int confirmed2 = 0, dismissed2 = 0;
            var dialog2 = MdDialog.Open(anchor, "Enviar avistamento?", "",
                "Enviar", () => confirmed2++, "Cancelar", () => dismissed2++);
            MakeClickable(dialog2);
            yield return null;

            TestPointer.Click(dialog2.DismissButton);
            Assert.That(dismissed2, Is.EqualTo(1));
            Assert.That(confirmed2, Is.Zero, "dismiss tap doesn't leak into confirm");
            Assert.That(layer.Q<VisualElement>(className: MdDialog.UssClassName), Is.Null);
        }

        [UnityTest]
        public IEnumerator MdDialog_ScrimTapClosesWithoutActions()
        {
            var anchor = new MdButton { Text = "Abrir" };
            yield return Mount(anchor);

            int confirmed = 0, dismissed = 0, closed = 0;
            var dialog = MdDialog.Open(anchor, "Descartar registro?", "",
                "Sim", () => confirmed++, "Não", () => dismissed++);
            dialog.Closed += () => closed++;

            var layer = _document.rootVisualElement.Q<VisualElement>(className: MdOverlay.LayerClassName);
            var scrim = layer.Q<VisualElement>(className: MdDialog.ScrimClassName);

            // Themeless panel: no USS sizes and a zero-height layer chain (see
            // the note in MdDialog_ConfirmAndDismissActions) — pin the dialog
            // to the top-left with an explicit rect and give the scrim one too,
            // so a point clear of the dialog exists inside the pickable band
            // (same recipe as the MdDropdown scrim test).
            dialog.parent.style.justifyContent = Justify.FlexStart;
            dialog.parent.style.alignItems = Align.FlexStart;
            dialog.style.flexShrink = 0;
            dialog.style.width = 200;
            dialog.style.height = 100;
            scrim.style.right = StyleKeyword.Auto;
            scrim.style.bottom = StyleKeyword.Auto;
            scrim.style.width = 600;
            scrim.style.height = 600;
            yield return null;

            var point = new UnityEngine.Vector2(dialog.worldBound.xMax + 40, 24);
            Assert.That(scrim.worldBound.Contains(point), "scrim covers the click point");
            Assert.That(dialog.worldBound.Contains(point), Is.False, "dialog clear of the click point");
            TestPointer.ClickAt(scrim, point);
            yield return null;

            Assert.That(closed, Is.EqualTo(1), "scrim tap closes");
            Assert.That(confirmed, Is.Zero, "scrim tap raises no confirm");
            Assert.That(dismissed, Is.Zero, "scrim tap raises no dismiss");
            Assert.That(layer.Q<VisualElement>(className: MdDialog.UssClassName), Is.Null, "dialog removed");
        }

        [UnityTest]
        public IEnumerator MdSnackbar_ActionTapRaisesAndCloses()
        {
            var anchor = new MdButton { Text = "Mostrar" };
            yield return Mount(anchor);

            int action = 0, closed = 0;
            var snackbar = MdSnackbar.Show(anchor, "Sem conexão — na fila.", "Tentar",
                () => action++, durationSeconds: 0f);
            snackbar.Closed += () => closed++;

            var layer = _document.rootVisualElement.Q<VisualElement>(className: MdOverlay.LayerClassName);
            Assert.That(layer.Q<VisualElement>(className: MdSnackbar.UssClassName), Is.EqualTo(snackbar));

            // Themeless panel: dock the holder to the top (pickable band), stop
            // the zero-height shrink cascade (see the note in
            // MdDialog_ConfirmAndDismissActions), and size the action button.
            snackbar.parent.style.justifyContent = Justify.FlexStart;
            snackbar.style.flexShrink = 0;
            snackbar.ActionButton.style.width = 80;
            snackbar.ActionButton.style.height = 48;
            snackbar.ActionButton.style.flexShrink = 0;
            yield return null;

            TestPointer.Click(snackbar.ActionButton);
            Assert.That(action, Is.EqualTo(1));
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(layer.Q<VisualElement>(className: MdSnackbar.UssClassName), Is.Null, "snackbar removed");
        }

        [UnityTest]
        public IEnumerator MdSnackbar_ShowReplacesCurrentAndAutoDismisses()
        {
            var anchor = new MdButton { Text = "Mostrar" };
            yield return Mount(anchor);

            int firstClosed = 0;
            var first = MdSnackbar.Show(anchor, "Primeira.", durationSeconds: 0f);
            first.Closed += () => firstClosed++;
            yield return null;

            var second = MdSnackbar.Show(anchor, "Segunda.", durationSeconds: 0.2f);
            int secondClosed = 0;
            second.Closed += () => secondClosed++;
            Assert.That(firstClosed, Is.EqualTo(1), "a new snackbar replaces the current one");
            var layer = _document.rootVisualElement.Q<VisualElement>(className: MdOverlay.LayerClassName);
            Assert.That(layer.Query<VisualElement>(className: MdSnackbar.UssClassName).ToList().Count,
                Is.EqualTo(1), "only the replacement remains");

            yield return new WaitForSeconds(0.6f);
            Assert.That(secondClosed, Is.EqualTo(1), "auto-dismissed after its duration");
            Assert.That(layer.Q<VisualElement>(className: MdSnackbar.UssClassName), Is.Null);
        }

        [UnityTest]
        public IEnumerator MdBottomSheet_ScrimTapClosesButSurfaceTapDoesNot()
        {
            var anchor = new MdButton { Text = "Abrir" };
            yield return Mount(anchor);

            int closed = 0;
            var sheet = MdBottomSheet.Open(anchor);
            sheet.Closed += () => closed++;
            sheet.Add(new Label("Tubarão-martelo"));

            var root = _document.rootVisualElement;
            var layer = root.Q<VisualElement>(className: MdOverlay.LayerClassName);
            Assert.That(layer, Is.Not.Null, "overlay layer created on demand");
            Assert.That(root.IndexOf(layer), Is.EqualTo(root.childCount - 1), "overlay is the last child (on top)");
            Assert.That(layer.Q<VisualElement>(className: MdBottomSheet.UssClassName), Is.EqualTo(sheet));
            var scrim = layer.Q<VisualElement>(className: MdBottomSheet.ScrimClassName);
            Assert.That(scrim, Is.Not.Null);
            Assert.That(sheet.parent.ClassListContains(MdBottomSheet.HolderClassName));
            Assert.That(sheet.parent.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "holder is hit-transparent — only the sheet surface blocks taps");
            Assert.That(sheet.parent.style.justifyContent.value, Is.EqualTo(Justify.FlexEnd),
                "sheet docks to the bottom");

            // Themeless panel: no USS sizes and a zero-height layer chain (see
            // the note in MdDialog_ConfirmAndDismissActions) — pin the sheet to
            // the top-left with an explicit rect and give the scrim one too, so
            // both a covered and a clear point exist inside the pickable band.
            sheet.parent.style.justifyContent = Justify.FlexStart;
            sheet.parent.style.alignItems = Align.FlexStart;
            sheet.style.flexShrink = 0;
            sheet.style.width = 200;
            sheet.style.height = 100;
            scrim.style.right = StyleKeyword.Auto;
            scrim.style.bottom = StyleKeyword.Auto;
            scrim.style.width = 600;
            scrim.style.height = 600;
            yield return null;

            // A tap on the sheet's own surface must NOT close (the sheet is
            // pickable and swallows it before the scrim).
            var onSheet = sheet.worldBound.center;
            Assert.That(scrim.worldBound.Contains(onSheet), "scrim lies under the sheet too");
            TestPointer.ClickAt(scrim, onSheet);
            yield return null;
            Assert.That(closed, Is.Zero, "surface tap does not dismiss");

            var offSheet = new UnityEngine.Vector2(sheet.worldBound.xMax + 40, 24);
            Assert.That(scrim.worldBound.Contains(offSheet), "scrim covers the click point");
            Assert.That(sheet.worldBound.Contains(offSheet), Is.False, "sheet clear of the click point");
            TestPointer.ClickAt(scrim, offSheet);
            yield return null;

            Assert.That(closed, Is.EqualTo(1), "scrim tap closes");
            Assert.That(layer.Q<VisualElement>(className: MdBottomSheet.UssClassName), Is.Null, "sheet removed");
            Assert.That(layer.Q<VisualElement>(className: MdBottomSheet.ScrimClassName), Is.Null, "scrim removed");
            Assert.That(layer.Q<VisualElement>(className: MdBottomSheet.HolderClassName), Is.Null, "holder removed");
        }

        [UnityTest]
        public IEnumerator MdChip_Assist_ClickDoesNotSelect()
        {
            var chip = new MdChip { Kind = MdChipKind.Assist, Text = "Marés" };
            int clicked = 0;
            chip.Clicked += () => clicked++;
            yield return Mount(chip);

            TestPointer.Click(chip);
            Assert.That(clicked, Is.EqualTo(1));
            Assert.That(chip.Selected, Is.False);
        }

        // ---- Mv* components (DESIGN_IMPLEMENTATION.md §6) --------------------

        [UnityTest]
        public IEnumerator MvHeroHeader_BackAndSelector_RaiseSeparateEvents()
        {
            var hero = new MvHeroHeader
            {
                ShowBackButton = true,
                ShowSelector = true,
                SelectorText = "Baía do Sueste",
            };
            int back = 0, selector = 0;
            hero.BackClicked += () => back++;
            hero.SelectorClicked += () => selector++;
            yield return Mount(hero);

            // Themeless panel: no component USS, so nothing has an intrinsic size
            // and picking would find an empty worldBound. Size the two controls
            // (and the row that lays them out) explicitly.
            hero.style.height = 240;
            var topRow = hero.Q<VisualElement>(className: MvHeroHeader.TopRowClassName);
            topRow.style.height = 48;
            var backButton = hero.Q<MdIconButton>(className: MvHeroHeader.BackClassName);
            backButton.style.width = 48;
            backButton.style.height = 48;
            hero.Selector.style.width = 120;
            hero.Selector.style.height = 48;
            yield return null;

            TestPointer.Click(backButton);
            Assert.That(back, Is.EqualTo(1));
            Assert.That(selector, Is.Zero, "the back button must not raise SelectorClicked");

            TestPointer.Click(hero.Selector);
            Assert.That(selector, Is.EqualTo(1));
            Assert.That(back, Is.EqualTo(1), "the selector must not raise BackClicked");
        }

        [UnityTest]
        public IEnumerator MvMediaCarousel_TapCard_RaisesItsIndex()
        {
            var carousel = new MvMediaCarousel();
            carousel.SetItems(new[]
            {
                new MvMediaItem("Tubarão-limão", "Foto: Bianca Rangel"),
                new MvMediaItem("Tartaruga-verde", "Foto: Marcos Lima"),
            });
            int last = -1, raised = 0;
            carousel.ItemClicked += i => { last = i; raised++; };
            yield return Mount(carousel);

            // Themeless panel again: size the scroll chain and the cards, and keep
            // the tap inside the 200dp-wide mounted viewport (a horizontal
            // ScrollView clips what hangs past it, so card 1 is not clickable here).
            carousel.style.height = 171;
            var scroll = carousel.Q<ScrollView>();
            scroll.style.flexGrow = 1;
            scroll.style.height = 171;
            var cards = carousel.Query<VisualElement>(className: MvMediaCarousel.CardClassName).ToList();
            foreach (var card in cards)
            {
                card.style.width = 90;
                card.style.height = 120;
            }
            yield return null;

            TestPointer.Click(cards[0]);
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(last, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MdCheckbox_Click_TogglesAndRaises()
        {
            var box = new MdCheckbox();
            int raised = 0;
            bool? last = null;
            box.ValueChanged += v => { raised++; last = v; };
            yield return Mount(box);

            TestPointer.Click(box);
            Assert.That(box.Checked, Is.True);
            Assert.That(last, Is.True);
            Assert.That(box.ClassListContains(MdCheckbox.CheckedClassName));

            TestPointer.Click(box);
            Assert.That(box.Checked, Is.False);
            Assert.That(last, Is.False);
            Assert.That(raised, Is.EqualTo(2));

            box.SetEnabled(false);
            TestPointer.Click(box);
            Assert.That(raised, Is.EqualTo(2), "a disabled checkbox ignores clicks");
        }

        [UnityTest]
        public IEnumerator MvOptionCard_ClickRaisesWithoutSelfSelecting_AndTheCallerKeepsTheGroupExclusive()
        {
            var tourist = new MvOptionCard { Icon = "person", Text = "Turista / Visitante" };
            var guide = new MvOptionCard { Icon = "explore", Text = "Condutor / Guia" };
            var group = new[] { tourist, guide };
            string lastClicked = null;
            foreach (var card in group)
            {
                var captured = card;
                card.Clicked += () =>
                {
                    lastClicked = captured.Text;
                    foreach (var other in group)
                        other.Selected = other == captured;
                };
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(tourist);
            row.Add(guide);
            yield return Mount(row);

            // Themeless panel: no component USS, so the cards have no intrinsic
            // size — give each an explicit rect and stop the shrink cascade.
            foreach (var card in group)
            {
                card.style.width = 90;
                card.style.height = 48;
                card.style.flexShrink = 0;
            }
            yield return null;

            TestPointer.Click(tourist);
            Assert.That(lastClicked, Is.EqualTo("Turista / Visitante"));
            Assert.That(tourist.Selected, Is.True, "the CALLER selected it — the card never selects itself");
            Assert.That(guide.Selected, Is.False);

            TestPointer.Click(guide);
            Assert.That(guide.Selected, Is.True);
            Assert.That(tourist.Selected, Is.False, "exactly one stays selected");

            // Re-tapping the selected card raises again but cannot deselect it:
            // an option card is one arm of a radio group, not a toggle.
            TestPointer.Click(guide);
            Assert.That(guide.Selected, Is.True);

            guide.SetEnabled(false);
            lastClicked = null;
            TestPointer.Click(guide);
            Assert.That(lastClicked, Is.Null, "a disabled option card ignores clicks");
        }

        [UnityTest]
        public IEnumerator MvMediaPicker_EmptyBoxAddTileAndRemoveButtonsRaiseTheRightEvents()
        {
            var picker = new MvMediaPicker { MaxItems = 3 };
            int adds = 0, lastRemoved = -1, removes = 0;
            picker.AddRequested += () => adds++;
            picker.RemoveRequested += i => { lastRemoved = i; removes++; };
            yield return Mount(picker);

            // 1. The empty dashed box is itself the add button.
            var empty = picker.Q<VisualElement>(className: MvMediaPicker.EmptyClassName);
            empty.style.width = 200;
            empty.style.height = 48;
            empty.style.flexShrink = 0;
            yield return null;

            TestPointer.Click(empty);
            Assert.That(adds, Is.EqualTo(1));

            // 2. With items, the add tile and each remove button are separate targets.
            var textures = new[] { new Texture2D(2, 2), new Texture2D(2, 2) };
            picker.SetItems(new[]
            {
                new MvMediaPickerItem(textures[0]),
                new MvMediaPickerItem(textures[1]),
            });

            // Themeless panel again: size the card chain and every tap target.
            var card = picker.Q<VisualElement>(className: MvMediaPicker.CardClassName);
            card.style.flexShrink = 0;
            var grid = picker.Q<VisualElement>(className: MvMediaPicker.GridClassName);
            grid.style.flexShrink = 0;
            var add = picker.Q<VisualElement>(className: MvMediaPicker.AddClassName);
            var removeButtons = picker.Query<VisualElement>(className: MvMediaPicker.RemoveClassName).ToList();
            Assert.That(removeButtons.Count, Is.EqualTo(2));
            foreach (var target in new[] { add, removeButtons[0], removeButtons[1] })
            {
                // The remove buttons are absolutely positioned inside their tile;
                // pin them to explicit, non-overlapping rects so picking can tell
                // them apart in a panel with no USS geometry.
                target.style.position = Position.Absolute;
                target.style.width = 28;
                target.style.height = 28;
                target.style.flexShrink = 0;
            }
            add.style.left = 0;
            add.style.top = 0;
            removeButtons[0].style.left = 40;
            removeButtons[0].style.top = 0;
            removeButtons[1].style.left = 80;
            removeButtons[1].style.top = 0;
            yield return null;

            TestPointer.Click(removeButtons[1]);
            Assert.That(removes, Is.EqualTo(1));
            Assert.That(lastRemoved, Is.EqualTo(1), "each tile reports its own index");
            Assert.That(adds, Is.EqualTo(1), "a remove tap does not leak into AddRequested");

            TestPointer.Click(removeButtons[0]);
            Assert.That(lastRemoved, Is.Zero);

            TestPointer.Click(add);
            Assert.That(adds, Is.EqualTo(2));
            Assert.That(removes, Is.EqualTo(2), "an add tap does not leak into RemoveRequested");

            foreach (var texture in textures)
                Object.Destroy(texture);
        }

        /// <summary>
        /// MvStateView's only interactive part is its action button, and the root
        /// deliberately ignores picking. This pins both: the tap reaches the
        /// button and comes back out as ActionInvoked, and a tap anywhere else on
        /// the view raises nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator MvStateView_ActionClick_RaisesActionInvoked()
        {
            var view = new MvStateView
            {
                Title = "Algo deu errado por aqui",
                Body = "Nao conseguimos carregar as informacoes.",
                ActionText = "Tentar novamente",
            };
            int invoked = 0;
            view.ActionInvoked += () => invoked++;
            yield return Mount(view);

            // Themeless panel: no USS geometry, so pin the footer chain and give
            // the button an explicit rect below the (0-height) content block.
            var footer = view.Q<VisualElement>(className: MvStateView.FooterClassName);
            footer.style.flexShrink = 0;
            var action = view.Q<MdButton>(className: MvStateView.ActionClassName);
            action.style.position = Position.Absolute;
            action.style.left = 0;
            action.style.top = 0;
            action.style.width = 180;
            action.style.height = 47;
            action.style.flexShrink = 0;
            yield return null;

            TestPointer.Click(action);
            Assert.That(invoked, Is.EqualTo(1));

            // The root ignores picking, so a tap that misses the button is inert
            // rather than being swallowed by the state view.
            TestPointer.ClickAt(view, new Vector2(view.worldBound.xMax - 2f, view.worldBound.yMax - 2f));
            Assert.That(invoked, Is.EqualTo(1), "only the action button is interactive");
        }
    }
}
