using System;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// Every pt-BR string the educational-content feature can show, pinned the way
    /// <see cref="BeachContentFormatterTests"/> pins the Praias copy: change them here
    /// or not at all.
    ///
    /// <para>The null-returns are pinned as hard as the strings, because they are the
    /// feature's honesty rules rather than incidental behaviour: a reading estimate with
    /// no word count behind it, an "Atualizado em" with no date, a retry button for a
    /// state that cannot be retried. Each of those would be a made-up claim if it
    /// rendered, so each has its own assertion with the reason on it.</para>
    /// </summary>
    public class ArticleFormatterTests
    {
        // ---- Index chrome ---------------------------------------------------

        [Test]
        public void IndexChromeIsTheFeatureName()
        {
            Assert.That(ArticleFormatter.IndexTitle, Is.EqualTo("Conteúdo educativo"));
            Assert.That(ArticleFormatter.IndexSubtitle,
                Is.EqualTo("Espécies, praias e boas práticas de Fernando de Noronha."));
            Assert.That(ArticleFormatter.EntryPointLabel, Is.EqualTo("Conteúdo educativo"));
        }

        // ---- Reading time ---------------------------------------------------

        [Test]
        public void ReadingTimeRoundsUpToWholeMinutes()
        {
            Assert.That(ArticleFormatter.WordsPerMinute, Is.EqualTo(200),
                "the assumed reading speed is the whole basis of the estimate — moving it " +
                "changes every article's label, so it changes here deliberately or not at all");

            Assert.That(ArticleFormatter.ReadingTime(200), Is.EqualTo("1 min de leitura"));
            Assert.That(ArticleFormatter.ReadingTime(201), Is.EqualTo("2 min de leitura"));
            Assert.That(ArticleFormatter.ReadingTime(400), Is.EqualTo("2 min de leitura"));
            Assert.That(ArticleFormatter.ReadingTime(412), Is.EqualTo("3 min de leitura"));
            Assert.That(ArticleFormatter.ReadingTime(1000), Is.EqualTo("5 min de leitura"));
        }

        [Test]
        public void ReadingTimeNeverRoundsDownToZero()
        {
            Assert.That(ArticleFormatter.ReadingTime(1), Is.EqualTo("1 min de leitura"),
                "a 1-word body is not '0 min' — anything readable is at least a minute");
            Assert.That(ArticleFormatter.ReadingTime(40), Is.EqualTo("1 min de leitura"));
        }

        [Test]
        public void ReadingTimeIsNullWhenThereIsNothingToEstimateFrom()
        {
            Assert.That(ArticleFormatter.ReadingTime(0), Is.Null,
                "a derived value must not assert a fact the data does not support — " +
                "'0 min de leitura' claims something about an article nobody can read");
            Assert.That(ArticleFormatter.ReadingTime(-5), Is.Null);
        }

        // ---- Updated date ---------------------------------------------------

        [Test]
        public void UpdatedOnSpellsThePtBrMonth()
        {
            Assert.That(ArticleFormatter.UpdatedOn(new DateTime(2026, 9, 29)),
                Is.EqualTo("Atualizado em 29 de setembro de 2026"));
            Assert.That(ArticleFormatter.UpdatedOn(new DateTime(2026, 3, 1)),
                Is.EqualTo("Atualizado em 1 de março de 2026"),
                "the day is not zero-padded, and março keeps its cedilla");
            Assert.That(ArticleFormatter.UpdatedOn(new DateTime(2025, 12, 31)),
                Is.EqualTo("Atualizado em 31 de dezembro de 2025"));
        }

        [Test]
        public void UpdatedOnIsNullWithoutADate()
        {
            Assert.That(ArticleFormatter.UpdatedOn(null), Is.Null,
                "the `updated` key is optional — the line hides rather than guessing a date");
        }

        [Test]
        public void UpdatedOnIgnoresTheTimeOfDay()
        {
            // ArticleSummary.Updated is a calendar date with DateTimeKind.Unspecified on
            // purpose. Nothing in this path may convert it, so a value that happens to
            // carry a time must still print the same day it names.
            var midnight = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Unspecified);
            var lateEvening = new DateTime(2026, 9, 29, 23, 59, 0, DateTimeKind.Unspecified);
            Assert.That(ArticleFormatter.UpdatedOn(midnight),
                Is.EqualTo(ArticleFormatter.UpdatedOn(lateEvening)));
        }

        [Test]
        public void MonthNamesAreLowercasePtBr()
        {
            Assert.That(ArticleFormatter.MonthName(1), Is.EqualTo("janeiro"));
            Assert.That(ArticleFormatter.MonthName(2), Is.EqualTo("fevereiro"));
            Assert.That(ArticleFormatter.MonthName(3), Is.EqualTo("março"));
            Assert.That(ArticleFormatter.MonthName(4), Is.EqualTo("abril"));
            Assert.That(ArticleFormatter.MonthName(5), Is.EqualTo("maio"));
            Assert.That(ArticleFormatter.MonthName(6), Is.EqualTo("junho"));
            Assert.That(ArticleFormatter.MonthName(7), Is.EqualTo("julho"));
            Assert.That(ArticleFormatter.MonthName(8), Is.EqualTo("agosto"));
            Assert.That(ArticleFormatter.MonthName(9), Is.EqualTo("setembro"));
            Assert.That(ArticleFormatter.MonthName(10), Is.EqualTo("outubro"));
            Assert.That(ArticleFormatter.MonthName(11), Is.EqualTo("novembro"));
            Assert.That(ArticleFormatter.MonthName(12), Is.EqualTo("dezembro"));
            Assert.That(ArticleFormatter.MonthName(0), Is.Null);
            Assert.That(ArticleFormatter.MonthName(13), Is.Null);
        }

        // ---- Index grouping -------------------------------------------------

        [Test]
        public void CategoryHeadingIsTheAuthorsOwnText()
        {
            Assert.That(ArticleFormatter.CategoryHeading("Espécies"), Is.EqualTo("Espécies"));
            Assert.That(ArticleFormatter.CategoryHeading("  Boas práticas  "), Is.EqualTo("Boas práticas"),
                "trimmed, but never re-cased — the heading must read as the author typed it");
            Assert.That(ArticleFormatter.CategoryHeading("boas práticas"), Is.EqualTo("boas práticas"));
        }

        [Test]
        public void CategoryHeadingIsNullWhenBlank()
        {
            Assert.That(ArticleFormatter.CategoryHeading(null), Is.Null);
            Assert.That(ArticleFormatter.CategoryHeading("   "), Is.Null);
        }

        [Test]
        public void ArticleCountAgreesWithPortuguesePlurals()
        {
            Assert.That(ArticleFormatter.ArticleCount(1), Is.EqualTo("1 conteúdo"));
            Assert.That(ArticleFormatter.ArticleCount(2), Is.EqualTo("2 conteúdos"));
            Assert.That(ArticleFormatter.ArticleCount(12), Is.EqualTo("12 conteúdos"));
        }

        [Test]
        public void ArticleCountIsNullAtZero()
        {
            Assert.That(ArticleFormatter.ArticleCount(0), Is.Null,
                "at zero the state view already says it — a '0 conteúdos' caption above it " +
                "would say the same thing twice");
            Assert.That(ArticleFormatter.ArticleCount(-3), Is.Null);
        }

        // ---- State view -----------------------------------------------------

        [Test]
        public void StateCopyTellsTheTwoFailuresApart()
        {
            Assert.That(ArticleFormatter.StateTitle(unavailable: false),
                Is.EqualTo("Nada por aqui ainda"));
            Assert.That(ArticleFormatter.StateBody(unavailable: false),
                Is.EqualTo("Os conteúdos educativos ainda estão sendo preparados."));

            Assert.That(ArticleFormatter.StateTitle(unavailable: true),
                Is.EqualTo(StateViewCopy.ErrorTitle));
            Assert.That(ArticleFormatter.StateBody(unavailable: true),
                Is.EqualTo("Não conseguimos carregar os conteúdos educativos."));

            Assert.That(ArticleFormatter.StateBody(unavailable: true),
                Is.Not.EqualTo(ArticleFormatter.StateBody(unavailable: false)),
                "an empty library and a broken build are different things to tell a reader");
        }

        [Test]
        public void StateCopyLivesInStateViewCopy()
        {
            // The strings live with the rest of MvStateView's copy; the formatter only
            // selects. Pinned so nobody re-homes half of it and leaves two spellings.
            Assert.That(ArticleFormatter.StateTitle(true), Is.EqualTo(StateViewCopy.ArticlesTitle(true)));
            Assert.That(ArticleFormatter.StateTitle(false), Is.EqualTo(StateViewCopy.ArticlesTitle(false)));
            Assert.That(ArticleFormatter.StateBody(true), Is.EqualTo(StateViewCopy.ArticlesBody(true)));
            Assert.That(ArticleFormatter.StateBody(false), Is.EqualTo(StateViewCopy.ArticlesBody(false)));
        }

        [Test]
        public void StateViewOffersNoRetryBecauseNeitherStateIsRetryable()
        {
            Assert.That(ArticleFormatter.StateActionLabel(unavailable: true), Is.Null,
                "articles.json ships in the APK — a missing file is just as missing on the " +
                "second tap, so offering 'Tentar novamente' would be a lie the user pays for");
            Assert.That(ArticleFormatter.StateActionLabel(unavailable: false), Is.Null,
                "an empty library is content work; no amount of retrying authors an article");
        }

        [Test]
        public void ThereIsNoOfflineArticleCopy()
        {
            // articles.json is a shipped Resource, so "offline" is not a cause this
            // surface can have evidence for. If someone adds an offline body later, this
            // is the test that should make them justify it.
            Assert.That(ArticleFormatter.StateBody(unavailable: true),
                Is.Not.EqualTo(StateViewCopy.ConditionsOfflineBody));
            Assert.That(ArticleFormatter.StateTitle(unavailable: true),
                Is.Not.EqualTo(StateViewCopy.OfflineTitle));
        }

        // ---- Callouts -------------------------------------------------------

        [Test]
        public void CalloutToneLabelsNameTheKindOfNotice()
        {
            Assert.That(ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Info),
                Is.EqualTo("Informação"));
            Assert.That(ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Warning),
                Is.EqualTo("Atenção"));
            Assert.That(ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Success),
                Is.EqualTo("Recomendação"));
            Assert.That(ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Error),
                Is.EqualTo("Perigo"));
        }

        [Test]
        public void CalloutToneLabelsAreAllDistinct()
        {
            // The tint is the only other thing carrying the distinction, and a tint is
            // invisible to a screen reader — so two tones sharing a label would lose it.
            var labels = new[]
            {
                ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Info),
                ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Warning),
                ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Success),
                ArticleFormatter.CalloutToneLabel(ArticleCalloutTone.Error),
            };
            Assert.That(labels, Is.Unique);
        }

        [Test]
        public void CalloutToneLabelIsNullForAToneOutsideTheEnum()
        {
            Assert.That(ArticleFormatter.CalloutToneLabel((ArticleCalloutTone)99), Is.Null);
        }

        // ---- Quotes ---------------------------------------------------------

        [Test]
        public void QuoteAttributionAddsTheEmDash()
        {
            Assert.That(ArticleFormatter.QuoteAttribution("Ana Silva"), Is.EqualTo("— Ana Silva"));
            Assert.That(ArticleFormatter.QuoteAttribution("  Ana Silva  "), Is.EqualTo("— Ana Silva"));
        }

        [Test]
        public void QuoteAttributionDoesNotDoubleADashTheAuthorAlreadyTyped()
        {
            Assert.That(ArticleFormatter.QuoteAttribution("— Ana Silva"), Is.EqualTo("— Ana Silva"));
            Assert.That(ArticleFormatter.QuoteAttribution("- Ana Silva"), Is.EqualTo("— Ana Silva"),
                "an ASCII hyphen is normalised to the em dash, not stacked behind one");
            Assert.That(ArticleFormatter.QuoteAttribution("– Ana Silva"), Is.EqualTo("— Ana Silva"),
                "an en dash likewise");
        }

        [Test]
        public void QuoteAttributionIsNullWhenUnattributed()
        {
            Assert.That(ArticleFormatter.QuoteAttribution(null), Is.Null,
                "an unattributed pull quote is legitimate — a bare dash on its own line is not");
            Assert.That(ArticleFormatter.QuoteAttribution("   "), Is.Null);
        }

        // ---- List markers ---------------------------------------------------

        [Test]
        public void ListNumbersAreOneBasedAndCarryTheDot()
        {
            Assert.That(ArticleFormatter.ListNumber(0), Is.EqualTo("1."));
            Assert.That(ArticleFormatter.ListNumber(9), Is.EqualTo("10."));
            Assert.That(ArticleFormatter.ListNumber(0), Is.Not.EqualTo(BeachContentFormatter.TipNumber(0)),
                "an article's inline list keeps the dot; MvNumberedList's circled digits do not");
        }

        [Test]
        public void BulletMarkerIsTheProseBullet()
        {
            Assert.That(ArticleFormatter.BulletMarker, Is.EqualTo("•"));
        }

        // ---- Captions, credits, clips ----------------------------------------

        [Test]
        public void CaptionIsVerbatimOrNull()
        {
            Assert.That(ArticleFormatter.Caption("  Tubarões no Sancho  "), Is.EqualTo("Tubarões no Sancho"));
            Assert.That(ArticleFormatter.Caption(null), Is.Null);
            Assert.That(ArticleFormatter.Caption("  "), Is.Null,
                "a decorative image legitimately has no caption");
        }

        [Test]
        public void CreditIsVerbatimAndSharedWithTheSpeciesPages()
        {
            const string raw = "Foto: Albert Kok / CC BY-SA 3.0 (Wikimedia Commons)";
            Assert.That(ArticleFormatter.Credit(raw), Is.EqualTo(raw),
                "a credit is a licence condition — never shortened, reflowed or re-worded");
            Assert.That(ArticleFormatter.Credit(raw), Is.EqualTo(SpeciesMediaFormatter.Credit(raw)),
                "one project answer to 'how is a credit rendered'");
            Assert.That(ArticleFormatter.Credit(null), Is.Null);
        }

        [Test]
        public void VideoCopyIsSharedWithTheSpeciesPages()
        {
            Assert.That(ArticleFormatter.VideoTitle("  Berçário de tubarões  "),
                Is.EqualTo(SpeciesMediaFormatter.VideoTitle("  Berçário de tubarões  ")));
            Assert.That(ArticleFormatter.VideoTitle(null), Is.Null);
            Assert.That(ArticleFormatter.WatchLabel, Is.EqualTo(SpeciesMediaFormatter.WatchLabel));
        }

        // ---- Cross-references -----------------------------------------------

        [Test]
        public void CrossReferenceLabelsMatchTheRestOfTheApp()
        {
            Assert.That(ArticleFormatter.SpeciesRefLabel, Is.EqualTo("Saiba mais sobre a espécie"));
            Assert.That(ArticleFormatter.SpeciesRefLabel,
                Is.EqualTo(BeachDetailViewModel.SpeciesLearnMoreLabel),
                "both links open the same Espécie screen — two wordings would teach the " +
                "reader they are two different destinations");
            Assert.That(ArticleFormatter.BeachRefLabel, Is.EqualTo("Saiba mais sobre a praia"));
        }

        // ---- Helpers ---------------------------------------------------------

        [Test]
        public void MetaLineDropsTheMissingHalfAndItsSeparator()
        {
            Assert.That(ArticleFormatter.MetaLine("3 min de leitura", "Espécies"),
                Is.EqualTo("3 min de leitura · Espécies"));
            Assert.That(ArticleFormatter.MetaLine("3 min de leitura", null),
                Is.EqualTo("3 min de leitura"));
            Assert.That(ArticleFormatter.MetaLine(null, "Espécies"), Is.EqualTo("Espécies"));
            Assert.That(ArticleFormatter.MetaLine(null, null), Is.Null);
            Assert.That(ArticleFormatter.MetaLine("  ", "  "), Is.Null);
        }

        [Test]
        public void SeparatorIsTheSameMiddleDotTheRestOfTheAppUses()
        {
            Assert.That(ArticleFormatter.Separator, Is.EqualTo(" · "));
            Assert.That(ArticleFormatter.Separator, Is.EqualTo(ReportFormatter.Separator));
        }

        [Test]
        public void OrNoValueUsesTheSharedDash()
        {
            Assert.That(ArticleFormatter.NoValue, Is.EqualTo("—"));
            Assert.That(ArticleFormatter.NoValue, Is.EqualTo(ConditionsFormatter.NoValue));
            Assert.That(ArticleFormatter.OrNoValue("  x  "), Is.EqualTo("x"));
            Assert.That(ArticleFormatter.OrNoValue(null), Is.EqualTo("—"));
        }

        // ---- Token vocabulary ------------------------------------------------
        // ArticleTokens is a contract (build_articles.py emits these exact strings), so
        // it is pinned here alongside the copy rather than only inside the loader.

        [Test]
        public void EveryBlockTypeTokenParses()
        {
            AssertKind("heading", ArticleBlockKind.Heading);
            AssertKind("paragraph", ArticleBlockKind.Paragraph);
            AssertKind("bulletList", ArticleBlockKind.BulletList);
            AssertKind("numberedList", ArticleBlockKind.NumberedList);
            AssertKind("callout", ArticleBlockKind.Callout);
            AssertKind("quote", ArticleBlockKind.Quote);
            AssertKind("image", ArticleBlockKind.Image);
            AssertKind("video", ArticleBlockKind.Video);
            AssertKind("speciesRef", ArticleBlockKind.SpeciesRef);
            AssertKind("beachRef", ArticleBlockKind.BeachRef);
        }

        [Test]
        public void BlockTypeParsingIsForgivingOnCaseAndWhitespace()
        {
            AssertKind("  BULLETLIST  ", ArticleBlockKind.BulletList);
            AssertKind("SpeciesRef", ArticleBlockKind.SpeciesRef);
        }

        [Test]
        public void AnUnreadableBlockTypeIsUnknownAndReported()
        {
            Assert.That(ArticleTokens.TryParseBlockKind("table", out var kind), Is.False,
                "false is what makes the loader warn");
            Assert.That(kind, Is.EqualTo(ArticleBlockKind.Unknown),
                "Unknown renders as nothing — never as a placeholder");

            Assert.That(ArticleTokens.TryParseBlockKind(null, out kind), Is.False);
            Assert.That(kind, Is.EqualTo(ArticleBlockKind.Unknown));
            Assert.That(ArticleTokens.TryParseBlockKind("", out kind), Is.False,
                "unlike a beach's riskLevel, a blank block type is not an 'unfilled field' — " +
                "it is a block nothing can be done with");
        }

        [Test]
        public void EveryCalloutToneTokenParses()
        {
            AssertTone("info", ArticleCalloutTone.Info);
            AssertTone("warning", ArticleCalloutTone.Warning);
            AssertTone("success", ArticleCalloutTone.Success);
            AssertTone("error", ArticleCalloutTone.Error);
            AssertTone("  ERROR ", ArticleCalloutTone.Error);
        }

        [Test]
        public void AnUnreadableToneFallsBackToInfoRatherThanDroppingTheText()
        {
            Assert.That(ArticleTokens.TryParseCalloutTone("danger", out var tone), Is.False);
            Assert.That(tone, Is.EqualTo(ArticleCalloutTone.Info),
                "losing a tint is a cosmetic degradation; dropping the author's sentence is not, " +
                "and a guessed Error would over-state a notice");
        }

        [Test]
        public void ABlankToneIsNotAnError()
        {
            Assert.That(ArticleTokens.TryParseCalloutTone(null, out var tone), Is.True,
                "the generator may omit a default it considers redundant — that is not worth a warning");
            Assert.That(tone, Is.EqualTo(ArticleCalloutTone.Info));
        }

        // ---- Video player copy ----------------------------------------------
        //
        // Five aliases of SpeciesMediaFormatter's player copy. They exist because
        // ArticleViewModel holds no playback state (an article body can carry any number
        // of clips and the reader identifies the playing one by IVideoPlayback.Url), so
        // the SCREEN is the one asking "what does this control say" — and rule 2 says it
        // may not answer for itself.
        //
        // What these tests pin is the SHARING, not the wording: an article's inline clip
        // and a species page's inline clip are the same control on the same player, so a
        // reader who meets both must meet one vocabulary. If someone re-words one side,
        // these fail and force the decision to be made on purpose.

        [Test]
        public void TheVideoControlsCopyIsTheSameOneTheSpeciesPagesUse()
        {
            Assert.That(ArticleFormatter.WatchLabel, Is.EqualTo(SpeciesMediaFormatter.WatchLabel));
            Assert.That(ArticleFormatter.VideoErrorText, Is.EqualTo(SpeciesMediaFormatter.ErrorText));

            foreach (VideoPlaybackState state in Enum.GetValues(typeof(VideoPlaybackState)))
            {
                Assert.That(ArticleFormatter.VideoActionLabel(state),
                    Is.EqualTo(SpeciesMediaFormatter.ActionLabel(state)), state.ToString());
                Assert.That(ArticleFormatter.VideoActionIcon(state),
                    Is.EqualTo(SpeciesMediaFormatter.ActionIcon(state)), state.ToString());
            }
        }

        /// <summary>Every state has a word AND a glyph — a control with one and not the
        /// other is a control the reader cannot use.</summary>
        [Test]
        public void EveryPlaybackStateHasBothAWordAndAGlyph()
        {
            foreach (VideoPlaybackState state in Enum.GetValues(typeof(VideoPlaybackState)))
            {
                Assert.That(ArticleFormatter.VideoActionLabel(state), Is.Not.Null.And.Not.Empty,
                    state.ToString());
                Assert.That(ArticleFormatter.VideoActionIcon(state), Is.Not.Null.And.Not.Empty,
                    state.ToString());
            }
        }

        /// <summary>
        /// The clock hides while the duration is unknown — which it is until the stream
        /// is prepared, and forever on a failed one. "0:00 / 0:00" would read as a clip
        /// of no length rather than a clip not loaded yet: the same honesty rule as
        /// <see cref="ArticleFormatter.ReadingTime"/>'s refusal to print "0 min".
        /// </summary>
        [Test]
        public void ThePlaybackClockIsNullUntilTheDurationIsKnown()
        {
            Assert.That(ArticleFormatter.VideoTimeText(0d, 0d), Is.Null);
            Assert.That(ArticleFormatter.VideoTimeText(12d, 0d), Is.Null);
            Assert.That(ArticleFormatter.VideoTimeText(30d, 125d), Is.EqualTo("0:30 / 2:05"));
        }

        [Test]
        public void TheProgressFractionIsZeroUntilTheDurationIsKnown()
        {
            Assert.That(ArticleFormatter.VideoProgress(12d, 0d), Is.EqualTo(0f),
                "an unprepared clip draws an empty track, not a full one");
            Assert.That(ArticleFormatter.VideoProgress(0d, 100d), Is.EqualTo(0f));
            Assert.That(ArticleFormatter.VideoProgress(50d, 100d), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(ArticleFormatter.VideoProgress(200d, 100d), Is.EqualTo(1f), "clamped");
        }

        static void AssertKind(string token, ArticleBlockKind expected)
        {
            Assert.That(ArticleTokens.TryParseBlockKind(token, out var kind), Is.True, token);
            Assert.That(kind, Is.EqualTo(expected), token);
        }

        static void AssertTone(string token, ArticleCalloutTone expected)
        {
            Assert.That(ArticleTokens.TryParseCalloutTone(token, out var tone), Is.True, token);
            Assert.That(tone, Is.EqualTo(expected), token);
        }
    }
}
