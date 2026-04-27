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
        if (screenManager != null) screenManager.ShowMain();
        else Debug.LogWarning("[SplashScreen] ScreenManager reference is not assigned.");
    }
}
