using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class PrivacyV2Tests
    {
        [Test]
        public void BrewingAndSaltingProduceTheSameNeutralWitnessFields()
        {
            MatchSetup setup = TestMatch.Setup();
            setup.ForceCards = new List<List<SabotageType>> { new List<SabotageType> { SabotageType.SaltCoffee } };
            var m = new TestMatch(setup: setup);
            m.Work(); m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            Vec3 coffee = m.Layout.GetStation(EntityIds.CoffeeMachine).Position;
            m.Move(1, coffee + new Vec3(0, 0, 1), 180);
            m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            PrivateNote brewed = m.View(1).Me.Notes.Last(n => n.Kind == NoteKind.Witness && n.Subject == 0);
            int notesBefore = m.View(1).Me.Notes.Count;
            m.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
            Assert.AreEqual(notesBefore + 1, m.View(1).Me.Notes.Count);
            PrivateNote salted = m.View(1).Me.Notes.Last();
            Assert.AreEqual(PublicActivity.Brewing, brewed.Activity);
            Assert.AreEqual(brewed.Kind, salted.Kind);
            Assert.AreEqual(brewed.Activity, salted.Activity);
            Assert.AreEqual(brewed.TargetId, salted.TargetId);
            Assert.AreEqual(brewed.DeskIndex, salted.DeskIndex);
            Assert.AreEqual(brewed.HeldBefore, salted.HeldBefore);
            Assert.AreEqual(brewed.HeldAfter, salted.HeldAfter);
            Assert.IsFalse(salted.Salted);
            Assert.IsEmpty(salted.Calls);
            Assert.IsFalse(m.Item(EntityIds.Mug(0), 1).KnownSalted);
            Assert.IsTrue(m.Item(EntityIds.Mug(0), 0).KnownSalted);
        }
    }
}
