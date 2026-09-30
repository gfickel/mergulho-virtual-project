using System;
using System.Text.RegularExpressions;
using MergulhoVirtual.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The <see cref="ArticleLibrary"/> parser, driven through its
/// <c>ParseForTests</c> seam — no Resources asset, no editor, no cache.
///
/// <para><b>Every degradation case gets its own test, and each one asserts that the
/// catalog comes back USABLE</b>, not merely that a warning was logged. That is the
/// contract the whole feature rests on: the file is generated, so a malformed one means
/// a broken build script rather than a fixable typo, and a screen that threw on it
/// would take the app down over a content problem. The worst outcome allowed anywhere
/// below is an empty catalog with <c>IsAvailable == false</c>.</para>
///
/// <para>Expected warnings are declared with <see cref="LogAssert.Expect"/> the way
/// <c>JobQueueTests</c> does — an unexpected warning or error fails an EditMode test, so
/// a test that does NOT declare one is also asserting that the happy path is
/// silent.</para>
/// </summary>
public class ArticleLibraryTests
{
    // ---- Fixtures -----------------------------------------------------------

    /// <summary>
    /// One article carrying all ten block kinds, in the wire shape contract §3 fixes:
    /// a flat object per block with a string <c>type</c> discriminator and only the
    /// fields that kind uses.
    /// </summary>
    const string AllTenKinds = @"{
  ""schemaVersion"": 1,
  ""_generated"": ""tools/build_articles.py — do not hand-edit"",
  ""articles"": [
    {
      ""id"": ""tubaroes-de-noronha"",
      ""title"": ""Os tubarões de Fernando de Noronha"",
      ""summary"": ""Quem vive aqui, e por que eles não são o que o cinema contou."",
      ""category"": ""Espécies"",
      ""heroImage"": ""Animals/tiger_shark"",
      ""heroCredit"": ""Foto: Albert Kok / CC BY-SA 3.0 (Wikimedia Commons)"",
      ""order"": 10,
      ""updated"": ""2026-09-29"",
      ""wordCount"": 412,
      ""blocks"": [
        { ""type"": ""heading"", ""level"": 2, ""text"": ""Quem vive aqui"" },
        { ""type"": ""paragraph"", ""text"": ""O arquipélago abriga <b>cinco</b> espécies."" },
        { ""type"": ""bulletList"", ""items"": [""Tubarão-limão"", ""Tubarão-tigre""] },
        { ""type"": ""numberedList"", ""items"": [""Entre na água devagar"", ""Não toque""] },
        { ""type"": ""callout"", ""tone"": ""warning"", ""text"": ""Nunca alimente os animais."" },
        { ""type"": ""quote"", ""text"": ""O mar não perdoa pressa."", ""attribution"": ""Ana Silva"" },
        { ""type"": ""image"", ""src"": ""Beaches/praia_do_sancho"", ""caption"": ""Sancho ao amanhecer"", ""credit"": ""Foto: Autor / CC BY 4.0 (Wikimedia Commons)"" },
        { ""type"": ""video"", ""url"": ""https://storage.googleapis.com/conteudos-educacionais/videos/a.mp4"", ""title"": ""Berçário de tubarões"" },
        { ""type"": ""speciesRef"", ""key"": ""tiger_shark"" },
        { ""type"": ""beachRef"", ""name"": ""Sueste Beach"" }
      ]
    }
  ]
}";

    static string OneArticle(string id = "a", string title = "Um", int order = 10,
        string extra = "") =>
        "{\"schemaVersion\":1,\"articles\":[{\"id\":\"" + id + "\",\"title\":\"" + title +
        "\",\"summary\":\"Resumo.\",\"category\":\"Espécies\",\"order\":" + order +
        ",\"wordCount\":400" + extra + "}]}";

    static ArticleBlock BlockOf(ArticleLibrary.ParseResult result, string id, int index) =>
        result.Find(id).Blocks[index];

    // ---- Happy path ---------------------------------------------------------

    [Test]
    public void AllTenBlockKindsParseWithTheirOwnFields()
    {
        var result = ArticleLibrary.ParseForTests(AllTenKinds);

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Summaries.Count, Is.EqualTo(1));

        var article = result.Find("tubaroes-de-noronha");
        Assert.That(article, Is.Not.Null);
        Assert.That(article.Blocks.Count, Is.EqualTo(10));

