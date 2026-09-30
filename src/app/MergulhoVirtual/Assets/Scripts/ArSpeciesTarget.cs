using UnityEngine;

/// <summary>
/// Marks a spawned AR object as "this is species X", so
/// <see cref="ObjectInteraction"/> can turn a raycast hit into a species key
/// without knowing anything about which animals exist.
///
/// <para>This is the generalisation CLAUDE.md asked for: the old
/// <c>ObjectInteraction</c> compared <c>hit.collider.gameObject.name</c> against a
/// single hardcoded string ("Tubarão Martelo"), which matched nothing — the
/// spawner names its instances after the prefab ("tiger_shark") — so tap-to-info
/// had never actually worked. A component on the target carries the key instead,
/// and adding a species needs no change here.</para>
///
/// <para><b>The key is the AnimalDef asset file name</b> ("tiger_shark"), which is
/// also the shark prefab's name and the key <c>beaches_content.json</c> and
/// <c>ISpeciesCatalog.Find</c> use. That three-way agreement is why
/// <see cref="BeachSharkSpawner"/> can attach these with just
/// <c>prefab.name</c>; a species whose prefab and AnimalDef names ever diverge
/// needs an explicit <see cref="ArSpeciesTarget"/> authored on its prefab, which
/// <see cref="Attach"/> then leaves alone.</para>
/// </summary>
[DisallowMultipleComponent]
public class ArSpeciesTarget : MonoBehaviour
{
    [Tooltip("AnimalDef asset file name, e.g. \"tiger_shark\". A lookup key, never a label.")]
    [SerializeField] private string speciesKey;

    /// <summary>The species this object represents. Empty if never set.</summary>
    public string SpeciesKey => speciesKey;

    /// <summary>
    /// Tags <paramref name="root"/> with <paramref name="key"/> and makes sure it
    /// is actually hittable. Idempotent, and it never overrides a key a prefab
    /// authored for itself.
    ///
    /// <para><b>The collider is added here because none of the shark prefabs has
    /// one.</b> They are Prefab Variants of Sketchfab FBXs, which import with a
    /// SkinnedMeshRenderer and nothing else, so a raycast could never hit them —
    /// the other half of why tap-to-info did not work. A BoxCollider fitted to the
    /// union of the renderer bounds is the cheap, animation-proof choice: a
    /// MeshCollider off a skinned mesh is both expensive and stuck in the bind
    /// pose, and the tap only has to say "that animal", not "that fin". The bounds
    /// are the bind-pose AABB (the same caveat AnimalsScreenController documents
    /// for its auto-framing), which is fine for a touch target.</para>
    /// </summary>
    public static void Attach(GameObject root, string key)
    {
        if (root == null || string.IsNullOrEmpty(key)) return;

        var target = root.GetComponent<ArSpeciesTarget>();
        if (target == null) target = root.AddComponent<ArSpeciesTarget>();
        // A prefab that names its own species wins — it knows better than the
        // spawner's prefab-name convention.
        if (string.IsNullOrEmpty(target.speciesKey)) target.speciesKey = key;

        if (root.GetComponentInChildren<Collider>() != null) return;
        if (!TryGetLocalBounds(root, out var bounds)) return;

        var box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
    }

    /// <summary>
    /// Union of every renderer's world bounds, expressed in
    /// <paramref name="root"/>'s local space. False when the hierarchy renders
    /// nothing, in which case there is nothing to tap and no collider is added.
    /// </summary>
    static bool TryGetLocalBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        // Each world AABB is re-expressed in root space by mapping its eight
        // corners, not by scaling its size: the prefabs carry a yaw (the
        // hammerhead is at Y=180) and a rotated AABB's extents do not survive a
        // component-wise divide. Eight points per renderer, once per spawn.
        var t = root.transform;
        bool started = false;
        foreach (var renderer in renderers)
        {
            var world = renderer.bounds;
            Vector3 min = world.min, max = world.max;
            for (int c = 0; c < 8; c++)
            {
                var corner = t.InverseTransformPoint(new Vector3(
                    (c & 1) == 0 ? min.x : max.x,
                    (c & 2) == 0 ? min.y : max.y,
                    (c & 4) == 0 ? min.z : max.z));
                if (!started)
                {
                    bounds = new Bounds(corner, Vector3.zero);
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(corner);
                }
            }
        }
        return started;
    }
}
