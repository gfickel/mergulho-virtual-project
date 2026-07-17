using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// One GNSS fix, already reduced to plain values (safe to copy across threads).
/// </summary>
[Serializable]
public struct GnssFix
{
    public double latitude;
    public double longitude;
    public float horizontalAccuracy; // meters (1-sigma-ish, as reported)
    public double timestampMs;       // epoch ms (native) or device time (fallback)
    public float speedMps;           // course speed over ground
    public bool hasSpeed;
    public float bearingDeg;         // course over ground, degrees from true north
    public bool hasBearing;
    public int satellites;           // 0 when unknown
}

/// <summary>
/// Best-available GNSS source for the Kalman fusion.
///
/// On an Android device it bypasses Unity's LocationService and subscribes
/// directly to the platform LocationManager's raw GPS_PROVIDER, which gives:
///   - faster updates (~1–2 Hz vs Unity's throttled polling),
///   - true per-fix horizontal accuracy,
///   - course-over-ground speed + bearing (used to auto-calibrate the AR→ENU
///     heading — far better than the magnetometer),
///   - satellite count when the platform reports it (HUD/telemetry).
///
/// Everywhere else (editor, iOS, or if the native hookup fails) it falls back
/// to polling Input.location.lastData — which GPSHandler already starts — so
/// the fusion still works, just with coarser data.
///
/// Requires ACCESS_FINE_LOCATION; GPSHandler already requests it at runtime.
/// This component waits for the grant before subscribing.
/// </summary>
public class GnssProvider : MonoBehaviour
{
    [Header("Native GNSS request (Android)")]
    [Tooltip("Minimum interval between native GNSS fixes, milliseconds.")]
    public long minTimeMs = 500;

    [Tooltip("Minimum distance between native GNSS fixes, meters (0 = every fix).")]
    public float minDistanceM = 0f;

    /// <summary>Latest accepted fix. Only meaningful when FixCount > 0.</summary>
    public GnssFix Latest { get; private set; }

    /// <summary>Increments once per new fix; consumers track the last count they processed.</summary>
    public int FixCount { get; private set; }

    public bool HasFix => FixCount > 0;

    /// <summary>True when fixes come from the raw Android GPS provider.</summary>
    public bool UsingNativeGnss { get; private set; }

    /// <summary>Seconds since the last fix arrived (float.MaxValue before the first).</summary>
    public float FixAgeSeconds =>
        FixCount > 0 ? Time.realtimeSinceStartup - lastFixRealtime : float.MaxValue;

    float lastFixRealtime;
    double lastFallbackTimestamp;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject locationManager;
    GpsLocationListener listener;

    /// <summary>
    /// android.location.LocationListener proxy. Callbacks arrive on the Android
    /// main thread, NOT Unity's — extract primitives immediately, hand the plain
    /// struct to Unity's Update() through a lock.
    /// </summary>
    class GpsLocationListener : AndroidJavaProxy
    {
        readonly object sync = new object();
        GnssFix pending;
        bool hasPending;

        public GpsLocationListener() : base("android.location.LocationListener") { }

        void onLocationChanged(AndroidJavaObject location)
        {
            if (location == null) return;
            try
            {
                var fix = new GnssFix
                {
                    latitude = location.Call<double>("getLatitude"),
                    longitude = location.Call<double>("getLongitude"),
                    horizontalAccuracy = location.Call<bool>("hasAccuracy")
                        ? location.Call<float>("getAccuracy") : 99f,
                    timestampMs = location.Call<long>("getTime"),
                    hasSpeed = location.Call<bool>("hasSpeed"),
                    hasBearing = location.Call<bool>("hasBearing"),
                };
                if (fix.hasSpeed) fix.speedMps = location.Call<float>("getSpeed");
                if (fix.hasBearing) fix.bearingDeg = location.Call<float>("getBearing");
                try
                {
                    // The GPS provider usually attaches the used-satellite count.
                    using (var extras = location.Call<AndroidJavaObject>("getExtras"))
                    {
                        if (extras != null)
                            fix.satellites = extras.Call<int>("getInt", "satellites", 0);
                    }
                }
                catch { /* extras are best-effort */ }

                lock (sync) { pending = fix; hasPending = true; }
            }
            finally
            {
                location.Dispose();
            }
        }

        // LocationListener's other members — must exist so the java.lang.reflect
        // proxy dispatch finds them instead of throwing.
        void onStatusChanged(string provider, int status, AndroidJavaObject extras) { }
        void onProviderEnabled(string provider) { }
        void onProviderDisabled(string provider) { }
        void onFlushComplete(int requestCode) { }

        public bool TryTake(out GnssFix fix)
        {
            lock (sync)
            {
                fix = pending;
                if (!hasPending) return false;
                hasPending = false;
                return true;
            }
        }
    }
#endif

    IEnumerator Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // GPSHandler fires the permission dialog; just wait for the grant.
        while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            yield return new WaitForSeconds(0.5f);

        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var looperClass = new AndroidJavaClass("android.os.Looper"))
            using (var mainLooper = looperClass.CallStatic<AndroidJavaObject>("getMainLooper"))
            {
                locationManager = activity.Call<AndroidJavaObject>("getSystemService", "location");
                listener = new GpsLocationListener();
                locationManager.Call("requestLocationUpdates",
                    "gps", minTimeMs, minDistanceM, listener, mainLooper);
                UsingNativeGnss = true;
                Debug.Log("GnssProvider: subscribed to native GPS provider.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("GnssProvider: native GNSS unavailable, falling back to Input.location. " + e.Message);
            UsingNativeGnss = false;
        }
#else
        yield break;
#endif
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (UsingNativeGnss)
        {
            if (listener != null && listener.TryTake(out var fix))
                Publish(fix);
            return;
        }
#endif
        // Fallback: piggyback on Unity's LocationService (started by GPSHandler).
        if (Input.location.status != LocationServiceStatus.Running) return;
        var data = Input.location.lastData;
        if (data.timestamp <= lastFallbackTimestamp) return;
        lastFallbackTimestamp = data.timestamp;
        Publish(new GnssFix
        {
            latitude = data.latitude,
            longitude = data.longitude,
            horizontalAccuracy = data.horizontalAccuracy,
            timestampMs = data.timestamp * 1000.0,
        });
    }

    void Publish(GnssFix fix)
    {
        Latest = fix;
        FixCount++;
        lastFixRealtime = Time.realtimeSinceStartup;
    }

    void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (locationManager != null && listener != null)
        {
            try { locationManager.Call("removeUpdates", listener); }
            catch { }
        }
        locationManager?.Dispose();
        locationManager = null;
        listener = null;
#endif
    }
}
