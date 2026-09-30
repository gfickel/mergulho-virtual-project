using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The AR species card's copy (DESIGN_IMPLEMENTATION.md §8.4). Small surface,
    /// but it carries the decision that matters most on this screen: a spec row
    /// with no value is <b>dropped</b> rather than shown as "—", which is the
    /// opposite of what the Praias stat columns do — and since every AnimalDef
    /// ships those three fields blank (Decision D8), "dropped" is the state the
    /// app is actually in.
    /// </summary>
    public class SpeciesCardFormatterTests
    {
        static SpeciesInfo Tiger() => new SpeciesInfo
        {
            Key = "tiger_shark",
            DisplayName = "Tubarão-tigre",
            Binomial = "Galeocerdo cuvier",
        };

        // ---- Labels are fixed strings, not data -----------------------------

        [Test]
        public void Labels_AreTheFramesStrings()
        {
            Assert.That(SpeciesCardFormatter.SizeLabel, Is.EqualTo("TAMANHO APROX."));
            Assert.That(SpeciesCardFormatter.DietLabel, Is.EqualTo("DIETA"));
            Assert.That(SpeciesCardFormatter.BehaviourLabel, Is.EqualTo("COMPORTAMENTO"));
        }

        /// <summary>
        /// V2 draws these uppercase via a Figma text-case transform while the text
        /// nodes are title case; UI Toolkit has no text-transform and the screen may
        /// not manufacture strings, so the case is baked into the formatter. Pinned
        /// here so nobody re-title-cases them from the frame's raw text nodes, and
        /// so nobody adds a ToUpper() in a view instead.
        /// </summary>
        [Test]
        public void Labels_ShipUppercased_BecauseTheViewCannotTransformThem()
        {
            Assert.That(SpeciesCardFormatter.SizeLabel, Is.EqualTo(SpeciesCardFormatter.SizeLabel.ToUpperInvariant()));
            Assert.That(SpeciesCardFormatter.DietLabel, Is.EqualTo(SpeciesCardFormatter.DietLabel.ToUpperInvariant()));
            Assert.That(SpeciesCardFormatter.BehaviourLabel, Is.EqualTo(SpeciesCardFormatter.BehaviourLabel.ToUpperInvariant()));
        }

        // ---- Title / binomial ------------------------------------------------

        [Test]
        public void Title_IsTheDisplayName()
        {
            Assert.That(SpeciesCardFormatter.Title(Tiger()), Is.EqualTo("Tubarão-tigre"));
        }

        [Test]
        public void Title_FallsBackToTheKey_WhenThereIsNoDisplayName()
        {
            var species = new SpeciesInfo { Key = "tiger_shark", DisplayName = "  " };
            Assert.That(SpeciesCardFormatter.Title(species), Is.EqualTo("tiger_shark"));
        }

        [Test]
        public void Title_IsNull_ForNoSpecies()
        {
            Assert.That(SpeciesCardFormatter.Title(null), Is.Null);
        }

        [Test]
        public void Binomial_IsNull_WhenTheIdentificationIsOpen()
        {
            var species = Tiger();
            species.Binomial = "";
            Assert.That(SpeciesCardFormatter.Binomial(species), Is.Null,
                "a blank binomial must hide the line, never print a guess");
            Assert.That(SpeciesCardFormatter.Binomial(Tiger()), Is.EqualTo("Galeocerdo cuvier"));
        }

        // ---- Spec rows -------------------------------------------------------

        [Test]
        public void SpecRows_AreEmpty_WhenNothingIsAuthored()
        {
            // The state of every shipped AnimalDef today.
            Assert.That(SpeciesCardFormatter.SpecRows(Tiger()), Is.Empty);
        }

        [Test]
        public void SpecRows_AreEmpty_ForNoSpecies()
        {
            Assert.That(SpeciesCardFormatter.SpecRows(null), Is.Empty);
        }

        [Test]
        public void SpecRows_DropTheOnesWithNoValue_RatherThanShowingADash()
        {
            var species = Tiger();
            species.Diet = "Peixes, tartarugas e moluscos";

            var rows = SpeciesCardFormatter.SpecRows(species);

            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].Label, Is.EqualTo(SpeciesCardFormatter.DietLabel));
            Assert.That(rows[0].Value, Is.EqualTo("Peixes, tartarugas e moluscos"));
            Assert.That(rows[0].Value, Is.Not.EqualTo(BeachContentFormatter.NoValue));
        }

        [Test]
        public void SpecRows_KeepTheFramesOrder_AndTrim()
        {
            var species = Tiger();
            species.ApproximateSize = " 3 a 4 metros ";
            species.Diet = "Peixes, tartarugas e moluscos";
            species.Behaviour = "Solitário e noturno";

            var rows = SpeciesCardFormatter.SpecRows(species);

            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows[0].Label, Is.EqualTo(SpeciesCardFormatter.SizeLabel));
            Assert.That(rows[0].Value, Is.EqualTo("3 a 4 metros"));
            Assert.That(rows[1].Label, Is.EqualTo(SpeciesCardFormatter.DietLabel));
            Assert.That(rows[2].Label, Is.EqualTo(SpeciesCardFormatter.BehaviourLabel));
        }
    }
}
