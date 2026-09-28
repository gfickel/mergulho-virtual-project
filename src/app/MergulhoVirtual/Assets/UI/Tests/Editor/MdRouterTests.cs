using System;
using System.Collections.Generic;
using MergulhoVirtual.UI.Navigation;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// MdRouter is plain C# over UIElements — no MonoBehaviour, no scene — so the
    /// whole navigation contract (register / navigate / push / back, OnEnter ↔
    /// OnExit ordering, which tab the bar highlights, inset forwarding) is
    /// exercised in EditMode.
    /// </summary>
    public class MdRouterTests
    {
        sealed class FakeScreen : IAppScreen
        {
            readonly List<string> log;

            public string Key { get; }
            public VisualElement Root { get; }
            public int EnterCount, ExitCount;
            public float Top, Left, Right, Bottom;
            public int InsetCalls;

            public FakeScreen(string key, List<string> log = null)
            {
                Key = key;
                this.log = log;
                Root = new VisualElement { name = key };
            }

            public void OnEnter() { EnterCount++; log?.Add("enter:" + Key); }
            public void OnExit() { ExitCount++; log?.Add("exit:" + Key); }

            public void SetEdgeInsets(float top, float left, float right, float bottom)
            {
                Top = top; Left = left; Right = right; Bottom = bottom;
                InsetCalls++;
            }
        }

        MdRouter router;
        List<string> log;
        FakeScreen home, mergulho, praias, detalhe;
        List<string> routeEvents;

        [SetUp]
        public void SetUp()
        {
            log = new List<string>();
            routeEvents = new List<string>();
            router = new MdRouter();
            router.SetTabs(AppTabs.Default);
            router.RouteChanged += r => routeEvents.Add(r);

            home = new FakeScreen(AppRoutes.Home, log);
            mergulho = new FakeScreen(AppRoutes.Mergulho, log);
            praias = new FakeScreen(AppRoutes.Praias, log);
            detalhe = new FakeScreen(AppRoutes.PraiaDetalhe, log);
        }

        void RegisterAll()
        {
            router.Register(home);
            router.Register(mergulho);
            router.Register(praias);
            router.Register(detalhe);
        }

        static DisplayStyle Display(FakeScreen s) => s.Root.style.display.value;

        // ---- Registration ---------------------------------------------------

        [Test]
        public void Register_ParentsTheScreenHiddenAndDoesNotEnterIt()
        {
            router.Register(home);

            Assert.IsTrue(router.IsRegistered(AppRoutes.Home));
            Assert.AreSame(router.ScreenContainer, home.Root.parent);
            Assert.AreEqual(DisplayStyle.None, Display(home));
            Assert.AreEqual(0, home.EnterCount);
            Assert.IsNull(router.Current);
        }

        [Test]
        public void Register_RejectsDuplicateRouteKeys()
        {
            router.Register(home);
            Assert.Throws<ArgumentException>(() => router.Register(new FakeScreen(AppRoutes.Home)));
        }

        [Test]
        public void Register_RejectsNullAndEmptyKeys()
        {
            Assert.Throws<ArgumentNullException>(() => router.Register(null));
            Assert.Throws<ArgumentException>(() => router.Register(new FakeScreen("")));
        }

        // ---- Navigate -------------------------------------------------------

        [Test]
        public void Navigate_ShowsExactlyOneScreenAndRaisesRouteChanged()
        {
            RegisterAll();

            Assert.IsTrue(router.Navigate(AppRoutes.Praias));

            Assert.AreEqual(AppRoutes.Praias, router.Current);
            Assert.AreEqual(DisplayStyle.Flex, Display(praias));
            Assert.AreEqual(DisplayStyle.None, Display(home));
            Assert.AreEqual(DisplayStyle.None, Display(mergulho));
            CollectionAssert.AreEqual(new[] { AppRoutes.Praias }, routeEvents);
        }

        [Test]
        public void Navigate_CallsOnExitBeforeOnEnter()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Home);
            log.Clear();

            router.Navigate(AppRoutes.Praias);

            CollectionAssert.AreEqual(new[] { "exit:" + AppRoutes.Home, "enter:" + AppRoutes.Praias }, log);
        }

        [Test]
        public void Navigate_ToUnregisteredRoute_IsIgnored()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Home);
            log.Clear();
            routeEvents.Clear();

            Assert.IsFalse(router.Navigate(AppRoutes.Sos));

            Assert.AreEqual(AppRoutes.Home, router.Current);
            CollectionAssert.IsEmpty(log);
            CollectionAssert.IsEmpty(routeEvents);
        }

        [Test]
        public void Navigate_ToCurrentRoute_DoesNotReEnter()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Home);
            log.Clear();
            routeEvents.Clear();

            Assert.IsTrue(router.Navigate(AppRoutes.Home));

            Assert.AreEqual(1, home.EnterCount);
            CollectionAssert.IsEmpty(log);
            CollectionAssert.IsEmpty(routeEvents);
        }

        [Test]
        public void Navigate_SelectsTheMatchingBottomBarDestination()
        {
            RegisterAll();

            router.Navigate(AppRoutes.Home);
            Assert.AreEqual(0, router.NavigationBar.SelectedIndex);

            router.Navigate(AppRoutes.Praias);
            Assert.AreEqual(2, router.NavigationBar.SelectedIndex);
            Assert.AreEqual(AppRoutes.Praias, router.CurrentTab);
        }

        // ---- Push / Back ----------------------------------------------------

        [Test]
        public void Push_KeepsTheOriginatingTabSelected()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);

            Assert.IsTrue(router.Push(AppRoutes.PraiaDetalhe));

            Assert.AreEqual(AppRoutes.PraiaDetalhe, router.Current);
            Assert.AreEqual(AppRoutes.Praias, router.CurrentTab);
            // Praias is index 2 — the sub-screen must NOT move or clear the pill.
            Assert.AreEqual(2, router.NavigationBar.SelectedIndex);
            Assert.AreEqual(1, router.StackDepth);
            Assert.IsTrue(router.CanGoBack);
        }

        [Test]
        public void Push_CallsOnExitBeforeOnEnterAndShowsOnlyTheSubScreen()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);
            log.Clear();

            router.Push(AppRoutes.PraiaDetalhe);

            CollectionAssert.AreEqual(
                new[] { "exit:" + AppRoutes.Praias, "enter:" + AppRoutes.PraiaDetalhe }, log);
            Assert.AreEqual(DisplayStyle.Flex, Display(detalhe));
            Assert.AreEqual(DisplayStyle.None, Display(praias));
        }

        [Test]
        public void Back_ReturnsToTheOriginAndEmptiesTheStack()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);
            router.Push(AppRoutes.PraiaDetalhe);
            log.Clear();

            Assert.IsTrue(router.Back());

            CollectionAssert.AreEqual(
                new[] { "exit:" + AppRoutes.PraiaDetalhe, "enter:" + AppRoutes.Praias }, log);
            Assert.AreEqual(AppRoutes.Praias, router.Current);
            Assert.AreEqual(0, router.StackDepth);
            Assert.IsFalse(router.CanGoBack);
            Assert.AreEqual(2, router.NavigationBar.SelectedIndex);
        }

        [Test]
        public void Back_OnAnEmptyStack_IsANoOp()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);
            log.Clear();
            routeEvents.Clear();

            Assert.IsFalse(router.Back());

            Assert.AreEqual(AppRoutes.Praias, router.Current);
            CollectionAssert.IsEmpty(log);
            CollectionAssert.IsEmpty(routeEvents);
        }

        [Test]
        public void Back_BeforeAnyNavigation_IsANoOp()
        {
            RegisterAll();
            Assert.IsFalse(router.Back());
            Assert.IsNull(router.Current);
        }

        [Test]
        public void Navigate_ClearsAPendingBackStack()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);
            router.Push(AppRoutes.PraiaDetalhe);

            router.Navigate(AppRoutes.Home);

            Assert.AreEqual(0, router.StackDepth);
            Assert.IsFalse(router.Back());
            Assert.AreEqual(AppRoutes.Home, router.Current);
        }

        [Test]
        public void Push_OntoTheCurrentScreen_IsIgnored()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);

            Assert.IsFalse(router.Push(AppRoutes.Praias));
            Assert.AreEqual(0, router.StackDepth);
        }

        // ---- Bottom bar taps -------------------------------------------------

        [Test]
        public void TappingADestination_NavigatesToItsRoute()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Home);

            // What MdNavigationBar's Clickable does on tap.
            router.NavigationBar.SelectedIndex = 1;

            Assert.AreEqual(AppRoutes.Mergulho, router.Current);
            Assert.AreEqual(AppRoutes.Mergulho, router.CurrentTab);
        }

        [Test]
        public void TappingADestinationWithNoScreen_SnapsTheBarBack()
        {
            // Home deliberately not registered — the state before HomeScreen lands.
            router.Register(mergulho);
            router.Register(praias);
            router.Navigate(AppRoutes.Praias);

            router.NavigationBar.SelectedIndex = 0; // Início

            Assert.AreEqual(AppRoutes.Praias, router.Current);
            Assert.AreEqual(2, router.NavigationBar.SelectedIndex);
        }

        [Test]
        public void TappingADestinationWhileOnASubScreen_LeavesTheSubScreen()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);
            router.Push(AppRoutes.PraiaDetalhe);

            router.NavigationBar.SelectedIndex = 1;

            Assert.AreEqual(AppRoutes.Mergulho, router.Current);
            Assert.AreEqual(0, router.StackDepth);
        }

        // ---- Edge insets -----------------------------------------------------

        [Test]
        public void EdgeInsets_ReachTheActiveScreenWithTheBarAbsorbingTheBottom()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);

            router.SetEdgeInsets(52f, 4f, 6f, 34f);

            Assert.AreEqual(52f, praias.Top);
            Assert.AreEqual(4f, praias.Left);
            Assert.AreEqual(6f, praias.Right);
            // The bar sits below the screen, so the screen reserves nothing.
            Assert.AreEqual(0f, praias.Bottom);
            Assert.AreEqual(34f, router.NavigationBar.style.paddingBottom.value.value);
        }

        [Test]
        public void EdgeInsets_AreReappliedToEachScreenOnEntry()
        {
            RegisterAll();
            router.SetEdgeInsets(52f, 0f, 0f, 34f);

            router.Navigate(AppRoutes.Home);
            Assert.AreEqual(52f, home.Top);

            router.Navigate(AppRoutes.Praias);
            Assert.AreEqual(52f, praias.Top);
        }

        [Test]
        public void EdgeInsets_GoToTheScreenWhenTheBarIsHidden()
        {
            RegisterAll();
            router.Navigate(AppRoutes.Praias);
            router.SetEdgeInsets(52f, 0f, 0f, 34f);

            router.NavigationBarVisible = false;

            Assert.AreEqual(34f, praias.Bottom);
            Assert.AreEqual(DisplayStyle.None, router.NavigationBar.style.display.value);
        }
    }
}
