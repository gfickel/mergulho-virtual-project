using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Headless Windows Standalone build of the throwaway <b>design-review</b> player —
/// Step 5 of <c>docs/windows-review-build.md</c>.
///
///   make win-build
///
/// Headless: Unity -batchmode -quit -nographics -buildTarget Win64
///                 -executeMethod WindowsReviewBuild.BuildHeadless
///
/// <para><b>Mono, never IL2CPP.</b> The WindowsStandaloneSupport module installed on
/// this machine ships <c>Variations/win64_{development,nondevelopment}_mono</c> only —
/// there is no <c>_il2cpp</c> variation, because Windows IL2CPP needs a Windows host
/// toolchain. Requesting IL2CPP here fails on a missing toolchain, so the backend is
/// asserted explicitly below rather than left to whatever the project happens to
/// default to.</para>
///
/// <para><b>The scene list is explicit, on purpose</b> — same reason as
/// <c>AndroidBuilder</c>: <c>EditorBuildSettings</c> holds only a disabled
/// SampleScene, so a build driven off it would ship no scenes.</para>
///
/// <para><b>Nothing here is committed.</b> The review player needs a portrait,
/// resizable, windowed player; the repo's committed profile is the Android one. Every
/// setting this script touches is snapshotted before the build and put back in a
/// <c>finally</c>, so <c>ProjectSettings/ProjectSettings.asset</c> is byte-unchanged
/// when the process exits and there is nothing to accidentally commit — see the
/// doc's "Do not" section, first bullet.</para>
///
/// <para>Exit codes: <c>0</c> success, <c>1</c> the build itself failed,
/// <c>2</c> pre-flight failed (module missing, scene missing) so no build was run.</para>
/// </summary>
public static class WindowsReviewBuild
{
    const string ScenePath = "Assets/Scenes/MainScene.unity";

    /// <summary>Relative to the Unity project folder, which is what the project's
    /// .gitignore excludes — resolve it against that, not the process CWD (which is
    /// the repo root when invoked from the Makefile).</summary>
    const string OutputRelative = "Builds/Windows/MergulhoVirtual.exe";

    // Step 2. Starting window only: large enough that the tallest device preset
    // (932 dp) fits at 1x with bezel to spare. The designer resizes freely from
    // there and the runtime refit rescales the device frame to whatever fits;
    // nothing calls Screen.SetResolution.
    const int WindowWidth = 700;
    const int WindowHeight = 1000;

    /// <summary>Every player setting this script overrides, captured before the build.</summary>
    struct SavedSettings
    {
        public int DefaultScreenWidth;
        public int DefaultScreenHeight;
        public FullScreenMode FullScreen;
        public bool ResizableWindow;
        public bool RunInBackground;
        public ScriptingImplementation StandaloneBackend;

        /// <summary>False when the Standalone backend was already Mono2x and so was
        /// left untouched — see <see cref="ApplyReviewSettings"/>.</summary>
        public bool StandaloneBackendWritten;
    }

