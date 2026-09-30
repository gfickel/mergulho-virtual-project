using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// Switches the conditions fixture to "nothing loaded and the fetch failed"
        /// for the duration of one subject, so the error/offline states of §8.7 can
        /// be shot. Set and cleared by the harness around a single capture; the
        /// registry holds one instance per interface, so a per-subject flag is the
        /// cheapest honest way to get two states out of one fake.
        /// </summary>
        public static bool ConditionsUnavailable;

        /// <summary>Makes <see cref="IConnectivity"/> report no link, which is what
        /// picks the offline variant over the generic error.</summary>
        public static bool Offline;

        /// <summary>Every fake, keyed by the interface a constructor may ask for.</summary>
        public static readonly IReadOnlyDictionary<Type, object> Services =
            new Dictionary<Type, object>
            {
                [typeof(IBeachCatalog)] = new FixedBeachCatalog(),
                [typeof(IConditionsService)] = new FixedConditions(),
            [typeof(IConnectivity)] = new FixedConnectivity(),
                [typeof(ITideService)] = new FixedTides(),
                [typeof(IBeachOverride)] = new NoopBeachOverride(),
                [typeof(IOnboardingState)] = new FirstRunOnboarding(),
                [typeof(IBeachContent)] = new FileBeachContent(),
                [typeof(ISpeciesCatalog)] = new ResourcesSpeciesCatalog(),
                [typeof(IActiveBeach)] = new FixedActiveBeach(),
                [typeof(ISightingReports)] = new FixedSightingReports(),
                [typeof(IPhotoPicker)] = new FixedPhotoPicker(),
                [typeof(IArSelection)] = new SilentArSelection(),
                [typeof(ISpeciesModelViewer)] = new StandInModelViewer(),
                [typeof(IVideoPlayback)] = new StandInVideoPlayback(),
                [typeof(IArticleCatalog)] = new FileArticleCatalog(),
            };

        /// <summary>Beach photos really do come from Resources — they are committed assets, so still deterministic.</summary>
        public static Sprite LoadBeachSprite(string imageName) =>
            string.IsNullOrEmpty(imageName) ? null : Resources.Load<Sprite>("Beaches/" + imageName);

        /// <summary>Species photos, same deal — Resources/Animals is committed.</summary>
        public static Sprite LoadSpeciesSprite(string imageName) =>
            string.IsNullOrEmpty(imageName) ? null : Resources.Load<Sprite>("Animals/" + imageName);

        /// <summary>
        /// Article covers and inline figures. <b>No folder prefix</b>, unlike the two
        /// above: an article authors a complete Resources path
        /// ("Beaches/praia_do_sancho", "Animals/tiger_shark"), which is what lets one
        /// article illustrate itself from several folders. Mirrors
        /// <c>AppUiHost.LoadArticleSprite</c> — prefixing would make every figure in
        /// every shot resolve to null and vanish with no warning.
        /// </summary>
        public static Sprite LoadArticleSprite(string resourcePath) =>
            string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<Sprite>(resourcePath);

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
            static readonly ConditionsData Snapshot = new ConditionsData
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

            // Computed, not an initialiser, so the failure subjects can flip the
            // whole registry's answer for the duration of one shot.
            public ConditionsData Current => ConditionsUnavailable ? null : Snapshot;
            public bool LastFetchFailed => ConditionsUnavailable;
            public bool IsFetching => false;
            public void Refresh() { }

#pragma warning disable 67 // Never raised: the fixture is frozen by design.
            public event Action<ConditionsData> Changed;
