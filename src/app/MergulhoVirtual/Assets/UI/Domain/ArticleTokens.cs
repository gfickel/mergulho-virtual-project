namespace MergulhoVirtual.UI
{
    /// <summary>
    /// The token vocabulary of <c>Assets/Resources/articles.json</c> — the two fields
    /// written as a single word on the wire (a block's <c>type</c> and a callout's
    /// <c>tone</c>) and how they map to the enums the UI binds to.
    ///
    /// <para>Kept here rather than inside <c>ArticleLibrary</c> for the same reason
    /// <see cref="BeachContentTokens"/> is: it is a <b>contract</b>, not an
    /// implementation detail. <c>tools/build_articles.py</c> emits these exact strings
    /// and the authoring syntax in the content docs promises them, so the vocabulary
    /// is pinned by a test in this assembly rather than living where only the loader
    /// can see it. It also keeps the loader a file reader.</para>
    ///
    /// <para>Parsing is forgiving on case and stray whitespace, because a token can be
    /// hand-typed while debugging a generated file — but it <b>never guesses</b>. An
    /// unrecognised token is reported as such so the loader can warn, and degrades to
    /// the only honest fallback for that field: <see cref="ArticleBlockKind.Unknown"/>
    /// for a block (this build cannot render a payload it does not understand) and
    /// <see cref="ArticleCalloutTone.Info"/> for a tone (the text is still worth
    /// showing; only the tint is lost). There are no accents to strip here — every
    /// token in both vocabularies is ASCII.</para>
    /// </summary>
    public static class ArticleTokens
    {
        // Block `type` discriminators, exactly as the generator writes them. camelCase
        // for the two-word ones, matching the JSON style of the rest of the file.
        public const string TypeHeading = "heading";
        public const string TypeParagraph = "paragraph";
        public const string TypeBulletList = "bulletlist";
        public const string TypeNumberedList = "numberedlist";
        public const string TypeCallout = "callout";
        public const string TypeQuote = "quote";
        public const string TypeImage = "image";
        public const string TypeVideo = "video";
        public const string TypeSpeciesRef = "speciesref";
        public const string TypeBeachRef = "beachref";

        // Callout `tone` values — the four the authoring syntax accepts in `> [!info]`.
        public const string ToneInfo = "info";
        public const string ToneWarning = "warning";
        public const string ToneSuccess = "success";
        public const string ToneError = "error";

        /// <summary>
        /// Parses a block <c>type</c> token. Returns false for an unrecognised or
        /// blank token — <b>both</b> are worth a warning here, unlike
        /// <see cref="BeachContentTokens.TryParseRisk"/> where blank is the documented
        /// "unfilled" value: a block with no type is not an unfilled field, it is a
        /// block nothing can be done with. Either way <paramref name="kind"/> comes
        /// back as <see cref="ArticleBlockKind.Unknown"/>, which renders as nothing.
        ///
        /// <para>The comparison is case-insensitive, so the constants above are
        /// lowercase while the wire format's <c>bulletList</c> / <c>speciesRef</c>
        /// spelling still matches.</para>
        /// </summary>
        public static bool TryParseBlockKind(string token, out ArticleBlockKind kind)
        {
            kind = ArticleBlockKind.Unknown;
            string t = Normalize(token);
            if (t.Length == 0) return false;
            switch (t)
            {
                case TypeHeading: kind = ArticleBlockKind.Heading; return true;
                case TypeParagraph: kind = ArticleBlockKind.Paragraph; return true;
                case TypeBulletList: kind = ArticleBlockKind.BulletList; return true;
                case TypeNumberedList: kind = ArticleBlockKind.NumberedList; return true;
                case TypeCallout: kind = ArticleBlockKind.Callout; return true;
                case TypeQuote: kind = ArticleBlockKind.Quote; return true;
                case TypeImage: kind = ArticleBlockKind.Image; return true;
                case TypeVideo: kind = ArticleBlockKind.Video; return true;
                case TypeSpeciesRef: kind = ArticleBlockKind.SpeciesRef; return true;
                case TypeBeachRef: kind = ArticleBlockKind.BeachRef; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Parses a callout <c>tone</c> token. Blank is <b>not</b> an error — the
        /// authoring form <c>&gt; [!info]</c> is the only way to write a callout, so an
        /// absent tone means the generator omitted a default it considered redundant —
        /// and yields <see cref="ArticleCalloutTone.Info"/> with a true result. An
        /// unrecognised token returns false (the loader warns) and also yields
        /// <c>Info</c>: the neutral tint cannot over- or under-state a notice, which a
        /// guessed <c>Error</c> would.
        /// </summary>
        public static bool TryParseCalloutTone(string token, out ArticleCalloutTone tone)
        {
            tone = ArticleCalloutTone.Info;
            string t = Normalize(token);
            if (t.Length == 0) return true;
            switch (t)
            {
                case ToneInfo: tone = ArticleCalloutTone.Info; return true;
                case ToneWarning: tone = ArticleCalloutTone.Warning; return true;
                case ToneSuccess: tone = ArticleCalloutTone.Success; return true;
                case ToneError: tone = ArticleCalloutTone.Error; return true;
                default: return false;
            }
        }

        /// <summary>Lowercases and trims. No accent folding: both vocabularies are
        /// ASCII, and inventing a fold here would only make a typo match something.</summary>
        static string Normalize(string token) =>
            string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim().ToLowerInvariant();
    }
}
