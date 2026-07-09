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
        public string imageName;
        public string description;
        public string photoCredit;
        public List<PointData> points;
    }

    [System.Serializable]
    public class PlaceListWrapper
    {
        public List<PlaceData> places;
    }

    private static List<PlaceData> loadedPlaces;

    // Per-place polygon rings as Vector2[] (lon->x, lat->y), built once from loadedPlaces
    // and reused across calls so GetPlaceName (invoked every frame) allocates nothing.
    private static Vector2[][] placePolygons;

    /// <summary>Default beach buffer (meters). A point resolves to a beach when it is
    /// within this distance of the beach polygon (0 if inside). GPSHandler overrides this
    /// from its Inspector field; this constant only backs the single-argument overload
    /// (used by the reverse-geocoding tests).</summary>
    public const float DefaultBufferMeters = 50f;

    // Local flat-earth projection, good to sub-meter over Noronha's few km.
    // Matches tools/beach_polygon_debug.py so the desktop debugger and the app agree.
    private const float MetersPerDegLat = 111320f;
    private const float RefLatDeg = -3.85f;
    private static readonly float MetersPerDegLon =
        MetersPerDegLat * Mathf.Cos(RefLatDeg * Mathf.Deg2Rad);

    public static void LoadPlaces()
    {
        placePolygons = null; // force rebuild against the freshly loaded data
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

    public static List<string> GetAllPlaceNames()
    {
        if (loadedPlaces == null)
        {
            LoadPlaces();
        }

        var names = new List<string>();
        if (loadedPlaces != null)
        {
            foreach (var place in loadedPlaces)
            {
                if (!string.IsNullOrEmpty(place.name)) names.Add(place.name);
            }
        }
        return names;
    }

    public static List<PlaceData> GetAllPlaces()
    {
        if (loadedPlaces == null)
        {
            LoadPlaces();
        }
        return loadedPlaces ?? new List<PlaceData>();
    }

    /// <summary>
    /// Centroid (average of polygon vertices) for the given place, mapped lon→x, lat→y
    /// to match GetPlaceName's convention. Returns null if the place is unknown.
    /// </summary>
    public static Vector2? GetCentroid(string placeName)
    {
        var place = GetPlace(placeName);
        if (place == null || place.points == null || place.points.Count == 0) return null;

        double sumLon = 0, sumLat = 0;
        foreach (var p in place.points)
        {
            sumLon += p.lon;
            sumLat += p.lat;
        }
        int n = place.points.Count;
        return new Vector2((float)(sumLon / n), (float)(sumLat / n));
    }

    public static PlaceData GetPlace(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (loadedPlaces == null)
        {
            LoadPlaces();
        }
        if (loadedPlaces == null) return null;
        foreach (var place in loadedPlaces)
        {
            if (place.name == name) return place;
        }
        return null;
    }

    // Builds the cached Vector2[] rings from loadedPlaces on first use (idempotent).
    private static void EnsurePolygons()
    {
        if (loadedPlaces == null) LoadPlaces();
        if (loadedPlaces == null || placePolygons != null) return;

        placePolygons = new Vector2[loadedPlaces.Count][];
        for (int p = 0; p < loadedPlaces.Count; p++)
        {
            var pts = loadedPlaces[p].points;
            if (pts == null)
            {
                placePolygons[p] = System.Array.Empty<Vector2>();
                continue;
            }
            var ring = new Vector2[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                // Map lon to x and lat to y (the app's convention everywhere).
                ring[i] = new Vector2((float)pts[i].lon, (float)pts[i].lat);
            }
            placePolygons[p] = ring;
        }
    }

    // Distance (meters) from a point to segment a->b, all already in the local meter frame.
    private static float SegmentDistanceMeters(float px, float py,
                                               float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float l2 = dx * dx + dy * dy;
        float t = l2 == 0f ? 0f : Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / l2);
        float cx = ax + t * dx, cy = ay + t * dy;
        float ex = px - cx, ey = py - cy;
        return Mathf.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>
    /// 0 if <paramref name="coordinate"/> (lon->x, lat->y) is inside the polygon,
    /// otherwise the distance in meters to the nearest polygon edge. This is the exact
    /// "within D meters of the beach" test without ever building the buffered polygon.
    /// </summary>
    public static float BeachDistanceMeters(Vector2 coordinate, Vector2[] polygon)
    {
        if (polygon == null || polygon.Length < 3) return float.PositiveInfinity;
        if (IsPointInQuadrilateral(coordinate, polygon)) return 0f;

        float px = coordinate.x * MetersPerDegLon;
        float py = coordinate.y * MetersPerDegLat;
        float best = float.PositiveInfinity;
        int n = polygon.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % n];
            float d = SegmentDistanceMeters(px, py,
                a.x * MetersPerDegLon, a.y * MetersPerDegLat,
                b.x * MetersPerDegLon, b.y * MetersPerDegLat);
            if (d < best) best = d;
        }
        return best;
    }

    public static string GetPlaceName(Vector2 coordinate)
    {
        return GetPlaceName(coordinate, DefaultBufferMeters);
    }

    /// <summary>
    /// Resolves the coordinate to the nearest beach, accepting it only when that beach is
    /// within <paramref name="bufferMeters"/> (0 if the point is inside the polygon).
    /// Nearest-distance-wins makes overlapping polygons harmless: whichever beach is closest
    /// wins regardless of file order, so the old first-match-inside ambiguity is gone.
    /// Returns null when no beach is within the buffer.
    /// </summary>
    public static string GetPlaceName(Vector2 coordinate, float bufferMeters)
    {
        EnsurePolygons();
        if (loadedPlaces == null || placePolygons == null) return null;

        float bestDist = float.PositiveInfinity;
        string bestName = null;
        for (int i = 0; i < placePolygons.Length; i++)
        {
            var poly = placePolygons[i];
            if (poly == null || poly.Length < 3) continue;

            float d = BeachDistanceMeters(coordinate, poly);
            if (d < bestDist)
            {
                bestDist = d;
                bestName = loadedPlaces[i].name;
            }
        }

        return bestDist <= bufferMeters ? bestName : null;
    }
}