    public static void BuildHeadless()
    {
        // BuildPipeline.IsBuildTargetSupported has no NamedBuildTarget overload in
        // Unity 6 — BuildTargetGroup is the only signature it offers, and it is not
        // marked obsolete. Everywhere else below uses NamedBuildTarget.
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
        {
            Debug.LogError(
                "[WindowsReviewBuild] StandaloneWindows64 is not supported by this editor install: the " +
                "'WindowsStandaloneSupport' playback engine is missing from " +
                "<editor>/Editor/Data/PlaybackEngines/. Install it (docs/windows-review-build.md, " +
                "\"Prerequisite — build module\") and re-run. Nothing was built.");
            EditorApplication.Exit(2);
            return;
        }

        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"[WindowsReviewBuild] Scene not found: {ScenePath}. Nothing was built.");
            EditorApplication.Exit(2);
            return;
        }

        // -buildTarget Win64 on the command line does the switch before any script
        // runs. Without it BuildPlayer switches platforms mid-build, which triggers a
        // full reimport and is slow enough to look like a hang.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.LogWarning(
                $"[WindowsReviewBuild] Active build target is {EditorUserBuildSettings.activeBuildTarget}, " +
                "not StandaloneWindows64 — pass -buildTarget Win64 on the Unity command line to avoid a " +
                "full reimport.");
        }

        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var output = Path.GetFullPath(Path.Combine(projectRoot, OutputRelative));
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        int exitCode;
        var saved = ApplyReviewSettings();
        try
        {
            exitCode = RunBuild(output);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WindowsReviewBuild] Build threw: {e}");
            exitCode = 1;
        }
        finally
        {
            // Outside the try/finally the player settings would leak into the working
            // tree as a Standalone-shaped ProjectSettings.asset diff.
            RestoreSettings(saved);
        }

        EditorApplication.Exit(exitCode);
    }

    static int RunBuild(string output)
    {
        Debug.Log($"[WindowsReviewBuild] Development build -> {output}\n" +
                  $"  scene:   {ScenePath}\n" +
                  $"  window:  {PlayerSettings.defaultScreenWidth}x{PlayerSettings.defaultScreenHeight} " +
                  $"{PlayerSettings.fullScreenMode}, resizable={PlayerSettings.resizableWindow}\n" +
                  $"  backend: {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)}\n" +
                  $"  defines: {PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone)}");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            // Development keeps Player.log and the profiler, which the "hand the
            // designer this list" instructions in the doc ask bug reports to attach.
            options = BuildOptions.Development,
        });

        var summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[WindowsReviewBuild] Build {summary.result} — " +
                           $"{summary.totalErrors} error(s), {summary.totalWarnings} warning(s), " +
                           $"in {summary.totalTime}.");

            var shown = 0;
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type != LogType.Error && msg.type != LogType.Exception) continue;
                    Debug.LogError($"[WindowsReviewBuild] {step.name}: {msg.content}");
                    if (++shown >= 10)
                    {
                        Debug.LogError("[WindowsReviewBuild] (further errors truncated — see the full log)");
                        return 1;
                    }
                }
            }

            return 1;
        }

        var sizeMb = (summary.totalSize / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
        Debug.Log($"[WindowsReviewBuild] BUILD OK  {output}  {sizeMb} MB  in {summary.totalTime}");

        // One grep-able line the Makefile awks, same contract as UI-SHOTS-SUMMARY.
        // sizeMB is invariant-formatted and comes first so the path, which is the only
        // field that could ever contain a space, is last.
        Console.WriteLine($"WIN-BUILD-SUMMARY sizeMB={sizeMb} path={output}");
        return 0;
    }

    static SavedSettings ApplyReviewSettings()
    {
        var saved = new SavedSettings
        {
            DefaultScreenWidth = PlayerSettings.defaultScreenWidth,
            DefaultScreenHeight = PlayerSettings.defaultScreenHeight,
            FullScreen = PlayerSettings.fullScreenMode,
            ResizableWindow = PlayerSettings.resizableWindow,
            RunInBackground = PlayerSettings.runInBackground,
            StandaloneBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone),
            StandaloneBackendWritten = false,
        };

        PlayerSettings.defaultScreenWidth = WindowWidth;
        PlayerSettings.defaultScreenHeight = WindowHeight;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;   // the device frame refits on resize
        PlayerSettings.runInBackground = true;   // survives alt-tabbing to Figma

        // Mono2x is asserted, not assumed (there is no Windows IL2CPP toolchain on a
        // Linux host). It is also the Unity default, and ProjectSettings.asset has no
        // Standalone entry in its scriptingBackend map today — writing one that says
        // "Mono2x" would mean the same thing but would ADD a line to a committed file,
        // which the restore could not take back out through the public API. So: verify
        // when it already resolves to Mono2x, override only when it does not (in which
        // case the key exists and the restore is exact).
        if (saved.StandaloneBackend != ScriptingImplementation.Mono2x)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            saved.StandaloneBackendWritten = true;
            Debug.Log($"[WindowsReviewBuild] Standalone scripting backend {saved.StandaloneBackend} -> Mono2x " +
                      "for this build only (Windows IL2CPP needs a Windows host).");
        }

        return saved;
    }

    /// <summary>
    /// Puts every overridden setting back, so ProjectSettings.asset ends up
    /// byte-unchanged. Without this the Windows profile leaks into the working tree
    /// and eventually into a commit, where a stray fullscreenMode/resizableWindow is
    /// harmless for Android today and a confusing landmine later.
    /// </summary>
    static void RestoreSettings(SavedSettings saved)
    {
        PlayerSettings.defaultScreenWidth = saved.DefaultScreenWidth;
        PlayerSettings.defaultScreenHeight = saved.DefaultScreenHeight;
        PlayerSettings.fullScreenMode = saved.FullScreen;
        PlayerSettings.resizableWindow = saved.ResizableWindow;
        PlayerSettings.runInBackground = saved.RunInBackground;

        if (saved.StandaloneBackendWritten)
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, saved.StandaloneBackend);

        // Flush the restored values to disk before EditorApplication.Exit, which does
        // not run a normal save path.
        AssetDatabase.SaveAssets();
        Debug.Log("[WindowsReviewBuild] Player settings restored (ProjectSettings.asset unchanged).");
    }
}
