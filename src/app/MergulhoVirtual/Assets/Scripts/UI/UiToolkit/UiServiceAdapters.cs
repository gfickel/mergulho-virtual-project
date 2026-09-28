using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    /// <summary>
    /// The user's own sighting reports, over the Assembly-CSharp
    /// <see cref="JobQueue"/> — submit one, and read back the ones still in
    /// flight for the "Seus avistamentos pendentes" feed.
    ///
    /// <para><b>It owns the photo file.</b> The picker hands back a path in the
    /// app's cache (Android may evict it; iOS exported a temporary) and the user
    /// can delete the original from their gallery at any moment, so
    /// <see cref="Submit"/> copies the bytes into
    /// <c>persistentDataPath/sightings/&lt;guid&gt;.&lt;ext&gt;</c> before
    /// queueing and the job deletes that copy on success. The copy is
    /// <see cref="File.Copy"/> — raw bytes, never decode/re-encode — because EXIF
    /// (GPS, DateTimeOriginal, camera) is the point of the photo path.</para>
    ///
    /// <para><b>The guid is the idempotency key</b>, baked into both the file name
    /// and the job, so a retry after a TCP timeout the server already honoured
    /// collapses server-side instead of creating a second document.</para>
    ///
    /// <para>Constructing this also spins up the queue
    /// (<see cref="JobQueue.GetOrCreate"/>) and loads what is on disk. That is
    /// load-bearing, not incidental: nothing else in the scene instantiates the
    /// queue, so without it a sighting left queued by a previous run would never
    /// resume <i>and</i> would be missing from the feed.</para>
    /// </summary>
    public sealed class SightingReportsAdapter : ISightingReports, IDisposable
    {
        /// <summary>Prod endpoint (CLAUDE.md, "Updating Unity URLs for prod").
        /// Point the constructor at a LAN <c>BACKEND_DEBUG=1</c> backend for
        /// editor testing — that mode ignores the missing App Check header.</summary>
        public const string DefaultUploadUrl = "https://mergulhovirtual.dev/api/v1/avistamentos";

        readonly string uploadUrl;
        readonly JobQueue queue;

        public event Action Changed;

        public SightingReportsAdapter(string uploadUrl = null)
        {
            this.uploadUrl = string.IsNullOrWhiteSpace(uploadUrl) ? DefaultUploadUrl : uploadUrl;
            queue = JobQueue.GetOrCreate();
            // GetOrCreate's Start() loads from disk a frame later; do it now so the
            // very first render of the feed is right.
            queue.LoadPendingFromDisk();
            queue.JobCompleted += OnJobCompleted;
        }

        public void Dispose()
        {
            if (queue != null) queue.JobCompleted -= OnJobCompleted;
        }

        void OnJobCompleted(string jobId, JobResult result) => Changed?.Invoke();

        public bool Submit(SightingDraft draft)
        {
            if (draft == null) return false;
            if (string.IsNullOrEmpty(draft.PhotoPath) || !File.Exists(draft.PhotoPath))
            {
                Debug.LogWarning("[SightingReports] Submit called with no readable photo — dropping.");
                return false;
            }

            string ext = Path.GetExtension(draft.PhotoPath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            string sightingId = Guid.NewGuid().ToString("N");
            string destDir = Path.Combine(Application.persistentDataPath, "sightings");
            string destPath = Path.Combine(destDir, sightingId + ext);

            try
            {
                Directory.CreateDirectory(destDir);
                File.Copy(draft.PhotoPath, destPath, overwrite: true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SightingReports] Could not copy the photo: {e.Message}");
                return false;
            }

            var job = new ReportSightingJob
            {
                Url = uploadUrl,
                ImagePath = destPath,
                MimeType = GuessMime(ext),
                BeachName = draft.BeachKey,
                IsoTimestamp = ToIsoUtc(draft.WhenUtc),
                SpeciesGuess = draft.SpeciesLabel,
                Notes = draft.Notes,
                IdempotencyKey = sightingId,
                SpeciesKey = draft.SpeciesKey,
                SizeBucket = draft.SizeBucket,
                Behaviours = ToArray(draft.BehaviourKeys),
                ReporterName = draft.ReporterName,
                ReporterEmail = draft.ReporterEmail,
                ReporterProfile = draft.ProfileKey,
            };

            if (!queue.Enqueue(job))
            {
                Debug.LogWarning("[SightingReports] Job queue full — sighting not taken.");
                try { File.Delete(destPath); } catch { /* best effort */ }
                return false;
            }

            Changed?.Invoke();
            return true;
        }

        public IReadOnlyList<SightingRecord> ListPending() => Map(queue?.ListPending(ReportSightingJob.JobType));

        public IReadOnlyList<SightingRecord> ListFailed() => Map(queue?.ListFailed(ReportSightingJob.JobType));

        static IReadOnlyList<SightingRecord> Map(IReadOnlyList<JobRecord> records)
        {
            var list = new List<SightingRecord>();
            if (records == null) return list;
            foreach (var record in records)
            {
                var job = record.Snapshot<ReportSightingJob>();
                if (job == null) continue;
                list.Add(new SightingRecord
                {
                    // The idempotency key IS the job id today; fall back to the
                    // envelope id so a row always has a stable identity.
                    Id = string.IsNullOrEmpty(job.IdempotencyKey) ? record.Id : job.IdempotencyKey,
                    SpeciesKey = job.SpeciesKey,
                    SpeciesLabel = job.SpeciesGuess,
                    BeachKey = string.IsNullOrEmpty(job.BeachName) ? null : job.BeachName,
                    WhenUtc = ParseIsoUtc(job.IsoTimestamp, record.CreatedAtUtc),
                    PhotoPath = job.ImagePath,
                    State = MapState(record.State),
                    AttemptCount = record.AttemptCount,
                });
            }
            return list;
        }

        static SightingState MapState(JobRecordState state)
        {
            switch (state)
            {
                case JobRecordState.Retrying: return SightingState.Retrying;
                case JobRecordState.WaitingForNetwork: return SightingState.WaitingForNetwork;
                case JobRecordState.Failed: return SightingState.Failed;
                default: return SightingState.Queued;
            }
        }

        static string[] ToArray(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return Array.Empty<string>();
            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++) array[i] = values[i];
            return array;
        }

        /// <summary>RFC3339 in UTC. An Unspecified kind is taken as already-UTC
        /// rather than converted from local — the draft field is named WhenUtc and
        /// a test clock that hands back an unspecified DateTime must not shift.</summary>
        static string ToIsoUtc(DateTime when)
        {
            DateTime utc = when.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(when, DateTimeKind.Utc)
                : when.ToUniversalTime();
            return utc.ToString("o", CultureInfo.InvariantCulture);
        }

        static DateTime ParseIsoUtc(string iso, DateTime fallbackUtc)
        {
            if (!string.IsNullOrEmpty(iso) &&
                DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            return fallbackUtc;
        }

        static string GuessMime(string ext)
        {
            switch ((ext ?? string.Empty).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".heic": return "image/heic";
                case ".heif": return "image/heif";
                case ".webp": return "image/webp";
                default: return "application/octet-stream";
            }
        }
    }

    /// <summary>
    /// The device photo gallery, over <see cref="GalleryPicker"/> (yasirkula's
    /// NativeGallery on device, a file dialog in the Editor).
    ///
    /// <para>The picker reports a user cancel and an OS permission denial
    /// identically — both arrive as a null path — so both map to
    /// <see cref="PhotoPickOutcome.Cancelled"/>. That is the honest mapping: the
    /// app cannot tell them apart without a separate permission query, and
    /// showing "acesso negado" after a plain cancel would be a lie.</para>
    /// </summary>
    public sealed class GalleryPhotoPickerAdapter : IPhotoPicker
    {
        public void PickPhoto(Action<PhotoPickResult> onResult)
        {
            if (onResult == null) return;
            GalleryPicker.PickImage((path, error) =>
            {
                if (!string.IsNullOrEmpty(error))
                {
                    onResult(error == "cancelled"
                        ? PhotoPickResult.Cancelled()
                        : PhotoPickResult.Failed(error));
                    return;
                }

                long size = 0;
                try { size = new FileInfo(path).Length; }
                catch (Exception e)
                {
                    // Unknown size: 0 passes the limit check rather than blocking a
                    // pick over a stat failure. The backend still bounds the upload.
                    Debug.LogWarning($"[PhotoPicker] Could not stat {path}: {e.Message}");
                }
                onResult(PhotoPickResult.Picked(path, size));
            });
        }
    }
}
