using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// Pins the error/offline copy. Two things are being protected: the strings
    /// transcribed from Figma frames 79:1304 / 81:1403 (which must stay verbatim),
    /// and the rule that the offline wording is only ever chosen when the device
    /// says there is no link — every other failure gets the claim that is always
    /// true.
    /// </summary>
    public class StateViewCopyTests
    {
        [Test]
        public void DesignedStrings_AreVerbatimFromTheFrames()
        {
            Assert.That(StateViewCopy.ErrorTitle, Is.EqualTo("Algo deu errado por aqui"));
            Assert.That(StateViewCopy.ErrorBody, Is.EqualTo("Não conseguimos carregar as informações."));
            Assert.That(StateViewCopy.RetryAction, Is.EqualTo("Tentar novamente"));
        }

        [Test]
        public void Title_NamesTheOfflineCase_AndFallsBackToTheGenericOne()
        {
            Assert.That(StateViewCopy.Title(offline: true), Is.EqualTo(StateViewCopy.OfflineTitle));
            Assert.That(StateViewCopy.Title(offline: false), Is.EqualTo(StateViewCopy.ErrorTitle));
        }

        [Test]
        public void ConditionsBody_SaysWhatFailed_AndOnlyMentionsTheConnectionWhenThereIsNone()
        {
            Assert.That(StateViewCopy.ConditionsBody(offline: false), Is.EqualTo(StateViewCopy.ConditionsErrorBody));
            Assert.That(StateViewCopy.ConditionsBody(offline: false), Does.Not.Contain("conex"),
                "an online failure cannot blame the connection — it may be the API, DNS or a portal");

            Assert.That(StateViewCopy.ConditionsBody(offline: true), Is.EqualTo(StateViewCopy.ConditionsOfflineBody));
            Assert.That(StateViewCopy.ConditionsBody(offline: true), Does.Contain("internet"));
        }

        [Test]
        public void EveryString_IsNonEmptyAndEndsAsASentence()
        {
            foreach (var body in new[]
                     {
                         StateViewCopy.ErrorBody,
                         StateViewCopy.ConditionsErrorBody,
                         StateViewCopy.ConditionsOfflineBody,
                     })
            {
                Assert.That(body, Is.Not.Empty);
                Assert.That(body, Does.EndWith("."));
            }

            foreach (var title in new[] { StateViewCopy.ErrorTitle, StateViewCopy.OfflineTitle })
            {
                Assert.That(title, Is.Not.Empty);
                Assert.That(title, Does.Not.EndWith("."), "titles are not sentences in V2");
            }
        }

        /// <summary>
        /// The retry a feed row offers and the retry a state view offers are the
        /// same promise, so they are the same words by construction rather than by
        /// two people happening to type the same thing.
        /// </summary>
        [Test]
        public void TheFeedRowRetry_IsTheSameWordsAsTheStateViewAction()
        {
            Assert.That(ReportFormatter.RetryLabel, Is.EqualTo(StateViewCopy.RetryAction));
        }
    }
}
