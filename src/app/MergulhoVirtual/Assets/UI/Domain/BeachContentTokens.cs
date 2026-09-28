using System;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// The token vocabulary of <c>Assets/Resources/beaches_content.json</c> — the
    /// two fields content authors write as a single word (<c>riskLevel</c>,
    /// <c>idealTide</c>) and how they map to the enums the UI binds to.
    ///
    /// <para>Kept here rather than in the loader because it is a contract, not an
    /// implementation detail: the file documents the legal values in its own
    /// <c>riskLevelValues</c>/<c>idealTideValues</c> arrays, docs/beaches-content-todo.md
    /// repeats them for the biologists, and the ViewModel tests pin them. Parsing is
    /// forgiving on case, accents and stray whitespace (the file is hand-edited in a
    /// text editor) but never guesses: an unrecognized token is reported as such so
    /// the loader can warn, and becomes <c>Unknown</c> — i.e. "not filled in" —
    /// rather than a plausible default.</para>
    /// </summary>
    public static class BeachContentTokens
    {
        public const string RiskLow = "baixo";
        public const string RiskMedium = "medio";
        public const string RiskHigh = "alto";

        public const string TideLow = "baixa";
        public const string TideHigh = "alta";
        public const string TideAny = "qualquer";

        /// <summary>
        /// Parses a <c>riskLevel</c> token. Returns false for an unrecognized
        /// token (so callers can warn); blank is *not* an error — it is the
        /// documented "unfilled" value and yields <see cref="BeachRiskLevel.Unknown"/>
        /// with a true result.
        /// </summary>
        public static bool TryParseRisk(string token, out BeachRiskLevel level)
        {
            level = BeachRiskLevel.Unknown;
            string t = Normalize(token);
            if (t.Length == 0) return true;
            switch (t)
            {
                case RiskLow: level = BeachRiskLevel.Low; return true;
                case RiskMedium: level = BeachRiskLevel.Medium; return true;
                case RiskHigh: level = BeachRiskLevel.High; return true;
                default: return false;
            }
        }

        /// <summary>Parses an <c>idealTide</c> token; same contract as <see cref="TryParseRisk"/>.</summary>
        public static bool TryParseIdealTide(string token, out BeachIdealTide tide)
        {
            tide = BeachIdealTide.Unknown;
            string t = Normalize(token);
            if (t.Length == 0) return true;
            switch (t)
            {
                case TideLow: tide = BeachIdealTide.Low; return true;
                case TideHigh: tide = BeachIdealTide.High; return true;
                case TideAny: tide = BeachIdealTide.Any; return true;
                default: return false;
            }
        }

        /// <summary>Lowercases, trims and strips the accents a hand editor may add
        /// ("Médio" → "medio"). Only the vowels the two vocabularies can contain.</summary>
        static string Normalize(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;
            token = token.Trim().ToLowerInvariant();
            var buffer = new char[token.Length];
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                switch (c)
                {
                    case 'á': case 'à': case 'â': case 'ã': c = 'a'; break;
                    case 'é': case 'ê': c = 'e'; break;
                    case 'í': c = 'i'; break;
                    case 'ó': case 'ô': case 'õ': c = 'o'; break;
                    case 'ú': c = 'u'; break;
                    case 'ç': c = 'c'; break;
                }
                buffer[i] = c;
            }
            return new string(buffer);
        }
    }
}
