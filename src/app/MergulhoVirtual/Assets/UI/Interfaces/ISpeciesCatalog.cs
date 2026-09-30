using System;
using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// One educational clip — the engine-free mirror of the Assembly-CSharp
    /// <c>VideoRef</c> (which is a global, <c>[Serializable]</c> class this
    /// assembly cannot see).
    ///
    /// <para><see cref="Url"/> is a directly streamable HTTPS object in the public
    /// <c>conteudos-educacionais</c> GCS bucket — no signing, no backend endpoint.
    /// It is never rendered; only <see cref="Title"/> is.</para>
    /// </summary>
    public sealed class SpeciesVideo
    {
        public readonly string Title;
        public readonly string Url;

        public SpeciesVideo(string title, string url)
        {
            Title = title;
            Url = url;
        }

        /// <summary>A clip with no URL cannot be played and is dropped by the catalog adapter.</summary>
        public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
    }

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

        /// <summary>
        /// The three "spec sheet" rows of the AR species card (§8.4): approximate
        /// size ("3 a 4 metros"), diet ("Peixes, tartarugas e moluscos") and
        /// behaviour ("Solitário e noturno"). Free pt-BR text, because that is
        /// what the card prints — there is no vocabulary to parse.
        ///
        /// <para><b>All three are blank on every shipped AnimalDef</b> and that is
        /// the standing rule, not an oversight (Decision D8): a blank beats an
        /// invented fact, so the card drops a row it has no value for rather than
        /// printing a plausible-looking guess. Filling them is content work for
        /// someone who knows the animals.</para>
        ///
        /// <para><see cref="Behaviour"/> is the species' behaviour in general.
        /// The per-beach line on the Praia detalhe species card is a different
        /// field on a different type (<c>BeachSpeciesContent.Behaviour</c>,
        /// "Comportamento nessa praia") — do not fold them together.</para>
        /// </summary>
        public string ApproximateSize;

        /// <inheritdoc cref="ApproximateSize"/>
        public string Diet;

        /// <inheritdoc cref="ApproximateSize"/>
        public string Behaviour;

        /// <summary>
        /// Attribution for <see cref="ImageName"/> ("Foto: Albert kok / CC BY-SA
        /// 4.0"). <b>A licence condition, not decoration</b> — every shipped photo
        /// is CC-BY-SA or public domain, so wherever the photo is shown large this
        /// has to be shown with it. Filled on all five species.
        /// </summary>
        public string PhotoCredit;

        /// <summary>
        /// Attribution for the 3D model, same licence reasoning as
        /// <see cref="PhotoCredit"/> and filled on all five species. Often several
        /// lines of Sketchfab/Meshy licence text, so it must be allowed to wrap
        /// freely rather than being squeezed into a fixed slot.
        /// </summary>
        public string ModelCredit;

        /// <summary>
        /// The species has a usable 3D model — i.e. its <c>AnimalDef.prefab</c> slot
        /// is filled. Set by the adapter, because a <c>GameObject</c> cannot cross
        /// into this assembly; the model itself is reached through
        /// <see cref="ISpeciesModelViewer"/>, keyed by <see cref="Key"/>.
        ///
        /// <para>False is a real state: the prefab reference silently dangles
        /// whenever a species' FBX is replaced (CLAUDE.md, "Replacing an existing
        /// species' FBX"), which is exactly the case the Espécie screen must render
        /// as "no 3D section" rather than as an empty black box.</para>
        /// </summary>
        public bool HasModel;

        /// <summary>
        /// Educational clips, in authoring order. Never null; <b>empty for four of
        /// the five shipped species</b> (only lemon_shark has any), so an absent
        /// video section is the common case, not a fault.
        /// </summary>
        public IReadOnlyList<SpeciesVideo> Videos = Array.Empty<SpeciesVideo>();

        public bool HasBinomial => !string.IsNullOrWhiteSpace(Binomial);
        public bool HasImage => !string.IsNullOrWhiteSpace(ImageName);
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
        public bool HasApproximateSize => !string.IsNullOrWhiteSpace(ApproximateSize);
        public bool HasDiet => !string.IsNullOrWhiteSpace(Diet);
        public bool HasBehaviour => !string.IsNullOrWhiteSpace(Behaviour);
        public bool HasPhotoCredit => !string.IsNullOrWhiteSpace(PhotoCredit);
        public bool HasModelCredit => !string.IsNullOrWhiteSpace(ModelCredit);
        public bool HasVideos => Videos != null && Videos.Count > 0;
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
