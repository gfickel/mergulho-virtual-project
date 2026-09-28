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
    public GameObject prefab;

    public Vector3 viewerScale = Vector3.one;
    public Vector3 viewerOffset = Vector3.zero;

    [TextArea(2, 4)] public string photoCredit;
    [TextArea(2, 4)] public string modelCredit;

    // Zero or more educational clips. A VideoSection renders one inline,
    // tap-to-play card per entry. Empty = no video section is shown.
    public VideoRef[] videos;
}
