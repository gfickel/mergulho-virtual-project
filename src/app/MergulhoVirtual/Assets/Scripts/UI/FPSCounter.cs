using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Profiling;
using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private float windowSeconds = 5f;
    [SerializeField] private float currentSmoothingSeconds = 0.5f;
    [SerializeField] private float refreshInterval = 0.25f;

    private readonly Queue<Sample> samples = new Queue<Sample>();
    private float nextRefreshTime;

    private ProfilerRecorder mainThreadRecorder;
    private ProfilerRecorder renderThreadRecorder;
    private ProfilerRecorder gpuFrameRecorder;
    private ProfilerRecorder drawCallsRecorder;
    private ProfilerRecorder setPassRecorder;
    private ProfilerRecorder trianglesRecorder;
    private ProfilerRecorder gcAllocRecorder;
    private ProfilerRecorder gcReservedRecorder;
    private ProfilerRecorder systemMemoryRecorder;

    private readonly StringBuilder sb = new StringBuilder(512);

    private struct Sample
    {
        public float time;
        public float fps;
    }

    void Reset()
    {
        label = GetComponent<TMP_Text>();
    }

    void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        renderThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
        gpuFrameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time", 15);
        drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        gcReservedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
        systemMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
    }

    void OnDisable()
    {
        mainThreadRecorder.Dispose();
        renderThreadRecorder.Dispose();
        gpuFrameRecorder.Dispose();
        drawCallsRecorder.Dispose();
        setPassRecorder.Dispose();
        trianglesRecorder.Dispose();
        gcAllocRecorder.Dispose();
        gcReservedRecorder.Dispose();
        systemMemoryRecorder.Dispose();
    }

    void Update()
    {
        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        samples.Enqueue(new Sample { time = now, fps = 1f / dt });

        float cutoff = now - windowSeconds;
        while (samples.Count > 0 && samples.Peek().time < cutoff)
            samples.Dequeue();

        if (label == null || now < nextRefreshTime) return;
        nextRefreshTime = now + refreshInterval;

        float min = float.PositiveInfinity;
        float max = 0f;
        float currentSum = 0f;
        int currentCount = 0;
        float currentCutoff = now - currentSmoothingSeconds;

        foreach (Sample s in samples)
        {
            if (s.fps < min) min = s.fps;
            if (s.fps > max) max = s.fps;
            if (s.time >= currentCutoff)
            {
                currentSum += s.fps;
                currentCount++;
            }
        }

        float current = currentCount > 0 ? currentSum / currentCount : 1f / dt;

        sb.Clear();
        sb.AppendFormat("FPS {0:0}  min {1:0} / max {2:0}\n", current, min, max);
        sb.AppendFormat("CPU {0:0.0} ms  Render {1:0.0} ms  GPU {2:0.0} ms\n",
            NsToMs(AvgRecorder(mainThreadRecorder)),
            NsToMs(AvgRecorder(renderThreadRecorder)),
            NsToMs(AvgRecorder(gpuFrameRecorder)));
        sb.AppendFormat("Draws {0}  SetPass {1}  Tris {2}\n",
            drawCallsRecorder.LastValue, setPassRecorder.LastValue, trianglesRecorder.LastValue);
        sb.AppendFormat("GC/frame {0:0.0} KB  GC heap {1:0.0} MB  Sys {2:0.0} MB",
            gcAllocRecorder.LastValue / 1024f,
            gcReservedRecorder.LastValue / (1024f * 1024f),
            systemMemoryRecorder.LastValue / (1024f * 1024f));

        label.text = sb.ToString();
    }

    private static double AvgRecorder(ProfilerRecorder r)
    {
        if (!r.Valid) return 0;
        int count = r.Capacity;
        if (count == 0) return 0;
        double sum = 0;
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            var s = r.GetSample(i);
            if (s.Value > 0) { sum += s.Value; n++; }
        }
        return n > 0 ? sum / n : 0;
    }

    private static double NsToMs(double ns) => ns * 1e-6;
}
