using System;
using System.Collections.Generic;
using MergulhoVirtual.UI;
using UnityEngine;

/// <summary>
/// Loads the hand-authored beach content from
/// <c>Assets/Resources/beaches_content.json</c> and hands it to the UI layer as
/// <see cref="BeachContent"/>, keyed by the exact places.json <c>name</c>.
///
/// <para>Same shape as <see cref="ReverseGeocoding"/>'s places loader — lazy,
/// loaded once into memory, cached for the process — with one difference:
/// beaches_content.json is already a top-level <i>object</i>
/// (<c>{ "schemaVersion": 1, "beaches": [ … ] }</c>), so it needs none of the
/// <c>"{ \"places\": " + text + "}"</c> wrapping JsonUtility forces on a
/// top-level array. The <c>_comment</c>, <c>_todo</c> and <c>_sources</c> markers
/// the content authors work with have no counterpart field here and JsonUtility
/// ignores them.</para>
///
/// <para><b>It never throws into a screen.</b> A missing file, malformed JSON, an
/// entry with no key, a duplicate key and an unrecognized <c>riskLevel</c> /
/// <c>idealTide</c> token are each logged once and degraded: the worst case is an
/// empty catalog, which renders as a beach screen with every optional section
/// hidden — exactly what an all-blank entry renders as, which is the common case
/// today anyway.</para>
///
/// <para>The UI layer reaches this through
/// <c>UiServiceAdapters.BeachContentAdapter</c>, which is the
/// <see cref="IBeachContent"/> implementation; this class is the file reader.</para>
/// </summary>
public static class BeachContentLibrary
{
    /// <summary>Resources path (no extension), as Resources.Load wants it.</summary>
    public const string ResourceName = "beaches_content";

    /// <summary>Schema this loader understands. Bumped only by a breaking field change;
    /// a newer file is read anyway (unknown fields are ignored) but logs a warning.</summary>
    public const int SupportedSchemaVersion = 1;

    static Dictionary<string, BeachContent> byName;

    /// <summary>Number of beaches with an entry in the file; 0 before the first load.</summary>
    public static int Count => byName?.Count ?? 0;

    /// <summary>
    /// Content for a beach key, or null when the file has no entry for it. Callers
    /// that render should go through <c>IBeachContent.ForBeach</c>, which turns a
    /// null into an empty content.
    /// </summary>
    public static BeachContent Find(string beachName)
    {
        if (string.IsNullOrEmpty(beachName)) return null;
        EnsureLoaded();
        return byName.TryGetValue(beachName, out var content) ? content : null;
    }

    /// <summary>Every loaded key — for content tooling and diagnostics.</summary>
    public static IEnumerable<string> LoadedBeachNames()
    {
        EnsureLoaded();
        return byName.Keys;
    }

    /// <summary>Drops the cache so the next lookup re-reads the file (editor/tests).</summary>
    public static void Reload()
    {
        byName = null;
        EnsureLoaded();
    }

