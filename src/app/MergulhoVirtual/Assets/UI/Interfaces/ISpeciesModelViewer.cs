using System;
using UnityEngine;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// The 3D turntable viewer, as the Espécie screen sees it — Decision D1's
    /// "the 3D viewer comes with the species sub-screen".
    ///
    /// <para><b>Everything about the rig stays in Assembly-CSharp.</b>
    /// <c>AnimalViewerRig</c> is a scene-root GameObject at world (5000, 0, 0) with
    /// its own camera, a three-point light rig and a turntable, all restricted to
    /// the <c>AnimalViewer</c> layer, and its model comes from
    /// <c>AnimalDef.prefab</c> — a <c>GameObject</c>. None of that can cross the
    /// assembly boundary, so the whole of what does is a species key going in and a
    /// <see cref="UnityEngine.Texture"/> coming out. Exactly the shape
    /// <see cref="IArSelection"/> used for the AR tap source.</para>
    ///
    /// <para><b>The texture is a render target and it is NOT stable.</b> The rig
    /// renders into a <c>RenderTexture</c> sized to the on-screen viewport, so it is
    /// reallocated whenever <see cref="SetViewportSize"/> lands on a new size —
    /// which happens on the screen's first layout pass, and again on rotation. A
    /// consumer must re-read <see cref="Texture"/> on
    /// <see cref="TextureChanged"/> rather than caching it; a stale
    /// <c>RenderTexture</c> is a released one, and UI Toolkit will draw it black.</para>
    ///
    /// <para><b>Unavailability is normal, not an error.</b> There is no rig in the
    /// screenshot harness, none in a scene the builder has not run over, and none
    /// for a species whose <c>prefab</c> slot is empty or broken (the documented
    /// failure mode after an FBX is replaced — see CLAUDE.md, "Replacing an
    /// existing species' FBX"). Every one of those comes back as
    /// <see cref="IsAvailable"/> false or <see cref="Show"/> returning false, and
    /// the screen then does not build the block at all — the same discipline Praia
    /// detalhe applies to its empty content sections.</para>
    /// </summary>
    public interface ISpeciesModelViewer
    {
        /// <summary>
        /// The rig is wired and usable. False means "no 3D viewer in this build or
        /// this scene" and the section is simply absent; it is never an error to
        /// report to the user.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// The live render target, or null while nothing is mounted. Re-read it on
        /// every <see cref="TextureChanged"/> — see the class remarks.
        /// </summary>
        Texture Texture { get; }

        /// <summary>
        /// <see cref="Texture"/> is now a different object (a viewport resize
        /// reallocated the render target, or a model was mounted/unmounted).
        /// </summary>
        event Action TextureChanged;

        /// <summary>
        /// Mounts a species' model on the turntable, auto-centred and auto-framed,
        /// and starts the rig rendering. The key is the AnimalDef asset name — the
        /// same key <see cref="ISpeciesCatalog.Find"/> uses.
        ///
        /// <para>Returns false, having changed nothing, when the rig is unavailable
        /// or the species has no usable model. Calling it twice for the same
        /// species remounts, which is harmless but pointless.</para>
        /// </summary>
        bool Show(string speciesKey);

        /// <summary>
        /// Unmounts the model and stops the rig rendering. Idempotent, and
        /// <b>load-bearing</b>: without it a camera keeps rendering a shark nobody
        /// is looking at for as long as the app is open.
        /// </summary>
        void Hide();

        /// <summary>
        /// Matches the render target to the viewport, in <b>device pixels</b> (the
        /// screen multiplies its dp size by the panel scale). Clamped by the
        /// implementation; a no-op when the size has not actually changed, so it is
        /// safe to call from a layout callback.
        /// </summary>
        void SetViewportSize(int widthPx, int heightPx);

        /// <summary>Orbits the turntable around its Y axis by a horizontal drag, in pixels.</summary>
        void Rotate(float pixelsX);

        /// <summary>
        /// Moves the camera along its own view direction. Positive metres pull it
        /// away from the model; the implementation clamps to a sane range around
        /// the auto-framed distance.
        /// </summary>
        void Zoom(float metres);
    }
}
