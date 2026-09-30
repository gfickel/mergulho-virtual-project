using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Cross-platform gallery picker. On Android/iOS uses yasirkula/UnityNativeGallery
/// (preserves EXIF — Android does a raw byte copy via ContentResolver, iOS exports
/// the original PHAsset). In Editor falls back to EditorUtility.OpenFilePanel. On
/// standalone (the Windows review build) there is no gallery and no native dialog, so
/// it cycles through the JPEGs bundled in StreamingAssets/SamplePhotos.
///
/// Always returns a path to a file the caller owns and is free to read/move/delete.
/// On device, NativeGallery already wrote the picked image into the app's cache;
/// to make the path stable across app restarts, copy it into persistentDataPath
/// before enqueueing it as a Job.
/// </summary>
public static class GalleryPicker
{
    public delegate void PickCallback(string localPath, string error);

    public static void PickImage(PickCallback callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));

#if UNITY_EDITOR
        PickInEditor(callback);
#elif UNITY_ANDROID || UNITY_IOS
        PickOnDevice(callback);
#elif UNITY_STANDALONE
        PickSampleInStandalone(callback);
#else
        callback(null, "gallery picker not supported on this platform");
#endif
    }

#if UNITY_EDITOR
    static void PickInEditor(PickCallback callback)
    {
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "Pick image", "",
            "jpg,jpeg,png,heic,webp");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            callback(null, "cancelled");
            return;
        }
        callback(path, null);
    }
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    static void PickOnDevice(PickCallback callback)
    {
        // NativeGallery.GetImageFromGallery handles permission requests internally
        // and returns void; both user cancel and permission denial surface as a null
        // path in the callback.
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                callback(null, "cancelled");
                return;
            }
            callback(path, null);
        }, "Selecione uma foto", "image/*");
    }
#endif

#if UNITY_STANDALONE && !UNITY_EDITOR
    // Cycles so a second pick in the same session returns a different photo — the
    // Avistamentos form is worth reviewing with more than one thumbnail.
    static int sampleCursor;

    /// <summary>
    /// Desktop review builds have no gallery. Returns one of the JPEGs bundled under
    /// StreamingAssets/SamplePhotos, copied somewhere the caller owns.
    /// </summary>
    static void PickSampleInStandalone(PickCallback callback)
    {
        // On Windows/macOS/Linux StreamingAssets is a plain directory under
        // <exe>_Data, so ordinary File IO reads it. UnityWebRequest is only needed
        // where StreamingAssets lives inside an archive (Android APK, WebGL).
        string dir = Path.Combine(Application.streamingAssetsPath, "SamplePhotos");
        string[] files = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.jpg")
            : Array.Empty<string>();
        if (files.Length == 0)
        {
            callback(null, "no sample photos bundled");
            return;
        }
        Array.Sort(files);                                 // stable order across runs
        string src = files[sampleCursor++ % files.Length];

        // Copy rather than hand back the bundled path: this method's contract is "a
        // path to a file the caller owns and is free to read/move/delete", and
        // ReportSightingJob deletes the file on upload success — it must never delete
        // a bundled asset. A fresh GUID name also keeps concurrent picks from colliding.
        //
        // A plain byte copy, NEVER a Texture2D.LoadImage -> EncodeToJPG round-trip:
        // that would strip EXIF (see "Sighting reports" in CLAUDE.md) and make the
        // review build exercise a different upload path than the real one.
        string dst = Path.Combine(Application.temporaryCachePath,
                                  "sample-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            File.Copy(src, dst, true);
        }
        catch (Exception e)
        {
            callback(null, e.Message);
            return;
        }
        callback(dst, null);
    }
#endif
}
