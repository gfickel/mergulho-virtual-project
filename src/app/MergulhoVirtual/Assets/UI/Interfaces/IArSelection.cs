using System;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// "The user tapped an animal in the AR scene" — the only thing the Mergulho
    /// screen needs to know about AR, and the whole of what crosses the assembly
    /// boundary for it.
    ///
    /// <para>The hit source stays where it was: <c>ObjectInteraction</c> in
    /// Assembly-CSharp still owns the pointer read and the <c>Physics.SphereCast</c>
    /// (DESIGN_IMPLEMENTATION.md §7, Slice 4 — "keep ObjectInteraction as the hit
    /// source; only the presentation moves"). This interface carries the one piece
    /// of information out of it: the <b>species key</b>, i.e. the AnimalDef asset
    /// file name ("tiger_shark"), the same key
    /// <see cref="ISpeciesCatalog.Find"/> and <c>beaches_content.json</c> use. A
    /// key, never a label.</para>
    ///
    /// <para><b>A miss raises nothing.</b> Tapping empty water leaves the card
    /// exactly where it was, which is why V2 draws a close button on it (§8.4) —
    /// if a miss dismissed the card, half the screen would be a dismiss target and
    /// the ⨯ would be decoration. It also keeps the raycast from having to know
    /// anything about where the UI Toolkit panel painted its own controls.</para>
    /// </summary>
    public interface IArSelection
    {
        /// <summary>
        /// A tap resolved to an animal. The argument is the species key; an
        /// unknown key is possible (a prefab whose name is not in the catalog) and
        /// consumers must drop it rather than render a blank card.
        /// </summary>
        event Action<string> SpeciesSelected;

        /// <summary>
        /// Start/stop delivering taps. The AR HUD is one route of four and the
        /// hit source lives at the scene root, so it would otherwise raycast on
        /// every tap on every screen. Called from the screen's
        /// <c>OnEnter</c>/<c>OnExit</c>, which is the only place that knows
        /// whether the AR view is actually on screen.
        /// </summary>
        void SetListening(bool listening);
    }
}