        var heading = article.Blocks[0];
        Assert.That(heading.Kind, Is.EqualTo(ArticleBlockKind.Heading));
        Assert.That(heading.Level, Is.EqualTo(2));
        Assert.That(heading.Text, Is.EqualTo("Quem vive aqui"));

        var paragraph = article.Blocks[1];
        Assert.That(paragraph.Kind, Is.EqualTo(ArticleBlockKind.Paragraph));
        Assert.That(paragraph.Text, Is.EqualTo("O arquipélago abriga <b>cinco</b> espécies."),
            "rich text survives verbatim — the build script already escaped the author's " +
            "own angle brackets, so re-escaping here would print the tags");

        var bullets = article.Blocks[2];
        Assert.That(bullets.Kind, Is.EqualTo(ArticleBlockKind.BulletList));
        Assert.That(bullets.Items, Is.EqualTo(new[] { "Tubarão-limão", "Tubarão-tigre" }));

        var numbered = article.Blocks[3];
        Assert.That(numbered.Kind, Is.EqualTo(ArticleBlockKind.NumberedList));
        Assert.That(numbered.Items.Count, Is.EqualTo(2));

        var callout = article.Blocks[4];
        Assert.That(callout.Kind, Is.EqualTo(ArticleBlockKind.Callout));
        Assert.That(callout.Tone, Is.EqualTo(ArticleCalloutTone.Warning));
        Assert.That(callout.Text, Is.EqualTo("Nunca alimente os animais."));

        var quote = article.Blocks[5];
        Assert.That(quote.Kind, Is.EqualTo(ArticleBlockKind.Quote));
        Assert.That(quote.Attribution, Is.EqualTo("Ana Silva"));

        var image = article.Blocks[6];
        Assert.That(image.Kind, Is.EqualTo(ArticleBlockKind.Image));
        Assert.That(image.Src, Is.EqualTo("Beaches/praia_do_sancho"));
        Assert.That(image.Caption, Is.EqualTo("Sancho ao amanhecer"));
        Assert.That(image.Credit, Is.EqualTo("Foto: Autor / CC BY 4.0 (Wikimedia Commons)"));

        var video = article.Blocks[7];
        Assert.That(video.Kind, Is.EqualTo(ArticleBlockKind.Video));
        Assert.That(video.Url, Does.StartWith("https://"));
        Assert.That(video.Title, Is.EqualTo("Berçário de tubarões"));

        var species = article.Blocks[8];
        Assert.That(species.Kind, Is.EqualTo(ArticleBlockKind.SpeciesRef));
        Assert.That(species.SpeciesKey, Is.EqualTo("tiger_shark"));

        var beach = article.Blocks[9];
        Assert.That(beach.Kind, Is.EqualTo(ArticleBlockKind.BeachRef));
        Assert.That(beach.BeachName, Is.EqualTo("Sueste Beach"));

