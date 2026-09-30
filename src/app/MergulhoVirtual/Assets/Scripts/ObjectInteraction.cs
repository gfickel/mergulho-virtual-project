using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The AR scene's hit source: reads the pointer, casts into the world, and
/// reports which species was tapped. It does <b>not</b> present anything —
/// Slice 4 moved the presentation to the UI Toolkit <c>MergulhoScreen</c>, which
/// reaches this through <c>IArSelection</c> / <c>UiServiceAdapters.ArSelectionAdapter</c>
/// (DESIGN_IMPLEMENTATION.md §7: "keep ObjectInteraction as the hit source; only
/// the presentation moves").
///
/// <para><b>What changed and why it had never worked.</b> This used to compare
/// <c>hit.collider.gameObject.name</c> to a single hardcoded
/// <c>targetName = "Tubarão Martelo"</c> and toggle an <c>infoText</c> GameObject.
/// Nothing in the scene was ever called that (<see cref="BeachSharkSpawner"/>
/// names instances after the prefab, "hammerhead"), the shark prefabs carry no
/// colliders at all, and no GameObject in MainScene had this component — so the
/// feature was three separate kinds of dead. The comparison is now an
/// <see cref="ArSpeciesTarget"/> lookup up the hit's hierarchy, the spawner
/// attaches those targets (with a fitted collider), and AppUiBuilder puts this
/// component in the scene.</para>
///
/// <para><b>A miss raises nothing.</b> Tapping open water leaves the open card
/// alone, which is why V2 gives the card a close button. The raycast only runs
/// while <see cref="Listening"/> is true — the AR HUD is one route of four.</para>
///
/// <para><b>A tap the UI already consumed raises nothing either.</b>
/// <see cref="Pointer"/> is raw device state: UI Toolkit handling a click inside
/// its own panel does not clear it, so without a test here the ⨯ that closes the
/// species card would close it and immediately re-open it with the same tap, and
/// every row of the beach-selector menu would also poke the animal behind the
/// popup. <see cref="IsPointerOverUi"/> asks the panel itself
/// (<see cref="IPanel.Pick"/>), which is the only check that matches the HUD's
/// actual shape: <c>MergulhoScreen</c> is deliberately almost all
/// <see cref="PickingMode.Ignore"/> — transparent root, control strip and dock —
/// precisely so a tap on open water still reaches the sea, and <c>Pick</c> lets
/// exactly those through while blocking the card, its ⨯, the hero controls, the
/// nav bar and any open menu's scrim. A coarser "is the AR screen visible" test
/// would swallow the whole screen.</para>
/// </summary>
public class ObjectInteraction : MonoBehaviour
{
    [Tooltip("Radius of the sphere cast, in metres. Wider than a ray so a distant animal is still an easy tap target.")]
    [SerializeField] private float tapRadius = 0.2f;

    [Tooltip("Log every cast. Noisy — for on-beach debugging only.")]
    [SerializeField] private bool verbose;

    [Tooltip("The UIDocument whose panel carries the UI Toolkit shell (scene root 'AppUI'). " +
             "Optional: left empty, the first active UIDocument in the scene is found on the first tap. " +
             "Taps that land on a pickable element of that panel never reach the world.")]
    [SerializeField] private UIDocument uiPanelSource;

    // Resolved on first use and kept, so the scene search happens once rather than
    // on every tap. Cleared implicitly when the document is destroyed (Unity's
    // fake-null), which re-triggers the search.
    private UIDocument resolvedPanelSource;

    // Reused across taps; 16 is far more than a beach ever spawns plus whatever
    // AR planes happen to lie along the ray.
    private readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>
    /// A tap resolved to an animal; the argument is its species key (the
    /// AnimalDef asset file name). Never raised for a miss.
    /// </summary>
    public event Action<string> SpeciesSelected;

    /// <summary>
    /// Whether taps are read at all. False by default: the component lives at the
    /// scene root and the AR view is one destination of four, so the screen turns
    /// it on in OnEnter and off in OnExit.
    /// </summary>
    public bool Listening { get; set; }

    void Update()
    {
        if (!Listening) return;
        if (Pointer.current == null) return;
        if (!Pointer.current.press.wasPressedThisFrame) return;

        Vector2 screenPosition = Pointer.current.position.ReadValue();
        if (IsPointerOverUi(screenPosition))
        {
            if (verbose) Debug.Log($"[ObjectInteraction] tap at {screenPosition} consumed by the UI panel");
            return;
        }

        HandleTap(screenPosition);
    }

    /// <summary>
    /// Whether the tap landed on something the UI Toolkit panel draws and accepts.
    /// Screen coordinates go in (origin bottom-left, as the Input System reports
    /// them); <see cref="RuntimePanelUtils.ScreenToPanel"/> does the flip and the
    /// dp scaling, and <see cref="IPanel.Pick"/> answers with the topmost pickable
    /// element under the point — null when the shell painted nothing there, which
    /// is most of the AR HUD. No panel in the scene means nothing to block.
    /// </summary>
    bool IsPointerOverUi(Vector2 screenPosition)
    {
        IPanel panel = ResolvePanel();
        if (panel == null) return false;

        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
        return panel.Pick(panelPosition) != null;
    }

    IPanel ResolvePanel()
    {
        // The serialized reference wins when one is wired; otherwise find the
        // shell's document once. Deliberately NOT a reference from the UI assembly
        // to this class — Assets/UI may not see Assembly-CSharp, and this component
        // is the one that can see both worlds.
        var document = uiPanelSource != null ? uiPanelSource : resolvedPanelSource;
        if (document == null)
        {
            document = FindAnyObjectByType<UIDocument>();
            resolvedPanelSource = document;
        }

        // rootVisualElement is null while the document is disabled — UIDocument
        // tears its tree down on deactivate — and then there is no panel to hit.
        var root = document != null ? document.rootVisualElement : null;
        return root?.panel;
    }

    void HandleTap(Vector2 screenPosition)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            Debug.LogError("[ObjectInteraction] No Main Camera — is the AR camera tagged 'MainCamera'?");
            return;
        }

        Ray ray = camera.ScreenPointToRay(screenPosition);

        // NonAlloc over the single-hit SphereCast: that one returns only the
        // CLOSEST collider, and an AR plane (ARCore's detected sea surface / sand)
        // lying between the camera and an animal would mask it — the tap would then
        // silently do nothing. Every hit is considered and the nearest ANIMAL wins.
        int count = Physics.SphereCastNonAlloc(ray, tapRadius, hits);

        ArSpeciesTarget nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            // GetComponentInParent, not GetComponent: the collider may sit on a
            // child of the spawned root (a prefab that authored its own colliders
            // per fin), while the species key belongs to the root.
            var target = hits[i].collider.GetComponentInParent<ArSpeciesTarget>();
            if (target == null || string.IsNullOrEmpty(target.SpeciesKey)) continue;
            if (hits[i].distance >= nearestDistance) continue;
            nearest = target;
            nearestDistance = hits[i].distance;
        }

        if (nearest == null)
        {
            if (verbose) Debug.Log($"[ObjectInteraction] tap at {screenPosition}: {count} hit(s), no ArSpeciesTarget");
            return;
        }

        if (verbose) Debug.Log($"[ObjectInteraction] selected '{nearest.SpeciesKey}' at {nearestDistance:0.00} m");
        SpeciesSelected?.Invoke(nearest.SpeciesKey);
    }
}
