using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Headless Android APK build, so a device check never needs the Editor GUI.
///
///   make apk            development build (default)
///   make apk DEV=0      release build
///
/// Headless: Unity -batchmode -quit -buildTarget Android
///                 -executeMethod AndroidBuilder.BuildApk
///
/// <para><b>The scene list is explicit, on purpose.</b> ProjectSettings/
/// EditorBuildSettings.asset contains only a disabled SampleScene — MainScene
/// is not in it — so a build driven off <c>EditorBuildSettings.scenes</c> would
/// produce an APK with no scenes. Naming the scene here keeps the build
/// deterministic and avoids mutating a committed settings file as a side
/// effect of running a build.</para>
///
/// <para><b>Development vs release changes App Check.</b> AppCheckTokenProvider
/// branches on <c>Debug.isDebugBuild</c>: a development build uses the Debug
/// provider, which prints a debug token to logcat that must be registered once
/// in Firebase Console → App Check → Manage debug tokens. A release build uses
/// Play Integrity, which wants Play-Store distribution for a PLAY_RECOGNIZED
/// verdict. Development is the proven path for on-device checks; see CLAUDE.md
/// § "Debug tokens — editor and device differ".</para>
/// </summary>
public static class AndroidBuilder
{
    const string ScenePath = "Assets/Scenes/MainScene.unity";

    /// <summary>Relative to the Unity project folder when MV_BUILD_OUTPUT is unset.</summary>
    const string DefaultOutput = "Builds/Android/mergulho-virtual.apk";

    public static void BuildApk()
    {
        // -buildTarget Android on the command line does the switch before any
        // script runs; this is a guard against invoking the method without it,
        // which would silently build for the current platform instead.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Fail($"Active build target is {EditorUserBuildSettings.activeBuildTarget}, not Android. " +
                 "Pass -buildTarget Android on the Unity command line.");
            return;
        }

        if (!File.Exists(ScenePath))
        {
            Fail($"Scene not found: {ScenePath}");
            return;
        }

        var development = ReadBool("MV_BUILD_DEV", defaultValue: true);
        var output = ReadString("MV_BUILD_OUTPUT", DefaultOutput);
        if (!Path.IsPathRooted(output))
            output = Path.GetFullPath(output);

        Directory.CreateDirectory(Path.GetDirectoryName(output));

        // APK, not AAB — the device check installs over adb.
        EditorUserBuildSettings.buildAppBundle = false;

        var options = BuildOptions.None;
        if (development)
        {
            // Development alone is what flips Debug.isDebugBuild (and so the
            // App Check debug provider). Script debugging and the profiler are
            // opt-in because both add startup cost the AR-resume check measures.
            options |= BuildOptions.Development;
            if (ReadBool("MV_BUILD_SCRIPT_DEBUGGING", false)) options |= BuildOptions.AllowDebugging;
            if (ReadBool("MV_BUILD_PROFILER", false)) options |= BuildOptions.ConnectWithProfiler;
        }

        Debug.Log($"[AndroidBuilder] {(development ? "Development" : "Release")} build -> {output}\n" +
                  $"  scene:        {ScenePath}\n" +
                  $"  package:      {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}\n" +
                  $"  architecture: {PlayerSettings.Android.targetArchitectures}\n" +
                  $"  backend:      {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}\n" +
                  $"  defines:      {PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android)}");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = options,
        });

        var summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
            foreach (var msg in step.messages)
            {
                if (msg.type == LogType.Error || msg.type == LogType.Exception)
                    Debug.LogError($"[AndroidBuilder] {step.name}: {msg.content}");
            }

            Fail($"Build {summary.result} — {summary.totalErrors} error(s) in {summary.totalTime}.");
            return;
        }

        Debug.Log($"[AndroidBuilder] BUILD OK  {output}  " +
                  $"{summary.totalSize / (1024.0 * 1024.0):F1} MB  in {summary.totalTime}");

        // The Makefile greps for this line so it can report the artifact without
        // parsing Unity's log format.
        Console.WriteLine($"ANDROID-BUILD-OK path={output} bytes={summary.totalSize}");
    }

    static void Fail(string message)
    {
        Debug.LogError("[AndroidBuilder] " + message);
        EditorApplication.Exit(1);
    }

    static string ReadString(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    static bool ReadBool(string key, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        value = value.Trim();
        return value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }
}
