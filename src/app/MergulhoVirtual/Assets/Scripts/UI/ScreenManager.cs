using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// The AR-session performance gate — and, since Slice 1, nothing else.
/// Navigation moved to <see cref="MdRouter"/> (owned by <see cref="AppUiHost"/>);
/// this component only answers "should the AR session be running right now?"
/// and sets the frame-rate target accordingly.
///
/// <para>The gate itself is unchanged from the uGUI era: AR runs on the Mergulho
/// route and during the splash (so camera/tracker init hides behind the splash
/// image — the same rationale that drives the single-scene architecture), and is
/// paused everywhere else, where the render rate is unlocked to the display
/// refresh rate so UI scrolls at 90/120 Hz instead of the camera's 30.
/// <c>QualitySettings.vSyncCount = 0</c> is set once in Start because
/// <c>Application.targetFrameRate</c> is silently ignored on some Android targets
/// while vSync is on.</para>
///
/// <para>What it no longer owns: the per-screen GameObject fields, the Show*()
/// methods, the BottomNav toggle and the <c>useUiToolkitBeaches</c> strangler
/// switch. Legacy uGUI screens are activated by <see cref="LegacyUguiScreen"/>
/// through the router instead.</para>
/// </summary>
public class ScreenManager : MonoBehaviour
{
    [Tooltip("Legacy uGUI splash panel (ScreenUI/SplashScreen). AR deliberately stays alive while it is up.")]
    [SerializeField] private GameObject splashScreen;

    [Tooltip("The UI Toolkit shell. Hidden while the splash is up, then handed control.")]
    [SerializeField] private AppUiHost appUi;

    [SerializeField] private CameraFeedToInference cameraInference;
    [SerializeField] private ARSession arSession;

    [Header("Performance")]
    [Tooltip("Off = legacy behavior: AR Session and matchFrameRate stay on for the whole app, render is locked to ~30 fps everywhere. On = AR pauses on non-AR routes and the UI render rate is unlocked to the display refresh rate.")]
    [SerializeField] private bool useOptimizedPerformance = true;

    [Tooltip("UI frame rate target on non-AR routes. 0 = use the display's native refresh rate.")]
    [SerializeField] private int uiTargetFrameRate = 0;

    private const int ArTargetFrameRate = 30;
    private int displayRefreshRate = 60;

    private bool started;
    private bool splashActive;
    private string route;

    void Start()
    {
        if (useOptimizedPerformance)
        {
            // targetFrameRate is ignored on some platforms unless vSync is off.
            QualitySettings.vSyncCount = 0;
            double rate = Screen.currentResolution.refreshRateRatio.value;
            if (rate > 1.0) displayRefreshRate = (int)System.Math.Round(rate);
        }

        started = true;

        if (splashScreen != null)
        {
            splashActive = true;
            splashScreen.SetActive(true);
            if (appUi != null) appUi.SetShellVisible(false);
        }

        Apply();
    }

    /// <summary>
    /// Called by <see cref="AppUiHost"/> whenever the router's active route
    /// changes. May fire before Start (the shell builds its tree in OnEnable);
    /// the route is remembered and applied once Start has read the display rate.
    /// </summary>
    public void SetRoute(string routeKey)
    {
        route = routeKey;
        if (started) Apply();
    }

    /// <summary>
    /// Called by <see cref="SplashScreen"/> when its timer elapses: hides the
    /// splash and hands the screen to the UI Toolkit shell, which is already
    /// sitting on its initial route.
    /// </summary>
    public void OnSplashFinished()
    {
        if (!splashActive) return;
        splashActive = false;
        if (splashScreen != null) splashScreen.SetActive(false);
        if (appUi != null) appUi.SetShellVisible(true);
        Apply();
    }

    void Apply()
    {
        bool isArRoute = !splashActive && route == AppRoutes.Mergulho;

        if (cameraInference != null)
        {
            cameraInference.inferenceEnabled = isArRoute;
        }

        if (!useOptimizedPerformance) return;

        // Keep AR alive during Splash so camera/tracker init hides behind it;
        // pause it on every non-AR route to free GPU + camera for the UI.
        bool arShouldRun = isArRoute || splashActive;
        if (arSession != null && arSession.enabled != arShouldRun)
        {
            arSession.enabled = arShouldRun;
        }

        if (arSession != null)
        {
            arSession.matchFrameRateRequested = arShouldRun;
        }

        int uiRate = uiTargetFrameRate > 0 ? uiTargetFrameRate : displayRefreshRate;
        Application.targetFrameRate = arShouldRun ? ArTargetFrameRate : uiRate;
    }
}
