using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    [TestFixture]
    public sealed class LinePrivacyTests
    {
        private static byte[] ObjectBytes<T>(T value)
        {
            var method = typeof(LineCodec).GetMethod("Write" + typeof(T).Name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method, "Codec field writer supports privacy fingerprinting.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                method.Invoke(null, new object[] { writer, value }); writer.Flush(); return stream.ToArray();
            }
        }

        private static bool Contains(byte[] bytes, byte[] pattern)
        {
            for (int at = 0; at <= bytes.Length - pattern.Length; at++)
            {
                int i = 0; while (i < pattern.Length && bytes[at + i] == pattern[i]) i++;
                if (i == pattern.Length) return true;
            }
            return false;
        }

        private static byte[] Visible(LineTest m, int player)
        {
            var v = m.View(player); v.StateVersion = 0; return LineCodec.EncodeView(v);
        }

        private static void ScanAll(LineTest m)
        {
            int count = m.Setup.Players.Count; var views = new LineView[count]; var bytes = new byte[count][];
            for (int p = 0; p < count; p++) { views[p] = m.View(p); bytes[p] = LineCodec.EncodeView(views[p]); }
            for (int recipient = 0; recipient < count; recipient++)
            {
                Assert.IsTrue(Contains(bytes[recipient], ObjectBytes(views[recipient].Mine)), "Positive control: own private-state bytes exist.");
                for (int owner = 0; owner < count; owner++) if (owner != recipient)
                {
                    Assert.IsFalse(Contains(bytes[recipient], ObjectBytes(views[owner].Mine)), "Other agenda/trick/inbox/phone state at " + m.Engine.Phase);
                    foreach (var call in views[owner].Mine.CallLog)
                        if (call.Other != recipient)
                            Assert.IsFalse(Contains(bytes[recipient], ObjectBytes(call)), "Other call log at " + m.Engine.Phase);
                    if ((m.Engine.Phase == LinePhase.RoundIntro || m.Engine.Phase == LinePhase.RoundPlay) && views[owner].Round != null)
                    {
                        var ownRound = ObjectBytes(views[owner].Round); var recipientRound = ObjectBytes(views[recipient].Round);
                        // Majority topic and duplicate true slips may legitimately be identical. Distinct private
                        // round records must never appear in the recipient payload, including their choice.
                        if (!ownRound.SequenceEqual(recipientRound))
                            Assert.IsFalse(Contains(bytes[recipient], ownRound), "Other private round at " + m.Engine.Phase);
                    }
                }
                var meeting = views[recipient].Meeting;
                if (meeting != null)
                {
                    foreach (var pick in meeting.Recordings) Assert.IsEmpty(pick.CandidateCallIds, "Unplayed host call IDs are private.");
                    if (m.Engine.Phase != LinePhase.MeetingVerdict && m.Engine.Phase != LinePhase.FinalVerdict) Assert.IsEmpty(meeting.Votes);
                }
                if (m.Engine.Phase != LinePhase.WeekResults) Assert.IsNull(views[recipient].Result);
            }
            var events = m.Engine.DrainEvents();
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case LineEventType.CallIncoming: case LineEventType.CallConnected: case LineEventType.CallEnded:
                    case LineEventType.CallBusy: case LineEventType.MissedCall: case LineEventType.MailReceived: case LineEventType.TrickHit:
                    case LineEventType.TrickUsed: case LineEventType.TipFiled: case LineEventType.Wiretap:
                        Assert.AreEqual(LineAudience.Player, e.Audience); break;
                }
                for (int recipient = 0; recipient < count; recipient++)
                {
                    var encoded = LineCodec.EncodeEvents(events.Where(x => x.IsVisibleTo(recipient)).ToList());
                    if (!e.IsVisibleTo(recipient)) Assert.IsFalse(Contains(encoded, ObjectBytes(e)), "Private event audience.");
                }
            }
        }

        [Test]
        public void EveryRecipientEveryPhaseOfScriptedWeekPassesPrivacyByteScans()
        {
            var s = LineTest.MakeSetup(6);
            s.ForceRounds = new List<RoundType> { RoundType.PrimHavuzu, RoundType.IhbarKutusu, RoundType.FarkliEposta, RoundType.KayipRapor, RoundType.TerfiListesi, RoundType.OrtakSifre };
            s.ForceTricks = new List<TrickType> { TrickType.HatDinle, TrickType.SahteEposta, TrickType.KahveDok, TrickType.FisCek, TrickType.InternetKes, TrickType.HatDinle };
            var m = new LineTest(s, c => c.Profiles.ForEach(p => { p.TraceChance = 0; p.LeakChance = 0; }));
            var phases = new HashSet<LinePhase>(); var rounds = new HashSet<RoundType>();
            while (!m.Engine.IsFinished)
            {
                phases.Add(m.Engine.Phase); ScanAll(m);
                if (m.Engine.Phase == LinePhase.RoundPlay)
                {
                    rounds.Add(m.View().Round.Type);
                    m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer);
                    m.Ok(2, LineVerb.Call, 3); m.Ok(3, LineVerb.Answer);
                    ScanAll(m); m.Advance(8); m.Ok(0, LineVerb.HangUp); m.Ok(2, LineVerb.HangUp);
                    if (m.Engine.Day == 1 && m.View().RoundIndex == 0)
                    {
                        m.Ok(0, LineVerb.UseTrick, 2); m.Ok(5, LineVerb.UseTrick, 1);
                        m.Ok(1, LineVerb.UseTrick, 4, 7, 3); ScanAll(m);
                    }
                    if (m.Engine.Day == 2 && m.View().RoundIndex == 0) m.Ok(4, LineVerb.TipOff, 0);
                    for (int p = 0; p < 6; p++)
                    {
                        var r = m.View(p).Round;
                        if (r.Type == RoundType.OrtakSifre) m.Ok(p, LineVerb.SubmitChoice, a: 777777);
                        else m.Ok(p, LineVerb.SubmitChoice, (p + 1) % 6, r.Type == RoundType.PrimHavuzu ? p % 2 : r.Type == RoundType.IhbarKutusu ? p : r.IsOdd ? r.GuessOptions[0] : -1);
                    }
                    ScanAll(m);
                }
                if (m.Engine.Phase == LinePhase.MeetingVote || m.Engine.Phase == LinePhase.FinalVote)
                {
                    for (int p = 0; p < 6; p++) m.Ok(p, LineVerb.Vote,
                        m.Engine.Phase == LinePhase.FinalVote ? m.View().Meeting.Shortlist.First(id => id != p) : (p + 1) % 6);
                    ScanAll(m);
                }
                m.Next();
            }
            Assert.AreEqual(6, rounds.Count);
            foreach (LinePhase phase in Enum.GetValues(typeof(LinePhase)))
                if (phase != LinePhase.None && phase != LinePhase.Abandoned) CollectionAssert.Contains(phases, phase);
        }

        [Test]
        public void OtherAgendasAndTrickAssignmentsDoNotChangeRecipientBytesBeforeResults()
        {
            var aSetup = LineTest.MakeSetup(6); var bSetup = LineTest.MakeSetup(6);
            aSetup.ForceAgendas = new List<int> { 1, 2, 0, 4, 5, 3 };
            bSetup.ForceAgendas = new List<int> { 2, 0, 1, 4, 5, 3 };
            aSetup.ForceTricks = new List<TrickType> { TrickType.SahteEposta, TrickType.KahveDok, TrickType.FisCek, TrickType.InternetKes, TrickType.HatDinle, TrickType.HatDinle };
            bSetup.ForceTricks = new List<TrickType> { TrickType.KahveDok, TrickType.SahteEposta, TrickType.FisCek, TrickType.InternetKes, TrickType.HatDinle, TrickType.HatDinle };
            var a = new LineTest(aSetup); var b = new LineTest(bSetup);
            while (a.Engine.Phase != LinePhase.WeekResults)
            {
                CollectionAssert.AreEqual(Visible(a, 5), Visible(b, 5), "Secret assignment at " + a.Engine.Phase);
                CollectionAssert.AreEqual(LineCodec.EncodeEvents(a.Engine.DrainEvents().Where(e => e.IsVisibleTo(5)).ToList()), LineCodec.EncodeEvents(b.Engine.DrainEvents().Where(e => e.IsVisibleTo(5)).ToList()));
                a.Next(); b.Next();
            }
        }

        [Test]
        public void OtherCallPartnersAndLogsHaveIdenticalObserverBytes()
        {
            var a = new LineTest(); var b = new LineTest(); a.Engine.DrainEvents(); b.Engine.DrainEvents();
            a.Ok(0, LineVerb.Call, 1); b.Ok(0, LineVerb.Call, 2);
            CollectionAssert.AreEqual(Visible(a, 3), Visible(b, 3));
            a.Ok(1, LineVerb.Answer); b.Ok(2, LineVerb.Answer); a.Advance(9); b.Advance(9);
            CollectionAssert.AreEqual(Visible(a, 3), Visible(b, 3));
            a.Ok(0, LineVerb.HangUp); b.Ok(0, LineVerb.HangUp);
            CollectionAssert.AreEqual(Visible(a, 3), Visible(b, 3));
            Assert.IsEmpty(a.Engine.DrainEvents().Where(e => e.IsVisibleTo(3))); Assert.IsEmpty(b.Engine.DrainEvents().Where(e => e.IsVisibleTo(3)));
        }

        [TestCase(LinePhase.MeetingVote)] [TestCase(LinePhase.FinalVote)]
        public void MeetingVoteTargetCannotBeDistinguishedUntilVerdict(LinePhase phase)
        {
            var a = new LineTest(); var b = new LineTest(); a.Until(phase); b.Until(phase);
            int voter = phase == LinePhase.FinalVote ? a.View().Players.First(p => !a.View().Meeting.Shortlist.Contains(p.Id)).Id : 0;
            int left = phase == LinePhase.FinalVote ? a.View().Meeting.Shortlist[0] : 1;
            int right = phase == LinePhase.FinalVote ? a.View().Meeting.Shortlist[1] : 2;
            int observer = Enumerable.Range(0, 4).First(p => p != voter);
            a.Engine.DrainEvents(); b.Engine.DrainEvents(); a.Ok(voter, LineVerb.Vote, left); b.Ok(voter, LineVerb.Vote, right);
            CollectionAssert.AreEqual(Visible(a, observer), Visible(b, observer));
            CollectionAssert.AreEqual(LineCodec.EncodeEvents(a.Engine.DrainEvents().Where(e => e.IsVisibleTo(observer)).ToList()), LineCodec.EncodeEvents(b.Engine.DrainEvents().Where(e => e.IsVisibleTo(observer)).ToList()));
        }

        [Test]
        public void ComplaintAuthorsRemainAnonymousAtRevealAndDossierWithoutLeak()
        {
            var a = LineTest.WithTrick(TrickType.KahveDok); var b = LineTest.WithTrick(TrickType.KahveDok);
            a.Ok(0, LineVerb.SubmitChoice, 2, 5); a.Ok(1, LineVerb.SubmitChoice, -1, 0);
            b.Ok(1, LineVerb.SubmitChoice, 2, 5); b.Ok(0, LineVerb.SubmitChoice, -1, 0);
            a.Until(LinePhase.RoundReveal); b.Until(LinePhase.RoundReveal);
            CollectionAssert.AreEqual(Visible(a, 3), Visible(b, 3));
            a.Until(LinePhase.MeetingDossier); b.Until(LinePhase.MeetingDossier);
            CollectionAssert.AreEqual(Visible(a, 3), Visible(b, 3));
        }

        [Test]
        public void PrivateMailSentinelNeverAppearsInOtherRecipientBytes()
        {
            const int sentinel = 0x65247A31;
            var m = new LineTest(configure: c => c.MailTemplates = int.MaxValue);
            m.Ok(0, LineVerb.SendMail, 1, sentinel, 2);
            var secret = BitConverter.GetBytes(sentinel);
            Assert.IsTrue(Contains(Visible(m, 1), secret), "Positive control for authorized recipient.");
            for (int p = 0; p < 4; p++) if (p != 1)
                Assert.IsFalse(Contains(Visible(m, p), secret), "Private mail template must not escape inbox routing.");
        }

        private static LineTest BroadcastFixture(TrickType trick, int actor)
        {
            var s = LineTest.MakeSetup(); s.History = new LineSessionHistory { WeeksPlayed = 1 };
            s.ForceRounds = new List<RoundType> { RoundType.IhbarKutusu, RoundType.TerfiListesi, RoundType.PrimHavuzu, RoundType.FarkliEposta, RoundType.KayipRapor, RoundType.OrtakSifre };
            s.ForceTricks = new List<TrickType> { TrickType.SahteEposta, TrickType.KahveDok, TrickType.FisCek, TrickType.InternetKes };
            int original = s.ForceTricks.IndexOf(trick);
            s.ForceTricks[original] = s.ForceTricks[actor]; s.ForceTricks[actor] = trick;
            var m = new LineTest(s, c => c.Profiles.ForEach(p => p.TraceChance = 0));
            m.Until(LinePhase.RoundPlay); m.Engine.DrainEvents(); return m;
        }

        private static void SameBroadcastObserver(LineTest a, LineTest b)
        {
            CollectionAssert.AreEqual(Visible(a, 2), Visible(b, 2), "Recipient bytes cannot encode the skipped author's seat through mail IDs.");
            CollectionAssert.AreEqual(LineCodec.EncodeEvents(a.Engine.DrainEvents().Where(e => e.IsVisibleTo(2)).ToList()),
                LineCodec.EncodeEvents(b.Engine.DrainEvents().Where(e => e.IsVisibleTo(2)).ToList()));
        }

        [Test]
        public void FakeMailAuthorOnEitherSideOfObserverDoesNotChangeBytes()
        {
            var a = BroadcastFixture(TrickType.SahteEposta, 0); var b = BroadcastFixture(TrickType.SahteEposta, 3);
            a.Ok(0, LineVerb.UseTrick, 1, 7, 3); b.Ok(3, LineVerb.UseTrick, 1, 7, 3); SameBroadcastObserver(a, b);
        }

        [Test]
        public void GenuineAndSpoofedMailAreIndistinguishableToRecipient()
        {
            var a = BroadcastFixture(TrickType.SahteEposta, 0); var b = BroadcastFixture(TrickType.SahteEposta, 0);
            a.Ok(0, LineVerb.UseTrick, 1, 7, 3); b.Ok(1, LineVerb.SendMail, -1, 7, 3); SameBroadcastObserver(a, b);
        }

        [Test]
        public void InternetActorOnEitherSideOfObserverDoesNotChangeBytes()
        {
            var a = BroadcastFixture(TrickType.InternetKes, 0); var b = BroadcastFixture(TrickType.InternetKes, 3);
            a.Ok(0, LineVerb.UseTrick, 1); b.Ok(3, LineVerb.UseTrick, 1); SameBroadcastObserver(a, b);
        }

        [Test]
        public void PasswordCodeBytesAppearOnlyForSubmitterUntilRevealed()
        {
            var s = LineTest.MakeSetup(6); s.ForceRounds = new List<RoundType> { RoundType.OrtakSifre, RoundType.IhbarKutusu, RoundType.PrimHavuzu, RoundType.TerfiListesi, RoundType.FarkliEposta, RoundType.KayipRapor };
            var m = new LineTest(s); m.Until(LinePhase.RoundIntro); int code = 0;
            foreach (int owner in m.View().Round.PositionOwners) code = code * 10 + m.View(owner).Round.Digit;
            Assert.Greater(code, 10000, "Chosen fixture code has a distinctive int fingerprint.");
            var secret = BitConverter.GetBytes(code);
            for (int p = 0; p < 6; p++) Assert.IsFalse(Contains(Visible(m, p), secret));
            m.Next(); for (int p = 0; p < 6; p++) Assert.IsFalse(Contains(Visible(m, p), secret));
            m.Engine.DrainEvents(); m.Ok(0, LineVerb.SubmitChoice, a: code);
            Assert.IsTrue(Contains(Visible(m, 0), secret));
            for (int p = 1; p < 6; p++) Assert.IsFalse(Contains(Visible(m, p), secret), "Codes stay private until reveal.");
            Assert.IsFalse(Contains(LineCodec.EncodeEvents(m.Engine.DrainEvents()), secret));
            m.Reveal(); for (int p = 0; p < 6; p++) Assert.IsTrue(Contains(Visible(m, p), secret));
        }

        [Test]
        public void WeeklyAllowanceConsumptionIsPrivateBeforeDossier()
        {
            var a = LineTest.WithTrick(TrickType.HatDinle); var b = LineTest.WithTrick(TrickType.HatDinle);
            a.Engine.DrainEvents(); b.Engine.DrainEvents();
            a.Ok(0, LineVerb.UseTrick, 1); a.Ok(0, LineVerb.TipOff, 1);
            Assert.AreEqual(0, a.View().Mine.TricksLeftThisWeek); Assert.AreEqual(0, a.View().Mine.TipsLeftThisWeek);
            for (int p = 1; p < 4; p++) CollectionAssert.AreEqual(Visible(a, p), Visible(b, p));
            Assert.IsEmpty(a.Engine.DrainEvents().Where(e => e.IsVisibleTo(2)));
        }

        [TestCase(RoundType.PrimHavuzu, "SecondPartners")]
        [TestCase(RoundType.KayipRapor, "NoAlibi")]
        [TestCase(RoundType.OrtakSifre, "Digits")]
        public void NewRoundSecretsPassEncodedSentinelScan(RoundType type, string field)
        {
            const int sentinel = 0x65247A31;
            var s = LineTest.MakeSetup(5); s.ForceRounds = new List<RoundType> { type }; s.ForceHidden = new List<int> { 0 };
            var m = new LineTest(s); m.Until(LinePhase.RoundIntro);
            object state = m.Engine.GetType().GetField("round", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(m.Engine);
            object value = state.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(state);
            if (value is int[] array) array[0] = sentinel; else ((List<int>)value).Add(sentinel);
            int leftNeighbor = type == RoundType.OrtakSifre ? m.View().Round.PositionOwners[(m.View().Round.DigitPosition + 4) % 5] : -1;
            byte[] secret = BitConverter.GetBytes(sentinel);
            for (int p = 0; p < 5; p++)
                Assert.AreEqual(p == 0 || p == leftNeighbor, Contains(Visible(m, p), secret), "Recipient " + p);
        }
    }
}
