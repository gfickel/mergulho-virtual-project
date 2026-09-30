using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// The Mergulho (AR) HUD's own state — DESIGN_IMPLEMENTATION.md §8.4, Figma
    /// frame Tela 8 (31:3979). Plain C#, no UnityEngine, same contract as every
    /// other ViewModel here.
    ///
    /// <para><b>It owns exactly one thing: which species' card is open.</b> The
    /// beach pill on the same top bar is <see cref="PraiasViewModel"/>'s — the
    /// screen borrows it the way <see cref="PraiaDetalheScreen"/> does, because
    /// picking a beach there IS setting the GPS override, and a second notion of
    /// "the beach I am looking at" would leave the AR spawner and the conditions
    /// fetch pointing somewhere else. Everything AR — the camera, the session,
    /// the spawned animals, the raycast — stays in Assembly-CSharp behind
    /// <see cref="IArSelection"/>.</para>
    ///
    /// <para><b>No selection is the normal state.</b> The HUD opens as a bare top
    /// bar over the camera and stays that way until a tap lands on an animal; the
    /// card is then opened by <see cref="ShowSpecies"/> and closed only by
    /// <see cref="CloseCard"/> (the ⨯) or by leaving the route. A tap that misses
    /// raises nothing at all — see <see cref="IArSelection"/> for why.</para>
    ///
    /// <para><b>An unknown key opens nothing.</b> The key is a prefab/AnimalDef
    /// asset name coming out of the AR scene, so a species that was modelled but
    /// never catalogued is possible; it is dropped with the card left as it was,
    /// never rendered as a blank card.</para>
    /// </summary>
    public sealed class MergulhoViewModel : IDisposable
    {
        static readonly IReadOnlyList<SpeciesSpecRow> NoSpecRows = Array.Empty<SpeciesSpecRow>();

        readonly ISpeciesCatalog catalog;
        readonly IArSelection selection;
        IReadOnlyList<SpeciesSpecRow> specRows = NoSpecRows;
        bool listening;
        bool disposed;

        /// <summary>Raised when the open species changed (including to none).</summary>
        public event Action Changed;

        /// <summary>The species whose card is open, or null when none is.</summary>
        public SpeciesInfo SelectedSpecies { get; private set; }

        public MergulhoViewModel(ISpeciesCatalog catalog, IArSelection selection)
        {
            this.catalog = catalog;
            this.selection = selection;
            if (this.selection != null) this.selection.SpeciesSelected += OnSpeciesSelected;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (selection == null) return;
            selection.SpeciesSelected -= OnSpeciesSelected;
            if (listening) selection.SetListening(false);
        }

        // ---- AR selection ---------------------------------------------------

        /// <summary>
        /// Start/stop consuming AR taps. Driven by the screen's OnEnter/OnExit:
        /// the hit source lives at the scene root and would otherwise raycast on
        /// every tap on every route. Leaving the route also closes the card, so
        /// coming back to AR does not resurrect a species the user walked away
        /// from.
        /// </summary>
        public void SetListening(bool value)
        {
            if (listening == value) return;
            listening = value;
            selection?.SetListening(value);
            if (!value) CloseCard();
        }

        void OnSpeciesSelected(string key) => ShowSpecies(key);

        /// <summary>
        /// Opens the card for a species key (the AnimalDef asset name). Unknown or
        /// empty keys are ignored — the card stays exactly as it was. Public
        /// because the AR tap is not the only caller: the screenshot harness
        /// drives it to render the populated card.
        /// </summary>
        public void ShowSpecies(string key)
        {
            var found = string.IsNullOrEmpty(key) ? null : catalog?.Find(key);
            if (found == null) return;
            if (ReferenceEquals(found, SelectedSpecies)) return;
            SelectedSpecies = found;
            // Built once per selection, not once per render: the screen reads the
            // list and its count, and re-entering the route repaints.
            specRows = SpeciesCardFormatter.SpecRows(found);
            Changed?.Invoke();
        }

        /// <summary>Dismisses the card (the ⨯). Idempotent.</summary>
        public void CloseCard()
        {
            if (SelectedSpecies == null) return;
            SelectedSpecies = null;
            specRows = NoSpecRows;
            Changed?.Invoke();
        }

        // ---- Card content (all of it through SpeciesCardFormatter) -----------

        public bool HasSelection => SelectedSpecies != null;

        public string TitleText => SpeciesCardFormatter.Title(SelectedSpecies);

        /// <summary>Null hides the line — the species' identification is open.</summary>
        public string BinomialText => SpeciesCardFormatter.Binomial(SelectedSpecies);

        public bool HasBinomial => BinomialText != null;

        /// <summary>
        /// Size / diet / behaviour, minus the rows with no value. <b>Empty for
        /// every species today</b> (Decision D8): the fields exist on AnimalDef
        /// and nobody has authored them, and a blank beats a guess — so the card
        /// currently shows the name alone.
        /// </summary>
        public IReadOnlyList<SpeciesSpecRow> SpecRows => specRows;

        public bool HasSpecRows => specRows.Count > 0;

        public string CloseLabel => SpeciesCardFormatter.CloseLabel;
    }
}
