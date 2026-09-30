using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MergulhoVirtual.UI.Tests
{
    /// <summary>
    /// The AR HUD's ViewModel. Four things are worth pinning and are all easy to
    /// break: that a tap on an unknown species opens nothing rather than a blank
    /// card, that a miss never reaches here at all (the hit source does not raise
    /// one), that the listening flag really does reach the AR side — it is the
    /// only thing stopping the scene-root raycaster from firing on every screen —
    /// and that leaving the route closes the card.
    /// </summary>
    public class MergulhoViewModelTests
    {
        sealed class FakeSpecies : ISpeciesCatalog
        {
            public List<SpeciesInfo> Items = new List<SpeciesInfo>
            {
                new SpeciesInfo { Key = "tiger_shark", DisplayName = "Tubarão-tigre", Binomial = "Galeocerdo cuvier" },
                // No binomial: reef_shark's identification is genuinely open today.
                new SpeciesInfo { Key = "reef_shark", DisplayName = "Tubarão-de-recife" },
            };

            public IReadOnlyList<SpeciesInfo> Species => Items;

            public SpeciesInfo Find(string key)
            {
                foreach (var item in Items) if (item.Key == key) return item;
                return null;
            }
        }

        sealed class FakeArSelection : IArSelection
        {
            public int ListenCalls;
            public bool Listening;

            public event Action<string> SpeciesSelected;

            public void SetListening(bool listening)
            {
                Listening = listening;
                ListenCalls++;
            }

            /// <summary>What ObjectInteraction does when a cast lands on an animal.</summary>
            public void Tap(string speciesKey) => SpeciesSelected?.Invoke(speciesKey);
        }

        static MergulhoViewModel Build(out FakeSpecies catalog, out FakeArSelection ar)
        {
            catalog = new FakeSpecies();
            ar = new FakeArSelection();
            return new MergulhoViewModel(catalog, ar);
        }

        // ---- Opening and closing the card ------------------------------------

        [Test]
        public void NoCardIsOpen_Initially()
        {
            var vm = Build(out _, out _);
            Assert.That(vm.HasSelection, Is.False);
            Assert.That(vm.SelectedSpecies, Is.Null);
            Assert.That(vm.TitleText, Is.Null);
            Assert.That(vm.SpecRows, Is.Empty);
        }

        [Test]
        public void ATap_OpensTheCardForThatSpecies()
        {
            var vm = Build(out _, out var ar);
            int changed = 0;
            vm.Changed += () => changed++;

            ar.Tap("tiger_shark");

            Assert.That(vm.HasSelection, Is.True);
            Assert.That(vm.TitleText, Is.EqualTo("Tubarão-tigre"));
            Assert.That(vm.BinomialText, Is.EqualTo("Galeocerdo cuvier"));
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void ATapOnAnUnknownSpecies_ChangesNothing()
        {
            var vm = Build(out _, out var ar);
            ar.Tap("tiger_shark");
            int changed = 0;
            vm.Changed += () => changed++;

            // A modelled animal that was never catalogued: drop it, do NOT open a
            // blank card and do NOT close the one that is open.
            ar.Tap("megalodon");

            Assert.That(vm.TitleText, Is.EqualTo("Tubarão-tigre"));
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void ATapOnTheSameSpecies_DoesNotRepaint()
        {
            var vm = Build(out _, out var ar);
            ar.Tap("tiger_shark");
            int changed = 0;
            vm.Changed += () => changed++;

            ar.Tap("tiger_shark");

            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void ATapOnAnotherSpecies_SwapsTheCard()
        {
            var vm = Build(out _, out var ar);
            ar.Tap("tiger_shark");

            ar.Tap("reef_shark");

            Assert.That(vm.TitleText, Is.EqualTo("Tubarão-de-recife"));
            Assert.That(vm.HasBinomial, Is.False, "an open identification shows the common name alone");
            Assert.That(vm.BinomialText, Is.Null);
        }

        [Test]
        public void CloseCard_DismissesIt_AndIsIdempotent()
        {
            var vm = Build(out _, out var ar);
            ar.Tap("tiger_shark");
            int changed = 0;
            vm.Changed += () => changed++;

            vm.CloseCard();
            vm.CloseCard();

            Assert.That(vm.HasSelection, Is.False);
            Assert.That(changed, Is.EqualTo(1));
        }

        // ---- Spec rows -------------------------------------------------------

        [Test]
        public void SpecRows_AreEmpty_WhenTheAnimalDefFieldsAreBlank()
        {
            // Every shipped species today (Decision D8).
            var vm = Build(out _, out var ar);
            ar.Tap("tiger_shark");

            Assert.That(vm.HasSpecRows, Is.False);
            Assert.That(vm.SpecRows, Is.Empty);
        }

        [Test]
        public void SpecRows_FollowTheSpeciesOnceAuthored()
        {
            var vm = Build(out var catalog, out var ar);
            catalog.Items[0].ApproximateSize = "3 a 4 metros";
            catalog.Items[0].Behaviour = "Solitário e noturno";

            ar.Tap("tiger_shark");

            Assert.That(vm.HasSpecRows, Is.True);
            Assert.That(vm.SpecRows.Count, Is.EqualTo(2), "the unauthored diet row is dropped, not dashed");
            Assert.That(vm.SpecRows[0].Label, Is.EqualTo(SpeciesCardFormatter.SizeLabel));
            Assert.That(vm.SpecRows[1].Label, Is.EqualTo(SpeciesCardFormatter.BehaviourLabel));
        }

        // ---- Listening -------------------------------------------------------

        [Test]
        public void SetListening_ReachesTheArSide_AndDeduplicates()
        {
            var vm = Build(out _, out var ar);

            vm.SetListening(true);
            vm.SetListening(true);

            Assert.That(ar.Listening, Is.True);
            Assert.That(ar.ListenCalls, Is.EqualTo(1));
        }

        [Test]
        public void LeavingTheRoute_StopsListeningAndClosesTheCard()
        {
            var vm = Build(out _, out var ar);
            vm.SetListening(true);
            ar.Tap("tiger_shark");

            vm.SetListening(false);

            Assert.That(ar.Listening, Is.False);
            Assert.That(vm.HasSelection, Is.False,
                "coming back to AR must not resurrect a species the user walked away from");
        }

        [Test]
        public void Dispose_UnsubscribesAndReleasesTheHitSource()
        {
            var vm = Build(out _, out var ar);
            vm.SetListening(true);

            vm.Dispose();

            Assert.That(ar.Listening, Is.False);
            ar.Tap("tiger_shark");
            Assert.That(vm.HasSelection, Is.False);
        }

        [Test]
        public void NullDependencies_AreSurvivable()
        {
            // A scene with no ObjectInteraction (and, defensively, no catalog):
            // the HUD must degrade to "no card ever opens", not throw at the
            // composition root.
            var vm = new MergulhoViewModel(null, null);
            Assert.DoesNotThrow(() => vm.SetListening(true));
            Assert.DoesNotThrow(() => vm.ShowSpecies("tiger_shark"));
            Assert.That(vm.HasSelection, Is.False);
            Assert.DoesNotThrow(vm.Dispose);
        }
    }
}
