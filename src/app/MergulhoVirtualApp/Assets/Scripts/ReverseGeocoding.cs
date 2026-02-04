using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReverseGeocoding : MonoBehaviour
{
    /// <summary>
    /// Checks if a 2D point is inside a quadrilateral defined by 4 points.
    /// Uses the Ray Casting algorithm (Even-Odd Rule).
    /// </summary>
    /// <param name="p">The point to check.</param>
    /// <param name="poly">Array of 4 Vector2 points defining the quadrilateral.</param>
    /// <returns>True if the point is inside, false otherwise.</returns>
    public static bool IsPointInQuadrilateral(Vector2 p, Vector2[] poly)
    {
        if (poly == null || poly.Length < 3)
        {
            Debug.LogError("Polygon must have at least 3 points.");
            return false;
        }

        bool inside = false;
        // Iterate through each edge of the polygon
        // j is the previous vertex index (starts at last vertex)
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            // Check if horizontal ray from p intersects the edge (poly[i], poly[j])
            // 1. One vertex is above p.y and the other is below (ensures intersection in Y range)
            // 2. p.x is to the left of the edge's X-coordinate at p.y
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    [System.Serializable]
    public class PointData
    {
        public double lat;
        public double lon;
    }

    [System.Serializable]
    public class PlaceData
    {
        public string name;
        public List<PointData> points;
    }

    [System.Serializable]
    public class PlaceListWrapper
    {
        public List<PlaceData> places;
    }

    private static List<PlaceData> loadedPlaces;

    public static void LoadPlaces()
    {
        TextAsset targetFile = Resources.Load<TextAsset>("places");
        if (targetFile != null)
        {
            // JsonUtility doesn't support top-level arrays, so wrap it
            string wrappedJson = "{ \"places\": " + targetFile.text + "}";
            try 
            {
                PlaceListWrapper wrapper = JsonUtility.FromJson<PlaceListWrapper>(wrappedJson);
                if (wrapper != null)
                {
                    loadedPlaces = wrapper.places;
                    Debug.Log($"Loaded {loadedPlaces.Count} places.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error parsing places.json: " + e.Message);
            }
        }
        else
        {
            Debug.LogError("Places file not found in Resources");
        }
    }

    public static string GetPlaceName(Vector2 coordinate)
    {
        if (loadedPlaces == null)
        {
            LoadPlaces();
        }

        if (loadedPlaces != null)
        {
            foreach (var place in loadedPlaces)
            {
                if (place.points == null || place.points.Count < 3) continue;

                Vector2[] polygon = new Vector2[place.points.Count];
                for (int i = 0; i < place.points.Count; i++)
                {
                    // Map lon to x and lat to y as per plan
                    polygon[i] = new Vector2((float)place.points[i].lon, (float)place.points[i].lat);
                }

                if (IsPointInQuadrilateral(coordinate, polygon))
                {
                    return place.name;
                }
            }
        }

        return null;
    }
}
