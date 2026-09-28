using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// One species as the UI layer sees it. Mirror of the Assembly-CSharp
    /// <c>AnimalDef</c> ScriptableObject (Resources/Animals/*.asset), without
    /// referencing it — the catalog is the single source of truth for what a
    /// species is called, so the Praia detalhe chips, the Animais catalog and the
    /// AR species card can never drift into three spellings.
    /// </summary>
    public sealed class SpeciesInfo
    {
        /// <summary>
        /// Catalog key = the AnimalDef asset file name ("lemon_shark"). Matches
        /// <see cref="BeachSpeciesContent.SpeciesKey"/>. A lookup key, never a
        /// label — never render it.
        /// </summary>
        public string Key;

        /// <summary>pt-BR label to display ("Tubarão-limão"). Never a lookup key.</summary>
        public string DisplayName;

        /// <summary>
        /// Scientific binomial ("Negaprion brevirostris"), shown next to the name
        /// on the species card (§8.3) and the AR card (§8.4). <b>Optional</b> —
        /// empty where the species' identification is still unresolved, in which
        /// case the screen shows the name alone rather than a guess.
        /// </summary>
        public string Binomial;

        /// <summary>Sprite file name under Resources/Animals/. Optional.</summary>
        public string ImageName;

        /// <summary>Global species description (not per-beach). Optional.</summary>
        public string Description;

        public bool HasBinomial => !string.IsNullOrWhiteSpace(Binomial);
        public bool HasImage => !string.IsNullOrWhiteSpace(ImageName);
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    }

    /// <summary>
    /// Read-only species catalog, keyed by the AnimalDef asset name. Implemented
    /// by <c>UiServiceAdapters.SpeciesCatalogAdapter</c>.
    /// </summary>
    public interface ISpeciesCatalog
    {
        /// <summary>Every species the app knows about. Never null.</summary>
        IReadOnlyList<SpeciesInfo> Species { get; }

        /// <summary>
        /// Species for a key, or null when the key is unknown — which is what a
        /// typo in beaches_content.json looks like. Callers must drop unknown
        /// keys rather than render a blank chip.
        /// </summary>
        SpeciesInfo Find(string key);
    }
}
