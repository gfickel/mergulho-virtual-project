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
    [SerializeField] private TMP_Text detailCredits;
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
            item.Bind(LoadAnimalSprite(animal.imageName), animal.displayName, null, () => ShowDetail(captured));
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
        if (detailCredits != null) detailCredits.text = BuildCreditsText(animal);

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
        currentViewerInstance.transform.localScale = animal.viewerScale;

        int layer = LayerMask.NameToLayer(viewerLayerName);
        if (layer >= 0) SetLayerRecursively(currentViewerInstance, layer);
        else Debug.LogWarning($"[AnimalsScreenController] Layer '{viewerLayerName}' not found. Add it under Project Settings > Tags and Layers.");

        CenterOnTurntable(currentViewerInstance);
    }

    void CenterOnTurntable(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        Vector3 delta = turntable.position - bounds.center;
        instance.transform.position += delta;

        FitCameraToBounds(bounds.size);
    }

    void FitCameraToBounds(Vector3 size)
    {
        if (viewerRig == null || turntable == null) return;
        Camera cam = viewerRig.GetComponentInChildren<Camera>();
        if (cam == null) return;

        float maxDim = Mathf.Max(size.x, size.y, size.z);
        if (maxDim <= 0f) return;
        float distance = (maxDim * 0.5f) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        distance *= 1.15f;

        Vector3 toCam = cam.transform.position - turntable.position;
        if (toCam.sqrMagnitude < 0.0001f) toCam = -turntable.forward;
        cam.transform.position = turntable.position + toCam.normalized * distance;
        cam.transform.LookAt(turntable.position);
    }

    void DestroyViewerInstance()
    {
        if (currentViewerInstance != null)
        {
            Destroy(currentViewerInstance);
            currentViewerInstance = null;
        }
    }

    static string BuildCreditsText(AnimalDef animal)
    {
        string photo = animal.photoCredit?.Trim();
        string model = animal.modelCredit?.Trim();
        bool hasPhoto = !string.IsNullOrEmpty(photo);
        bool hasModel = !string.IsNullOrEmpty(model);
        if (hasPhoto && hasModel) return photo + "\n" + model;
        if (hasPhoto) return photo;
        if (hasModel) return model;
        return string.Empty;
    }

    static Sprite LoadAnimalSprite(string imageName)
    {
        if (string.IsNullOrEmpty(imageName)) return null;
        return Resources.Load<Sprite>("Animals/" + imageName);
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
