using System;
using System.Collections.Generic;
using MergulhoVirtual.UI;
using UnityEngine;

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
                            // Key stays the places.json `name` (spawner / content
                            // file / backend `local` all agree on it); the label is
                            // the pt-BR displayName, which BeachInfo falls back from
                            // when an older places.json has none.
                            Name = place.name,
                            DisplayName = place.displayName,
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

    /// <summary>
    /// The hand-authored per-beach content, read by <see cref="BeachContentLibrary"/>
    /// from Resources/beaches_content.json. Stateless and cheap to construct — the
    /// parsed file is cached process-wide by the library.
    ///
    /// <para><see cref="ForBeach"/> never returns null: an unknown beach, an
    /// unreadable file and an all-blank entry all come back as an empty
    /// <see cref="BeachContent"/>, so screens bind unconditionally and hide the
    /// sections whose Has* flag is false.</para>
    /// </summary>
    public sealed class BeachContentAdapter : IBeachContent
    {
        public BeachContent ForBeach(string beachName) =>
            BeachContentLibrary.Find(beachName) ?? BeachContent.EmptyFor(beachName);

        public bool TryGetContent(string beachName, out BeachContent content)
        {
            content = BeachContentLibrary.Find(beachName);
            if (content != null) return true;
            content = BeachContent.EmptyFor(beachName);
            return false;
        }
    }

    /// <summary>
    /// The species catalog over the AnimalDef ScriptableObjects in
    /// Resources/Animals — the same assets the Animais screen lists, so the beach
    /// species chips, the 3D catalog and (Slice 4) the AR card cannot drift into
    /// three spellings of the same animal.
    ///
    /// <para>Keyed by the asset file name ("lemon_shark"), which is what
    /// beaches_content.json writes. <c>Resources.LoadAll</c> runs once, lazily, on
    /// first use.</para>
    /// </summary>
    public sealed class SpeciesCatalogAdapter : ISpeciesCatalog
    {
        List<SpeciesInfo> species;
        Dictionary<string, SpeciesInfo> byKey;

        public IReadOnlyList<SpeciesInfo> Species
        {
            get
            {
                EnsureLoaded();
                return species;
            }
        }

        public SpeciesInfo Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureLoaded();
            return byKey.TryGetValue(key, out var info) ? info : null;
        }

        void EnsureLoaded()
        {
            if (species != null) return;
            species = new List<SpeciesInfo>();
            byKey = new Dictionary<string, SpeciesInfo>(StringComparer.Ordinal);

            foreach (var animal in Resources.LoadAll<AnimalDef>("Animals"))
            {
                if (animal == null || string.IsNullOrEmpty(animal.name)) continue;
                var info = new SpeciesInfo
                {
                    // Asset file name is the key; displayName is the only label.
                    Key = animal.name,
                    DisplayName = string.IsNullOrWhiteSpace(animal.displayName)
                        ? animal.name
                        : animal.displayName,
                    Binomial = animal.binomial,
                    ImageName = animal.imageName,
                    Description = animal.description,
                };
                if (byKey.ContainsKey(info.Key))
                {
                    Debug.LogWarning($"[SpeciesCatalog] Duplicate AnimalDef key \"{info.Key}\" — keeping the first.");
                    continue;
                }
                byKey[info.Key] = info;
                species.Add(info);
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

    /// <summary>
    /// The read side of the same state <see cref="BeachOverrideAdapter"/> writes:
    /// whichever beach <c>GPSHandler</c> currently reports, which is the GPS
    /// resolution or the manual override when one is set (SetBeachOverride emits
    /// through the same PlaceChanged event, so one subscription covers both).
    ///
    /// <para>Deliberately NOT sourced from <c>ConditionsService</c>, which falls
    /// back to a default beach when GPS has nothing — fine for fetching a
    /// forecast, a lie under the words "você está em". A null key here is the
    /// honest, common answer and the Praias landing has a state for it.</para>
    /// </summary>
    public sealed class ActiveBeachAdapter : IActiveBeach, IDisposable
    {
        readonly GPSHandler gps;

        public ActiveBeachAdapter(GPSHandler gps)
        {
            this.gps = gps;
            if (gps != null) gps.PlaceChanged += OnPlaceChanged;
        }

        public string ActiveBeachKey => gps != null ? gps.CurrentPlaceName : null;

        public event Action<string> ActiveBeachChanged;

        public void Dispose()
        {
            if (gps != null) gps.PlaceChanged -= OnPlaceChanged;
        }

        void OnPlaceChanged(string beachName) => ActiveBeachChanged?.Invoke(beachName);
    }

    /// <summary>
    /// First-run state over PlayerPrefs. Trivial on purpose: the UI layer must
    /// stay engine-free, so this is the whole of the persistence seam — one int
    /// per flag, keyed by <see cref="OnboardingPrefKeys"/> so the string is
    /// spelled in exactly one place.
    ///
    /// <para><c>Save()</c> is explicit because Unity only flushes PlayerPrefs on
    /// a clean quit: without it, dismissing the welcome card and then killing the
    /// app (or letting Android reclaim it) brings the card back.</para>
    /// </summary>
    public sealed class OnboardingStateAdapter : IOnboardingState
    {
        public bool WelcomeDismissed =>
            PlayerPrefs.GetInt(OnboardingPrefKeys.WelcomeDismissed, 0) != 0;

        public void DismissWelcome()
        {
            PlayerPrefs.SetInt(OnboardingPrefKeys.WelcomeDismissed, 1);
            PlayerPrefs.Save();
        }
    }
}
