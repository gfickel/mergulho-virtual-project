using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// The app's ScrollView. Identical to <see cref="ScrollView"/> except that it
    /// never draws a scrollbar — every scrolling screen uses this instead of
    /// <c>new ScrollView()</c>, which is the single place that decision lives.
    ///
    /// <para><b>Why a subclass and not USS.</b> Scroller visibility is not a USS
    /// property, and hiding <c>.unity-scroller</c> with <c>display: none</c> does
    /// not work: <see cref="ScrollView"/> writes the scrollers' <c>display</c> as an
    /// INLINE style on every layout pass (that is how <see cref="ScrollerVisibility.Auto"/>
    /// appears and disappears), and inline styles beat stylesheets. Setting
    /// <see cref="ScrollerVisibility.Hidden"/> is the supported way to say "no
    /// scrollbar, still scrollable", and a subclass makes it the default for
    /// everything that comes after rather than a line each screen has to remember.</para>
    ///
    /// <para><b>Why hidden at all.</b> UI Toolkit's stock scroller is a desktop
    /// control — a permanently visible track with ▲▼ stepper buttons. It is in none
    /// of the V2 frames, and mobile convention is no persistent scrollbar (the
    /// platform overlay indicators UI Toolkit would need to imitate do not exist
    /// here). Touch/wheel/keyboard scrolling are unaffected.</para>
    /// </summary>
    public sealed class AppScrollView : ScrollView
    {
        public AppScrollView() : this(ScrollViewMode.Vertical) { }

        public AppScrollView(ScrollViewMode scrollViewMode) : base(scrollViewMode)
        {
            verticalScrollerVisibility = ScrollerVisibility.Hidden;
            horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        }
    }
}
