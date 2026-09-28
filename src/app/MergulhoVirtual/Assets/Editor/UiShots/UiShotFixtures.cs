using System;
using System.Collections.Generic;
using MergulhoVirtual.UI;
using UnityEngine;

namespace MergulhoVirtual.UiShots
{
    /// <summary>
    /// Deterministic stand-ins for every service the UI layer consumes, so a
    /// screenshot only changes when the UI changes. Nothing here touches GPS,
    /// the network, the backend or <see cref="DateTime.Now"/>.
    ///
    /// <para><b>Why these are mirrored rather than shared with the EditMode tests.</b>
    /// The fakes in <c>Assets/UI/Tests/Editor/BeachesViewModelTests.cs</c> are
    /// private nested classes in <c>MergulhoVirtual.UI.Tests.Editor</c>, an
    /// assembly that is <c>autoReferenced: false</c> and gated on the
    /// <c>UNITY_INCLUDE_TESTS</c> define constraint — so Assembly-CSharp-Editor
    /// cannot reference it, and making it referenceable would mean un-isolating
    /// the test assembly. The shapes are ~40 lines of trivial property bags, and
    /// the two have different jobs: the tests want <i>empty</i> data to assert the
    /// "—" fallbacks, the harness wants <i>plausible</i> data that exercises every
    /// row. Mirroring keeps each free to serve its own purpose.</para>
    ///
    /// <para><b>Adding a service.</b> When a screen's ViewModel starts taking a new
    /// <c>MergulhoVirtual.UI</c> interface, add a fake here and register it in
    /// <see cref="Services"/>; the harness resolves constructor parameters from
    /// that dictionary and skips (with a log line naming the parameter) otherwise.</para>
    /// </summary>
    public static class UiShotFixtures
    {
        /// <summary>
        /// Frozen clock. Picked so every time-relative string has something to say:
        /// conditions fetched 8 min ago, tide rising toward a high later today.
        /// </summary>
        public static readonly DateTime FixedNowUtc =
            new DateTime(2026, 9, 15, 17, 30, 0, DateTimeKind.Utc);

        /// <summary>
        /// Fernando de Noronha is UTC−2 year-round. Applied as a fixed offset (not
        /// <see cref="DateTime.ToLocalTime"/>) so the PNGs are identical on any
        /// machine's timezone.
        /// </summary>
        public static DateTime ToNoronhaLocal(DateTime utc) => utc.AddHours(-2);

        /// <summary>Beach whose detail screen the "…-detail" subjects open.</summary>
        public const string DetailBeachName = "Praia do Sancho";

        /// <summary>Every fake, keyed by the interface a constructor may ask for.</summary>
        public static readonly IReadOnlyDictionary<Type, object> Services =
            new Dictionary<Type, object>
            {
                [typeof(IBeachCatalog)] = new FixedBeachCatalog(),
                [typeof(IConditionsService)] = new FixedConditions(),
                [typeof(ITideService)] = new FixedTides(),
                [typeof(IBeachOverride)] = new NoopBeachOverride(),
                [typeof(IOnboardingState)] = new FirstRunOnboarding(),
                [typeof(IBeachContent)] = new FileBeachContent(),
                [typeof(ISpeciesCatalog)] = new ResourcesSpeciesCatalog(),
                [typeof(IActiveBeach)] = new FixedActiveBeach(),
            };

        /// <summary>Beach photos really do come from Resources — they are committed assets, so still deterministic.</summary>
        public static Sprite LoadBeachSprite(string imageName) =>
            string.IsNullOrEmpty(imageName) ? null : Resources.Load<Sprite>("Beaches/" + imageName);

        /// <summary>Species photos, same deal — Resources/Animals is committed.</summary>
        public static Sprite LoadSpeciesSprite(string imageName) =>
            string.IsNullOrEmpty(imageName) ? null : Resources.Load<Sprite>("Animals/" + imageName);

        // ------------------------------------------------------------------
        // Catalog — the real places.json, read through the production adapter so
        // the cards show real names, teasers and photos. It is a committed file,
        // so this stays deterministic; the hardcoded list is only a fallback for
        // when the adapter or Resources load fails (e.g. mid-refactor).
        // ------------------------------------------------------------------
        sealed class FixedBeachCatalog : IBeachCatalog
        {
            List<BeachInfo> cached;

            public IReadOnlyList<BeachInfo> Beaches
            {
                get
                {
                    if (cached != null) return cached;
                    cached = new List<BeachInfo>();
                    try
                    {
                        foreach (var beach in new global::UiServiceAdapters.BeachCatalogAdapter().Beaches)
                            cached.Add(beach);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[ui-shots] places.json catalog unavailable ({e.Message}); using the fallback list.");
                    }
                    if (cached.Count == 0) cached.AddRange(Fallback);
                    return cached;
                }
            }

            static readonly BeachInfo[] Fallback =
            {
                new BeachInfo { Name = DetailBeachName, ImageName = "praia_do_sancho", Description = "Considerada uma das praias mais bonitas do mundo." },
                new BeachInfo { Name = "Baía dos Porcos", ImageName = "baia_dos_porcos", Description = "Pequena enseada cercada por falésias e recifes." },
                new BeachInfo { Name = "Praia da Cacimba do Padre", ImageName = "praia_da_cacimba_do_padre", Description = "Praia de 700 metros emoldurada pelos Morros Dois Irmãos." },
                new BeachInfo { Name = "Praia do Leão", ImageName = "praia_do_leao", Description = "Principal área de desova de tartarugas da ilha." },
            };
        }