    static void EnsureLoaded()
    {
        if (byName != null) return;
        byName = new Dictionary<string, BeachContent>(StringComparer.Ordinal);

        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
        {
            Debug.LogWarning($"[BeachContent] Resources/{ResourceName}.json not found — " +
                             "beach screens will render with every editorial section hidden.");
            return;
        }

        FileDto file;
        try
        {
            file = JsonUtility.FromJson<FileDto>(asset.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"[BeachContent] Could not parse {ResourceName}.json ({e.Message}) — " +
                           "beach screens will render with every editorial section hidden.");
            return;
        }

        if (file == null || file.beaches == null)
        {
            Debug.LogWarning($"[BeachContent] {ResourceName}.json has no \"beaches\" array — " +
                             "beach screens will render with every editorial section hidden.");
            return;
        }

        if (file.schemaVersion != 0 && file.schemaVersion != SupportedSchemaVersion)
        {
            Debug.LogWarning($"[BeachContent] {ResourceName}.json declares schemaVersion " +
                             $"{file.schemaVersion}; this build understands {SupportedSchemaVersion}. " +
                             "Reading it anyway — unknown fields are ignored.");
        }

        int skipped = 0;
        foreach (var dto in file.beaches)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.name)) { skipped++; continue; }
            string key = dto.name.Trim();
            if (byName.ContainsKey(key))
            {
                Debug.LogWarning($"[BeachContent] Duplicate entry for \"{key}\" — keeping the first one.");
                continue;
            }
            byName[key] = Map(key, dto);
        }

        if (skipped > 0)
            Debug.LogWarning($"[BeachContent] Skipped {skipped} entr{(skipped == 1 ? "y" : "ies")} with no \"name\" key.");

        Debug.Log($"[BeachContent] Loaded content for {byName.Count} beaches.");
    }

    // ---- Mapping ------------------------------------------------------------

    static BeachContent Map(string key, BeachDto dto)
    {
        if (!BeachContentTokens.TryParseRisk(dto.riskLevel, out var risk))
            Debug.LogWarning($"[BeachContent] \"{key}\": unknown riskLevel \"{dto.riskLevel}\" — " +
                             "treating it as unfilled (no risk pill). Expected baixo/medio/alto.");

        if (!BeachContentTokens.TryParseIdealTide(dto.idealTide, out var tide))
            Debug.LogWarning($"[BeachContent] \"{key}\": unknown idealTide \"{dto.idealTide}\" — " +
                             "treating it as unfilled. Expected baixa/alta/qualquer.");

        return new BeachContent
        {
            BeachName = key,
            RiskLevel = risk,
            IdealTide = tide,
            BestSeason = Clean(dto.bestSeason),
            SightingPeak = Clean(dto.sightingPeak),
            LifeguardHours = Clean(dto.lifeguardHours),
            EnvironmentTags = CleanList(dto.environmentTags),
            Advisories = CleanList(dto.advisories),
            Tips = CleanList(dto.tips),
            Species = MapSpecies(key, dto.species),
        };
    }

    static IReadOnlyList<BeachSpeciesContent> MapSpecies(string key, List<SpeciesDto> dtos)
    {
        if (dtos == null || dtos.Count == 0) return Array.Empty<BeachSpeciesContent>();
        var list = new List<BeachSpeciesContent>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.key))
            {
                Debug.LogWarning($"[BeachContent] \"{key}\": a species entry has no \"key\" — skipped.");
                continue;
            }
            list.Add(new BeachSpeciesContent
            {
                SpeciesKey = dto.key.Trim(),
                Tag = Clean(dto.tag),
                Behaviour = Clean(dto.behaviour),
            });
        }
        return list.Count == 0 ? (IReadOnlyList<BeachSpeciesContent>)Array.Empty<BeachSpeciesContent>() : list;
    }

    /// <summary>Trims, and turns a blank into null so every consumer sees one "absent".</summary>
    static string Clean(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trims each entry and drops the blank ones; never returns null.</summary>
    static IReadOnlyList<string> CleanList(List<string> values)
    {
        if (values == null || values.Count == 0) return Array.Empty<string>();
        var list = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
        }
        return list.Count == 0 ? (IReadOnlyList<string>)Array.Empty<string>() : list;
    }

    // ---- JsonUtility DTOs ---------------------------------------------------
    // Field names must match beaches_content.json exactly. Fields the file has and
    // these types lack (_comment, _todo, _sources, riskLevelValues, …) are ignored.

    [Serializable]
    class FileDto
    {
        public int schemaVersion;
        public List<BeachDto> beaches;
    }

    [Serializable]
    class BeachDto
    {
        public string name;
        public string riskLevel;
        public List<string> environmentTags;
        public string bestSeason;
        public string idealTide;
        public string sightingPeak;
        public string lifeguardHours;
        public List<string> advisories;
        public List<SpeciesDto> species;
        public List<string> tips;
    }

    [Serializable]
    class SpeciesDto
    {
        public string key;
        public string tag;
        public string behaviour;
    }
}
