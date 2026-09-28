using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Risk badge level ("Risco: Baixo"). <see cref="Unknown"/> is the default and
    /// means <b>nobody has filled it in yet</b> — the screen draws no pill at all.
    /// It never means "safe": an unfilled risk level and a low risk level must not
    /// render the same way.
    /// </summary>
    public enum BeachRiskLevel
    {
        Unknown = 0,
        Low,
        Medium,
        High,
    }

    /// <summary>
    /// "Maré Ideal" for visiting/snorkelling. <see cref="Unknown"/> = unfilled;
    /// <see cref="Any"/> = "qualquer maré", which is an actual answer.
    /// </summary>
    public enum BeachIdealTide
    {
        Unknown = 0,
        Low,
        High,
        Any,
    }

    /// <summary>
    /// One species as a given beach describes it: the catalog key plus the two
    /// per-beach editorial bits. The species' own name, binomial and photo come
    /// from <see cref="ISpeciesCatalog"/> — this type deliberately carries none of
    /// them, so a beach can never disagree with the catalog about what a species
    /// is called.
    /// </summary>
    public sealed class BeachSpeciesContent
    {
        /// <summary>
        /// Catalog key — the AnimalDef asset name ("lemon_shark"). A lookup key,
        /// never a label: it is ASCII, English, and must never reach the screen.
        /// </summary>
        public string SpeciesKey;

        /// <summary>Small pill over the species photo ("Área de berçário"). Optional.</summary>
        public string Tag;

        /// <summary>"Comportamento nessa praia: …" body copy. Optional.</summary>
        public string Behaviour;

        public bool HasTag => !string.IsNullOrWhiteSpace(Tag);
        public bool HasBehaviour => !string.IsNullOrWhiteSpace(Behaviour);
    }

    /// <summary>
    /// The hand-authored editorial content for one beach — everything the V2
    /// Praias screens show that is not geometry, a photo or live conditions
    /// (DESIGN_IMPLEMENTATION.md §5.1). Mirrors
    /// <c>Assets/Resources/beaches_content.json</c>, keyed by the places.json
    /// <c>name</c>, and is engine-free so ViewModels stay plain-C# testable.
    ///
    /// <para><b>Every field is optional, and most of them are empty today</b> —
    /// risk level, best season, ideal tide, sighting peak, lifeguard hours, tips
    /// and per-beach species behaviour are blank until the project's biologists
    /// fill them (docs/beaches-content-todo.md). "Absent" is modelled explicitly:
    /// enums default to <c>Unknown</c>, lists default to empty (never null), and
    /// every field has a <c>Has*</c> companion. Ask <c>HasBestSeason</c>; do not
    /// compare strings to <c>""</c>, and never substitute an invented default —
    /// a guessed lifeguard schedule is worse than a blank line.</para>
    /// </summary>
    public sealed class BeachContent
    {
        /// <summary>
        /// The places.json <c>name</c> key this content belongs to (machine key,
        /// not a label). Set even when the beach has no entry in the file, so a
        /// returned empty content can still be attributed.
        /// </summary>
        public string BeachName;

        public BeachRiskLevel RiskLevel;

        /// <summary>"Set - Fev" — short free text. Optional.</summary>
        public string BestSeason;

        public BeachIdealTide IdealTide;

        /// <summary>"Jan-Mar manhã" — short free text. Optional.</summary>
        public string SightingPeak;

        /// <summary>"Das 08h às 17h" — short free text, rendered as "Salva-vidas: …". Optional.</summary>
        public string LifeguardHours;

        /// <summary>Dark pills over the cover photo ("Ambiente recifal"). Never null; empty = none.</summary>
        public IReadOnlyList<string> EnvironmentTags = Array.Empty<string>();

        /// <summary>Rules and restrictions for the amber alert bar. Never null; empty = none.</summary>
        public IReadOnlyList<string> Advisories = Array.Empty<string>();

        /// <summary>
        /// Species chips, in author order — the first one is the one the screen
        /// opens on. Never null; empty = the whole species section is hidden.
        /// </summary>
        public IReadOnlyList<BeachSpeciesContent> Species = Array.Empty<BeachSpeciesContent>();

        /// <summary>"Dicas de convivência", numbered 1-2-3 by the app (the text carries no number).
        /// Never null; empty = the section is hidden.</summary>
        public IReadOnlyList<string> Tips = Array.Empty<string>();

        public bool HasRiskLevel => RiskLevel != BeachRiskLevel.Unknown;
        public bool HasBestSeason => !string.IsNullOrWhiteSpace(BestSeason);
        public bool HasIdealTide => IdealTide != BeachIdealTide.Unknown;
        public bool HasSightingPeak => !string.IsNullOrWhiteSpace(SightingPeak);
        public bool HasLifeguardHours => !string.IsNullOrWhiteSpace(LifeguardHours);
        public bool HasEnvironmentTags => EnvironmentTags != null && EnvironmentTags.Count > 0;
        public bool HasAdvisories => Advisories != null && Advisories.Count > 0;
        public bool HasSpecies => Species != null && Species.Count > 0;
        public bool HasTips => Tips != null && Tips.Count > 0;

        /// <summary>
        /// True when not one field is filled — the common case today. Note this is
        /// also what a beach with no entry in the file at all returns, and that is
        /// deliberate: both render the same empty screen. Use
        /// <see cref="IBeachContent.TryGetContent"/> when the difference matters
        /// (tooling, diagnostics), not when rendering.
        /// </summary>
        public bool IsEmpty =>
            !HasRiskLevel && !HasBestSeason && !HasIdealTide && !HasSightingPeak &&
            !HasLifeguardHours && !HasEnvironmentTags && !HasAdvisories && !HasSpecies && !HasTips;

        /// <summary>An all-absent content for <paramref name="beachName"/>. Always a
        /// fresh instance — never hand out a shared mutable singleton.</summary>
        public static BeachContent EmptyFor(string beachName) =>
            new BeachContent { BeachName = beachName };
    }

    /// <summary>
    /// Read-only access to the hand-authored beach content, keyed by the exact
    /// places.json <c>name</c> (the same key <c>BeachSharkSpawner</c> and the
    /// backend's <c>local</c> field use). Implemented by
    /// <c>UiServiceAdapters.BeachContentAdapter</c> over
    /// <c>BeachContentLibrary</c>.
    /// </summary>
    public interface IBeachContent
    {
        /// <summary>
        /// Content for a beach. <b>Never returns null</b> — an unknown beach, an
        /// unreadable content file and a beach with an all-blank entry all return
        /// an empty <see cref="BeachContent"/>, so a screen can bind without
        /// null-checking and simply hides the sections whose <c>Has*</c> is false.
        /// </summary>
        BeachContent ForBeach(string beachName);

        /// <summary>
        /// False when the content file has no entry for this beach at all (as
        /// opposed to an entry whose fields are blank). For diagnostics and
        /// content tooling; screens should call <see cref="ForBeach"/>.
        /// </summary>
        bool TryGetContent(string beachName, out BeachContent content);
    }
}
