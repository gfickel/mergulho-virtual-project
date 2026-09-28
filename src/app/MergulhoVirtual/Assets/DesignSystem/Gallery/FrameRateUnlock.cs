using UnityEngine;
using UnityEngine.SceneManagement;

namespace MergulhoVirtual.DesignSystem.Gallery
{
    /// <summary>
    /// Unity's Android default caps rendering at 30 fps, which makes any UI test
    /// scene feel sluggish on 90/120 Hz panels and skews perf judgments (this is
    /// what made the App UI spike look slow). Unlock to the display's native
    /// refresh rate — but ONLY in UI test scenes: in MainScene, ScreenManager
    /// owns the frame-rate policy (30 on AR screens for battery).
    /// </summary>
    static class FrameRateUnlock
    {
        static readonly string[] TestScenePrefixes = { "GalleryScene", "UI Kit" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            foreach (var prefix in TestScenePrefixes)
            {
                if (sceneName.StartsWith(prefix))
                {
                    // targetFrameRate is silently ignored on some Android targets
                    // while vSync is on (same reasoning as ScreenManager.Start).
                    QualitySettings.vSyncCount = 0;
                    int refreshRate = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
                    Application.targetFrameRate = Mathf.Max(refreshRate, 60);
                    Debug.Log($"[FrameRateUnlock] {sceneName}: targetFrameRate={Application.targetFrameRate}");

                    var statsHost = new GameObject("FrameStatsOverlay");
                    statsHost.AddComponent<FrameStatsOverlay>();
                    return;
                }
            }
        }
    }
}
