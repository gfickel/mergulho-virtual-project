using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    /// <summary>
    /// A synthetic IPointerEvent for driving components in PlayMode tests via
    /// PointerDownEvent/PointerUpEvent.GetPooled(IPointerEvent).
    /// </summary>
    sealed class TestPointer : IPointerEvent
    {
        public int pointerId { get; set; } = PointerId.mousePointerId;
        public string pointerType { get; set; } = "mouse";
        public bool isPrimary { get; set; } = true;
        public int button { get; set; }
        public int pressedButtons { get; set; }
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 deltaPosition { get; set; }
        public float deltaTime { get; set; }
        public int clickCount { get; set; } = 1;
        public float pressure { get; set; } = 0.5f;
        public float tangentialPressure { get; set; }
        public float altitudeAngle { get; set; }
        public float azimuthAngle { get; set; }
        public float twist { get; set; }
        public Vector2 tilt { get; set; }
        public PenStatus penStatus { get; set; }
        public Vector2 radius { get; set; }
        public Vector2 radiusVariance { get; set; }
        public EventModifiers modifiers { get; set; }
        public bool shiftKey => false;
        public bool ctrlKey => false;
        public bool commandKey => false;
        public bool altKey => false;
        public bool actionKey => false;

        /// <summary>
        /// Clicks the element's center by dispatching position-based pointer
        /// events with no preset target, so the panel's picking resolves the
        /// element exactly like real input would. The element therefore needs a
        /// laid-out, non-empty worldBound (the test mount gives explicit sizes —
        /// themeless test panels have no USS min-heights and no fonts, so
        /// intrinsic sizes are 0).
        /// </summary>
        public static void Click(VisualElement element)
        {
            var center = element.worldBound.center;

            var downData = new TestPointer { position = center, localPosition = center, pressedButtons = 1 };
            using (var down = PointerDownEvent.GetPooled(downData))
                element.SendEvent(down);

            var upData = new TestPointer { position = center, localPosition = center, pressedButtons = 0 };
            using (var up = PointerUpEvent.GetPooled(upData))
                element.SendEvent(up);
        }

        /// <summary>
        /// Same as <see cref="Click"/> at an explicit panel position — for
        /// elements whose center is covered by something else (e.g. a scrim
        /// under a popup), or off-center taps. Dispatch stays position-based
        /// ON PURPOSE: presetting evt.target delivers the events but bypasses
        /// picking, so the panel's top-element-under-pointer cache goes stale
        /// and Clickable's release check (ContainsPointer) refuses to invoke —
        /// a preset target can deliver events but can never complete a click
        /// (diagnosed empirically on the MdMenu scrim). The position must
        /// therefore lie inside the panel's pickable viewport, which is small
        /// in headless test panels — keep positions near the mounted elements.
        /// </summary>
        public static void ClickAt(VisualElement element, Vector2 position)
        {
            var downData = new TestPointer { position = position, localPosition = position, pressedButtons = 1 };
            using (var down = PointerDownEvent.GetPooled(downData))
                element.SendEvent(down);

            var upData = new TestPointer { position = position, localPosition = position, pressedButtons = 0 };
            using (var up = PointerUpEvent.GetPooled(upData))
                element.SendEvent(up);
        }
    }
}
