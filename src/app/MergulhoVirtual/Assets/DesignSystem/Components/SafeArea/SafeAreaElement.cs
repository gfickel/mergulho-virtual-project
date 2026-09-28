using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// A container that pads itself by <see cref="Screen.safeArea"/> so children
    /// avoid notches and system bars. UI Toolkit does not do this automatically
    /// and the app renders edge-to-edge over the AR camera.
    /// Use as the outermost element of every screen.
    /// </summary>
    [UxmlElement]
    public partial class SafeAreaElement : VisualElement
    {
        public const string UssClassName = "md-safe-area";

        public SafeAreaElement()
        {
            AddToClassList(UssClassName);
            style.flexGrow = 1;
            pickingMode = PickingMode.Ignore;
            RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea());
        }

        void ApplySafeArea()
        {
            // Editor panels (EditMode tests, UI Builder) have no screen mapping.
            if (panel == null || panel.contextType != ContextType.Player)
                return;

            var safeArea = Screen.safeArea;
            // Screen.safeArea has a bottom-left origin; panels a top-left one.
            var topLeft = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(safeArea.xMin, Screen.height - safeArea.yMax));
            var bottomRight = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(Screen.width - safeArea.xMax, safeArea.yMin));

            style.paddingLeft = topLeft.x;
            style.paddingTop = topLeft.y;
            style.paddingRight = bottomRight.x;
            style.paddingBottom = bottomRight.y;
        }
    }
}