        foreach (var block in article.Blocks)
            Assert.That(block.IsRenderable, Is.True, block.Kind.ToString());
    }

    [Test]
    public void FrontMatterMapsOntoTheSummary()
    {
        var summary = ArticleLibrary.ParseForTests(AllTenKinds).Summaries[0];

        Assert.That(summary.Id, Is.EqualTo("tubaroes-de-noronha"));
        Assert.That(summary.Title, Is.EqualTo("Os tubarões de Fernando de Noronha"));
        Assert.That(summary.Category, Is.EqualTo("Espécies"));
        Assert.That(summary.HeroImage, Is.EqualTo("Animals/tiger_shark"));
        Assert.That(summary.HeroCredit, Does.Contain("CC BY-SA 3.0"));
        Assert.That(summary.Order, Is.EqualTo(10));
        Assert.That(summary.WordCount, Is.EqualTo(412));
        Assert.That(summary.Updated, Is.EqualTo(new DateTime(2026, 9, 29)));
        Assert.That(summary.Updated.Value.Kind, Is.EqualTo(DateTimeKind.Unspecified),
            "it is a calendar date, not an instant — tagging it UTC invites a local-time " +
            "conversion that would move it across midnight for half the world");
    }

    [Test]
    public void ABlankFieldBecomesNullSoEveryConsumerSeesOneAbsent()
    {
        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"  Um  \"," +
            "\"summary\":\"   \",\"category\":\"Espécies\",\"heroImage\":\"\"}]}");

        var summary = result.Summaries[0];
        Assert.That(summary.Title, Is.EqualTo("Um"), "trimmed");
        Assert.That(summary.Summary, Is.Null);
        Assert.That(summary.HasSummary, Is.False);
        Assert.That(summary.HeroImage, Is.Null);
        Assert.That(summary.Updated, Is.Null);
    }

    [Test]
    public void AnArticleWithNoBlocksIsLegitimate()
    {
        var result = ArticleLibrary.ParseForTests(OneArticle());

        Assert.That(result.IsAvailable, Is.True);
        var article = result.Find("a");
        Assert.That(article.Blocks, Is.Not.Null, "never null — a screen binds without a null check");
        Assert.That(article.Blocks, Is.Empty);
        Assert.That(article.HasBlocks, Is.False);
    }

    [Test]
    public void ListRowsAreTrimmedAndBlanksDropped()
    {
        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"type\":\"bulletList\",\"items\":[\"  um  \",\"   \",\"dois\"]}]}]}");

        Assert.That(BlockOf(result, "a", 0).Items, Is.EqualTo(new[] { "um", "dois" }));
    }

    [Test]
    public void FindIsExactAndReturnsNullForAnUnknownId()
    {
        var result = ArticleLibrary.ParseForTests(OneArticle("a"));

        Assert.That(result.Find("a"), Is.Not.Null);
        Assert.That(result.Find("  a  "), Is.Not.Null, "whitespace-tolerant, like the public API");
        Assert.That(result.Find("A"), Is.Null, "the id is a machine key — case matters");
        Assert.That(result.Find("nao-existe"), Is.Null);
        Assert.That(result.Find(null), Is.Null);
    }

    // ---- Index order --------------------------------------------------------

    [Test]
    public void IndexOrderIsOrderThenTitleThenId()
    {
        const string json = @"{""schemaVersion"":1,""articles"":[
          {""id"":""d"",""title"":""Zebra"",""order"":30},
          {""id"":""c"",""title"":""Beta"",""order"":20},
          {""id"":""b"",""title"":""Alfa"",""order"":20},
          {""id"":""a"",""title"":""Qualquer"",""order"":10}]}";

        var result = ArticleLibrary.ParseForTests(json);

        Assert.That(Ids(result), Is.EqualTo(new[] { "a", "b", "c", "d" }),
            "order wins; ties break on title, which puts Alfa before Beta");
    }

    [Test]
    public void TheIdTieBreakMakesTheOrderTotalAndThereforeDeterministic()
    {
        // Same order AND same title: without the id tie-break this would depend on
        // List.Sort's (unstable) behaviour and on the order the generator wrote them.
        const string json = @"{""schemaVersion"":1,""articles"":[
          {""id"":""z"",""title"":""Mesmo"",""order"":10},
          {""id"":""a"",""title"":""Mesmo"",""order"":10}]}";

        Assert.That(Ids(ArticleLibrary.ParseForTests(json)), Is.EqualTo(new[] { "a", "z" }));
    }

    [Test]
    public void AnAbsentOrderSortsFirstWhichIsWhyTheGeneratorAlwaysWritesOne()
    {
        // JsonUtility cannot tell an absent int from a 0, so this is the documented
        // consequence rather than a defect — pinned so nobody is surprised by it.
        const string json = @"{""schemaVersion"":1,""articles"":[
          {""id"":""com-order"",""title"":""A"",""order"":1000},
          {""id"":""sem-order"",""title"":""B""}]}";

        var result = ArticleLibrary.ParseForTests(json);

        Assert.That(result.Summaries[0].Id, Is.EqualTo("sem-order"));
        Assert.That(result.Summaries[0].Order, Is.Zero);
    }

    // ---- Degradation: the file itself ---------------------------------------

    [Test]
    public void AMissingArticlesKeyReadsAsAnEmptyLibrary_BecauseJsonUtilityCannotTellItFromAnEmptyArray()
    {
        // This is a JsonUtility fact, not a design choice, and it is pinned because the
        // loader's first draft asserted the opposite. JsonUtility mirrors Unity's own
        // serialiser, which never leaves a List field null, so `{"schemaVersion":1}`
        // deserialises with an EMPTY `articles` list — indistinguishable from
        // `{"articles":[]}`. There is therefore no "present but keyless" failure state to
        // report, and the file's realistic failure modes (never generated, half-written)
        // are caught as "missing" and "malformed JSON" instead.
        var result = ArticleLibrary.ParseForTests("{\"schemaVersion\":1}");

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Summaries, Is.Empty);
        Assert.That(result.Find("a"), Is.Null);
    }

    [Test]
    public void MalformedJsonIsUnavailableAndDoesNotThrow()
    {
        LogAssert.Expect(LogType.Error, new Regex(@"(?s).*articles\.json.*"));

        ArticleLibrary.ParseResult result = null;
        Assert.DoesNotThrow(() => result = ArticleLibrary.ParseForTests("{ isso não é json"));

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsAvailable, Is.False);
        Assert.That(result.Summaries, Is.Empty);
    }

    [Test]
    public void AnEmptyFileIsUnavailable()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*is empty.*"));

        var result = ArticleLibrary.ParseForTests("   ");

        Assert.That(result.IsAvailable, Is.False);
        Assert.That(result.Summaries, Is.Empty);
    }

    [Test]
    public void AnEmptyArticlesArrayIsAvailableAndEmpty()
    {
        var result = ArticleLibrary.ParseForTests("{\"schemaVersion\":1,\"articles\":[]}");

        Assert.That(result.IsAvailable, Is.True,
            "a readable file with nothing in it is 'nothing authored yet' — a different " +
            "thing to tell the reader than 'could not load'");
        Assert.That(result.Summaries, Is.Empty);
    }

    [Test]
    public void ASchemaVersionFromTheFutureWarnsAndIsReadAnyway()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*schemaVersion.*"));

        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":99,\"articles\":[{\"id\":\"a\",\"title\":\"Um\"}]}");

        Assert.That(result.IsAvailable, Is.True,
            "refusing a newer file would turn a forward-compatible change into a blank screen");
        Assert.That(result.Summaries.Count, Is.EqualTo(1));
    }

    [Test]
    public void AFileWithNoSchemaVersionIsReadWithoutAWarning()
    {
        // JsonUtility cannot tell an absent int from a 0, so 0 is treated as "not
        // declared" rather than as a version this build does not know.
        var result = ArticleLibrary.ParseForTests(
            "{\"articles\":[{\"id\":\"a\",\"title\":\"Um\"}]}");

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Summaries.Count, Is.EqualTo(1));
    }

    [Test]
    public void TheSupportedSchemaVersionIsTheOneTheGeneratorWrites()
    {
        Assert.That(ArticleLibrary.SupportedSchemaVersion, Is.EqualTo(1));
        Assert.That(ArticleLibrary.ResourceName, Is.EqualTo("articles"));
    }

    // ---- Degradation: individual entries ------------------------------------

    [Test]
    public void AnArticleWithNoIdIsSkippedAndTheRestSurvive()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*no .id.*"));

        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"title\":\"Sem id\"}," +
            "{\"id\":\"  \",\"title\":\"Id em branco\"},{\"id\":\"a\",\"title\":\"Um\"}]}");

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(Ids(result), Is.EqualTo(new[] { "a" }),
            "the id is the stable key — an article without one cannot be linked to");
    }

    [Test]
    public void ADuplicateIdKeepsTheFirstEntryAndWarns()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*Duplicate article id.*"));

        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Primeiro\"}," +
            "{\"id\":\"a\",\"title\":\"Segundo\"}]}");

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Summaries.Count, Is.EqualTo(1));
        Assert.That(result.Find("a").Title, Is.EqualTo("Primeiro"), "first wins");
    }

    [Test]
    public void AnUnparseableUpdatedDateHidesTheLineRatherThanGuessingOne()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*updated.*"));

        var result = ArticleLibrary.ParseForTests(
            OneArticle(extra: ",\"updated\":\"29/09/2026\""));

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Summaries[0].Updated, Is.Null);
        Assert.That(result.Summaries[0].HasUpdated, Is.False);
    }

    [Test]
    public void ANegativeWordCountBecomesNoEstimateRatherThanANonsenseOne()
    {
        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\"," +
            "\"wordCount\":-10}]}");

        Assert.That(result.Summaries[0].WordCount, Is.Zero);
        Assert.That(result.Summaries[0].HasWordCount, Is.False);
        Assert.That(ArticleFormatter.ReadingTime(result.Summaries[0].WordCount), Is.Null);
    }

    // ---- Degradation: individual blocks -------------------------------------

    [Test]
    public void AnUnknownBlockTypeBecomesUnknownAndIsKeptButNotRenderable()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*does not understand.*"));

        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"type\":\"table\",\"text\":\"linha | linha\"}," +
            "{\"type\":\"paragraph\",\"text\":\"Isso eu sei desenhar.\"}]}]}");

        Assert.That(result.IsAvailable, Is.True);
        var article = result.Find("a");
        Assert.That(article.Blocks.Count, Is.EqualTo(2),
            "kept so diagnostics and a future build can see it — ArticleViewModel.Blocks " +
            "is where it is filtered out");
        Assert.That(article.Blocks[0].Kind, Is.EqualTo(ArticleBlockKind.Unknown));
        Assert.That(article.Blocks[0].IsRenderable, Is.False,
            "an unknown kind must render as NOTHING, not as a placeholder");
        Assert.That(article.Blocks[1].IsRenderable, Is.True,
            "one unreadable block must not cost the reader the rest of the article");
    }

    [Test]
    public void ABlockWithNoTypeBecomesUnknown()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*does not understand.*"));

        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"text\":\"órfão\"}]}]}");

        Assert.That(BlockOf(result, "a", 0).Kind, Is.EqualTo(ArticleBlockKind.Unknown));
    }

    [Test]
    public void AnUnknownCalloutToneFallsBackToInfoAndKeepsTheText()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*unreadable .tone.*"));

        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"type\":\"callout\",\"tone\":\"danger\",\"text\":\"Cuidado com a corrente.\"}]}]}");

        var block = BlockOf(result, "a", 0);
        Assert.That(block.Kind, Is.EqualTo(ArticleBlockKind.Callout));
        Assert.That(block.Tone, Is.EqualTo(ArticleCalloutTone.Info),
            "losing a tint is cosmetic; dropping the author's sentence is not, and a " +
            "guessed Error would over-state the notice");
        Assert.That(block.Text, Is.EqualTo("Cuidado com a corrente."));
        Assert.That(block.IsRenderable, Is.True);
    }

    [Test]
    public void ACalloutWithNoToneIsInfoWithoutAWarning()
    {
        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"type\":\"callout\",\"text\":\"Texto.\"}]}]}");

        Assert.That(BlockOf(result, "a", 0).Tone, Is.EqualTo(ArticleCalloutTone.Info));
    }

    [Test]
    public void ItemsOnAKindWithNoListAreDroppedAndWarned()
    {
        LogAssert.Expect(LogType.Warning, new Regex(".*has no list.*"));

        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"type\":\"paragraph\",\"text\":\"Texto.\",\"items\":[\"um\",\"dois\"]}]}]}");

        var block = BlockOf(result, "a", 0);
        Assert.That(block.Items, Is.Empty, "only bulletList and numberedList have rows");
        Assert.That(block.HasItems, Is.False);
        Assert.That(block.Text, Is.EqualTo("Texto."), "the block itself still renders");
    }

    [Test]
    public void AHeadingLevelOutsideTwoAndThreeIsClamped()
    {
        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"type\":\"heading\",\"level\":1,\"text\":\"Um\"}," +
            "{\"type\":\"heading\",\"level\":7,\"text\":\"Sete\"}," +
            "{\"type\":\"heading\",\"text\":\"Sem nível\"}]}]}");

        var article = result.Find("a");
        Assert.That(article.Blocks[0].Level, Is.EqualTo(2),
            "level 1 does not exist — the title comes from front matter");
        Assert.That(article.Blocks[1].Level, Is.EqualTo(3),
            "there is no stylesheet rule for a level 7, so it renders as the deepest one there is");
        Assert.That(article.Blocks[2].Level, Is.EqualTo(2));
    }

    [Test]
    public void LevelIsZeroOnAnythingThatIsNotAHeading()
    {
        var result = ArticleLibrary.ParseForTests(
            "{\"schemaVersion\":1,\"articles\":[{\"id\":\"a\",\"title\":\"Um\",\"blocks\":[" +
            "{\"type\":\"paragraph\",\"level\":2,\"text\":\"Texto.\"}]}]}");

        Assert.That(BlockOf(result, "a", 0).Level, Is.Zero,
            "the field is meaningless off a heading; keeping a stray value would invite a " +
            "screen to read it");
    }

    [Test]
    public void SeveralDegradationsInOneFileAllStillLeaveAUsableCatalog()
    {
        // Warnings are declared in the order Parse emits them: duplicates during the
        // loop, then the skipped-id tally, then the aggregated token reports (tones,
        // types, stray items, dates).
        LogAssert.Expect(LogType.Warning, new Regex(".*Duplicate article id.*"));
        LogAssert.Expect(LogType.Warning, new Regex(".*no .id.*"));
        LogAssert.Expect(LogType.Warning, new Regex(".*unreadable .tone.*"));
        LogAssert.Expect(LogType.Warning, new Regex(".*does not understand.*"));
        LogAssert.Expect(LogType.Warning, new Regex(".*has no list.*"));
        LogAssert.Expect(LogType.Warning, new Regex(".*updated.*"));

        const string json = @"{""schemaVersion"":1,""articles"":[
          {""id"":""a"",""title"":""Primeiro"",""order"":10,""updated"":""ontem"",""blocks"":[
            {""type"":""callout"",""tone"":""danger"",""text"":""C""},
            {""type"":""table"",""text"":""T""},
            {""type"":""quote"",""text"":""Q"",""items"":[""x""]},
            {""type"":""paragraph"",""text"":""O texto que sobra.""}]},
          {""id"":""a"",""title"":""Duplicado""},
          {""title"":""Sem id""}]}";

        var result = ArticleLibrary.ParseForTests(json);

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(Ids(result), Is.EqualTo(new[] { "a" }));
        var article = result.Find("a");
        Assert.That(article.Title, Is.EqualTo("Primeiro"));
        Assert.That(article.Updated, Is.Null);
        Assert.That(article.Blocks.Count, Is.EqualTo(4));
        Assert.That(RenderableCount(article), Is.EqualTo(3),
            "only the block this build genuinely cannot read is lost");
    }

    // ---- The real shipped file ----------------------------------------------

    [Test]
    public void REAL_FILE_TheShippedArticlesJsonParses()
    {
        // The generated Resources/articles.json is authored by a parallel workstream and
        // may not exist yet in this working copy. Ignoring (rather than failing) keeps
        // the suite green before `make articles` has ever run, while still catching a
        // real file that stops parsing once one is committed.
        var asset = Resources.Load<TextAsset>(ArticleLibrary.ResourceName);
        if (asset == null)
            Assert.Ignore($"Resources/{ArticleLibrary.ResourceName}.json is not in the project " +
                          "yet — run `make articles` to generate it. This test asserts nothing " +
                          "until then; every parser behaviour is covered by the fixtures above.");

        LogAssert.ignoreFailingMessages = true;
        ArticleLibrary.ParseResult result;
        try
        {
            result = ArticleLibrary.ParseForTests(asset.text);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
        }

        Assert.That(result.IsAvailable, Is.True,
            "the shipped file must be readable — if this fails, tools/build_articles.py and " +
            "this loader disagree about the wire format");
        Assert.That(result.Summaries.Count, Is.GreaterThan(0),
            "a committed articles.json with no articles in it is a broken generator run");

        foreach (var summary in result.Summaries)
        {
            Assert.That(summary.Id, Is.Not.Null.And.Not.Empty);
            Assert.That(summary.HasTitle, Is.True, summary.Id);
            Assert.That(summary.HasCategory, Is.True, summary.Id);
            var article = result.Find(summary.Id);
            Assert.That(article, Is.Not.Null, summary.Id);
            Assert.That(article.Blocks, Is.Not.Null, summary.Id);
        }
    }

    // ---- Helpers ------------------------------------------------------------

    static string[] Ids(ArticleLibrary.ParseResult result)
    {
        var ids = new string[result.Summaries.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = result.Summaries[i].Id;
        return ids;
    }

    static int RenderableCount(Article article)
    {
        int n = 0;
        foreach (var block in article.Blocks) if (block.IsRenderable) n++;
        return n;
    }
}
