using System;
using System.Collections.Generic;
using System.Globalization;
using MergulhoVirtual.UI;
using UnityEngine;

/// <summary>
/// Loads the educational articles from <c>Assets/Resources/articles.json</c> and
/// hands them to the UI layer as <see cref="ArticleSummary"/> / <see cref="Article"/>.
///
/// <para>Same shape as <see cref="BeachContentLibrary"/>, which is the precedent —
/// lazy, read once into memory, cached for the process, a top-level JSON
/// <i>object</i> so none of the <c>"{ \"places\": " + text + "}"</c> wrapping
/// <c>JsonUtility</c> forces on a top-level array is needed. The <c>_generated</c>
/// marker the build script writes has no counterpart field here and JsonUtility
/// ignores it.</para>
///
/// <para><b>The file is generated, never hand-edited</b> —
/// <c>tools/build_articles.py</c> parses <c>content/articles/*.md</c> into it. So a
/// malformed file means a broken build script, not a typo a content author can fix,
/// which is why every failure below is logged loudly and then degraded.</para>
///
/// <para><b>It never throws into a screen.</b> A missing file, malformed JSON, a
/// schemaVersion from the future, an entry with no <c>id</c>, a duplicate <c>id</c>,
/// a block with an unreadable <c>type</c> or <c>tone</c>, an <c>items</c> array on a
/// block kind that has no list, an unparseable <c>updated</c> date — each is logged
/// once and degraded. The worst case is an empty catalog with
/// <see cref="IsAvailable"/> false, which the "Conteúdo educativo" screen renders as
/// a state view rather than a blank page.</para>
///
/// <para><b>Unknown block types are kept, not dropped.</b> They are the
/// forward-compatibility case — a newer <c>articles.json</c> inside an older APK —
/// and <see cref="ArticleBlock.IsRenderable"/> is what filters them out downstream.
/// Dropping them here would make <see cref="ArticleBlockKind.Unknown"/> unreachable
/// and hide from diagnostics the fact that content was skipped.</para>
///
/// <para>The UI layer reaches this through
/// <c>UiServiceAdapters.ArticleCatalogAdapter</c>, which is the
/// <see cref="IArticleCatalog"/> implementation; this class is the file reader.
/// <see cref="ParseForTests"/> is the seam that lets the parser be unit-tested with
/// no Resources file at all (visible to <c>Assembly-CSharp-Editor</c> through the
/// existing <c>InternalsVisibleTo</c> in <c>Assets/Scripts/AssemblyInfo.cs</c>).</para>
/// </summary>
public static class ArticleLibrary
{
    /// <summary>Resources path (no extension), as Resources.Load wants it.</summary>
    public const string ResourceName = "articles";

    /// <summary>Schema this loader understands. Bumped only by a breaking field
    /// change; a newer file is read anyway (unknown fields are ignored, unknown block
    /// types degrade to <see cref="ArticleBlockKind.Unknown"/>) but logs a warning.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>Log prefix, so a device log can be filtered to this feature.</summary>
    const string Tag = "[Articles]";

    /// <summary>How many offending tokens an aggregated warning names before it
    /// stops — enough to fix the content, short of flooding a device log.</summary>
    const int MaxReportedTokens = 8;

    static ParseResult cache;

    /// <summary>Articles loaded; 0 before the first read and 0 when the file is unreadable.</summary>
    public static int Count
    {
        get
        {
            EnsureLoaded();
            return cache.Summaries.Count;
        }
    }

