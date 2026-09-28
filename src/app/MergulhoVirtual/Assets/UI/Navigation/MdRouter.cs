using System;
using System.Collections.Generic;
using MergulhoVirtual.DesignSystem;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Navigation
{
    /// <summary>
    /// One bottom-bar destination: the route it navigates to plus how the bar
    /// presents it. Icon names come from the shipped Material Symbols subset
    /// (tools/design_system/material_symbols_icons.txt); an empty icon renders
    /// nothing (see <see cref="AppTabs"/> for the Avistamentos placeholder).
    /// </summary>
    public readonly struct RouterTab
    {
        public readonly string Route;
        public readonly string Icon;
        public readonly string Label;

        public RouterTab(string route, string icon, string label)
        {
            Route = route ?? "";
            Icon = icon ?? "";
            Label = label ?? "";
        }
    }

    /// <summary>The V2 bottom bar, in order (DESIGN_IMPLEMENTATION.md §4).</summary>
    public static class AppTabs
    {
        /// <remarks>
        /// Mergulho is `filter_center_focus` (an AR focus frame), NOT `landscape`
        /// (mountains) — V2's Flaticon glyph is the focus bracket, and the mountains
        /// read as scenery on a diving tab.
        ///
        /// <para>Avistamentos renders
        /// <see cref="AppIcons.AvistamentosPlaceholder"/> — the SAME stand-in as the
        /// Início feature tile, because the real asset is a custom shark fin that is
        /// still owed (Decision D7). An empty icon here made the tab read as broken;
        /// see the TODO on that constant before changing it.</para>
        /// </remarks>
        public static readonly IReadOnlyList<RouterTab> Default = new[]
        {
            new RouterTab(AppRoutes.Home,         "home",                "Início"),
            new RouterTab(AppRoutes.Mergulho,     "filter_center_focus", "Mergulho"),
            new RouterTab(AppRoutes.Praias,       "location_on",         "Praias"),
            new RouterTab(AppRoutes.Avistamentos, AppIcons.AvistamentosPlaceholder, "Avistamentos"),
        };
    }

    /// <summary>
    /// App shell: a screen container stacked above a persistent
    /// <see cref="MdNavigationBar"/>. Owns which <see cref="IAppScreen"/> is
    /// visible, the sub-screen back stack, and edge-inset forwarding.
    ///
    /// <para>Two kinds of navigation:</para>
    /// <list type="bullet">
    /// <item><b><see cref="Navigate"/></b> — a bottom-bar destination. Clears the
    /// back stack and moves the bar's selection.</item>
    /// <item><b><see cref="Push"/></b> / <b><see cref="Back"/></b> — a sub-screen
    /// (Praia detalhe, SOS, Espécie…). The bar keeps the <i>originating</i> tab
    /// selected: sub-screens have no tab of their own. (The SOS frame in Figma
    /// shows Praias highlighted — that is a copy-paste artifact, §8.6.)</item>
    /// </list>
    ///
    /// <para>Screens are registered once and kept alive; switching toggles
    /// <c>display</c>, so per-visit work belongs in
    /// <see cref="IAppScreen.OnEnter"/>/<see cref="IAppScreen.OnExit"/>. Exactly
    /// one screen is visible at a time, and the outgoing screen's
    /// <c>OnExit</c> always runs before the incoming screen's <c>OnEnter</c>.</para>
    ///
    /// <para>Layout is set inline rather than in USS because it is pure flex
    /// plumbing with no colors — nothing here needs a design token, and it keeps
    /// the router free of a stylesheet the host would have to load.</para>
    /// </summary>
    public sealed class MdRouter : VisualElement
    {
        public const string UssClassName = "mv-router";
        public const string ScreensUssClassName = "mv-router__screens";

        readonly Dictionary<string, IAppScreen> screens = new Dictionary<string, IAppScreen>();
        readonly List<RouterTab> tabs = new List<RouterTab>();
        readonly List<string> backStack = new List<string>();
        readonly VisualElement screenContainer;
        readonly MdNavigationBar navBar;

        string current;
        string currentTab;
        bool suppressNavEvent;
        bool navBarVisible = true;
        float insetTop, insetLeft, insetRight, insetBottom;

        /// <summary>Raised after the visible screen changed, with the new route key.</summary>
        public event Action<string> RouteChanged;

        /// <summary>Route key of the visible screen, or null before the first navigation.</summary>
        public string Current => current;

        /// <summary>Route key of the bottom-bar destination whose stack we are in, or null.</summary>
        public string CurrentTab => currentTab;

        /// <summary>Depth of the sub-screen back stack; 0 at a tab root.</summary>
        public int StackDepth => backStack.Count;

        public bool CanGoBack => backStack.Count > 0;

        public VisualElement ScreenContainer => screenContainer;

        public MdNavigationBar NavigationBar => navBar;

        public IReadOnlyList<RouterTab> Tabs => tabs;

        public MdRouter()
        {
            AddToClassList(UssClassName);
            // Transparent, non-pickable shell: where no screen paints, taps fall
            // through to the uGUI canvas underneath (the AR HUD still lives there).
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1f;

            screenContainer = new VisualElement { name = "screens", pickingMode = PickingMode.Ignore };
            screenContainer.AddToClassList(ScreensUssClassName);
            screenContainer.style.flexGrow = 1f;
            Add(screenContainer);

            navBar = new MdNavigationBar { name = "nav-bar" };
            navBar.style.flexShrink = 0f;
            navBar.SelectionChanged += OnNavSelectionChanged;
            Add(navBar);
        }

        // ---- Destinations ---------------------------------------------------

        /// <summary>
        /// Replaces the bottom-bar destinations. Call before or after
        /// <see cref="Register"/> — a destination whose route has no registered
        /// screen simply refuses to activate (and the bar snaps back).
        /// </summary>
        public void SetTabs(IReadOnlyList<RouterTab> newTabs)
        {
            tabs.Clear();
            var destinations = new List<MdNavDestination>();
            if (newTabs != null)
            {
                for (int i = 0; i < newTabs.Count; i++)
                {
                    tabs.Add(newTabs[i]);
                    destinations.Add(new MdNavDestination(newTabs[i].Icon, newTabs[i].Label));
                }
            }

            suppressNavEvent = true;
            navBar.SelectedIndex = -1;
            navBar.SetDestinations(destinations);
            suppressNavEvent = false;

            SyncTabSelection();
        }

        /// <summary>
        /// When false the bar is hidden and the full bottom safe-area inset is
        /// forwarded to the screen instead of being absorbed by the bar.
        /// </summary>
        public bool NavigationBarVisible
        {
            get => navBarVisible;
            set
            {
                if (navBarVisible == value) return;
                navBarVisible = value;
                navBar.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                SetEdgeInsets(insetTop, insetLeft, insetRight, insetBottom);
            }
        }

        // ---- Registration ---------------------------------------------------

        /// <summary>
        /// Adds a screen under its <see cref="IAppScreen.Key"/>. The screen's root
        /// is parented into the container hidden; the router never constructs a
        /// screen, the host composition root does.
        /// </summary>
        public void Register(IAppScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (string.IsNullOrEmpty(screen.Key))
                throw new ArgumentException("IAppScreen.Key must be a non-empty route key.", nameof(screen));
            if (screens.ContainsKey(screen.Key))
                throw new ArgumentException($"A screen is already registered for route '{screen.Key}'.", nameof(screen));

            screens.Add(screen.Key, screen);

            var root = screen.Root;
            if (root != null)
            {
                root.style.display = DisplayStyle.None;
                root.style.flexGrow = 1f;
                screenContainer.Add(root);
            }
        }

        public bool IsRegistered(string key) => key != null && screens.ContainsKey(key);

        // ---- Navigation -----------------------------------------------------

        /// <summary>
        /// Shows a top-level route and clears the back stack. Returns false (and
        /// changes nothing) when no screen is registered for <paramref name="key"/>
        /// — that is the normal state for a route whose screen has not landed yet.
        /// Navigating to the already-current route only drops the back stack.
        /// </summary>
        public bool Navigate(string key)
        {
            if (!IsRegistered(key))
            {
                UnityEngine.Debug.LogWarning($"[MdRouter] No screen registered for route '{key}' — navigation ignored.");
                return false;
            }

            backStack.Clear();
            if (IndexOfTab(key) >= 0) currentTab = key;
            Activate(key);
            return true;
        }

        /// <summary>
        /// Shows a sub-screen and remembers where we came from. The bar keeps the
        /// originating tab selected. Returns false for an unregistered route or a
        /// push onto the current screen.
        /// </summary>
        public bool Push(string key)
        {
            if (!IsRegistered(key))
            {
                UnityEngine.Debug.LogWarning($"[MdRouter] No screen registered for route '{key}' — push ignored.");
                return false;
            }
            if (key == current) return false;

            if (current != null) backStack.Add(current);
            Activate(key);
            return true;
        }

        /// <summary>
        /// Pops the back stack. Returns false and does nothing when the stack is
        /// empty — Android's system back button is deliberately not wired here.
        /// </summary>
        public bool Back()
        {
            if (backStack.Count == 0) return false;

            int last = backStack.Count - 1;
            string key = backStack[last];
            backStack.RemoveAt(last);

            if (!IsRegistered(key)) return false;
            if (IndexOfTab(key) >= 0) currentTab = key;
            Activate(key);
            return true;
        }

        // ---- Insets ---------------------------------------------------------

        /// <summary>
        /// Edge insets in panel units, pushed down from the host.
        ///
        /// <para>Top/left/right are the safe area and go straight to the active
        /// screen. The bottom inset is absorbed by the bar as padding while the
        /// bar is visible — V2's 34dp "home indicator" strip below the 64dp tab
        /// row IS this padding, not a drawn element (§4), so MdNavigationBar.uss
        /// must style the row only and never bake that strip in. Screens then get
        /// bottom = 0: they are laid out above the bar, so they have nothing to
        /// reserve.</para>
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            insetTop = top;
            insetLeft = left;
            insetRight = right;
            insetBottom = bottom;

            navBar.style.paddingBottom = navBarVisible ? bottom : 0f;

            if (current != null && screens.TryGetValue(current, out var screen)) ApplyInsets(screen);
        }

        void ApplyInsets(IAppScreen screen) =>
            screen.SetEdgeInsets(insetTop, insetLeft, insetRight, navBarVisible ? 0f : insetBottom);

        // ---- Internals ------------------------------------------------------

        void Activate(string key)
        {
            if (current == key)
            {
                SyncTabSelection();
                return;
            }

            IAppScreen previous = null;
            if (current != null) screens.TryGetValue(current, out previous);
            var next = screens[key];

            if (previous != null)
            {
                SetScreenVisible(previous, false);
                previous.OnExit();
            }

            current = key;
            SetScreenVisible(next, true);
            ApplyInsets(next);
            next.OnEnter();

            SyncTabSelection();
            RouteChanged?.Invoke(current);
        }

        static void SetScreenVisible(IAppScreen screen, bool visible)
        {
            var root = screen.Root;
            if (root != null) root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        int IndexOfTab(string route)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].Route == route) return i;
            }
            return -1;
        }

        void SyncTabSelection()
        {
            int index = currentTab != null ? IndexOfTab(currentTab) : -1;
            if (navBar.SelectedIndex == index) return;
            suppressNavEvent = true;
            navBar.SelectedIndex = index;
            suppressNavEvent = false;
        }

        void OnNavSelectionChanged(int index)
        {
            if (suppressNavEvent) return;
            if (index < 0 || index >= tabs.Count) return;

            // A tap on a destination with no screen yet must not leave the bar
            // highlighting a route we did not go to.
            if (!Navigate(tabs[index].Route)) SyncTabSelection();
        }
    }
}
