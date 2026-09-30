using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// Pins the Espécie screen's media copy — the inline player's words and clocks,
    /// and the credit/description passthroughs. Same job as
    /// <see cref="SpeciesCardFormatterTests"/>: the screen may not build a
    /// user-visible string, so the strings live here and their spelling is a test.
    /// </summary>
    public class SpeciesMediaFormatterTests
    {
        // ---- Clock -----------------------------------------------------------

        [Test]
        public void Clock_IsMinutesAndPaddedSeconds()
        {
            Assert.That(SpeciesMediaFormatter.Clock(0d), Is.EqualTo("0:00"));
            Assert.That(SpeciesMediaFormatter.Clock(7d), Is.EqualTo("0:07"));
            Assert.That(SpeciesMediaFormatter.Clock(42d), Is.EqualTo("0:42"));
            Assert.That(SpeciesMediaFormatter.Clock(60d), Is.EqualTo("1:00"));
            Assert.That(SpeciesMediaFormatter.Clock(135d), Is.EqualTo("2:15"));
        }

        /// <summary>Seconds are truncated, not rounded — a playhead at 0:41.9 has not
        /// reached 0:42, and a clock that runs ahead of the video reads as a bug.</summary>
        [Test]
        public void Clock_TruncatesTowardsZero()
        {
            Assert.That(SpeciesMediaFormatter.Clock(41.9d), Is.EqualTo("0:41"));
        }

        [Test]
        public void Clock_GrowsAnHoursFieldOnlyWhenItNeedsOne()
        {
            Assert.That(SpeciesMediaFormatter.Clock(3599d), Is.EqualTo("59:59"));
            Assert.That(SpeciesMediaFormatter.Clock(3600d), Is.EqualTo("1:00:00"));
            Assert.That(SpeciesMediaFormatter.Clock(3725d), Is.EqualTo("1:02:05"));
        }

        /// <summary>A decoder that has not started reports odd numbers; none of them
        /// may reach the screen as text.</summary>
        [Test]
        public void Clock_SurvivesNonsense()
        {
            Assert.That(SpeciesMediaFormatter.Clock(-5d), Is.EqualTo("0:00"));
            Assert.That(SpeciesMediaFormatter.Clock(double.NaN), Is.EqualTo("0:00"));
            Assert.That(SpeciesMediaFormatter.Clock(double.PositiveInfinity), Is.EqualTo("0:00"));
        }

        // ---- PlaybackTime ----------------------------------------------------

        [Test]
        public void PlaybackTime_IsPositionOverDuration()
        {
            Assert.That(SpeciesMediaFormatter.PlaybackTime(14d, 42d), Is.EqualTo("0:14 / 0:42"));
        }

        /// <summary>
        /// The duration is unknown until the stream is prepared, and forever on a
        /// failed one. "0:00 / 0:00" would read as a clip of no length rather than one
        /// that has not loaded, so the label is hidden instead.
        /// </summary>
        [Test]
        public void PlaybackTime_IsNullWhileTheDurationIsUnknown()
        {
            Assert.That(SpeciesMediaFormatter.PlaybackTime(0d, 0d), Is.Null);
            Assert.That(SpeciesMediaFormatter.PlaybackTime(3d, double.NaN), Is.Null);
            Assert.That(SpeciesMediaFormatter.PlaybackTime(3d, -1d), Is.Null);
        }

        [Test]
        public void PlaybackTime_ClampsAPositionPastTheEnd()
        {
            Assert.That(SpeciesMediaFormatter.PlaybackTime(99d, 42d), Is.EqualTo("0:42 / 0:42"));
            Assert.That(SpeciesMediaFormatter.PlaybackTime(-3d, 42d), Is.EqualTo("0:00 / 0:42"));
        }

        // ---- Progress --------------------------------------------------------

        [Test]
        public void Progress_IsTheFractionPlayed()
        {
            Assert.That(SpeciesMediaFormatter.Progress(21d, 42d), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void Progress_IsZeroWithNoDuration_AndClampsToTheEnds()
        {
            Assert.That(SpeciesMediaFormatter.Progress(5d, 0d), Is.EqualTo(0f));
            Assert.That(SpeciesMediaFormatter.Progress(-5d, 42d), Is.EqualTo(0f));
            Assert.That(SpeciesMediaFormatter.Progress(99d, 42d), Is.EqualTo(1f));
        }

        // ---- Action control --------------------------------------------------

        /// <summary>
        /// The label and the glyph come from one switch each so they cannot disagree.
        /// Idle is "Assistir" — the call to action — and every card that does not own
        /// the player is Idle by definition.
        /// </summary>
        [Test]
        public void ActionLabel_NamesWhatTheTapWillDo()
        {
            Assert.That(SpeciesMediaFormatter.ActionLabel(VideoPlaybackState.Idle), Is.EqualTo("Assistir"));
            Assert.That(SpeciesMediaFormatter.ActionLabel(VideoPlaybackState.Loading), Is.EqualTo("Carregando…"));
            Assert.That(SpeciesMediaFormatter.ActionLabel(VideoPlaybackState.Playing), Is.EqualTo("Pausar"));
            Assert.That(SpeciesMediaFormatter.ActionLabel(VideoPlaybackState.Paused), Is.EqualTo("Continuar"));
            Assert.That(SpeciesMediaFormatter.ActionLabel(VideoPlaybackState.Failed), Is.EqualTo("Tentar de novo"));
        }

        [Test]
        public void ActionIcon_MatchesTheLabel()
        {
            Assert.That(SpeciesMediaFormatter.ActionIcon(VideoPlaybackState.Idle), Is.EqualTo("play_arrow"));
            Assert.That(SpeciesMediaFormatter.ActionIcon(VideoPlaybackState.Paused), Is.EqualTo("play_arrow"));
            Assert.That(SpeciesMediaFormatter.ActionIcon(VideoPlaybackState.Playing), Is.EqualTo("pause"));
            Assert.That(SpeciesMediaFormatter.ActionIcon(VideoPlaybackState.Failed), Is.EqualTo("refresh"));
        }

        /// <summary>The user never sees Unity's technical English — see IVideoPlayback.</summary>
        [Test]
        public void ErrorText_IsOneFixedPortugueseLine()
        {
            Assert.That(SpeciesMediaFormatter.ErrorText,
                Is.EqualTo("Não foi possível carregar o vídeo. Verifique sua conexão."));
        }

        // ---- Passthroughs ----------------------------------------------------

        /// <summary>
        /// Credits are rendered VERBATIM. The authored values disagree about carrying
        /// their own prefix — hammerhead's photo credit starts "Foto: ", tiger_shark's
        /// does not — so adding one would print "Foto: Foto: …" for some species.
        /// </summary>
        [Test]
        public void Credit_IsVerbatimAndTrimmed()
        {
            Assert.That(SpeciesMediaFormatter.Credit("  Foto: Albert kok / CC BY-SA 4.0 "),
                Is.EqualTo("Foto: Albert kok / CC BY-SA 4.0"));
        }

        [Test]
        public void Credit_IsNullWhenThereIsNothingToCredit()
        {
            Assert.That(SpeciesMediaFormatter.Credit(null), Is.Null);
            Assert.That(SpeciesMediaFormatter.Credit(""), Is.Null);
            Assert.That(SpeciesMediaFormatter.Credit("   "), Is.Null);
        }

        [Test]
        public void Description_AndVideoTitle_FollowTheSameNullRule()
        {
            Assert.That(SpeciesMediaFormatter.Description("  Uma espécie… "), Is.EqualTo("Uma espécie…"));
            Assert.That(SpeciesMediaFormatter.Description("  "), Is.Null);
            Assert.That(SpeciesMediaFormatter.VideoTitle(" Tubarão-limão em ação "),
                Is.EqualTo("Tubarão-limão em ação"));
            Assert.That(SpeciesMediaFormatter.VideoTitle(null), Is.Null);
        }

        /// <summary>
        /// Nothing here returns <see cref="ConditionsFormatter.NoValue"/>. That dash
        /// belongs to a fixed stat slot with a sibling that would be stranded without
        /// it; a credit line or a clock with nothing in it is simply not drawn.
        /// </summary>
        [Test]
        public void NothingHereEverReturnsTheDashPlaceholder()
        {
            Assert.That(SpeciesMediaFormatter.Credit(""), Is.Not.EqualTo(ConditionsFormatter.NoValue));
            Assert.That(SpeciesMediaFormatter.Description(""), Is.Not.EqualTo(ConditionsFormatter.NoValue));
            Assert.That(SpeciesMediaFormatter.PlaybackTime(0d, 0d), Is.Not.EqualTo(ConditionsFormatter.NoValue));
        }
    }
}
