using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BeachSelectorDropdown : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private GPSHandler gpsHandler;
    [SerializeField] private string autoOptionLabel = "Automático (GPS)";

    void Start()
    {
        if (dropdown == null || gpsHandler == null)
        {
            Debug.LogWarning("[BeachSelectorDropdown] Missing dropdown or GPSHandler reference.");
            return;
        }

        List<string> placeNames = ReverseGeocoding.GetAllPlaceNames();

        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData(autoOptionLabel)
        };
        foreach (string name in placeNames)
        {
            options.Add(new TMP_Dropdown.OptionData(name));
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(0);
        dropdown.onValueChanged.AddListener(OnDropdownChanged);
    }

    void OnDestroy()
    {
        if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
    }

    void OnDropdownChanged(int index)
    {
        if (index <= 0)
        {
            gpsHandler.ClearBeachOverride();
        }
        else
        {
            gpsHandler.SetBeachOverride(dropdown.options[index].text);
        }
    }
}
