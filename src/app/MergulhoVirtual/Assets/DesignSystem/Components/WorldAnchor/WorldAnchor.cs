using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// Pins a screen-space VisualElement to a 3D world position (e.g. a species
    /// label above an AR shark). Wraps RuntimePanelUtils.CameraTransformWorldToPanel
    /// and re-projects on the panel's frame scheduler. The element is hidden while
    /// the target is behind the camera.
    /// The element must be absolutely positioned (style.position = Absolute is set
    /// by Track) and its parent should span the panel.
    /// </summary>
    public sealed class WorldAnchor
    {
        readonly VisualElement _element;
        IVisualElementScheduledItem _updater;
        Func<Vector3> _worldPosition;
        Camera _camera;

        /// <summary>Pivot within the element that lands on the projected point (0..1, default bottom-center).</summary>
        public Vector2 Pivot { get; set; } = new(0.5f, 1f);

        public WorldAnchor(VisualElement element)
        {
            _element = element ?? throw new ArgumentNullException(nameof(element));
        }

        public void Track(Func<Vector3> worldPosition, Camera camera)
        {
            _worldPosition = worldPosition ?? throw new ArgumentNullException(nameof(worldPosition));
            _camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            _element.style.position = Position.Absolute;
            _updater?.Pause();
            _updater = _element.schedule.Execute(UpdatePosition).Every(16);
        }

        public void Stop()
        {
            _updater?.Pause();
            _updater = null;
        }

        void UpdatePosition()
        {
            if (_element.panel == null || _camera == null)
                return;

            var world = _worldPosition();
            var viewport = _camera.WorldToViewportPoint(world);
            if (viewport.z <= 0f)
            {
                _element.style.visibility = Visibility.Hidden;
                return;
            }

            var panelPos = RuntimePanelUtils.CameraTransformWorldToPanel(_element.panel, world, _camera);
            _element.style.visibility = Visibility.Visible;
            _element.style.left = panelPos.x - _element.resolvedStyle.width * Pivot.x;
            _element.style.top = panelPos.y - _element.resolvedStyle.height * Pivot.y;
        }
    }
}
