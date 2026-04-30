using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BeachesScreenController : MonoBehaviour
{
    [Header("List view")]
    [SerializeField] private GameObject listPanel;
    [SerializeField] private Transform listContent;
    [SerializeField] private ListItemView listItemPrefab;

    [Header("Detail view")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private Image detailImage;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailDescription;
    [SerializeField] private Button backButton;

    private readonly List<ListItemView> spawnedItems = new List<ListItemView>();
    private bool listPopulated;
    private string pendingDetailName;

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
        if (!string.IsNullOrEmpty(pendingDetailName))
        {
            ShowDetail(pendingDetailName);
            pendingDetailName = null;
        }
        else
        {
            ShowList();
        }
    }

    /// <summary>
    /// Public deep-link entry point — call this BEFORE activating the screen.
    /// If the screen is already active, it switches to the detail immediately;
    /// otherwise OnEnable picks up the pending name on the next activation.
    /// </summary>
    public void ShowDetailFor(string beachName)
    {
        if (string.IsNullOrEmpty(beachName)) return;
        if (gameObject.activeInHierarchy)
        {
            ShowDetail(beachName);
        }
        else
        {
            pendingDetailName = beachName;
        }
    }

    void PopulateList()
    {
        if (listContent == null || listItemPrefab == null)
        {
            Debug.LogWarning("[BeachesScreenController] List content or item prefab not assigned.");
            return;
        }

        foreach (var place in ReverseGeocoding.GetAllPlaces())
        {
            if (place == null || string.IsNullOrEmpty(place.name)) continue;
            ListItemView item = Instantiate(listItemPrefab, listContent);
            Sprite thumb = LoadBeachSprite(place.imageName);
            string capturedName = place.name;
            item.Bind(thumb, place.name, null, () => ShowDetail(capturedName));
            spawnedItems.Add(item);
        }

        listPopulated = true;
    }

    void ShowList()
    {
        if (listPanel != null) listPanel.SetActive(true);
        if (detailPanel != null) detailPanel.SetActive(false);
    }

    void ShowDetail(string placeName)
    {
        var place = ReverseGeocoding.GetPlace(placeName);
        if (place == null) return;

        if (detailImage != null)
        {
            Sprite sprite = LoadBeachSprite(place.imageName);
            detailImage.sprite = sprite;
            detailImage.enabled = sprite != null;
        }
        if (detailName != null) detailName.text = place.name;
        if (detailDescription != null) detailDescription.text = place.description;

        if (listPanel != null) listPanel.SetActive(false);
        if (detailPanel != null) detailPanel.SetActive(true);
    }

    static Sprite LoadBeachSprite(string imageName)
    {
        if (string.IsNullOrEmpty(imageName)) return null;
        return Resources.Load<Sprite>("Beaches/" + imageName);
    }
}