    /// <summary>
    /// False when the file was missing or could not be read at all. True for a file
    /// that parsed — including one that parsed to zero articles, which is "nothing
    /// authored yet" and a different thing to say to the user.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return cache.IsAvailable;
        }
    }

    /// <summary>Index rows in index order (<c>order</c> asc, then title, then id).
    /// Never null; may be empty.</summary>
    public static IReadOnlyList<ArticleSummary> All()
    {
        EnsureLoaded();
        return cache.Summaries;
    }

    /// <summary>The full article for an id, or null when there is none.</summary>
    public static Article Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        EnsureLoaded();
        return cache.Find(id);
    }

    /// <summary>Drops the cache so the next lookup re-reads the file (editor/tests).</summary>
    public static void Reload()
    {
        cache = null;
        EnsureLoaded();
    }

    static void EnsureLoaded()
    {
        if (cache != null) return;

        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
        {
            // Not an author error: the file is generated, so its absence means
            // `make articles` has never run in this working copy.
            Debug.LogWarning($"{Tag} Resources/{ResourceName}.json not found — the " +
                             "educational-content screens will show their \"could not load\" state. " +
                             "Run `make articles` to generate it from content/articles/*.md.");
            cache = ParseResult.Unavailable;
            return;
        }

        cache = Parse(asset.text);
    }

    // ---- Parsing ------------------------------------------------------------

    /// <summary>
    /// Parses the file's text. The whole of the loader's behaviour lives here so it
    /// can be exercised without a Resources asset — see <see cref="ParseForTests"/>.
    /// </summary>
    static ParseResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError($"{Tag} {ResourceName}.json is empty — the educational-content " +
                           "screens will show their \"could not load\" state.");
            return ParseResult.Unavailable;
        }

        FileDto file;
        try
        {
            file = JsonUtility.FromJson<FileDto>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} Could not parse {ResourceName}.json ({e.Message}) — the " +
                           "educational-content screens will show their \"could not load\" state.");
            return ParseResult.Unavailable;
        }

        if (file == null)
        {
            Debug.LogError($"{Tag} {ResourceName}.json did not deserialise into an object — the " +
                           "educational-content screens will show their \"could not load\" state.");
            return ParseResult.Unavailable;
        }

        // ⚠️ A missing "articles" key reads as an EMPTY LIBRARY, not as an unreadable
        // file — and that is a JsonUtility fact, not a choice. JsonUtility mirrors
        // Unity's own serialiser, which never leaves a List field null, so a parsed
        // `{"schemaVersion":1}` is indistinguishable here from `{"articles":[]}`. The
        // first draft of this loader did treat a null list as unreadable and the branch
        // was simply dead. It is kept as a null-coalesce rather than an error because
        // the realistic ways this file goes wrong — never generated, or half-written —
        // are already caught above as "missing" and "malformed JSON".
        var entries = file.articles ?? new List<ArticleDto>();

        if (file.schemaVersion != 0 && file.schemaVersion != SupportedSchemaVersion)
        {
            Debug.LogWarning($"{Tag} {ResourceName}.json declares schemaVersion " +
                             $"{file.schemaVersion}; this build understands {SupportedSchemaVersion}. " +
                             "Reading it anyway — unknown fields are ignored and unknown block " +
                             "types are skipped when rendering.");
        }

        var summaries = new List<ArticleSummary>(entries.Count);
        var articles = new Dictionary<string, Article>(entries.Count, StringComparer.Ordinal);
        var unknownTypes = new List<string>();
        var unknownTones = new List<string>();
        var strayItems = new List<string>();
        var badDates = new List<string>();
        int skipped = 0;

        foreach (var dto in entries)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.id)) { skipped++; continue; }
            string id = dto.id.Trim();

            if (articles.ContainsKey(id))
            {
                Debug.LogWarning($"{Tag} Duplicate article id \"{id}\" — keeping the first one. " +
                                 "Two content/articles/*.md files declare the same id.");
                continue;
            }

            var summary = MapSummary(id, dto, badDates);
            var blocks = MapBlocks(id, dto.blocks, unknownTypes, unknownTones, strayItems);

            summaries.Add(summary);
            articles[id] = Article.With(summary, blocks);
        }

        if (skipped > 0)
            Debug.LogWarning($"{Tag} Skipped {skipped} " +
                             $"{(skipped == 1 ? "article with no \"id\"" : "articles with no \"id\"")} — " +
                             "the id is the stable key and an article without one cannot be linked to.");

        ReportTokens(unknownTones,
            "block(s) with an unreadable \"tone\" — shown with the neutral Info tint, " +
            "because losing a tint is better than dropping the author's sentence. " +
            "Expected info/warning/success/error");
        ReportTokens(unknownTypes,
            "block(s) with a \"type\" this build does not understand — they will not be " +
            "rendered at all. This is the expected degradation when articles.json was " +
            "generated by a newer build_articles.py than the app was built against");
        ReportTokens(strayItems,
            "block(s) carrying an \"items\" array on a kind that has no list — the items " +
            "were dropped. Only bulletList and numberedList have rows");
        ReportTokens(badDates,
            "article(s) with an unreadable \"updated\" date — the \"Atualizado em\" line is " +
            "hidden rather than showing a guessed date. Expected ISO yyyy-MM-dd");

        Sort(summaries);
        Debug.Log($"{Tag} Loaded {summaries.Count} article(s).");
        return new ParseResult(true, summaries, articles);
    }

    /// <summary>
    /// One aggregated warning per class of bad token, naming the offenders so the
    /// content is fixable — rather than one line per occurrence, which on a long
    /// article would bury everything else in the device log.
    /// </summary>
    static void ReportTokens(List<string> offenders, string what)
    {
        if (offenders.Count == 0) return;
        int shown = Math.Min(offenders.Count, MaxReportedTokens);
        string list = string.Join(", ", offenders.GetRange(0, shown));
        if (offenders.Count > shown) list += $", … (+{offenders.Count - shown} more)";
        Debug.LogWarning($"{Tag} {offenders.Count} {what}: {list}");
    }

    /// <summary>
    /// Index order: the author's <c>order</c> first, then title, then id. The id tie-break
    /// is what makes the order <b>total</b>, so it does not depend on
    /// <c>List.Sort</c> being stable (it is not) or on the order the generator happened
    /// to write the entries in.
    ///
    /// <para>Ordinal comparison, not culture-aware: the repo keeps every string
    /// operation culture-free because IL2CPP can strip culture data from a player
    /// build, and a sort that silently changes between editor and device is worse than
    /// one that puts "Água" after "Zebra". <c>order</c> is the lever an author is meant
    /// to reach for anyway.</para>
    /// </summary>
    static void Sort(List<ArticleSummary> summaries)
    {
        summaries.Sort((a, b) =>
        {
            int byOrder = a.Order.CompareTo(b.Order);
            if (byOrder != 0) return byOrder;
            int byTitle = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            if (byTitle != 0) return byTitle;
            return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });
    }

    // ---- Mapping ------------------------------------------------------------

    static ArticleSummary MapSummary(string id, ArticleDto dto, List<string> badDates)
    {
        return new ArticleSummary
        {
            Id = id,
            Title = Clean(dto.title),
            Summary = Clean(dto.summary),
            Category = Clean(dto.category),
            HeroImage = Clean(dto.heroImage),
            HeroCredit = Clean(dto.heroCredit),
            Order = dto.order,
            Updated = ParseDate(id, dto.updated, badDates),
            // A negative count would make the reading estimate nonsense; clamp to the
            // "not counted" value, which hides the line.
            WordCount = dto.wordCount > 0 ? dto.wordCount : 0,
        };
    }

    static IReadOnlyList<ArticleBlock> MapBlocks(
        string id, List<BlockDto> dtos,
        List<string> unknownTypes, List<string> unknownTones, List<string> strayItems)
    {
        if (dtos == null || dtos.Count == 0) return Array.Empty<ArticleBlock>();

        var blocks = new List<ArticleBlock>(dtos.Count);
        for (int i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];
            if (dto == null) continue;

            if (!ArticleTokens.TryParseBlockKind(dto.type, out var kind))
                unknownTypes.Add($"{id}[{i}]=\"{dto.type}\"");

            var tone = ArticleCalloutTone.Info;
            if (kind == ArticleBlockKind.Callout && !ArticleTokens.TryParseCalloutTone(dto.tone, out tone))
                unknownTones.Add($"{id}[{i}]=\"{dto.tone}\"");

            bool wantsItems = kind == ArticleBlockKind.BulletList || kind == ArticleBlockKind.NumberedList;
            var items = wantsItems ? CleanList(dto.items) : Array.Empty<string>();
            if (!wantsItems && dto.items != null && dto.items.Count > 0)
                strayItems.Add($"{id}[{i}]=\"{dto.type}\"");

            blocks.Add(new ArticleBlock
            {
                Kind = kind,
                Level = ClampHeadingLevel(kind, dto.level),
                // Text keeps its rich-text markup verbatim — the build script already
                // escaped the author's own angle brackets, so trimming is the only
                // thing safe to do to it.
                Text = Clean(dto.text),
                Items = items,
                Tone = tone,
                Attribution = Clean(dto.attribution),
                Src = Clean(dto.src),
                Caption = Clean(dto.caption),
                Credit = Clean(dto.credit),
                Url = Clean(dto.url),
                Title = Clean(dto.title),
                SpeciesKey = Clean(dto.key),
                BeachName = Clean(dto.name),
            });
        }
        return blocks.Count == 0 ? (IReadOnlyList<ArticleBlock>)Array.Empty<ArticleBlock>() : blocks;
    }

    /// <summary>
    /// Headings are 2 or 3 and nothing else — the title comes from front matter, so a
    /// body <c>#</c> is an error the build script refuses, and there is no stylesheet
    /// rule for a level 4. Anything out of range is clamped rather than kept, so a
    /// heading always renders as one of the two sizes that exist. Non-headings keep 0,
    /// where the field is meaningless.
    /// </summary>
    static int ClampHeadingLevel(ArticleBlockKind kind, int level)
    {
        if (kind != ArticleBlockKind.Heading) return 0;
        if (level < 2) return 2;
        if (level > 3) return 3;
        return level;
    }

    /// <summary>
    /// Parses the ISO <c>yyyy-MM-dd</c> the front matter carries into an
    /// <b>unspecified-kind</b> DateTime — it is a calendar date, not an instant, and
    /// tagging it UTC would invite a local-time conversion that moves it across
    /// midnight. Blank is not an error (the key is optional); an unparseable value is
    /// reported so the content can be fixed, and yields null so the line hides.
    /// </summary>
    static DateTime? ParseDate(string id, string raw, List<string> badDates)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return parsed;
        badDates.Add($"{id}=\"{raw}\"");
        return null;
    }

    /// <summary>Trims, and turns a blank into null so every consumer sees one "absent".</summary>
    static string Clean(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trims each row and drops the blank ones; never returns null.</summary>
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

    // ---- Test seam ----------------------------------------------------------

    /// <summary>
    /// Parses <paramref name="json"/> exactly as <see cref="EnsureLoaded"/> would,
    /// without touching <c>Resources</c> or the process-wide cache. This is the seam
    /// every parser test uses: the degradation cases (malformed JSON, a keyless entry,
    /// a future schemaVersion, an unknown block type) are all easier to author as a
    /// string than as a Resources asset, and a test that loaded the real shipped file
    /// could only ever assert what today's content happens to contain.
    ///
    /// <para><c>internal</c>, reached from <c>Assembly-CSharp-Editor</c> through the
    /// <c>InternalsVisibleTo</c> already in <c>Assets/Scripts/AssemblyInfo.cs</c> for
    /// the JobQueue tests — no new seam and no new asmdef.</para>
    /// </summary>
    internal static ParseResult ParseForTests(string json) => Parse(json);

    /// <summary>
    /// The outcome of one parse: whether the file was readable at all, the index rows
    /// in index order, and the bodies by id. Immutable, so the process-wide cache
    /// cannot be mutated by a caller.
    /// </summary>
    internal sealed class ParseResult
    {
        /// <summary>A readable file with nothing in it — what every failure degrades to.</summary>
        public static readonly ParseResult Unavailable = new ParseResult(
            false, new List<ArticleSummary>(), new Dictionary<string, Article>(StringComparer.Ordinal));

        public readonly bool IsAvailable;
        public readonly IReadOnlyList<ArticleSummary> Summaries;
        readonly Dictionary<string, Article> byId;

        public ParseResult(bool available, List<ArticleSummary> summaries, Dictionary<string, Article> byId)
        {
            IsAvailable = available;
            Summaries = summaries;
            this.byId = byId;
        }

        public Article Find(string id) =>
            id != null && byId.TryGetValue(id.Trim(), out var article) ? article : null;
    }

    // ---- JsonUtility DTOs ---------------------------------------------------
    // Field names must match articles.json exactly — they are a persisted wire
    // format, so ADD a field, never rename one. Fields the file has and these types
    // lack (_generated, and anything a newer generator adds) are ignored.

    [Serializable]
    class FileDto
    {
        public int schemaVersion;
        public List<ArticleDto> articles;
    }

    [Serializable]
    class ArticleDto
    {
        public string id;
        public string title;
        public string summary;
        public string category;
        public string heroImage;
        public string heroCredit;
        public int order;
        public string updated;
        public int wordCount;
        public List<BlockDto> blocks;
    }

    /// <summary>
    /// One block, FLAT, carrying the whole field union with a string <c>type</c>
    /// discriminator — because <c>JsonUtility</c> cannot deserialise polymorphically.
    /// This is the same trick <c>JobEnvelope</c> plays for <c>Job</c> subclasses, with
    /// one difference: a block's payload is small and fixed, so the union is spelled
    /// out here rather than nested as an opaque JSON string.
    ///
    /// <para>Fields a given <c>type</c> does not use are omitted from the JSON and
    /// land as C# defaults, which is why <see cref="MapBlocks"/> decides what to read
    /// from <c>type</c> and never from "which fields happen to be filled".</para>
    /// </summary>
    [Serializable]
    class BlockDto
    {
        public string type;
        public int level;
        public string text;
        public List<string> items;
        public string tone;
        public string attribution;
        public string src;
        public string caption;
        public string credit;
        public string url;
        public string title;
        public string key;
        public string name;
    }
}