        // ------------------------------------------------------------------
        // Conditions — one plausible snapshot, every field populated so no row
        // renders the "—" fallback.
        // ------------------------------------------------------------------
        sealed class FixedConditions : IConditionsService
        {
            public ConditionsData Current { get; } = new ConditionsData
            {
                BeachName = DetailBeachName,
                FetchedAtUtc = FixedNowUtc.AddMinutes(-8),
                WaveHeightM = 1.4f,
                WavePeriodS = 8.6f,
                WaveDirectionDeg = 130f,
                SeaTempC = 27.4f,
                WindSpeedKmh = 22.6f,
                WindDirectionDeg = 105f,
            };

#pragma warning disable 67 // Never raised: the fixture is frozen by design.
            public event Action<ConditionsData> Changed;
#pragma warning restore 67
        }

        // ------------------------------------------------------------------
        // Tides — a synthetic but realistic semidiurnal curve around the DHN
        // "Nível Médio" (LAT datum) so MdSparkline has a real shape to draw.
        // ------------------------------------------------------------------
        sealed class FixedTides : ITideService
        {
            public TideData Current { get; } = BuildTide();

#pragma warning disable 67
            public event Action<TideData> Changed;
#pragma warning restore 67

            static TideData BuildTide()
            {
                // Window starts on the hour containing "now".
                var windowStart = new DateTime(
                    FixedNowUtc.Year, FixedNowUtc.Month, FixedNowUtc.Day, FixedNowUtc.Hour, 0, 0, DateTimeKind.Utc);

                const float mean = 1.28f;      // Nível Médio (the sparkline baseline)
                const float amplitude = 0.92f;
                const double periodH = 12.42;  // M2 semidiurnal
                const double phaseH = 3.2;     // hours from windowStart to the first high

                var heights = new float[24];
                for (int h = 0; h < 24; h++)
                    heights[h] = mean + amplitude * (float)Math.Cos(2.0 * Math.PI * (h - phaseH) / periodH);

                return new TideData
                {
                    Valid = true,
                    CurrentHeightM = heights[0],
                    Rising = true,
                    NextHighAtUtc = windowStart.AddHours(phaseH),          // 20:42Z → 18:42 local
                    NextHighM = mean + amplitude,
                    NextLowAtUtc = windowStart.AddHours(phaseH + periodH / 2),
                    NextLowM = mean - amplitude,
                    Next24hHeights = heights,
                    WindowStartUtc = windowStart,
                };
            }
        }

        sealed class NoopBeachOverride : IBeachOverride
        {
            public void SetOverride(string beachName) { }
            public void ClearOverride() { }
        }

        // ------------------------------------------------------------------
        // Beach editorial content + species catalog — read through the
        // production adapters, i.e. the REAL beaches_content.json and the real
        // AnimalDef assets. Both are committed, so the shots stay deterministic,
        // and — more to the point — they show what the Praias screens actually
        // render today: mostly empty. A fixture full of invented risk levels and
        // tips would make the one thing worth looking at invisible.
        // ------------------------------------------------------------------
        sealed class FileBeachContent : IBeachContent
        {
            readonly global::UiServiceAdapters.BeachContentAdapter inner =
                new global::UiServiceAdapters.BeachContentAdapter();

            public BeachContent ForBeach(string beachName)
            {
                try { return inner.ForBeach(beachName); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ui-shots] beaches_content.json unavailable ({e.Message}); rendering an empty beach.");
                    return BeachContent.EmptyFor(beachName);
                }
            }

            public bool TryGetContent(string beachName, out BeachContent content)
            {
                content = ForBeach(beachName);
                return !content.IsEmpty;
            }
        }

        sealed class ResourcesSpeciesCatalog : ISpeciesCatalog
        {
            readonly global::UiServiceAdapters.SpeciesCatalogAdapter inner =
                new global::UiServiceAdapters.SpeciesCatalogAdapter();

            public IReadOnlyList<SpeciesInfo> Species
            {
                get
                {
                    try { return inner.Species; }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[ui-shots] Resources/Animals unavailable ({e.Message}).");
                        return Array.Empty<SpeciesInfo>();
                    }
                }
            }

            public SpeciesInfo Find(string key)
            {
                try { return inner.Find(key); }
                catch { return null; }
            }
        }

        /// <summary>
        /// "You are here" without a GPS: the Praias landing opens on
        /// <see cref="DetailBeachName"/>. Never raises — the fixture is frozen.
        /// </summary>
        sealed class FixedActiveBeach : IActiveBeach
        {
            public string ActiveBeachKey => DetailBeachName;

#pragma warning disable 67
            public event Action<string> ActiveBeachChanged;
#pragma warning restore 67
        }

        /// <summary>First run: the Início welcome card is showing (Tela 6).</summary>
        sealed class FirstRunOnboarding : IOnboardingState
        {
            public bool WelcomeDismissed => false;
            public void DismissWelcome() { }
        }
    }
}
