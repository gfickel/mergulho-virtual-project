using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// Shape helpers that USS cannot express.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all — read <c>Tokens/_shape.uss</c> first.</b>
    /// CSS resolves an over-large <c>border-radius</c> by scaling every radius by
    /// one shared factor, which preserves the stadium shape. UI Toolkit instead
    /// clamps each axis independently (<c>rx = min(r, width/2)</c>,
    /// <c>ry = min(r, height/2)</c>), so a "very large radius" on a wide, short
    /// element yields a full ellipse. A stadium needs <c>r == height/2</c>
    /// exactly, which means the height has to be known.
    /// </para>
    /// <para>
    /// <b>Prefer the USS literal.</b> Where a pill's height is authored
    /// (<c>height: 40px</c> -&gt; <c>border-radius: 20px</c>) write the literal
    /// and do not call this: it costs nothing at runtime, it stays overridable
    /// by a screen's stylesheet, and <c>ShapeDisciplineTests</c> pins the pair.
    /// This helper is for the other case only — a pill sized by its content,
    /// where the height depends on font metrics, wrapping or the OS font scale
    /// and therefore cannot be written down. <see cref="MvTag"/> is the only
    /// such component today.
    /// </para>
    /// <para>
    /// <b>Cost and consequence.</b> One <see cref="GeometryChangedEvent"/>
    /// callback and an inline style per element. The inline style outranks every
    /// stylesheet, so once an element is registered here its corner radius can no
    /// longer be restyled from USS — which is the point, but it does mean this is
    /// a decision about the component, not a decorative tweak.
    /// </para>
    /// </remarks>
    public static class MdShape
    {
        /// <summary>
        /// Pins <paramref name="element"/>'s corner radius to half its resolved
        /// height, now and on every subsequent layout, so it stays a true
        /// stadium at any height. Safe to call once per element in its
        /// constructor; calling it twice simply registers a second no-op
        /// callback. Writing the radius does not affect layout, so this cannot
        /// feed back into another geometry change.
        /// </summary>
        public static void KeepStadium(VisualElement element)
        {
            if (element == null)
                return;
            element.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            // The element may already be laid out (helper called after mount);
            // GeometryChangedEvent would not fire again on its own.
            ApplyStadium(element, element.resolvedStyle.height);
        }

        static void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.target is VisualElement element)
                ApplyStadium(element, evt.newRect.height);
        }

        /// <summary>
        /// Sets all four corner radii to <paramref name="height"/>/2, skipping
        /// the write when it would be a no-op (an unconditional write would dirty
        /// the element's style on every layout pass).
        /// </summary>
        static void ApplyStadium(VisualElement element, float height)
        {
            if (float.IsNaN(height) || height <= 0f)
                return;

            float radius = height * 0.5f;

            var current = element.style.borderTopLeftRadius;
            if (current.keyword == StyleKeyword.Undefined &&
                current.value.unit == LengthUnit.Pixel &&
                Mathf.Approximately(current.value.value, radius))
                return;

            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
