using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>One label/value line of the AR species card's spec table.</summary>
    public readonly struct SpeciesSpecRow
    {
        /// <summary>Left column ("Tamanho aprox."). Always a fixed string from
        /// <see cref="SpeciesCardFormatter"/>, never data.</summary>
        public readonly string Label;

        /// <summary>Right column — the species' own value. Never null or blank:
        /// a row with nothing to say is not produced at all.</summary>
        public readonly string Value;

        public SpeciesSpecRow(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// pt-BR copy for the AR species card — DESIGN_IMPLEMENTATION.md §8.4,
    /// Figma frame Tela 8 (31:3979).
    ///
    /// <para>Same contract as <see cref="BeachContentFormatter"/> and
    /// <see cref="ConditionsFormatter"/>: plain static C#, no UnityEngine, no
    /// state, and <b>the screen never builds a user-visible string itself</b>.</para>
    ///
    /// <para><b>Which of the two "missing" conventions applies here, and why.</b>
    /// The other screens keep a "—" in a stat COLUMN, because a column is a fixed
    /// slot sitting next to a sibling that would be stranded at half width if it
    /// vanished. The three spec rows are stacked full-width lines with nothing
    /// beside them, so an unfilled row is simply <b>dropped</b>
    /// (<see cref="SpecRows"/> never emits one) — "DIETA —" three times over a
    /// live camera is noise, and a card that shows only the animal's name is a
    /// correct, readable card. When all three are missing the screen also drops
    /// the divider above them, the way Praia detalhe drops its stats rule.</para>
    ///
    /// <para>Every value is blank on every shipped AnimalDef today (Decision D8 —
    /// a blank beats an invented fact), so the honest card is currently name +
    /// binomial. Filling them is content work, not a code change.</para>
    /// </summary>
    public static class SpeciesCardFormatter
    {
        // Labels, transcribed from Tela 8's `specs` frame, and spelled UPPERCASE
        // because that is how V2 renders them — the Figma text nodes are title case
        // under a text-case transform, and the transform is the thing the reader
        // sees. UI Toolkit has no `text-transform` and the screen may not
        // manufacture a string, so the case has to be baked in here; the 0.5px
        // tracking the stylesheets put on these labels is an all-caps device and
        // only reads correctly against all-caps text.
        public const string SizeLabel = "TAMANHO APROX.";
        public const string DietLabel = "DIETA";
        public const string BehaviourLabel = "COMPORTAMENTO";

        /// <summary>Accessible name of the card's dismiss control (V2 draws a bare ⨯).</summary>
        public const string CloseLabel = "Fechar";

        /// <summary>
        /// The card's title. Falls back to the catalog key only when a species
        /// somehow has no display name — <see cref="ISpeciesCatalog"/>'s adapter
        /// already substitutes the key, so this is belt and braces rather than a
        /// second naming rule.
        /// </summary>
        public static string Title(SpeciesInfo species)
        {
            if (species == null) return null;
            return string.IsNullOrWhiteSpace(species.DisplayName) ? species.Key : species.DisplayName;
        }

        /// <summary>
        /// The scientific binomial, or <b>null</b> to hide the line. Blank where
        /// the identification is still open (reef_shark today), and a wrong
        /// binomial is worse than none — so the card shows the common name alone.
        /// </summary>
        public static string Binomial(SpeciesInfo species) =>
            species != null && species.HasBinomial ? species.Binomial : null;

        /// <summary>
        /// The spec table, in Tela 8's order (size, diet, behaviour), with the
        /// rows that have no value left out. Never null; empty is the normal
        /// result today.
        /// </summary>
        public static IReadOnlyList<SpeciesSpecRow> SpecRows(SpeciesInfo species)
        {
            var rows = new List<SpeciesSpecRow>(3);
            if (species == null) return rows;
            if (species.HasApproximateSize) rows.Add(new SpeciesSpecRow(SizeLabel, species.ApproximateSize.Trim()));
            if (species.HasDiet) rows.Add(new SpeciesSpecRow(DietLabel, species.Diet.Trim()));
            if (species.HasBehaviour) rows.Add(new SpeciesSpecRow(BehaviourLabel, species.Behaviour.Trim()));
            return rows;
        }
    }
}
