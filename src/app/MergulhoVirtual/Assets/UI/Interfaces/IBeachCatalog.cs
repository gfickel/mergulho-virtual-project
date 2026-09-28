using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// One beach as the UI layer sees it. Mirrors the places.json entry shape
    /// (ReverseGeocoding.PlaceData) without referencing Assembly-CSharp types.
    ///
    /// <para><b>Two names, one of which is not for humans.</b> places.json mixes
    /// Portuguese and English keys ("Sueste Beach", "Boldró Beach") because the
    /// key is wired into the AR spawner's Inspector list, beaches_content.json and
    /// the backend's <c>local</c> field. <see cref="Name"/> is that key;
    /// <see cref="DisplayName"/> is the pt-BR label. Render
    /// <see cref="DisplayName"/> and look up by <see cref="Name"/> — swapping them
    /// either shows "Sueste Beach" to a user or silently breaks every lookup.</para>
    /// </summary>
    public sealed class BeachInfo
    {
        /// <summary>
        /// Machine key, from places.json <c>name</c>. Matches the BeachSharkSpawner
        /// Inspector list, the beaches_content.json keys and the backend's
        /// <c>local</c> field. <b>Never show it to the user and never translate
        /// it</b> — use <see cref="DisplayName"/>.
        /// </summary>
        public string Name;

        string displayName;

        /// <summary>
        /// pt-BR label the UI shows, from places.json <c>displayName</c>. Falls back
        /// to <see cref="Name"/> when the entry has none, so this is always safe to
        /// render and never empty. <b>Never use it as a lookup key</b> — it is
        /// editorial and may be re-worded at any time (D4).
        /// </summary>
        public string DisplayName
        {
            get => string.IsNullOrEmpty(displayName) ? Name : displayName;
            set => displayName = value;
        }

        public string ImageName;
        public string Description;
        public string PhotoCredit;
    }

    /// <summary>Read-only catalog of the beaches shown on the Beaches screen.</summary>
    public interface IBeachCatalog
    {
        IReadOnlyList<BeachInfo> Beaches { get; }
    }
}
