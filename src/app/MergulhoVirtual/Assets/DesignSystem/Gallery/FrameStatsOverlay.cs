using UnityEngine;

namespace MergulhoVirtual.DesignSystem.Gallery
{
    /// <summary>
    /// Minimal on-screen frame stats for UI test scenes (spawned by
    /// FrameRateUnlock): average fps, 1%-low fps, and worst frame time over a
    /// rolling 2-second window. Judge smoothness by the numbers while
    /// scrolling, not by feel — "avg 88 / worst 12ms" is smooth; "avg 85 /
    /// worst 60ms" is jank hiding in a good average.
    /// Uses OnGUI so it needs no panel/theme and can't perturb the UI under test
    /// beyond a constant, uniform cost.
    /// </summary>
    sealed class FrameStatsOverlay : MonoBehaviour
    {
        const int WindowSize = 180; // ~2s at 90Hz

        readonly float[] _samples = new float[WindowSize];
        int _count, _next;
        float _refreshTimer;
        string _text = "";
        GUIStyle _style;

        void Update()
        {
            _samples[_next] = Time.unscaledDeltaTime;
            _next = (_next + 1) % WindowSize;
            if (_count < WindowSize) _count++;

            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer < 0.25f || _count < 10)
                return;
            _refreshTimer = 0f;

            var window = new float[_count];
            System.Array.Copy(_samples, window, _count);
            System.Array.Sort(window); // ascending delta = descending fps

            float sum = 0f;
            foreach (var dt in window) sum += dt;
            float avgFps = _count / sum;
            float worstMs = window[_count - 1] * 1000f;
            // 1%-low: average of the slowest 1% of frames (at least one frame).
            int lowCount = Mathf.Max(1, _count / 100);
            float lowSum = 0f;
            for (int i = _count - lowCount; i < _count; i++) lowSum += window[i];
            float lowFps = lowCount / lowSum;

            _text = $"avg {avgFps:F0} fps · 1% low {lowFps:F0} fps · worst {worstMs:F0} ms · target {Application.targetFrameRate}";
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.dpi / 8f),
                normal = { textColor = Color.yellow },
            };
            GUI.Label(new Rect(10, 10, Screen.width - 20, Screen.height * 0.1f), _text, _style);
        }
    }
}
