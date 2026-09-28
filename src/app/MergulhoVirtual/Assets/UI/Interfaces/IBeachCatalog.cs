using System.Collections.Generic;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// One beach as the UI layer sees it. Mirrors the places.json entry shape
    /// (ReverseGeocoding.PlaceData) without referencing Assembly-CSharp types.
    /// </summary>
    public sealed class BeachInfo
    {
        public string Name;
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
