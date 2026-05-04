using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnimalsScreenController : MonoBehaviour
{
    [Header("List view")]
    [SerializeField] private GameObject listPanel;
    [SerializeField] private Transform listContent;
    [SerializeField] private ListItemView listItemPrefab;

    [Header("Detail view")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private ScrollRect detailScroll;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailDescription;
    [SerializeField] private Button backButton;

    [Header("3D viewer")]
    [SerializeField] private GameObject viewerRig;
    [SerializeField] private Transform turntable;
    [SerializeField] private string viewerLayerName = "AnimalViewer";

    private readonly List<ListItemView> spawnedItems = new List<ListItemView>();
    private GameObject currentViewerInstance;
    private AnimalDef[] animals;
    private bool listPopulated;

    void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(ShowList);
    }

    void OnDestroy()
    {
        if (backButton != null) backButton.onClick.RemoveListener(ShowList);
    }

    void OnEnable()
    {
        if (!listPopulated) PopulateList();
        ShowList();
    }

    void OnDisable()
    {
        if (viewerRig != null) viewerRig.SetActive(false);
        DestroyViewerInstance();
    }

    void PopulateList()
    {
        if (listContent == null || listItemPrefab == null)
        {
            Debug.LogWarning("[AnimalsScreenController] List content or item prefab not assigned.");
            return;
        }

        animals = Resources.LoadAll<AnimalDef>("Animals");
        foreach (AnimalDef animal in animals)
        {
            if (animal == null) continue;
            ListItemView item = Instantiate(listItemPrefab, listContent);
            AnimalDef captured = animal;
            item.Bind(animal.thumbnail, animal.displayName, null, () => ShowDetail(captured));
            spawnedItems.Add(item);
        }

        listPopulated = true;
    }

    void ShowList()
    {
        if (listPanel != null) listPanel.SetActive(true);
        if (detailPanel != null) detailPanel.SetActive(false);
        if (viewerRig != null) viewerRig.SetActive(false);
        DestroyViewerInstance();
    }

    void ShowDetail(AnimalDef animal)
    {
        if (animal == null) return;

        if (detailName != null) detailName.text = animal.displayName;
        if (detailDescription != null) detailDescription.text = animal.description;

        if (listPanel != null) listPanel.SetActive(false);
        if (detailPanel != null) detailPanel.SetActive(true);
        if (detailScroll != null) detailScroll.verticalNormalizedPosition = 1f;

        if (viewerRig != null) viewerRig.SetActive(true);
        SpawnViewerInstance(animal);
    }

    void SpawnViewerInstance(AnimalDef animal)
    {
        DestroyViewerInstance();
        if (turntable == null || animal.prefab == null) return;

        currentViewerInstance = Instantiate(animal.prefab, turntable);
        currentViewerInstance.transform.localPosition = animal.viewerOffset;
        currentViewerInstance.transform.localRotation = Quaternion.identity;
        currentViewerInstance.transform.localScale = animal.viewerScale;

        int layer = LayerMask.NameToLayer(viewerLayerName);
        if (layer >= 0) SetLayerRecursively(currentViewerInstance, layer);
        else Debug.LogWarning($"[AnimalsScreenController] Layer '{viewerLayerName}' not found. Add it under Project Settings > Tags and Layers.");
    }

    void DestroyViewerInstance()
    {
        if (currentViewerInstance != null)
        {
            Destroy(currentViewerInstance);
            currentViewerInstance = null;
        }
    }

    static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
