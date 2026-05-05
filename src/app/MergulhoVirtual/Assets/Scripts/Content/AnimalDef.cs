using UnityEngine;

[CreateAssetMenu(fileName = "Animal", menuName = "Mergulho Virtual/Animal", order = 0)]
public class AnimalDef : ScriptableObject
{
    public string displayName;
    public string imageName;
    [TextArea(3, 10)] public string description;
    public GameObject prefab;

    public Vector3 viewerScale = Vector3.one;
    public Vector3 viewerOffset = Vector3.zero;

    [TextArea(2, 4)] public string photoCredit;
}
