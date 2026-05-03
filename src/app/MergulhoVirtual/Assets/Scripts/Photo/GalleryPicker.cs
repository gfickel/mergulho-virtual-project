using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Cross-platform gallery picker. On Android/iOS uses yasirkula/UnityNativeGallery
/// (preserves EXIF — Android does a raw byte copy via ContentResolver, iOS exports
/// the original PHAsset). In Editor falls back to EditorUtility.OpenFilePanel.
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
}
