using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SharkSizeReporter
{
    private const string PrefabFolder = "Assets/Prefabs/Sharks";

    private static readonly Dictionary<string, float> TargetLengthsM = new Dictionary<string, float>
    {
        { "hammerhead",  4.0f },
        { "tiger_shark", 3.5f },
        { "lemon_shark", 2.5f },
    };

    [MenuItem("Tools/Mergulho Virtual/Print Shark Sizes")]
    public static void Report()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning($"[SharkSizeReporter] No prefabs found under {PrefabFolder}");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Shark size report ===");
        sb.AppendLine("Native length is measured with the prefab root forced to scale (1,1,1).");
        sb.AppendLine("To bake the suggested size: open the FBX (Project → click the .fbx) → Inspector → Model tab → set Scale Factor to the suggested value → Apply. Then open the prefab and reset its Transform scale to (1,1,1).");
        sb.AppendLine();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                Physics.SyncTransforms();

                if (!TryComputeBounds(instance, out Bounds combined))
                {
                    sb.AppendLine($"{asset.name}: no Renderers found, skipped");
                    sb.AppendLine();
                    continue;
                }

                Vector3 size = combined.size;
                float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

                string fbxPath = FindFbxPathFor(asset.name);
                ModelImporter importer = string.IsNullOrEmpty(fbxPath)
                    ? null
                    : AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                float currentScaleFactor = importer != null ? importer.globalScale : 1f;

                sb.AppendLine($"{asset.name}");
                sb.AppendLine($"  prefab path: {path}");
                if (!string.IsNullOrEmpty(fbxPath))
                {
                    sb.AppendLine($"  fbx path: {fbxPath}");
                    sb.AppendLine($"  current FBX Scale Factor: {currentScaleFactor:F4}");
                }
                sb.AppendLine($"  native bounds (units): x={size.x:F3}  y={size.y:F3}  z={size.z:F3}");
                sb.AppendLine($"  longest axis (units): {longest:F3}");
                sb.AppendLine($"  prefab transform scale (current): {asset.transform.localScale.x:F4}");
                sb.AppendLine($"  effective real-world length now: {(longest * asset.transform.localScale.x):F2} m");

                if (TargetLengthsM.TryGetValue(asset.name, out float target))
                {
                    float effective = longest * asset.transform.localScale.x;
                    bool prefabScaleIsOne = Mathf.Abs(asset.transform.localScale.x - 1f) < 0.001f;
                    bool effectiveOnTarget = Mathf.Abs(effective - target) < 0.05f;
                    if (prefabScaleIsOne && effectiveOnTarget)
                    {
                        sb.AppendLine($"  ✓ already at target ({target:F2} m) — no change needed");
                    }
                    else
                    {
                        float suggested = target / longest;
                        sb.AppendLine($"  TARGET length: {target:F2} m  →  set FBX Scale Factor to {suggested:F4}, then prefab scale = (1,1,1)");
                    }
                }
                else
                {
                    sb.AppendLine("  (no target length mapped — add an entry to TargetLengthsM in SharkSizeReporter.cs)");
                }
                sb.AppendLine();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        Debug.Log(sb.ToString());
    }

    private static bool TryComputeBounds(GameObject root, out Bounds combined)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers.Length == 0)
        {
            combined = default;
            return false;
        }

        combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }
        return true;
    }

    private static string FindFbxPathFor(string prefabName)
    {
        string[] guids = AssetDatabase.FindAssets($"{prefabName} t:Model", new[] { "Assets/Models" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return path;
        }
        return null;
    }
}
