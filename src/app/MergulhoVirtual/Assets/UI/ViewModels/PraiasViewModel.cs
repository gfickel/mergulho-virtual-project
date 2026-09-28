using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// The Praias slice's ViewModel: what the landing (Tela 1) shows about the
    /// beach the user is standing on, and the beach selector both Praias screens
    /// share (Tela 9's dropdown). Plain C#, no UnityEngine — same contract as
    /// <see cref="HomeViewModel"/> and <see cref="BeachesViewModel"/>.
    ///
    /// <para><b>It owns no beach data of its own.</b> Everything the landing
    /// renders comes from <see cref="Detail"/>, the one
    /// <see cref="BeachDetailViewModel"/> the slice uses, which this class keeps
    /// pointed at the active beach. The landing and the pushed detail screen
    /// therefore read the SAME instance and can never disagree about which beach
    /// is open — there is no second copy to drift.</para>
    ///
    /// <para><b>Why selecting a beach sets the GPS override.</b> V2 deletes the
    /// browsable beach list, so the hero pill is the only way to reach a beach
    /// other than the resolved one (DESIGN_IMPLEMENTATION.md §7 / §8.3). Rather
    /// than invent a second, invisible notion of "the beach I am browsing" that
    /// disagrees with "the beach I am at" — and would leave the AR spawner and
    /// the conditions fetch on the old one — picking a beach in the selector IS
    /// declaring where you are: it sets the override
    /// (<see cref="BeachesViewModel.SelectOverride"/>), which re-emits through
    /// <see cref="IActiveBeach"/> and re-points <see cref="Detail"/>, the AR
    /// spawner and the conditions service together. Entry 0 of the selector is
    /// "Automático (GPS)", which is how the user hands control back.</para>
    ///
    /// <para><b>Null is a supported state.</b> GPS resolves no beach far more
    /// often than the design admits (off every polygon, no fix yet, no GPS at
    /// all), and an active-beach key that is not in the catalog is equally
    /// possible. Both close <see cref="Detail"/> and flip the landing card to
    /// <see cref="ChooseBeachLabel"/>, which opens the selector — so the screen
    /// always has a way forward and never shows an invented beach.</para>
    /// </summary>
    public sealed class PraiasViewModel : IDisposable
    {
        // ---- Copy (transcribed from Tela 1; same convention as HomeViewModel).

        public const string CtaTitle = "Ciência cidadã";

        public const string CtaBody =
            "Reporte avistamentos locais e colabore diretamente com a conservação do parque.";

        public const string CtaButtonLabel = "Reportar";

        /// <summary>
        /// Stands in for the beach name when none is resolved. NOT a placeholder
        /// value dressed up as data — it is the label of an action (it opens the
        /// beach selector), which is why the "Você está em" caption above it is
        /// hidden in that state instead of being left to contradict it.
        /// </summary>
        public const string ChooseBeachLabel = "Escolha uma praia";

        /// <summary>The floating emergency button on Praia detalhe (Tela 4).
        /// An acronym, identical in pt-BR and en, but it is still a user-visible
        /// string and so lives here rather than inline in the screen.</summary>
        public const string SosButtonLabel = "SOS";

        readonly BeachesViewModel beaches;
        readonly IActiveBeach activeBeach;
        bool disposed;

        /// <summary>
        /// The open beach's state — the landing reads a subset of it, the Praia
        /// detalhe screen reads all of it. Always pointed at the active beach.
        /// </summary>
        public BeachDetailViewModel Detail { get; }

        /// <summary>Raised when the active beach changed or its data may have.</summary>
        public event Action Changed;

        public PraiasViewModel(
            BeachDetailViewModel detail,
            BeachesViewModel beaches,
            IActiveBeach activeBeach)
        {
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            this.beaches = beaches;
            this.activeBeach = activeBeach;

            Detail.Changed += OnDetailChanged;
            if (this.activeBeach != null) this.activeBeach.ActiveBeachChanged += OnActiveBeachChanged;

            SyncToActiveBeach();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Detail.Changed -= OnDetailChanged;
            if (activeBeach != null) activeBeach.ActiveBeachChanged -= OnActiveBeachChanged;
        }

        void OnDetailChanged() => Changed?.Invoke();

        void OnActiveBeachChanged(string _) => SyncToActiveBeach();

        /// <summary>
        /// Re-points <see cref="Detail"/> at the active beach. Called on
        /// construction and on every <see cref="IActiveBeach"/> change; screens
        /// may call it on entry, it is idempotent.
        /// </summary>
        public void SyncToActiveBeach()
        {
            string key = activeBeach?.ActiveBeachKey;
            if (!string.IsNullOrEmpty(key) && IndexOfBeach(key) >= 0) Detail.ShowBeach(key);
            else Detail.Close();
            // Detail raises Changed itself when it actually moved; this covers the
            // no-op case (same beach, or still none) so a screen that re-enters
            // always gets one render.
            Changed?.Invoke();
        }

        int IndexOfBeach(string key)
        {
            var all = Detail.Beaches;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Name == key) return i;
            }
            return -1;
        }

        // ---- Landing location card ------------------------------------------

        /// <summary>
        /// The resolved active beach's key, or null. This is the beach actually
        /// being rendered — an active key the catalog does not know reads as null
        /// here, so the screen can never ask for a beach that is not open.
        /// </summary>
        public string ActiveBeachKey => Detail.BeachKey;

        public bool HasActiveBeach => Detail.HasBeach;

        /// <summary>"Você está em" — hidden when no beach is resolved, because it
        /// would then caption an action label ("Escolha uma praia"), not a place.</summary>
        public string YouAreAtText => BeachDetailViewModel.YouAreAtLabel;

        public bool HasYouAreAtLabel => HasActiveBeach;

        /// <summary>The beach name, or the selector's call to action when there is none.</summary>
        public string LocationTitleText => HasActiveBeach ? Detail.Title : ChooseBeachLabel;

        public string RegionText => BeachDetailViewModel.RegionLabel;

        // ---- Beach selector (hero pill on Tela 4/9, location card on Tela 1) --

        /// <summary>
        /// Selector entries: "Automático (GPS)" first, then every beach in
        /// places.json order, as <see cref="BeachInfo.DisplayName"/>s. Never keys
        /// — <see cref="SelectBeach"/> maps the index back to one.
        /// </summary>
        public IReadOnlyList<string> SelectorChoices =>
            beaches != null ? beaches.OverrideChoices : Array.Empty<string>();

        /// <summary>
        /// Which entry the selector marks as chosen: the beach the user pinned,
        /// or 0 ("Automático (GPS)") while GPS is driving. It tracks the override
        /// MODE, not the resolved beach — with GPS in charge, no beach row is
        /// "chosen", the automatic row is.
        /// </summary>
        public int SelectorIndex => beaches != null ? beaches.OverrideIndex : 0;

        /// <summary>
        /// Applies a selector entry: 0 hands control back to GPS, anything else
        /// pins that beach. Out-of-range is a no-op. The resulting active-beach
        /// change comes back through <see cref="IActiveBeach"/> and re-points
        /// <see cref="Detail"/> — this method deliberately does not open the beach
        /// itself, so there is exactly one path from "which beach" to the screen.
        /// </summary>
        public void SelectBeach(int index) => beaches?.SelectOverride(index);
    }
}
