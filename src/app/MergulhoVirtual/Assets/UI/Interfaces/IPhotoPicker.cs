using System;

namespace MergulhoVirtual.UI
{
    /// <summary>How a photo pick ended. Cancelling is not an error — it is the
    /// single most common outcome and must not put an error on screen.</summary>
    public enum PhotoPickOutcome
    {
        /// <summary>The user chose a file; <see cref="PhotoPickResult.Path"/> is set.</summary>
        Picked,

        /// <summary>The user backed out, or the OS denied gallery access — the
        /// native picker reports both the same way, so they are one state here.</summary>
        Cancelled,

        /// <summary>The picker itself failed; <see cref="PhotoPickResult.ErrorMessage"/> says why.</summary>
        Failed,
    }

    /// <summary>The result of one pick. Immutable; constructed by the adapter.</summary>
    public sealed class PhotoPickResult
    {
        public readonly PhotoPickOutcome Outcome;

        /// <summary>Local file path, only when <see cref="PhotoPickOutcome.Picked"/>.</summary>
        public readonly string Path;

        /// <summary>Size of the picked file in bytes — the form enforces the
        /// design's 20 MB limit (§8.5) and only the platform side can stat the
        /// file. 0 when unknown.</summary>
        public readonly long SizeBytes;

        /// <summary>Human-readable failure reason, only when <see cref="PhotoPickOutcome.Failed"/>.</summary>
        public readonly string ErrorMessage;

        PhotoPickResult(PhotoPickOutcome outcome, string path, long sizeBytes, string error)
        {
            Outcome = outcome;
            Path = path;
            SizeBytes = sizeBytes;
            ErrorMessage = error;
        }

        public static PhotoPickResult Picked(string path, long sizeBytes) =>
            new PhotoPickResult(PhotoPickOutcome.Picked, path, sizeBytes, null);

        public static PhotoPickResult Cancelled() =>
            new PhotoPickResult(PhotoPickOutcome.Cancelled, null, 0, null);

        public static PhotoPickResult Failed(string error) =>
            new PhotoPickResult(PhotoPickOutcome.Failed, null, 0, error);
    }

    /// <summary>
    /// Opens the device photo gallery. A platform call — the native picker is an
    /// Android/iOS plugin and the Editor path is a file dialog — so it is a seam
    /// rather than something the ViewModel does, and a fake makes the form fully
    /// testable.
    ///
    /// <para><b>Asynchronous with no guarantee of ordering or timing.</b> The
    /// callback arrives on the Unity main thread but possibly many frames later
    /// (the user is in another app meanwhile), and may never arrive if the OS
    /// kills the app mid-pick. Callers must tolerate a result that lands after
    /// they have moved on.</para>
    ///
    /// <para><b>EXIF is load-bearing</b> (CLAUDE.md, "Sighting reports"): the
    /// returned file is the original bytes, and nothing on the path to upload may
    /// decode and re-encode it.</para>
    /// </summary>
    public interface IPhotoPicker
    {
        void PickPhoto(Action<PhotoPickResult> onResult);
    }
}
