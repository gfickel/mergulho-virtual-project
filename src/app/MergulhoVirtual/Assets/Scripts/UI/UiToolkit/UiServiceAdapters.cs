using System;
using System.Collections.Generic;
using MergulhoVirtual.UI;

/// <summary>
/// Adapters that hand the existing Assembly-CSharp services to the
/// MergulhoVirtual.UI layer through its engine-free interfaces. This is the
/// only place that maps ConditionsSnapshot/TideSnapshot/PlaceData to the UI
/// layer's ConditionsData/TideData/BeachInfo — keep the mapping here, not in
/// the ViewModels.
/// </summary>
public static class UiServiceAdapters
{
    public sealed class BeachCatalogAdapter : IBeachCatalog
    {
        List<BeachInfo> beaches;

        public IReadOnlyList<BeachInfo> Beaches
        {
            get
            {
                if (beaches == null)
                {
                    beaches = new List<BeachInfo>();
                    foreach (var place in ReverseGeocoding.GetAllPlaces())
                    {
                        if (place == null || string.IsNullOrEmpty(place.name)) continue;
                        beaches.Add(new BeachInfo
                        {
                            Name = place.name,
                            ImageName = place.imageName,
                            Description = place.description,
                            PhotoCredit = place.photoCredit,
                        });
                    }
                }
                return beaches;
            }
        }
    }

    public sealed class ConditionsServiceAdapter : IConditionsService, IDisposable
    {
        readonly ConditionsService service;

        public ConditionsData Current => Map(service != null ? service.CurrentConditions : null);
        public event Action<ConditionsData> Changed;

        public ConditionsServiceAdapter(ConditionsService service)
        {
            this.service = service;
            if (service != null) service.ConditionsChanged += OnChanged;
        }

        public void Dispose()
        {
            if (service != null) service.ConditionsChanged -= OnChanged;
        }

        void OnChanged(ConditionsSnapshot snap) => Changed?.Invoke(Map(snap));

        static ConditionsData Map(ConditionsSnapshot s)
        {
            if (s == null) return null;
            return new ConditionsData
            {
                BeachName = s.beachName,
                FetchedAtUtc = s.fetchedAtTicksUtc > 0 ? s.FetchedAtUtc : DateTime.MinValue,
                WaveHeightM = s.WaveHeightM,
                WavePeriodS = s.WavePeriodS,
                WaveDirectionDeg = s.WaveDirectionDeg,
                SeaTempC = s.SeaTempC,
                WindSpeedKmh = s.WindSpeedKmh,
                WindDirectionDeg = s.WindDirectionDeg,
            };
        }
    }

    public sealed class TideServiceAdapter : ITideService, IDisposable
    {
        readonly TideService service;

        public TideData Current => Map(service != null ? service.CurrentTide : default);
        public event Action<TideData> Changed;

        public TideServiceAdapter(TideService service)
        {
            this.service = service;
            if (service != null) service.TideChanged += OnChanged;
        }

        public void Dispose()
        {
            if (service != null) service.TideChanged -= OnChanged;
        }

        void OnChanged(TideSnapshot snap) => Changed?.Invoke(Map(snap));

        static TideData Map(TideSnapshot t) => new TideData
        {
            Valid = t.valid,
            CurrentHeightM = t.currentHeightM,
            Rising = t.rising,
            NextHighAtUtc = t.nextHighAt,
            NextHighM = t.nextHighM,
            NextLowAtUtc = t.nextLowAt,
            NextLowM = t.nextLowM,
            Next24hHeights = t.next24hHeights,
            WindowStartUtc = t.windowStart,
        };
    }

    public sealed class BeachOverrideAdapter : IBeachOverride
    {
        readonly GPSHandler gps;

        public BeachOverrideAdapter(GPSHandler gps) => this.gps = gps;

        public void SetOverride(string beachName)
        {
            if (gps != null) gps.SetBeachOverride(beachName);
        }

        public void ClearOverride()
        {
            if (gps != null) gps.ClearBeachOverride();
        }
    }
}
