using System;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Adapts a legacy uGUI screen GameObject to <see cref="IAppScreen"/> so
/// <see cref="MdRouter"/> can route to it exactly like a UI Toolkit screen.
/// This is what keeps the strangler rule workable: the AR HUD (Mergulho) and
/// the uGUI Reportar screen stay live and routable until Slices 4 and 3 replace
/// them, without the router knowing uGUI exists.
///
/// <para>Lives in Assembly-CSharp because it is the only assembly allowed to see
/// both worlds — MergulhoVirtual.UI must not reference AR/uGUI screen code.</para>
///
/// <para><see cref="Root"/> is an empty, non-pickable element: it occupies the
/// screen container so the router's show/hide bookkeeping is uniform, but it
/// paints nothing and hit-tests nothing, so taps pass straight through the
/// UI Toolkit panel to the uGUI canvas drawing underneath.</para>
/// </summary>
public sealed class LegacyUguiScreen : IAppScreen
{
    readonly GameObject screenObject;
    readonly Action onEnter;
    readonly Action onExit;

    public string Key { get; }
    public VisualElement Root { get; }

    /// <param name="key">Route key, one of the <see cref="AppRoutes"/> constants.</param>
    /// <param name="screenObject">The uGUI panel GameObject under ScreenUI. Deactivated immediately — the router owns its activation from here on.</param>
    /// <param name="onEnter">Optional extra work on entry (after the SetActive).</param>
    /// <param name="onExit">Optional extra work on exit (before the SetActive).</param>
    public LegacyUguiScreen(string key, GameObject screenObject, Action onEnter = null, Action onExit = null)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Route key required.", nameof(key));
        Key = key;
        this.screenObject = screenObject;
        this.onEnter = onEnter;
        this.onExit = onExit;

        Root = new VisualElement { name = "legacy-" + key, pickingMode = PickingMode.Ignore };
        Root.style.flexGrow = 1f;

        if (screenObject != null) screenObject.SetActive(false);
    }

    public void OnEnter()
    {
        if (screenObject != null) screenObject.SetActive(true);
        onEnter?.Invoke();
    }

    public void OnExit()
    {
        onExit?.Invoke();
        if (screenObject != null) screenObject.SetActive(false);
    }

    /// <summary>
    /// No-op: uGUI screens position themselves against the Canvas, not against
    /// the UI Toolkit panel, and they already carry their own bottom clearance.
    /// </summary>
    public void SetEdgeInsets(float top, float left, float right, float bottom) { }
}
