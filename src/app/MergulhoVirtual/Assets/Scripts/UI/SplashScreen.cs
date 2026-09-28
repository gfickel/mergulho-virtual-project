using UnityEngine;

public class SplashScreen : MonoBehaviour
{
    [SerializeField] private ScreenManager screenManager;
    [SerializeField] private float duration = 2f;

    void OnEnable()
    {
        Invoke(nameof(GoToMain), duration);
    }

    void OnDisable()
    {
        CancelInvoke();
    }

    void GoToMain()
    {
        // ScreenManager hides this panel and hands the screen to the UI Toolkit
        // shell, which is already sitting on its initial route.
        if (screenManager != null) screenManager.OnSplashFinished();
        else Debug.LogWarning("[SplashScreen] ScreenManager reference is not assigned.");
    }
}
