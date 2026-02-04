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

    GPSLoc currLoc = new GPSLoc();


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
           Debug.Log("Location " + Input.location.lastData.latitude + " " + Input.location.lastData.longitude);
           debugTxt.text += "\nLocation: \nLat:" + Input.location.lastData.latitude + "\nLon:" + Input.location.lastData.longitude;
           gpsOk = true;

       }

    }


    // Update is called once per frame
    void Update()
    {
        if (gpsOk)
        {
            debugTxt.text = "\nLocation \nLat: " + Input.location.lastData.latitude + "\nLon: " + Input.location.lastData.longitude;

            currLoc.lat = Input.location.lastData.latitude;
            currLoc.lon = Input.location.lastData.longitude;

        }
        
    }
}

public class GPSLoc
{
    public float lon;
    public float lat;

    public GPSLoc()
    {
        lon = 0;
        lat = 0;
    }

    public GPSLoc(float lon, float lat)
    {
        this.lon = lon;
        this.lat = lat;
    }

    public string getLocData()
    {
        return "Lat: " + lat + "\nLon: " + lon;
    }

}
