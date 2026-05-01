using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class ScreenManager : MonoBehaviour
{
    [SerializeField] private GameObject splashScreen;
    [SerializeField] private GameObject mainScreen;
    [SerializeField] private GameObject beachesScreen;
    [SerializeField] private GameObject animalsScreen;
    [SerializeField] private GameObject aboutScreen;
    [SerializeField] private GameObject bottomNav;

    [SerializeField] private CameraFeedToInference cameraInference;
    [SerializeField] private ARSession arSession;

    [Header("Performance")]
    [Tooltip("Off = legacy behavior: AR Session and matchFrameRate stay on for the whole app, render is locked to ~30 fps everywhere. On = AR pauses on non-AR screens and the UI render rate is unlocked to the display refresh rate.")]
    [SerializeField] private bool useOptimizedPerformance = true;

    [Tooltip("UI frame rate target on non-AR screens. 0 = use the display's native refresh rate.")]
    [SerializeField] private int uiTargetFrameRate = 0;

    private const int ArTargetFrameRate = 30;
    private int displayRefreshRate = 60;

    void Start()
    {
        if (useOptimizedPerformance)
        {
            // targetFrameRate is ignored on some platforms unless vSync is off.
            QualitySettings.vSyncCount = 0;
            double rate = Screen.currentResolution.refreshRateRatio.value;
            if (rate > 1.0) displayRefreshRate = (int)System.Math.Round(rate);
        }

        if (splashScreen != null) ShowSplash();
        else ShowMain();
    }

    public void ShowSplash()  => Show(splashScreen);
    public void ShowMain()    => Show(mainScreen);
    public void ShowBeaches() => Show(beachesScreen);
    public void ShowAnimals() => Show(animalsScreen);
    public void ShowAbout()   => Show(aboutScreen);

    void Show(GameObject target)
    {
        if (splashScreen  != null) splashScreen.SetActive(target == splashScreen);
        if (mainScreen    != null) mainScreen.SetActive(target == mainScreen);
        if (beachesScreen != null) beachesScreen.SetActive(target == beachesScreen);
        if (animalsScreen != null) animalsScreen.SetActive(target == animalsScreen);
        if (aboutScreen   != null) aboutScreen.SetActive(target == aboutScreen);

        if (bottomNav != null) bottomNav.SetActive(target != splashScreen);

        bool isArScreen = (target == mainScreen);

        if (cameraInference != null)
        {
            cameraInference.inferenceEnabled = isArScreen;
        }

        if (useOptimizedPerformance)
        {
            ApplyOptimizedFrameRate(target, isArScreen);
        }
    }

    void ApplyOptimizedFrameRate(GameObject target, bool isArScreen)
    {
        // Keep AR alive during Splash so camera/tracker init hides behind it;
        // pause it on Beaches/Animals/About to free GPU + camera for the UI.
        bool arShouldRun = isArScreen || (target == splashScreen);
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