#pragma warning restore 67
        }

        /// <summary>Connectivity, switched by <see cref="Offline"/>.</summary>
        sealed class FixedConnectivity : IConnectivity
        {
            public bool IsOnline => !Offline;
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

        /// <summary>
        /// The real AnimalDef assets, with one deliberate exception.
        ///
        /// <para><b>The AR species card's three spec rows are blank on every
        /// shipped asset</b> (Decision D8 — a blank beats an invented fact), so
        /// with the raw catalog the <c>mergulho-card</c> shot would show a card
        /// with a name and nothing else, and the frame it exists to be compared
        /// against would be unreviewable. <see cref="SpecFixtures"/> fills those
        /// three fields for the ONE species Tela 8 itself draws, with the exact
        /// strings the designer put in the frame — sample content from the design,
        /// not marine biology invented here, and it never leaves this file.</para>
        ///
        /// <para>It fills blanks only, so the moment somebody authors the real
        /// values on tiger_shark.asset the shot switches to them; and it names one
        /// species only, so <c>mergulho</c> and every other screen still render the
        /// honest, empty state. The same rule the beach-content fixture follows:
        /// the harness must not hide what the app actually shows today.</para>
        /// </summary>
        sealed class ResourcesSpeciesCatalog : ISpeciesCatalog
        {
            /// <summary>key → (approximate size, diet, behaviour), verbatim from Tela 8 (31:3979).</summary>
            static readonly Dictionary<string, (string Size, string Diet, string Behaviour)> SpecFixtures =
                new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
                {
                    ["tiger_shark"] = ("3 a 4 metros", "Peixes, tartarugas e moluscos", "Solitário e noturno"),
                };

            readonly global::UiServiceAdapters.SpeciesCatalogAdapter inner =
                new global::UiServiceAdapters.SpeciesCatalogAdapter();

            List<SpeciesInfo> cached;
            Dictionary<string, SpeciesInfo> byKey;

            public IReadOnlyList<SpeciesInfo> Species
            {
                get
                {
                    EnsureLoaded();
                    return cached;
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
                if (cached != null) return;
                cached = new List<SpeciesInfo>();
                byKey = new Dictionary<string, SpeciesInfo>(StringComparer.Ordinal);

                IReadOnlyList<SpeciesInfo> source;
                try { source = inner.Species; }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ui-shots] Resources/Animals unavailable ({e.Message}).");
                    return;
                }

                foreach (var real in source)
                {
                    if (real == null || string.IsNullOrEmpty(real.Key)) continue;
                    // Copied rather than mutated: the adapter's instances are its
                    // own cache, and a shot must not edit them.
                    var info = new SpeciesInfo
                    {
                        Key = real.Key,
                        DisplayName = real.DisplayName,
                        Binomial = real.Binomial,
                        ImageName = real.ImageName,
                        Description = real.Description,
                        ApproximateSize = real.ApproximateSize,
                        Diet = real.Diet,
                        Behaviour = real.Behaviour,
                        // Espécie screen. Copying these is what makes the shot show the
                        // real credits, the real clip list and the real "this species
                        // has a model" flag — i.e. lemon_shark as the only one with a
                        // video section. A field forgotten here silently deletes a
                        // whole block from the PNG with no warning anywhere.
                        PhotoCredit = real.PhotoCredit,
                        ModelCredit = real.ModelCredit,
                        HasModel = real.HasModel,
                        Videos = real.Videos,
                    };
                    if (SpecFixtures.TryGetValue(info.Key, out var sample))
                    {
                        if (!info.HasApproximateSize) info.ApproximateSize = sample.Size;
                        if (!info.HasDiet) info.Diet = sample.Diet;
                        if (!info.HasBehaviour) info.Behaviour = sample.Behaviour;
                    }
                    cached.Add(info);
                    byKey[info.Key] = info;
                }
            }
        }

        /// <summary>
        /// No AR in batchmode: nothing raycasts, so nothing is ever selected. The
        /// <c>mergulho-card</c> subject opens its card by calling
        /// <c>MergulhoViewModel.ShowSpecies</c> directly, which is the same method
        /// a real tap ends up in.
        /// </summary>
        sealed class SilentArSelection : IArSelection
        {
#pragma warning disable 67 // Never raised: there is no AR scene in a screenshot.
            public event Action<string> SpeciesSelected;
#pragma warning restore 67

            public void SetListening(bool listening) { }
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

        // ------------------------------------------------------------------
        // Sighting reports (Reportar) — a frozen feed, not a queue. Two rows so
        // the shot shows BOTH pill treatments: one report still waiting (the grey
        // "Pendente" of Tela 11/12) and one the backend rejected, which must not
        // look like the first. Submit is never called from a shot — the subjects
        // only fill the form — so it just says yes.
        // ------------------------------------------------------------------
        sealed class FixedSightingReports : ISightingReports
        {
            // 11:32Z = 9:32 local, i.e. Figma's "Hoje, 9:32 · Baía do Sueste"
            // against the frozen clock.
            readonly List<SightingRecord> pending = new List<SightingRecord>
            {
                new SightingRecord
                {
                    Id = "shot-pending-1",
                    SpeciesKey = "lemon_shark",
                    SpeciesLabel = "Tubarão-limão",
                    BeachKey = "Sueste Beach",
                    WhenUtc = FixedNowUtc.AddHours(-5).AddMinutes(-58),
                    State = SightingState.Queued,
                },
            };

            readonly List<SightingRecord> failed = new List<SightingRecord>
            {
                new SightingRecord
                {
                    Id = "shot-failed-1",
                    // No species: the row falls back to ReportFormatter's
                    // "Avistamento", which is a real submission, not a gap.
                    BeachKey = "Praia do Sancho",
                    WhenUtc = FixedNowUtc.AddDays(-1),
                    State = SightingState.Failed,
                    AttemptCount = 3,
                },
            };

            public bool Submit(SightingDraft draft) => true;
            public IReadOnlyList<SightingRecord> ListPending() => pending;
            public IReadOnlyList<SightingRecord> ListFailed() => failed;

            /// <summary>The shots never tap it; a frozen feed has nothing to move.</summary>
            public bool Retry(string id) => false;

#pragma warning disable 67 // Never raised: the fixture is frozen by design.
            public event Action Changed;
#pragma warning restore 67
        }

        /// <summary>
        /// Hands back a committed photo synchronously, so a subject that calls
        /// <c>PickPhoto</c> gets a real thumbnail in the same frame. The file is
        /// Resources/Animals/lemon_shark.jpg — a shark, in the repo, so the shot
        /// stays deterministic and nothing reaches the OS gallery.
        /// </summary>
        sealed class FixedPhotoPicker : IPhotoPicker
        {
            public void PickPhoto(Action<PhotoPickResult> onResult)
            {
                if (onResult == null) return;
                string path = Path.Combine(Application.dataPath, "Resources/Animals/lemon_shark.jpg");
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[ui-shots] no sample photo at {path}; the picker stays empty.");
                    onResult(PhotoPickResult.Cancelled());
                    return;
                }
                onResult(PhotoPickResult.Picked(path, new FileInfo(path).Length));
            }
        }

        // ------------------------------------------------------------------
        // Educational content — the REAL articles.json, read through the production
        // adapter, for the same reason the beach-content fixture reads the real file:
        // it is a committed generated artefact (so the shots stay deterministic) and
        // it is what the screens actually render today. A fixture full of invented
        // articles would hide both the block types nobody has authored yet and the
        // ones that are there.
        // ------------------------------------------------------------------
        sealed class FileArticleCatalog : IArticleCatalog
        {
            readonly global::UiServiceAdapters.ArticleCatalogAdapter inner =
                new global::UiServiceAdapters.ArticleCatalogAdapter();

            public IReadOnlyList<ArticleSummary> All()
            {
                try { return inner.All(); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ui-shots] articles.json unavailable ({e.Message}); rendering an empty index.");
                    return Array.Empty<ArticleSummary>();
                }
            }

            public Article Find(string id)
            {
                try { return inner.Find(id); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ui-shots] articles.json unavailable ({e.Message}).");
                    return null;
                }
            }

            public bool IsAvailable
            {
                get
                {
                    try { return inner.IsAvailable; }
                    catch { return false; }
                }
            }
        }

        /// <summary>First run: the Início welcome card is showing (Tela 6).</summary>
        sealed class FirstRunOnboarding : IOnboardingState
        {
            public bool WelcomeDismissed => false;
            public void DismissWelcome() { }
        }

        // ------------------------------------------------------------------
        // Espécie (Decision D1) — the 3D turntable and the video player.
        //
        // NEITHER CAN BE REAL HERE, and the shots have to say so honestly. The
        // turntable is a scene-root rig in MainScene with its own camera and lights;
        // the harness builds a throwaway panel in an empty edit-mode context, so there
        // is no rig to render and nothing would make one appear short of loading the
        // scene. The video player needs a decoder, and on a Linux editor Unity cannot
        // decode H.264 at all (CLAUDE.md says so twice), so even with a player there
        // would be no frame.
        //
        // Both fakes therefore report AVAILABLE and hand back a flat stand-in texture.
        // That is deliberately more than "return null": it is what proves the two
        // Image elements actually fill their boxes — absolute inset, StretchToFill on
        // one and ScaleToFit on the other — which a null texture would leave
        // unverified behind a placeholder. What the shots cannot show is whether a
        // SHARK frames well in that box; that needs Play mode or a device, and it is
        // called out in the handover.
        // ------------------------------------------------------------------

        /// <summary>
        /// Stands in for the scene's <c>AnimalViewerRig</c>. Gestures are accepted and
        /// dropped — there is nothing to rotate — and the "render target" is a fixed
        /// vertical gradient in the rig's own deep-navy range, so the viewport reads as
        /// a rendered surface rather than as a broken image.
        /// </summary>
        sealed class StandInModelViewer : ISpeciesModelViewer
        {
            Texture2D texture;

            public bool IsAvailable => true;

            public Texture Texture => texture ?? (texture = MakeGradient(
                new Color(0.043f, 0.082f, 0.137f), new Color(0.094f, 0.161f, 0.239f)));

#pragma warning disable 67 // Never raised: the stand-in texture never changes.
            public event Action TextureChanged;
#pragma warning restore 67

            public bool Show(string speciesKey) => !string.IsNullOrEmpty(speciesKey);
            public void Hide() { }
            public void SetViewportSize(int widthPx, int heightPx) { }
            public void Rotate(float pixelsX) { }
            public void Zoom(float metres) { }
        }

        /// <summary>
        /// Stands in for the <c>VideoPlayer</c>. It skips <see cref="VideoPlaybackState.Loading"/>
        /// and goes straight to Playing with a fixed length, so a subject that calls
        /// <c>ToggleVideo</c> renders the state that has the most in it to review — the
        /// frame, the seek track at a real position and the clock — without a decoder
        /// and without a clock that would make the PNG change every run.
        /// </summary>
        sealed class StandInVideoPlayback : IVideoPlayback
        {
            const double FixedDuration = 42d;

            /// <summary>A third of the way in: far enough along that the filled part of
            /// the track is unmistakably a fill and not a rounding artifact.</summary>
            const double FixedPosition = 14d;

            Texture2D texture;

            public bool IsAvailable => true;
            public string Url { get; private set; }
            public VideoPlaybackState State { get; private set; } = VideoPlaybackState.Idle;

            public Texture Texture => State == VideoPlaybackState.Idle
                ? null
                : texture ?? (texture = MakeGradient(
                    new Color(0.055f, 0.145f, 0.184f), new Color(0.129f, 0.278f, 0.325f)));

            public double PositionSeconds => State == VideoPlaybackState.Idle ? 0d : FixedPosition;
            public double DurationSeconds => State == VideoPlaybackState.Idle ? 0d : FixedDuration;

            public event Action Changed;

            public void Play(string url)
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                Url = url;
                State = VideoPlaybackState.Playing;
                Changed?.Invoke();
            }

            public void Pause()
            {
                if (State != VideoPlaybackState.Playing) return;
                State = VideoPlaybackState.Paused;
                Changed?.Invoke();
            }

            public void Stop()
            {
                if (State == VideoPlaybackState.Idle && Url == null) return;
                Url = null;
                State = VideoPlaybackState.Idle;
                Changed?.Invoke();
            }

            /// <summary>Frozen: the playhead must not move, or the PNG changes per run.</summary>
            public void Seek(double seconds) { }
        }

        /// <summary>A 4x64 top-to-bottom gradient — stretched over a viewport, bilinear
        /// filtering makes it a smooth one. Cheap, deterministic, and obviously not a
        /// photograph.</summary>
        static Texture2D MakeGradient(Color top, Color bottom)
        {
            const int width = 4;
            const int height = 64;
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "ui-shots stand-in",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                // Row 0 is the BOTTOM of a Texture2D.
                var color = Color.Lerp(bottom, top, y / (float)(height - 1));
                for (int x = 0; x < width; x++) pixels[y * width + x] = color;
            }
            t.SetPixels(pixels);
            t.Apply(false);
            return t;
        }
    }
}
