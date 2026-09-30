using System.Collections.Generic;
using UnityEngine;

public class BeachSharkSpawner : MonoBehaviour
{
    [System.Serializable]
    public class BeachSharks
    {
        public string beachName;
        public GameObject[] sharkPrefabs;
    }

    [SerializeField] private GPSHandler gpsHandler;
    [SerializeField] private Transform spawnRoot;
    [SerializeField] private List<BeachSharks> beaches = new List<BeachSharks>();

    private readonly List<GameObject> spawned = new List<GameObject>();

    void OnEnable()
    {
        if (gpsHandler == null)
        {
            Debug.LogWarning("[BeachSharkSpawner] GPSHandler reference is not assigned.");
            return;
        }

        gpsHandler.PlaceChanged += HandlePlaceChanged;
        HandlePlaceChanged(gpsHandler.CurrentPlaceName);
    }

    void OnDisable()
    {
        if (gpsHandler != null)
        {
            gpsHandler.PlaceChanged -= HandlePlaceChanged;
        }
        DespawnAll();
    }

    void HandlePlaceChanged(string placeName)
    {
        DespawnAll();
        if (string.IsNullOrEmpty(placeName)) return;

        BeachSharks match = beaches.Find(b => b.beachName == placeName);
        if (match == null || match.sharkPrefabs == null) return;

        Transform parent = spawnRoot != null ? spawnRoot : transform;
        foreach (GameObject prefab in match.sharkPrefabs)
        {
            if (prefab == null) continue;
            GameObject instance = Instantiate(prefab, parent);
            instance.name = prefab.name;
            // Makes the instance tappable and tells ObjectInteraction which
            // species it is. The prefab NAME is the key: the five shark prefabs
            // and the five AnimalDef assets under Resources/Animals share their
            // file names ("tiger_shark"), which is the same key
            // beaches_content.json and ISpeciesCatalog use. Attach also fits a
            // collider, because the Prefab Variants of the Sketchfab FBXs have
            // none — see ArSpeciesTarget.
            ArSpeciesTarget.Attach(instance, prefab.name);
            spawned.Add(instance);
        }
    }

    void DespawnAll()
    {
        foreach (GameObject obj in spawned)
        {
            if (obj != null) Destroy(obj);
        }
        spawned.Clear();
    }
}
