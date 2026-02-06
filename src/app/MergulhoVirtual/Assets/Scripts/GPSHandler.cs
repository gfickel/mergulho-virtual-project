using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // Required for Button component
using UnityEngine.Android;

using TMPro;
using System;

public class GPSHandler : MonoBehaviour
{
    public TextMeshProUGUI debugTxt;
    public bool gpsOk = false;

    GPSLocation currLoc = new GPSLocation();


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    IEnumerator Start()
    {
       if (!Input.location.isEnabledByUser)
       {
           Debug.Log("Location not enabled on device");
           debugTxt.text = "Location not enabled on device";
       }

       #if UNITY_ANDROID
       if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            Permission.RequestUserPermission(Permission.CoarseLocation);
        }
        #endif
       Input.location.Start();

       int maxWait = 20;

       while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
       {
           yield return new WaitForSeconds(1);
           maxWait--;
       }

       if (maxWait < 1)
       {
           Debug.Log("Timed out");
           debugTxt.text += "\nTimed Out";
           yield break;
       }

       if (Input.location.status == LocationServiceStatus.Failed)
       {
           Debug.LogError("Unable to determine device location");
           debugTxt.text += "\nUnable to determine device location";
           yield break;
       }
       else
       {
           currLoc = GetLocation();
           Debug.Log("Location " + currLoc.latitude + " " + currLoc.longitude);
           debugTxt.text += "\nLat:" + currLoc.latitude + "\nLon:" + currLoc.longitude;
           gpsOk = true;

       }

    }

    GPSLocation GetLocation()
    {
    #if UNITY_EDITOR
        return new GPSLocation(-32.44f, -3.85f);
    #else
        return new GPSLocation(Input.location.lastData.longitude, Input.location.lastData.latitude);
    #endif
    }


    // Update is called once per frame
    void Update()
    {
        if (gpsOk)
        {
            GPSLocation location = GetLocation();
            string placeName = ReverseGeocoding.GetPlaceName(new Vector2(location.longitude, location.latitude));
            debugTxt.text = "\nLat: " + location.latitude + "\nLon: " + location.longitude + "\nLocal: " + placeName;

            currLoc = location;

        }
        
    }
}

public class GPSLocation
{
    public float longitude;
    public float latitude;

    public GPSLocation()
    {
        longitude = 0;
        latitude = 0;
    }

    public GPSLocation(float longitude, float latitude)
    {
        this.longitude = longitude;
        this.latitude = latitude;
    }

    public string getLocData()
    {
        return "Lat: " + latitude + "\nLon: " + longitude;
    }

}
