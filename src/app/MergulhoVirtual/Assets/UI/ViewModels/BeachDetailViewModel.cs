using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>One "Espécies comuns" chip: the catalog key it selects and the pt-BR
    /// label it shows. The key never reaches the screen's text.</summary>
    public sealed class BeachSpeciesChip
    {
        /// <summary>Catalog key (AnimalDef asset name) — lookup only, never rendered.</summary>
        public readonly string Key;

        /// <summary>pt-BR label from the species catalog.</summary>
        public readonly string Label;

        public BeachSpeciesChip(string key, string label)
        {
            Key = key;
            Label = label;
        }
    }

    /// <summary>
    /// The selected species as the species card renders it: the catalog's identity
    /// (name, binomial, photo) joined with this beach's two editorial fields.
    /// Every optional field has a <c>Has*</c> companion; nothing is defaulted to a
    /// placeholder.
    /// </summary>
    public sealed class BeachSpeciesView
    {
        public readonly string Key;
        public readonly string DisplayName;
        public readonly string Binomial;
        public readonly string ImageName;
        public readonly string Description;

        /// <summary>Per-beach pill over the photo ("Área de berçário"); null when unfilled.</summary>
        public readonly string TagText;

        /// <summary>"Comportamento nessa praia: …"; null when unfilled.</summary>
        public readonly string BehaviourText;

        public BeachSpeciesView(SpeciesInfo info, BeachSpeciesContent content)
        {
            Key = info?.Key;
            DisplayName = info?.DisplayName;
            Binomial = info != null && info.HasBinomial ? info.Binomial : null;
            ImageName = info?.ImageName;
            Description = info?.Description;
            TagText = content != null && content.HasTag ? content.Tag : null;
            BehaviourText = BeachContentFormatter.SpeciesBehaviour(content?.Behaviour);
        }

        public bool HasBinomial => !string.IsNullOrWhiteSpace(Binomial);
        public bool HasImage => !string.IsNullOrWhiteSpace(ImageName);
        public bool HasTag => !string.IsNullOrWhiteSpace(TagText);
        public bool HasBehaviour => !string.IsNullOrWhiteSpace(BehaviourText);
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    }

    /// <summary>
    /// State + presentation logic for one beach — the Praia detalhe screen
    /// (Tela 4 / Tela 9) and the subset the Praias landing (Tela 1) shows for the
    /// beach the user is standing on. Plain C#, no UnityEngine, with the clock and
    /// the UTC→local conversion injected, so it is unit-testable and
    /// timezone-proof (same contract as <see cref="BeachesViewModel"/> and
    /// <see cref="HomeViewModel"/>).
    ///
    /// <para><b>Why a sibling of <see cref="BeachesViewModel"/> rather than more
    /// members on it.</b> BeachesViewModel is the *catalog* ViewModel: the beach
    /// list, the GPS-override dropdown and the five shared conditions rows, whose
    /// surface the live Beaches screen and fifteen tests already bind to. This one
    /// is per-beach state with its own lifecycle (which beach is open, which
    /// species chip is selected) and its own dependencies (editorial content and
    /// the species catalog, neither of which the list needs). Folding them together
    /// would make every list-screen construction drag in two services it never
    /// reads, and would put two unrelated selections behind one Changed event.</para>
    ///
    /// <para><b>Most of the content is blank today</b> (docs/beaches-content-todo.md).
    /// Every section therefore has a <c>Has*</c> flag the screen must honour: a
    /// section with nothing to say is hidden, a stat column with nothing to say
    /// shows "—". Nothing here invents a value.</para>
    /// </summary>
    public sealed class BeachDetailViewModel : IDisposable
    {
        // ---- Copy (transcribed from the Figma frames; same convention as HomeViewModel).
        public const string RegionLabel = "Fernando de Noronha, PE";
        public const string YouAreAtLabel = "Você está em";
        public const string BestSeasonLabel = "Melhor Época";
        public const string IdealTideLabel = "Maré Ideal";
        public const string SightingsLabel = "Avistamentos";
        public const string TideNowLabel = "Maré agora";
        public const string SightingPeakLabel = "Pico de avistamento";
        public const string SpeciesSectionTitle = "Espécies comuns";
        public const string SpeciesLearnMoreLabel = "Saiba mais sobre a espécie";
        public const string TipsSectionTitle = "Dicas de convivência";
        public const string GallerySectionTitle = "Galeria de avistamentos";

        readonly IBeachContent contentSource;
        readonly ISpeciesCatalog speciesCatalog;
        readonly ITideService tides;
        readonly Func<DateTime> utcNow;
        readonly Func<DateTime, DateTime> toLocalTime;
        readonly List<string> beachChoices;
        readonly List<BeachSpeciesChip> speciesChips = new List<BeachSpeciesChip>();
        readonly List<string> unknownSpeciesKeys = new List<string>();
        readonly List<string> alertLines = new List<string>();
        int? sightingCount;
        bool disposed;

        /// <summary>Every beach, in places.json order — the hero dropdown's source (Tela 9).</summary>
        public IReadOnlyList<BeachInfo> Beaches { get; }

        /// <summary>Hero dropdown labels, one per beach, in <see cref="Beaches"/> order.
        /// These are <see cref="BeachInfo.DisplayName"/>s — never the lookup keys.</summary>
        public IReadOnlyList<string> BeachChoices => beachChoices;

        /// <summary>Index into <see cref="Beaches"/> of the open beach; -1 when none.</summary>
        public int SelectedBeachIndex { get; private set; } = -1;

        /// <summary>The open beach, or null.</summary>
        public BeachInfo Beach { get; private set; }

        /// <summary>Editorial content for the open beach. Never null — an empty
        /// content when no beach is open or the beach has no entry.</summary>
        public BeachContent Content { get; private set; } = BeachContent.EmptyFor(null);

        /// <summary>Raised when the open beach, the selected species or the tide data changes.</summary>
        public event Action Changed;

        public BeachDetailViewModel(
            IBeachCatalog catalog,
            IBeachContent content,
            ISpeciesCatalog species,
            ITideService tides,
            Func<DateTime> utcNow = null,
            Func<DateTime, DateTime> toLocalTime = null)
        {
            contentSource = content;
            speciesCatalog = species;
            this.tides = tides;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.toLocalTime = toLocalTime ?? (d => d.ToLocalTime());

            Beaches = catalog?.Beaches ?? Array.Empty<BeachInfo>();
            beachChoices = new List<string>(Beaches.Count);
            foreach (var beach in Beaches) beachChoices.Add(beach.DisplayName);

            if (this.tides != null) this.tides.Changed += OnTideChanged;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (tides != null) tides.Changed -= OnTideChanged;
        }

        void OnTideChanged(TideData _) => Changed?.Invoke();

        /// <summary>Re-renders time-relative strings; call on the same cadence as the other ViewModels.</summary>
        public void NotifyTimePassed() => Changed?.Invoke();

        // ---- Navigation -----------------------------------------------------

        public bool HasBeach => Beach != null;

        /// <summary>
        /// Opens a beach by its places.json <b>key</b> (not its label). Unknown keys
        /// are a no-op, exactly like <c>BeachesViewModel.ShowDetail</c>.
        /// </summary>
        public void ShowBeach(string beachKey)
        {
            if (string.IsNullOrEmpty(beachKey)) return;
            for (int i = 0; i < Beaches.Count; i++)
            {
                if (Beaches[i].Name != beachKey) continue;
                OpenIndex(i);
                return;
            }
        }

        /// <summary>Opens the beach at a hero-dropdown index. Out-of-range is a no-op.</summary>
        public void SelectBeachByIndex(int index)
        {
            if (index < 0 || index >= Beaches.Count || index == SelectedBeachIndex) return;
            OpenIndex(index);
        }

        /// <summary>Closes the detail (back to the landing). Idempotent.</summary>
        public void Close()
        {
            if (Beach == null) return;
            Beach = null;
            SelectedBeachIndex = -1;
            Content = BeachContent.EmptyFor(null);
            RebuildSpecies();
            RebuildAlerts();
            Changed?.Invoke();
        }

        void OpenIndex(int index)
        {
            Beach = Beaches[index];
            SelectedBeachIndex = index;
            Content = contentSource?.ForBeach(Beach.Name) ?? BeachContent.EmptyFor(Beach.Name);
            sightingCount = null;
            RebuildSpecies();
            RebuildAlerts();
            Changed?.Invoke();
        }

        // ---- Header ---------------------------------------------------------

        /// <summary>
        /// The open beach's lookup key — for callers that pass the beach on (a
        /// prefilled sighting report, the AR spawner). Not for display; use
        /// <see cref="Title"/>.
        /// </summary>
        public string BeachKey => Beach?.Name;

        /// <summary>pt-BR beach name for the title and the hero pill.</summary>
        public string Title => Beach?.DisplayName ?? string.Empty;

        public string RegionText => RegionLabel;

        /// <summary>Cover photo file name (Resources/Beaches/…); null when the beach has none.</summary>
        public string ImageName => Beach?.ImageName;

        public bool HasImage => !string.IsNullOrWhiteSpace(ImageName);

        public string PhotoCreditText => Beach != null && !string.IsNullOrWhiteSpace(Beach.PhotoCredit)
            ? Beach.PhotoCredit
            : null;

        public bool HasPhotoCredit => PhotoCreditText != null;

        public string DescriptionText => Beach != null && !string.IsNullOrWhiteSpace(Beach.Description)
            ? Beach.Description
            : null;

        public bool HasDescription => DescriptionText != null;

        /// <summary>Dark pills over the cover photo; empty = draw no badge row.</summary>
        public IReadOnlyList<string> EnvironmentTags => Content.EnvironmentTags ?? Array.Empty<string>();

        public bool HasEnvironmentTags => Content.HasEnvironmentTags;

        /// <summary>The level itself, so the screen can pick the pill's color token.</summary>
        public BeachRiskLevel RiskLevel => Content.RiskLevel;

        /// <summary>"Risco: Baixo"; null when unfilled — draw no pill (never a default "Baixo").</summary>
        public string RiskPillText => BeachContentFormatter.RiskPill(Content.RiskLevel);

        public bool HasRisk => Content.HasRiskLevel;

        // ---- Stats card -----------------------------------------------------

        public string BestSeasonText => BeachContentFormatter.OrNoValue(Content.BestSeason);
        public bool HasBestSeason => Content.HasBestSeason;

        /// <summary>"Baixa" / "Alta" / "Qualquer" / "—".</summary>
        public string IdealTideText =>
            BeachContentFormatter.OrNoValue(BeachContentFormatter.IdealTide(Content.IdealTide));

        /// <summary>"próx. baixa 14:40" — the live caption under "Maré Ideal"; null when
        /// the ideal tide is unfilled/"qualquer" or the tide table has no such event.</summary>
        public string IdealTideNextEventText =>
            BeachContentFormatter.IdealTideNextEvent(Content.IdealTide, CurrentTide, toLocalTime);

        public bool HasIdealTide => Content.HasIdealTide;
        public bool HasIdealTideNextEvent => IdealTideNextEventText != null;

        public string SightingPeakText => BeachContentFormatter.OrNoValue(Content.SightingPeak);
        public bool HasSightingPeak => Content.HasSightingPeak;

        public TideData CurrentTide => tides?.Current ?? default;

        /// <summary>"1.4 m · subindo" for the landing's "Maré agora" stat; "—" when no tide data.</summary>
        public string TideNowText => BeachContentFormatter.TideNow(CurrentTide);

        /// <summary>
        /// Per-beach sighting count, when someone can supply one. Null today and by
        /// default: <c>/api/v1/avistamentos/count</c> is island-wide, not per beach
        /// (DESIGN_IMPLEMENTATION.md §5.1), and showing the global total under a
        /// beach name would be a lie. Settable so the row can light up the moment a
        /// per-beach endpoint exists, without reshaping the screen.
        /// </summary>
        public int? SightingCount
        {
            get => sightingCount;
            set
            {
                if (sightingCount == value) return;
                sightingCount = value;
                Changed?.Invoke();
            }
        }

        /// <summary>"120 avistamentos registrados"; null while the count is unknown.</summary>
        public string SightingCountText => BeachContentFormatter.SightingCount(sightingCount);

        public bool HasSightingCount => SightingCountText != null;

        /// <summary>False when every stat is unfilled — the whole card can be dropped.</summary>
        public bool HasStats => HasBestSeason || HasIdealTide || HasSightingCount;

        // ---- Alert bar ------------------------------------------------------

        /// <summary>"Salva-vidas: Das 08h às 17h"; null when unfilled.</summary>
        public string LifeguardText => BeachContentFormatter.Lifeguard(Content.LifeguardHours);

        public bool HasLifeguard => Content.HasLifeguardHours;

        /// <summary>
        /// Everything the amber alert bar shows, in order: the lifeguard line first
        /// when there is one, then each advisory. Empty = no bar at all.
        /// </summary>
        public IReadOnlyList<string> AlertLines => alertLines;

        public bool HasAlerts => alertLines.Count > 0;

        void RebuildAlerts()
        {
            alertLines.Clear();
            string lifeguard = LifeguardText;
            if (lifeguard != null) alertLines.Add(lifeguard);
            if (Content.Advisories != null)
            {
                foreach (var line in Content.Advisories)
                {
                    if (!string.IsNullOrWhiteSpace(line)) alertLines.Add(line.Trim());
                }
            }
        }

        // ---- Species --------------------------------------------------------

        /// <summary>Chips in author order; empty = hide the species section entirely.</summary>
        public IReadOnlyList<BeachSpeciesChip> SpeciesChips => speciesChips;

        public bool HasSpecies => speciesChips.Count > 0;

        /// <summary>Index into <see cref="SpeciesChips"/>; -1 when there are none.
        /// Resets to the first chip on every beach change (the file's order is the
        /// author's priority order).</summary>
        public int SelectedSpeciesIndex { get; private set; } = -1;

        /// <summary>The species card's data; null when the beach lists no species.</summary>
        public BeachSpeciesView SelectedSpecies { get; private set; }

        /// <summary>"Comportamento nessa praia: …"; null when unfilled — hide the line.</summary>
        public string SelectedSpeciesBehaviourText => SelectedSpecies?.BehaviourText;

        public bool HasSelectedSpeciesBehaviour => SelectedSpecies != null && SelectedSpecies.HasBehaviour;

        /// <summary>
        /// Species keys the content file lists that the catalog does not know —
        /// i.e. typos or species nobody has added an AnimalDef for. They are dropped
        /// from the chips (a blank chip is worse than no chip); exposed so tooling
        /// and tests can see them.
        /// </summary>
        public IReadOnlyList<string> UnknownSpeciesKeys => unknownSpeciesKeys;

        /// <summary>Selects a chip. Out-of-range is a no-op.</summary>
        public void SelectSpecies(int index)
        {
            if (index < 0 || index >= speciesChips.Count || index == SelectedSpeciesIndex) return;
            SelectedSpeciesIndex = index;
            SelectedSpecies = BuildSpeciesView(index);
            Changed?.Invoke();
        }

        void RebuildSpecies()
        {
            speciesChips.Clear();
            unknownSpeciesKeys.Clear();
            SelectedSpeciesIndex = -1;
            SelectedSpecies = null;

            var species = Content.Species;
            if (species == null) return;

            foreach (var entry in species)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.SpeciesKey)) continue;
                var info = speciesCatalog?.Find(entry.SpeciesKey);
                if (info == null || string.IsNullOrWhiteSpace(info.DisplayName))
                {
                    unknownSpeciesKeys.Add(entry.SpeciesKey);
                    continue;
                }
                speciesChips.Add(new BeachSpeciesChip(entry.SpeciesKey, info.DisplayName));
            }

            if (speciesChips.Count == 0) return;
            SelectedSpeciesIndex = 0;
            SelectedSpecies = BuildSpeciesView(0);
        }

        BeachSpeciesView BuildSpeciesView(int chipIndex)
        {
            string key = speciesChips[chipIndex].Key;
            var info = speciesCatalog?.Find(key);
            BeachSpeciesContent entry = null;
            if (Content.Species != null)
            {
                foreach (var candidate in Content.Species)
                {
                    if (candidate != null && candidate.SpeciesKey == key) { entry = candidate; break; }
                }
            }
            return new BeachSpeciesView(info, entry);
        }

        // ---- Tips -----------------------------------------------------------

        /// <summary>The tip sentences, unnumbered; empty = hide the section.</summary>
        public IReadOnlyList<string> Tips => Content.Tips ?? Array.Empty<string>();

        public bool HasTips => Content.HasTips;

        /// <summary>"1" / "2" / "3" for the numbered bullet of tip <paramref name="index"/>.</summary>
        public string TipNumberText(int index) => BeachContentFormatter.TipNumber(index);

        // ---- Gallery --------------------------------------------------------

        /// <summary>
        /// Always false. "Galeria de avistamentos" has no data source at all: user
        /// photos live in the private <c>avistamentos</c> bucket behind signed URLs
        /// and there is no public per-beach endpoint (DESIGN_IMPLEMENTATION.md §5.1).
        /// Exposed as a flag so the screen can leave the section wired but hidden
        /// instead of shipping fake cards.
        /// </summary>
        public bool HasGallery => false;
    }
}
