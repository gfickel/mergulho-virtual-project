using UnityEngine;

[CreateAssetMenu(fileName = "Animal", menuName = "Mergulho Virtual/Animal", order = 0)]
public class AnimalDef : ScriptableObject
{
    public string displayName;

    /// <summary>
    /// Scientific binomial ("Sphyrna mokarran"), shown next to the common name on
    /// the Praia detalhe species card and the AR species card. Optional and
    /// deliberately blank where the species' identification is still open — a
    /// wrong binomial is worse than none, so the screens render the name alone.
    /// </summary>
    public string binomial;

    public string imageName;
    [TextArea(3, 10)] public string description;

    /// <summary>
    /// The three "spec sheet" rows of the AR species card (DESIGN_IMPLEMENTATION.md
    /// §8.4): "3 a 4 metros" / "Peixes, tartarugas e moluscos" / "Solitário e
    /// noturno". Free pt-BR text — the card prints them verbatim, there is no
    /// vocabulary to parse — surfaced through SpeciesInfo/ISpeciesCatalog.
    ///
    /// <para><b>Blank on every shipped asset, deliberately</b>, exactly like
    /// <see cref="binomial"/> and for the same reason (Decision D8): an invented
    /// fact about an animal is worse than a blank, and the card drops a row it has
    /// no value for instead of printing a plausible-looking guess. Filling these in
    /// is content work for someone who knows the species — the code is done.</para>
    ///
    /// <para><see cref="behaviour"/> is the species' behaviour in general. The
    /// per-beach line on the Praia detalhe species card is a different field in a
    /// different file (beaches_content.json, <c>species[].behaviour</c>,
    /// "Comportamento nessa praia") — do not fold them together.</para>
    /// </summary>
    public string approximateSize;

    /// <inheritdoc cref="approximateSize"/>
    public string diet;

    /// <inheritdoc cref="approximateSize"/>
    public string behaviour;

    public GameObject prefab;

    public Vector3 viewerScale = Vector3.one;
    public Vector3 viewerOffset = Vector3.zero;

    [TextArea(2, 4)] public string photoCredit;
    [TextArea(2, 4)] public string modelCredit;

    // Zero or more educational clips. The Espécie screen renders one inline,
    // tap-to-play card per entry. Empty = no video section is shown.
    public VideoRef[] videos;
}
