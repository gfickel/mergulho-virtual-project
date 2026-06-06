using System;
using System.Threading.Tasks;
using UnityEngine;

#if FIREBASE_APPCHECK_ENABLED
using Firebase;
using Firebase.AppCheck;
#endif

/// <summary>
/// Fetches Firebase App Check tokens that authenticate the Unity client to the
/// FastAPI backend at /api/v1/*. Tokens prove the request came from a real
/// install of the signed APK on a non-tampered device — App Check is the
/// canonical defense for unauthenticated public mobile apps, where any static
/// API key baked into the APK gets extracted within minutes.
///
/// Until the Firebase Unity SDK is installed AND the FIREBASE_APPCHECK_ENABLED
/// scripting define is added (Project Settings → Player → Other Settings →
/// Scripting Define Symbols), GetTokenAsync() returns null and callers send no
/// header. The backend ignores the header in BACKEND_DEBUG=1 mode, so local LAN
/// development keeps working with no Firebase project.
///
/// Tests can bypass Firebase entirely by setting <see cref="TokenOverride"/> —
/// matches the JobQueue.IsOnlineOverride pattern, exposed to
/// Assembly-CSharp-Editor via InternalsVisibleTo.
/// </summary>
public static class AppCheckTokenProvider
{
    internal static Func<bool, Task<string>> TokenOverride;

    private static readonly object _initLock = new object();
    private static Task _initTask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapOnLoad()
    {
        // Kick init off in the background so the first GetTokenAsync caller
        // doesn't pay the full latency. Errors surface to whichever caller
        // awaits the task first.
        _ = EnsureInitialized();
    }

    private static Task EnsureInitialized()
    {
        if (_initTask != null) return _initTask;
        lock (_initLock)
        {
            if (_initTask == null) _initTask = InitializeAsync();
        }
        return _initTask;
    }

    private static async Task InitializeAsync()
    {
#if FIREBASE_APPCHECK_ENABLED
        var status = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (status != DependencyStatus.Available)
            throw new InvalidOperationException(
                $"Firebase dependencies unavailable: {status}. App Check tokens will fail.");

        // Editor/desktop uses the debug provider, but UNLIKE Android it does NOT
        // auto-generate and print a token — you must CREATE a debug token in
        // Firebase Console → App Check → (your app) → Manage debug tokens, then
        // feed it to the SDK via SetDebugToken() *before* setting the factory.
        // We read it from the FIREBASE_APPCHECK_DEBUG_TOKEN env var or a
        // gitignored <projectRoot>/firebase-appcheck-debug-token.txt so the
        // secret never lands in source control or a shipped build. Without it
        // the backend rejects every editor request as 401.
        // Play Integrity (Android release) and App Attest (iOS) bind tokens to a
        // real install on a non-tampered device — no per-developer setup needed.
#if UNITY_EDITOR
        var editorDebugToken = LoadEditorDebugToken();
        if (!string.IsNullOrWhiteSpace(editorDebugToken))
            DebugAppCheckProviderFactory.Instance.SetDebugToken(editorDebugToken);
        else
            Debug.LogWarning(
                "[AppCheck] No editor debug token found — backend will return 401. " +
                "Create one in Firebase Console → App Check → Manage debug tokens, then set " +
                "env var FIREBASE_APPCHECK_DEBUG_TOKEN or write it to " +
                "<projectRoot>/firebase-appcheck-debug-token.txt and restart the editor.");
        FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
#elif UNITY_ANDROID
        // Development builds (sideloaded debug APKs) aren't PLAY_RECOGNIZED, so
        // real Play Integrity attestation would fail App Check. Use the debug
        // provider instead — the device prints a debug token to logcat on first
        // run; register it in Firebase Console → App Check → Manage debug tokens.
        // Release builds (installed from Play) use real Play Integrity.
        if (Debug.isDebugBuild)
            FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
        else
            FirebaseAppCheck.SetAppCheckProviderFactory(PlayIntegrityProviderFactory.Instance);
#elif UNITY_IOS
        FirebaseAppCheck.SetAppCheckProviderFactory(AppAttestProviderFactory.Instance);
#else
        FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
#endif

        // Force a token fetch up-front to warm the SDK cache and surface
        // provider-init failures while we still have logs in front of us
        // rather than mid-upload from a queued ReportSightingJob.
        await FirebaseAppCheck.DefaultInstance.GetAppCheckTokenAsync(false);
#else
        // Firebase not installed yet. Init is a no-op and GetTokenAsync
        // returns null so the rest of the app keeps compiling and running
        // against a BACKEND_DEBUG=1 backend.
        await Task.CompletedTask;
#endif
    }

#if FIREBASE_APPCHECK_ENABLED && UNITY_EDITOR
    /// <summary>
    /// Loads the App Check debug token for editor/desktop runs. The editor's
    /// debug provider can't self-generate one (that's an on-device-only feature),
    /// so the token has to be created manually in the Firebase Console and
    /// supplied here. Resolution order: FIREBASE_APPCHECK_DEBUG_TOKEN env var
    /// first (works for terminal/CI launches), then a gitignored file at the
    /// project root (works for Unity Hub GUI launches that don't inherit a shell
    /// environment). Kept out of source control either way. Returns null if
    /// neither is present.
    /// </summary>
    private static string LoadEditorDebugToken()
    {
        var fromEnv = Environment.GetEnvironmentVariable("FIREBASE_APPCHECK_DEBUG_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        // Application.dataPath is <projectRoot>/Assets; go one up so the file
        // lives at the project root, outside Assets — never bundled into a build.
        var path = System.IO.Path.Combine(Application.dataPath, "..", "firebase-appcheck-debug-token.txt");
        if (System.IO.File.Exists(path))
        {
            var fromFile = System.IO.File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(fromFile)) return fromFile;
        }
        return null;
    }
#endif

    /// <summary>
    /// Returns a valid App Check token, or null if Firebase isn't installed.
    /// On a 401 retry, pass <paramref name="forceRefresh"/>=true to bypass the
    /// SDK token cache — the cached token is what the backend just rejected.
    /// </summary>
    public static async Task<string> GetTokenAsync(bool forceRefresh = false)
    {
        if (TokenOverride != null) return await TokenOverride(forceRefresh);

#if FIREBASE_APPCHECK_ENABLED
        await EnsureInitialized();
        var result = await FirebaseAppCheck.DefaultInstance.GetAppCheckTokenAsync(forceRefresh);
        return result.Token;
#else
        await EnsureInitialized();
        return null;
#endif
    }
}
