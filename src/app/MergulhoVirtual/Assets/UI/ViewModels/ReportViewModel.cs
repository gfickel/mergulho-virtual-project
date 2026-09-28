using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// One selectable option — a species chip, a size chip, a behaviour row or a
    /// profile card. All four are the same shape (a machine key the backend sees,
    /// a pt-BR label the user sees), so they share one type rather than four
    /// near-identical ones.
    /// </summary>
    public sealed class ReportOption
    {
        /// <summary>Machine value sent to the backend. A lookup key, never rendered.</summary>
        public readonly string Key;

        /// <summary>pt-BR label. Never a lookup key.</summary>
        public readonly string Label;

        public ReportOption(string key, string label)
        {
            Key = key;
            Label = label;
        }
    }

    /// <summary>
    /// One card of "Seus avistamentos pendentes", fully formatted. The screen
    /// draws these three strings and picks a pill color from
    /// <see cref="State"/> — it builds no text of its own.
    /// </summary>
    public sealed class ReportPendingRow
    {
        /// <summary>Stable row identity (the submission's idempotency key). Not shown.</summary>
        public readonly string Id;

        /// <summary>Species label, or <see cref="ReportFormatter.UnnamedSpeciesTitle"/>
        /// when the report named none.</summary>
        public readonly string TitleText;

        /// <summary>Pill copy — "Pendente" / "Tentando de novo" / "Sem conexão" / "Falhou".</summary>
        public readonly string StatusText;

        /// <summary>The state behind <see cref="StatusText"/>, so the pill can be
        /// colored (a failure is not a neutral grey pill).</summary>
        public readonly SightingState State;

        /// <summary>"Hoje, 9:32 · Baía do Sueste" — beach half dropped when unknown.</summary>
        public readonly string CaptionText;

        /// <summary>Local path of the queued photo for a thumbnail; may be null.
        /// Loading it is the screen's job (the ViewModel stays engine-free).</summary>
        public readonly string PhotoPath;

        public ReportPendingRow(
            string id, string titleText, string statusText, SightingState state,
            string captionText, string photoPath)
        {
            Id = id;
            TitleText = titleText;
            StatusText = statusText;
            State = state;
            CaptionText = captionText;
            PhotoPath = photoPath;
        }
    }

    /// <summary>
    /// State + presentation logic for the Reportar avistamento screen (Tela 11
    /// empty / Tela 12 filled). Plain C#, no UnityEngine, with the clock and the
    /// UTC→local conversion injected — same contract as
    /// <see cref="HomeViewModel"/>, <see cref="BeachesViewModel"/> and
    /// <see cref="BeachDetailViewModel"/>, so the whole form is unit-testable
    /// with fakes and timezone-proof.
    ///
    /// <para><b>Fire-and-forget is the rule, not an optimisation.</b>
    /// <see cref="Submit"/> hands the report to <see cref="ISightingReports"/>,
    /// which makes it durable on disk and uploads it in the background with
    /// retries; it never waits on the network and never reports a backend result.
    /// <see cref="Submitted"/> is raised even when the report could not be taken
    /// at all — the user is never held on the screen by infrastructure (CLAUDE.md,
    /// "Submit UX"). <see cref="LastSubmitAccepted"/> tells the screen which
    /// message it is showing on the way out.</para>
    ///
    /// <para><b>One photo this slice.</b> <see cref="MaxPhotos"/> is 1 and the
    /// screen draws the grid capped at it. Multi-photo is not an additive change:
    /// it alters the bucket layout (<c>originals/&lt;registro&gt;.&lt;ext&gt;</c>
    /// assumes one image per sighting) and the idempotency semantics
    /// (DESIGN_IMPLEMENTATION.md §5.2 / Decision D6).</para>
    ///
    /// <para><b>Only the photo is required.</b> Species, size, behaviours,
    /// identity and profile are all optional, and so is the beach — GPS resolving
    /// nothing is common and the backend accepts an empty <c>local</c>. Nothing
    /// here invents a value for an unanswered field.</para>
    /// </summary>
    public sealed class ReportViewModel : IDisposable
    {
        // ---- Copy (transcribed from Tela 11 / Tela 12; same convention as HomeViewModel).

        public const string ScreenTitle = "Reportar avistamento";
        public const string SpeciesSectionTitle = "Espécie identificada";
        public const string SizeSectionTitle = "Tamanho aproximado";
        public const string BehaviourSectionTitle = "Comportamento observado";
        public const string PhotoSectionTitle = "Adicionar foto ou vídeo";
        public const string PhotoEmptyActionText = "Selecione arquivos do dispositivo";
        public const string PhotoLimitText = "Limite de tamanho: 20MB";
        public const string IdentitySectionTitle = "Se identifique (opcional)";
        public const string NamePlaceholder = "Seu nome";
        public const string EmailPlaceholder = "Seu melhor email";
        public const string ProfileSectionTitle = "Selecione seu perfil";
        public const string SubmitLabel = "Enviar avistamento";
        public const string PendingSectionTitle = "Seus avistamentos pendentes";

        /// <summary>Shown while the screen bounces back to the previous route.</summary>
        public const string SubmittingText = "Enviando avistamento…";

        /// <summary>
        /// The report could not even be taken (the photo could not be copied, or
        /// the queue is full). Deliberately says nothing about the backend: the
        /// upload has not been attempted at this point and never will be for this
        /// submission.
        /// </summary>
        public const string SubmitFailedText = "Não foi possível guardar o avistamento. Tente de novo.";

        /// <summary>The one validation message — a photo is the only required field.</summary>
        public const string MissingPhotoText = "Adicione uma foto do avistamento.";

        /// <summary>Stands in for the beach line when GPS has resolved none. An
        /// honest statement of the state, not a placeholder beach.</summary>
        public const string NoBeachText = "Praia não detectada";

        /// <summary>The picker failed (not cancelled — cancelling says nothing).</summary>
        public const string PhotoPickFailedText = "Não foi possível abrir a galeria.";

        /// <summary>Design's stated upload ceiling (§8.5, "Limite de tamanho: 20MB").</summary>
        public const long MaxPhotoBytes = 20L * 1024L * 1024L;

        /// <summary>Photos per report in this slice. See the class remarks.</summary>
        public const int MaxPhotos = 1;

        // ---- Fixed option sets (keys are the wire values; labels are Figma's).

        public const string SizeKeyUnder1m = "lt_1m";
        public const string SizeKey1to2m = "1_2m";
        public const string SizeKeyOver3m = "gt_3m";

        public const string BehaviourKeyCalm = "calmo";
        public const string BehaviourKeyNearShore = "perto_da_praia";
        public const string BehaviourKeyFeeding = "alimentacao";
        public const string BehaviourKeyAggressive = "arredio";

        public const string ProfileKeyTourist = "turista";
        public const string ProfileKeyGuide = "condutor";

        /// <summary>
        /// Size buckets in Figma order. <b>The design's own set has a gap</b> —
        /// "Menor que 1m", "1m - 2m", "Maior que 3m" leaves 2–3 m unrepresented.
        /// Transcribed as designed rather than silently corrected to "Maior que
        /// 2m"; the question is the designer's to answer, and inventing an answer
        /// here would put a label on screen nobody approved.
        /// </summary>
        static readonly ReportOption[] Sizes =
        {
            new ReportOption(SizeKeyUnder1m, "Menor que 1m"),
            new ReportOption(SizeKey1to2m, "1m - 2m"),
            new ReportOption(SizeKeyOver3m, "Maior que 3m"),
        };

        static readonly ReportOption[] Behaviours =
        {
            new ReportOption(BehaviourKeyCalm, "Calmo e inofensivo"),
            new ReportOption(BehaviourKeyNearShore, "Nadando perto da praia"),
            new ReportOption(BehaviourKeyFeeding, "Em processo de alimentação"),
            new ReportOption(BehaviourKeyAggressive, "Comportamento arredio/agressivo"),
        };

        static readonly ReportOption[] Profiles =
        {
            new ReportOption(ProfileKeyTourist, "Turista / Visitante"),
            new ReportOption(ProfileKeyGuide, "Condutor / Guia"),
        };

        readonly ISightingReports reports;
        readonly IPhotoPicker photoPicker;
        readonly IActiveBeach activeBeach;
        readonly IBeachCatalog beaches;
        readonly Func<DateTime> utcNow;
        readonly Func<DateTime, DateTime> toLocalTime;
        readonly List<ReportOption> speciesOptions = new List<ReportOption>();
        readonly List<ReportPendingRow> pendingRows = new List<ReportPendingRow>();
        readonly HashSet<int> selectedBehaviours = new HashSet<int>();
        int pickToken;
        bool disposed;

        /// <summary>Raised when anything the screen renders changed.</summary>
        public event Action Changed;

        /// <summary>
        /// Raised once per accepted <see cref="Submit"/> call — including one the
        /// service refused — so the screen can show its brief status and navigate
        /// away. It never means "the backend has it".
        /// </summary>
        public event Action Submitted;

        public ReportViewModel(
            ISpeciesCatalog species,
            ISightingReports reports,
            IPhotoPicker photoPicker,
            IActiveBeach activeBeach = null,
            IBeachCatalog beaches = null,
            Func<DateTime> utcNow = null,
            Func<DateTime, DateTime> toLocalTime = null)
        {
            this.reports = reports;
            this.photoPicker = photoPicker;
            this.activeBeach = activeBeach;
            this.beaches = beaches;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.toLocalTime = toLocalTime ?? (d => d.ToLocalTime());

            // Catalog order, not alphabetical: it is the same order the Animais
            // screen lists, so the two never disagree about where a species sits.
            var all = species?.Species;
            if (all != null)
            {
                foreach (var info in all)
                {
                    if (info == null || string.IsNullOrWhiteSpace(info.DisplayName)) continue;
                    speciesOptions.Add(new ReportOption(info.Key, info.DisplayName));
                }
            }

            if (this.reports != null) this.reports.Changed += OnReportsChanged;
            RebuildPending();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (reports != null) reports.Changed -= OnReportsChanged;
        }

        void OnReportsChanged() => RefreshPending();

        /// <summary>
        /// Call on every screen entry: re-reads the pending feed and drops the
        /// status line left over from the previous submit. Does not touch the
        /// form, so a half-filled report survives a trip to another tab.
        /// </summary>
        public void Refresh()
        {
            StatusText = null;
            RebuildPending();
            Changed?.Invoke();
        }

        // ---- Species chips --------------------------------------------------

        /// <summary>Every species in the catalog, in catalog order. Empty is a
        /// real state (no AnimalDefs) — the screen hides the section.</summary>
        public IReadOnlyList<ReportOption> SpeciesOptions => speciesOptions;

        public bool HasSpeciesOptions => speciesOptions.Count > 0;

        /// <summary>Index into <see cref="SpeciesOptions"/>; -1 when none is chosen,
        /// which is a valid submission.</summary>
        public int SelectedSpeciesIndex { get; private set; } = -1;

        public bool HasSpecies => SelectedSpeciesIndex >= 0;

        public string SelectedSpeciesKey =>
            HasSpecies ? speciesOptions[SelectedSpeciesIndex].Key : null;

        public string SelectedSpeciesLabel =>
            HasSpecies ? speciesOptions[SelectedSpeciesIndex].Label : null;

        /// <summary>
        /// Selects a species chip; tapping the selected one clears it. Single
        /// selection, and de-selectable because the field is optional — without
        /// it a mis-tap would be unrecoverable. Out-of-range is a no-op.
        /// </summary>
        public void SelectSpecies(int index)
        {
            if (index < 0 || index >= speciesOptions.Count) return;
            SelectedSpeciesIndex = SelectedSpeciesIndex == index ? -1 : index;
            Changed?.Invoke();
        }

        // ---- Size chips -----------------------------------------------------

        public IReadOnlyList<ReportOption> SizeOptions => Sizes;

        public int SelectedSizeIndex { get; private set; } = -1;

        public bool HasSize => SelectedSizeIndex >= 0;

        public string SelectedSizeKey => HasSize ? Sizes[SelectedSizeIndex].Key : null;

        /// <summary>Selects a size bucket; tapping the selected one clears it.</summary>
        public void SelectSize(int index)
        {
            if (index < 0 || index >= Sizes.Length) return;
            SelectedSizeIndex = SelectedSizeIndex == index ? -1 : index;
            Changed?.Invoke();
        }

        // ---- Behaviour checkboxes (multi-select) ----------------------------

        public IReadOnlyList<ReportOption> BehaviourOptions => Behaviours;

        public bool IsBehaviourSelected(int index) => selectedBehaviours.Contains(index);

        public int SelectedBehaviourCount => selectedBehaviours.Count;

        /// <summary>Checks/unchecks one behaviour. Out-of-range is a no-op.</summary>
        public void ToggleBehaviour(int index)
        {
            if (index < 0 || index >= Behaviours.Length) return;
            if (!selectedBehaviours.Remove(index)) selectedBehaviours.Add(index);
            Changed?.Invoke();
        }

        /// <summary>Checked behaviour keys, always in option order regardless of
        /// the order the user ticked them — so two identical reports serialize
        /// identically.</summary>
        public IReadOnlyList<string> SelectedBehaviourKeys
        {
            get
            {
                var keys = new List<string>(selectedBehaviours.Count);
                for (int i = 0; i < Behaviours.Length; i++)
                {
                    if (selectedBehaviours.Contains(i)) keys.Add(Behaviours[i].Key);
                }
                return keys;
            }
        }

        // ---- Photo ----------------------------------------------------------

        /// <summary>Path of the picked photo, or null. The screen loads it for the
        /// thumbnail; the submission service owns copying it somewhere durable.</summary>
        public string PhotoPath { get; private set; }

        public bool HasPhoto => !string.IsNullOrEmpty(PhotoPath);

        /// <summary>Photos attached (0 or 1 this slice) — the grid's item count.</summary>
        public int PhotoCount => HasPhoto ? 1 : 0;

        /// <summary>False once the cap is reached: hide the "add more" tile.</summary>
        public bool CanAddPhoto => PhotoCount < MaxPhotos && !IsPickingPhoto;

        /// <summary>True between opening the picker and its result arriving —
        /// the user is in the OS gallery, possibly for minutes.</summary>
        public bool IsPickingPhoto { get; private set; }

        /// <summary>Why the last pick was rejected (too large, picker failure);
        /// null when there is nothing to say. Cancelling never sets it.</summary>
        public string PhotoErrorText { get; private set; }

        public bool HasPhotoError => PhotoErrorText != null;

        /// <summary>
        /// Opens the gallery. No-op while a pick is in flight or the cap is
        /// reached. The result may arrive many frames later — or never, if the OS
        /// killed the app mid-pick — so nothing here assumes it does.
        /// </summary>
        public void PickPhoto()
        {
            if (photoPicker == null || !CanAddPhoto) return;
            IsPickingPhoto = true;
            PhotoErrorText = null;
            int token = ++pickToken;
            Changed?.Invoke();
            photoPicker.PickPhoto(result => OnPhotoPicked(token, result));
        }

        void OnPhotoPicked(int token, PhotoPickResult result)
        {
            // A result for a pick the user has since abandoned (removed the photo,
            // submitted the report) must not overwrite the current state.
            if (token != pickToken) return;
            IsPickingPhoto = false;

            if (result == null || result.Outcome == PhotoPickOutcome.Cancelled)
            {
                Changed?.Invoke();
                return;
            }

            if (result.Outcome == PhotoPickOutcome.Failed)
            {
                PhotoErrorText = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? PhotoPickFailedText
                    : PhotoPickFailedText + " " + result.ErrorMessage;
                Changed?.Invoke();
                return;
            }

            if (result.SizeBytes > MaxPhotoBytes)
            {
                // Rejected here rather than at submit: the user is looking at the
                // picker's result, which is the only moment the choice is theirs.
                PhotoErrorText = ReportFormatter.PhotoTooLarge(result.SizeBytes, MaxPhotoBytes);
                Changed?.Invoke();
                return;
            }

            PhotoPath = result.Path;
            PhotoErrorText = null;
            StatusText = null;
            Changed?.Invoke();
        }

        /// <summary>Removes the attached photo (the grid's per-thumb × button).</summary>
        public void RemovePhoto()
        {
            if (!HasPhoto) return;
            pickToken++;               // invalidate any pick still in flight
            PhotoPath = null;
            PhotoErrorText = null;
            Changed?.Invoke();
        }

        // ---- Identity (optional) --------------------------------------------

        /// <summary>
        /// Reporter name. A plain property: text fields own their own editing, so
        /// writing it does NOT raise <see cref="Changed"/> (a rebuild mid-typing
        /// would eat the caret). Trimmed at submit.
        /// </summary>
        public string ReporterName { get; set; }

        /// <summary>
        /// Reporter e-mail, same no-Changed contract as <see cref="ReporterName"/>.
        ///
        /// <para><b>Personal data.</b> It lives in memory for the session and
        /// travels with the report; nothing persists it — not PlayerPrefs, not the
        /// pending feed, not a prefill. A privacy notice and a retention decision
        /// are owed before the backend stores it.</para>
        /// </summary>
        public string ReporterEmail { get; set; }

        public IReadOnlyList<ReportOption> ProfileOptions => Profiles;

        public int SelectedProfileIndex { get; private set; } = -1;

        public bool HasProfile => SelectedProfileIndex >= 0;

        public string SelectedProfileKey => HasProfile ? Profiles[SelectedProfileIndex].Key : null;

        /// <summary>Selects a profile card; tapping the selected one clears it.</summary>
        public void SelectProfile(int index)
        {
            if (index < 0 || index >= Profiles.Length) return;
            SelectedProfileIndex = SelectedProfileIndex == index ? -1 : index;
            Changed?.Invoke();
        }

        /// <summary>
        /// Clears name, e-mail and profile. Not called by <see cref="Submit"/> —
        /// the identity is the user's, not the report's, and re-typing it for a
        /// second sighting in the same session would be hostile — so it exists for
        /// a screen that wants to offer "esquecer meus dados".
        /// </summary>
        public void ClearIdentity()
        {
            ReporterName = null;
            ReporterEmail = null;
            SelectedProfileIndex = -1;
            Changed?.Invoke();
        }

        // ---- Beach (context, not an input) ----------------------------------

        /// <summary>The beach the report will be filed under: whatever GPS or the
        /// manual override resolved. Null is common and supported.</summary>
        public string BeachKey => activeBeach?.ActiveBeachKey;

        public bool HasBeach => !string.IsNullOrEmpty(BeachKey);

        /// <summary>The beach's pt-BR label, or <see cref="NoBeachText"/>. The V2
        /// form has no beach picker, so this is shown as context — the user should
        /// know where the report is being filed.</summary>
        public string BeachText => HasBeach ? BeachLabel(BeachKey) : NoBeachText;

        // ---- Submit ---------------------------------------------------------

        /// <summary>
        /// Whether a submit would be accepted. A photo is the only requirement —
        /// every other field on the form is optional, including the beach.
        /// </summary>
        public bool CanSubmit => HasPhoto;

        /// <summary>The blocking reason, or null when <see cref="CanSubmit"/>.
        /// The button stays tappable: tapping is how the user learns what is
        /// missing.</summary>
        public string ValidationText => CanSubmit ? null : MissingPhotoText;

        /// <summary>Transient line under the button — the submitting notice, the
        /// "could not take it" line, or the validation message. Null when silent.</summary>
        public string StatusText { get; private set; }

        public bool HasStatus => StatusText != null;

        /// <summary>
        /// Whether the last <see cref="Submit"/> was taken by the queue. False
        /// after a refused submit, so the screen can show the status in an error
        /// tone; meaningless before the first submit (true).
        /// </summary>
        public bool LastSubmitAccepted { get; private set; } = true;

        /// <summary>
        /// Validates, hands the report to the queue and resets the sighting fields.
        /// Returns false without raising <see cref="Submitted"/> when the form is
        /// incomplete (the user stays on the screen and reads
        /// <see cref="StatusText"/>).
        ///
        /// <para>When the service refuses the report — the photo could not be
        /// copied, the queue is full — this still raises <see cref="Submitted"/>
        /// (the user is never held on the screen by infrastructure) but keeps the
        /// form filled, so nothing they entered is lost if they come back.</para>
        /// </summary>
        public bool Submit()
        {
            if (!CanSubmit)
            {
                StatusText = ValidationText;
                LastSubmitAccepted = true;   // nothing was attempted
                Changed?.Invoke();
                return false;
            }

            var draft = new SightingDraft
            {
                PhotoPath = PhotoPath,
                BeachKey = BeachKey,
                WhenUtc = utcNow(),
                SpeciesKey = SelectedSpeciesKey,
                SpeciesLabel = SelectedSpeciesLabel,
                SizeBucket = SelectedSizeKey,
                BehaviourKeys = SelectedBehaviourKeys,
                ReporterName = Trimmed(ReporterName),
                ReporterEmail = Trimmed(ReporterEmail),
                ProfileKey = SelectedProfileKey,
            };

            bool accepted = reports != null && reports.Submit(draft);
            LastSubmitAccepted = accepted;
            StatusText = accepted ? SubmittingText : SubmitFailedText;
            if (accepted) ResetSightingFields();

            RebuildPending();
            Changed?.Invoke();
            Submitted?.Invoke();
            return accepted;
        }

        /// <summary>
        /// Clears the fields that describe one sighting — species, size,
        /// behaviours, photo, notes — and leaves the identity alone (see
        /// <see cref="ClearIdentity"/>).
        /// </summary>
        public void ResetSightingFields()
        {
            pickToken++;
            SelectedSpeciesIndex = -1;
            SelectedSizeIndex = -1;
            selectedBehaviours.Clear();
            PhotoPath = null;
            PhotoErrorText = null;
            IsPickingPhoto = false;
        }

        static string Trimmed(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // ---- Pending feed ---------------------------------------------------

        /// <summary>
        /// The user's undelivered reports, newest first: everything still queued
        /// plus anything the backend rejected outright. Empty = hide the section
        /// (the design has no empty state for it).
        ///
        /// <para>A delivered report simply disappears from this list — the queue
        /// keeps no success records, so "sent" is not a row state that can be
        /// shown (CLAUDE.md, "Background job system").</para>
        /// </summary>
        public IReadOnlyList<ReportPendingRow> PendingRows => pendingRows;

        public bool HasPending => pendingRows.Count > 0;

        public int PendingCount => pendingRows.Count;

        /// <summary>Re-reads the feed and notifies. Wired to
        /// <see cref="ISightingReports.Changed"/>; also safe to call on a timer.</summary>
        public void RefreshPending()
        {
            RebuildPending();
            Changed?.Invoke();
        }

        void RebuildPending()
        {
            pendingRows.Clear();
            if (reports == null) return;

            var records = new List<SightingRecord>();
            var pending = reports.ListPending();
            if (pending != null) records.AddRange(pending);
            var failed = reports.ListFailed();
            if (failed != null) records.AddRange(failed);

            // Newest first: the report the user just made is the one they are
            // looking for. Stable for equal timestamps (List.Sort is not, so sort
            // on a tuple with the original index as tiebreaker would be needed —
            // equal-to-the-tick submissions are not a real case, and the ids stay
            // distinct either way).
            records.Sort((a, b) => b.WhenUtc.CompareTo(a.WhenUtc));

            DateTime localNow = toLocalTime(utcNow());
            foreach (var record in records)
            {
                if (record == null) continue;
                string title = string.IsNullOrWhiteSpace(record.SpeciesLabel)
                    ? ReportFormatter.UnnamedSpeciesTitle
                    : record.SpeciesLabel;
                string caption = ReportFormatter.RowCaption(
                    ReportFormatter.DayTime(toLocalTime(record.WhenUtc), localNow),
                    record.BeachKey != null ? BeachLabel(record.BeachKey) : null);
                pendingRows.Add(new ReportPendingRow(
                    record.Id,
                    title,
                    ReportFormatter.StateLabel(record.State),
                    record.State,
                    caption,
                    record.PhotoPath));
            }
        }

        /// <summary>places.json key → pt-BR label; falls back to the key when the
        /// catalog has no entry, which beats showing nothing.</summary>
        string BeachLabel(string key)
        {
            var all = beaches?.Beaches;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && all[i].Name == key) return all[i].DisplayName;
                }
            }
            return key;
        }
    }
}
