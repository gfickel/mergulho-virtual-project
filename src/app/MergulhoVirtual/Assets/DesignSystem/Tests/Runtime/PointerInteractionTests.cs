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
    }
}
