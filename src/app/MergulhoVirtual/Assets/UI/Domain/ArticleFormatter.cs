using System;
using System.Globalization;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// pt-BR copy for the educational-content feature — the "Conteúdo educativo"
    /// index and the reading screen.
    ///
    /// <para>Same contract as <see cref="ConditionsFormatter"/>,
    /// <see cref="BeachContentFormatter"/> and <see cref="ReportFormatter"/>: plain
    /// static C#, no UnityEngine, no state, every clock value passed in by the caller.
    /// <b>Screens must not build user-visible strings themselves</b> — every pt-BR
    /// string this feature can show exists here (or, for the state view, in
    /// <see cref="StateViewCopy"/>, which this type selects from) and is pinned by a
    /// test.</para>
    ///
    /// <para>Two return conventions, deliberately different, and this file leans
    /// hard on the first: <b>null</b> means "there is nothing to say, hide this
    /// element" — a reading time with no word count, an "Atualizado em" with no date,
    /// a category heading with no category. <b>"—"</b>
    /// (<see cref="ConditionsFormatter.NoValue"/>) is only for a slot that always
    /// occupies a column, and this feature has none: an article page is a stack of
    /// blocks, so an absent line simply is not drawn. <b>Nothing here is ever
    /// invented</b> — see <see cref="ReadingTime"/> for the one derived number and why
    /// it refuses to print zero.</para>
    ///
    /// <para><b>Most of the copy below has no Figma frame behind it.</b> V2's frame
    /// inventory has no educational-content screen at all, so the index title, the
    /// subtitle, the callout tone labels and the quote prefix are designed against the
    /// language of the other screens rather than transcribed. Each is flagged
    /// <i>invented</i> at its declaration; they want a designer's eyes, and they are
    /// all one-liners to change.</para>
    /// </summary>
    public static class ArticleFormatter
    {
        /// <summary>Shared with the conditions rows — kept so a caller never has to
        /// reach into another formatter for the dash.</summary>
        public const string NoValue = ConditionsFormatter.NoValue;

        /// <summary>The middle dot the conditions rows and the report feed already
        /// use, so a metadata line here reads the same as one there.</summary>
        public const string Separator = ReportFormatter.Separator;

        // ---- Index chrome ---------------------------------------------------

        /// <summary>
        /// The index screen's headline. <b>Invented copy</b>, but not freely: it is the
        /// feature's own name from <c>docs/educational-content-options.md</c>, so the
        /// screen, the Início entry point and the docs all say the same thing.
        /// </summary>
        public const string IndexTitle = "Conteúdo educativo";

        /// <summary>
        /// One line under the headline. <b>Invented copy.</b> Deliberately descriptive
        /// rather than promotional — it says what the list contains so a reader can tell
        /// at a glance whether it is for them, and it names the island because every
        /// other screen's copy is local ("Fernando de Noronha, PE").
        /// </summary>
        public const string IndexSubtitle = "Espécies, praias e boas práticas de Fernando de Noronha.";

        /// <summary>
        /// Label of whatever control opens this screen from elsewhere (the Início
        /// card, today). <b>Invented copy</b>, and shorter than
        /// <see cref="IndexTitle"/> on purpose: a card title has less room than a
        /// screen headline.
        /// </summary>
        public const string EntryPointLabel = "Conteúdo educativo";

        // ---- Reading time ---------------------------------------------------

        /// <summary>
        /// Assumed silent-reading speed, in words per minute, for the "N min de
        /// leitura" estimate.
        ///
        /// <para><b>200 is an assumption, not a measurement of this audience.</b> It
        /// is the middle of the range usually quoted for adult silent reading of
        /// non-technical prose (roughly 180–260 wpm), taken at the low end because the
        /// articles carry marine-biology vocabulary and are read on a phone, often
        /// outdoors. Nobody has timed a reader of this app. The number only ever
        /// coarsens into whole minutes, so it would take a ±25 % error to move a
        /// typical article's estimate by one minute — which is the argument for
        /// publishing the estimate at all, and also the reason not to dress it up with
        /// a decimal.</para>
        /// </summary>
        public const int WordsPerMinute = 200;

        /// <summary>Suffix of the reading estimate; the number is prefixed to it.</summary>
        public const string ReadingTimeSuffix = " min de leitura";

        /// <summary>
        /// "5 min de leitura", or <b>null</b> when there is nothing to estimate from.
        ///
        /// <para><b>A derived value, so it is held to the honesty rule.</b> A word count
        /// of 0 means the body is empty or was never counted, and "0 min de leitura"
        /// would assert a fact about an article nobody can read — so the element hides
        /// instead. Anything above zero rounds <i>up</i> to at least 1 minute: a
        /// 40-word note is not "0 min", and rounding down would let the estimate
        /// under-promise in the one direction a reader notices.</para>
        /// </summary>
        public static string ReadingTime(int wordCount)
        {
            if (wordCount <= 0) return null;
            int minutes = (wordCount + WordsPerMinute - 1) / WordsPerMinute;
            if (minutes < 1) minutes = 1;
            return minutes.ToString(CultureInfo.InvariantCulture) + ReadingTimeSuffix;
        }

        // ---- Updated date ---------------------------------------------------

        public const string UpdatedPrefix = "Atualizado em ";

        /// <summary>
        /// pt-BR month names, lowercase as pt-BR writes them mid-sentence.
        ///
        /// <para><b>Hand-written rather than taken from a <c>CultureInfo("pt-BR")</c></b>,
        /// which is the rule the whole repo follows: IL2CPP can strip culture data out
        /// of a player build, so a date that renders correctly in the editor would come
        /// back in English — or throw — on a device. Every other formatter here formats
        /// against <see cref="CultureInfo.InvariantCulture"/> for the same reason.</para>
        /// </summary>
        static readonly string[] MonthNames =
        {
            "janeiro", "fevereiro", "março", "abril", "maio", "junho",
            "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
        };

        /// <summary>
        /// "Atualizado em 29 de setembro de 2026", or <b>null</b> when the author
        /// omitted the date — the line then hides rather than claiming a revision date
        /// nobody wrote down.
        ///
        /// <para>Spelled out rather than <c>29/09/2026</c>: this is a once-per-page
        /// footnote with room for words, unlike the report feed's dense
        /// <c>"27/09, 7:12"</c> rows. The date is a <b>calendar date</b>
        /// (<see cref="ArticleSummary.Updated"/> is deliberately kind-unspecified), so
        /// there is no clock and no timezone conversion in this path — converting it
        /// would move it across midnight for half the world.</para>
        /// </summary>
        public static string UpdatedOn(DateTime? updated)
        {
            if (!updated.HasValue) return null;
            var d = updated.Value;
            if (d.Month < 1 || d.Month > 12) return null;
            return string.Format(CultureInfo.InvariantCulture, "{0}{1} de {2} de {3}",
                UpdatedPrefix, d.Day, MonthNames[d.Month - 1], d.Year);
        }

        /// <summary>The month name alone, for a caller that needs its own phrasing.
        /// Null for a month out of range.</summary>
        public static string MonthName(int month) =>
            month >= 1 && month <= 12 ? MonthNames[month - 1] : null;

        // ---- Index grouping -------------------------------------------------

        /// <summary>
        /// The heading over one category group — the author's own category text,
        /// trimmed. <b>Null when it is blank</b>, in which case the group's cards still
        /// render and simply carry no heading: the category is required by the
        /// authoring contract, so a blank one is a content bug, and swallowing the
        /// articles would hide it from the reader as well as the author.
        ///
        /// <para>Deliberately <i>not</i> normalised, title-cased or translated. The
        /// category is free pt-BR text and is simultaneously the grouping key and the
        /// visible heading, so re-casing it here would make the heading disagree with
        /// what the author typed.</para>
        /// </summary>
        public static string CategoryHeading(string category) =>
            string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        /// <summary>
        /// "12 conteúdos" / "1 conteúdo", or <b>null</b> for zero — at zero the screen
        /// shows its empty state (<see cref="StateViewCopy.ArticlesEmptyBody"/>), and a
        /// "0 conteúdos" caption above it would say the same thing twice. A negative
        /// count is treated as zero rather than printed.
        /// </summary>
        public static string ArticleCount(int count)
        {
            if (count <= 0) return null;
            return count == 1
                ? "1 conteúdo"
                : count.ToString(CultureInfo.InvariantCulture) + " conteúdos";
        }

        // ---- State view (empty / could-not-load) ----------------------------
        // The strings live in StateViewCopy with the rest of MvStateView's copy; these
        // are the selectors, so a screen has one formatter to bind against. Same
        // aliasing ReportFormatter.RetryLabel does with StateViewCopy.RetryAction.

        /// <summary>Title of the index's state view. <paramref name="unavailable"/> is
        /// <c>IArticleCatalog.IsAvailable</c>, inverted.</summary>
        public static string StateTitle(bool unavailable) => StateViewCopy.ArticlesTitle(unavailable);

        /// <summary>Body of the index's state view.</summary>
        public static string StateBody(bool unavailable) => StateViewCopy.ArticlesBody(unavailable);

        /// <summary>
        /// Action label for the index's state view — <b>always null</b>, i.e. the state
        /// view draws no button.
        ///
        /// <para>Neither state is retryable and saying otherwise would be a lie the user
        /// pays for with a tap. <c>articles.json</c> ships inside the APK: an empty
        /// catalog is content work that no amount of retrying produces, and a missing or
        /// malformed file is a broken build that will be just as missing a second time.
        /// Compare the conditions card, where "Tentar novamente" is real because there
        /// is a network fetch behind it. The method exists rather than the screen simply
        /// knowing, so that if a reason to retry ever appears it appears here.</para>
        /// </summary>
        public static string StateActionLabel(bool unavailable) => null;

        // ---- Block chrome ---------------------------------------------------

        /// <summary>
        /// Accessible name of a callout, per tone — "Informação", "Atenção",
        /// "Recomendação", "Perigo".
        ///
        /// <para><b>Invented copy: V2 has no callout frame</b>, so these are not
        /// transcribed from anything. They are chosen to name the <i>kind</i> of notice
        /// rather than restate its content, because the tint is the only other thing
        /// carrying that distinction and a tint is invisible to a screen reader (and to
        /// a colour-blind reader). "Perigo" for the error tone is stronger than
        /// "Atenção" on purpose — in a marine-safety article the two tones mean
        /// genuinely different things, and collapsing them would waste the distinction
        /// the author reached for.</para>
        ///
        /// <para>Returns null for a tone outside the enum, which cannot happen through
        /// the loader (an unreadable token becomes <see cref="ArticleCalloutTone.Info"/>)
        /// but can through a cast.</para>
        /// </summary>
        public static string CalloutToneLabel(ArticleCalloutTone tone)
        {
            switch (tone)
            {
                case ArticleCalloutTone.Info: return "Informação";
                case ArticleCalloutTone.Warning: return "Atenção";
                case ArticleCalloutTone.Success: return "Recomendação";
                case ArticleCalloutTone.Error: return "Perigo";
                default: return null;
            }
        }

        /// <summary>
        /// The dash a quote's attribution is introduced with — an em dash and a space.
        /// <b>Not transcribed from a design</b> (there is no quote frame); it is the
        /// plain typographic convention, and the same one the authoring syntax asks for
        /// (<c>&gt; — Autor</c>), which the build script strips so the app owns the
        /// presentation.
        /// </summary>
        public const string QuoteAttributionPrefix = "— ";

        /// <summary>
        /// "— Ana Silva", or <b>null</b> when the quote is unattributed — an
        /// unattributed pull quote is legitimate, and a bare dash on its own line is
        /// not. Text the author already prefixed with a dash is not double-dashed.
        /// </summary>
        public static string QuoteAttribution(string attribution)
        {
            if (string.IsNullOrWhiteSpace(attribution)) return null;
            string trimmed = attribution.Trim();
            if (trimmed.StartsWith(QuoteAttributionPrefix, StringComparison.Ordinal)) return trimmed;
            // Also tolerate the ASCII hyphen and the en dash a text editor may produce.
            if (trimmed.Length > 1 && (trimmed[0] == '-' || trimmed[0] == '–' || trimmed[0] == '—'))
                return QuoteAttributionPrefix + trimmed.Substring(1).TrimStart();
            return QuoteAttributionPrefix + trimmed;
        }

        /// <summary>
        /// "1.", "2." — the visible marker of a <see cref="ArticleBlockKind.NumberedList"/>
        /// row. Numbered by the app, not by the author, so a row inserted mid-list
        /// cannot leave the sequence wrong.
        ///
        /// <para>The dot is the difference from
        /// <see cref="BeachContentFormatter.TipNumber"/>, which is bare because
        /// <c>MvNumberedList</c> draws its numbers inside circles. An article's
        /// numbered list is inline text, where a bare digit reads as a typo.</para>
        /// </summary>
        public static string ListNumber(int index) =>
            (index + 1).ToString(CultureInfo.InvariantCulture) + ".";

        /// <summary>The bullet glyph of a <see cref="ArticleBlockKind.BulletList"/> row —
        /// U+2022, the same mark the rest of the app's prose uses.</summary>
        public const string BulletMarker = "•";

        /// <summary>
        /// An image's caption, verbatim and trimmed; null hides the line. A decorative
        /// image legitimately has none.
        /// </summary>
        public static string Caption(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

        /// <summary>
        /// An attribution line, verbatim and trimmed; null hides it. Verbatim because a
        /// credit is a <b>licence condition</b> — the author name and the licence
        /// identifier are the parts that satisfy it, so this never shortens, reflows or
        /// re-words one. It also never gets <c>opacity</c> at the screen.
        ///
        /// <para>Delegates to <see cref="SpeciesMediaFormatter.Credit"/> so the
        /// project has exactly one answer to "how is a credit rendered", shared with the
        /// species pages.</para>
        /// </summary>
        public static string Credit(string raw) => SpeciesMediaFormatter.Credit(raw);

        /// <summary>
        /// A video card's caption, or null when the clip was authored without one — the
        /// card then shows its play control alone. Shared with the species pages'
        /// clips, deliberately.
        /// </summary>
        public static string VideoTitle(string raw) => SpeciesMediaFormatter.VideoTitle(raw);

        /// <summary>The play control's label on a video card, shared verbatim with the
        /// species pages ("Assistir").</summary>
        public const string WatchLabel = SpeciesMediaFormatter.WatchLabel;

        // ---- Video player state copy ----------------------------------------
        //
        // Five aliases of SpeciesMediaFormatter's player copy, shared DELIBERATELY
        // rather than re-worded: an article's inline clip and a species page's inline
        // clip are the same control on the same player, and two spellings of "Pausar"
        // would teach a reader they are two different things.
        //
        // They exist here at all because ArticleViewModel — unlike EspecieViewModel —
        // holds no playback state: an article body can carry any number of video
        // blocks and the reader identifies the playing one by URL off
        // IVideoPlayback.Url, which is that interface's documented contract. So the
        // screen is the one asking "what does this control say for this state", and
        // rule 2 says it may not answer for itself. Aliasing keeps the answer in one
        // place without adding state to a frozen ViewModel.

        /// <summary>Label on a clip's action control for a playback state
        /// ("Assistir" / "Carregando…" / "Pausar" / "Continuar" / "Tentar de novo").</summary>
        public static string VideoActionLabel(VideoPlaybackState state) =>
            SpeciesMediaFormatter.ActionLabel(state);

        /// <summary>Material Symbols name for the same control. Paired with
        /// <see cref="VideoActionLabel"/> at the source so the glyph and the word can
        /// never disagree.</summary>
        public static string VideoActionIcon(VideoPlaybackState state) =>
            SpeciesMediaFormatter.ActionIcon(state);

        /// <summary>"0:42 / 2:15", or null while the duration is unknown — which it is
        /// until the stream is prepared, and forever on a failed one.</summary>
        public static string VideoTimeText(double positionSeconds, double durationSeconds) =>
            SpeciesMediaFormatter.PlaybackTime(positionSeconds, durationSeconds);

        /// <summary>Fraction of the clip played, 0–1, for the seek track's fill.</summary>
        public static float VideoProgress(double positionSeconds, double durationSeconds) =>
            SpeciesMediaFormatter.Progress(positionSeconds, durationSeconds);

        /// <summary>The one line a failed stream shows. Worded as a network problem
        /// because that is what it nearly always is; on a Linux editor it is the ONLY
        /// state, since Unity cannot decode H.264 there.</summary>
        public const string VideoErrorText = SpeciesMediaFormatter.ErrorText;

        // ---- Cross-references -----------------------------------------------

        /// <summary>
        /// Label of a <see cref="ArticleBlockKind.SpeciesRef"/> block's link.
        /// <b>Deliberately the same string</b> as
        /// <c>BeachDetailViewModel.SpeciesLearnMoreLabel</c> — both open the same Espécie
        /// screen, and two wordings for one destination teach the reader that they are
        /// two different things. Written out rather than referenced so a Domain type does
        /// not depend on a ViewModel; if one changes, change both.
        /// </summary>
        public const string SpeciesRefLabel = "Saiba mais sobre a espécie";

        /// <summary>
        /// Label of a <see cref="ArticleBlockKind.BeachRef"/> block's link.
        /// <b>Invented copy</b>, phrased to match <see cref="SpeciesRefLabel"/>'s shape
        /// so a pair of references in one article reads as a pair.
        /// </summary>
        public const string BeachRefLabel = "Saiba mais sobre a praia";

        // ---- Helpers --------------------------------------------------------

        /// <summary>
        /// Joins the two metadata halves a card shows ("5 min de leitura · Espécies"),
        /// dropping whichever is absent so no stray separator is left behind. Null when
        /// both are absent.
        /// </summary>
        public static string MetaLine(string left, string right)
        {
            bool hasLeft = !string.IsNullOrWhiteSpace(left);
            bool hasRight = !string.IsNullOrWhiteSpace(right);
            if (hasLeft && hasRight) return left.Trim() + Separator + right.Trim();
            if (hasLeft) return left.Trim();
            if (hasRight) return right.Trim();
            return null;
        }

        /// <summary>The value, or "—" when unfilled. For a fixed slot that always
        /// occupies a column; this feature has none today, and it is here only so a
        /// future stat row does not reinvent it.</summary>
        public static string OrNoValue(string value) =>
            string.IsNullOrWhiteSpace(value) ? NoValue : value.Trim();
    }
}
