using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    [TestFixture]
    public sealed class LineOfficeTests
    {
        [Test]
        public void CallRingsAnswersAndHangsUpFromEitherSide()
        {
            var m = new LineTest(); m.Ok(0, LineVerb.Call, 1);
            Assert.AreEqual(CallStatus.Dialing, m.View().Mine.Phone.Status); Assert.AreEqual(CallStatus.Ringing, m.View(1).Mine.Phone.Status);
            Assert.AreEqual(LineFailure.AlreadyInCall, m.Do(0, LineVerb.Call, 2).Failure);
            Assert.AreEqual(LineFailure.Busy, m.Do(2, LineVerb.Call, 1).Failure);
            m.Ok(1, LineVerb.Answer); int first = m.Engine.ConnectedCallId(0); Assert.GreaterOrEqual(first, 1);
            Assert.AreEqual(first, m.Engine.ConnectedCallId(1)); Assert.AreEqual(1, m.Engine.CallPartner(0));
            m.Advance(7); m.Ok(1, LineVerb.HangUp); Assert.AreEqual(-1, m.Engine.CallPartner(0));
            Assert.AreEqual(CallEndReason.HungUp, m.View().Mine.CallLog.Last().EndReason);
            m.Ok(1, LineVerb.Call, 0); m.Ok(0, LineVerb.Answer); Assert.Greater(m.Engine.ConnectedCallId(0), first);
            m.Ok(1, LineVerb.HangUp); Assert.AreEqual(CallStatus.Idle, m.View().Mine.Phone.Status);
        }

        [TestCase(LineVerb.Decline, CallEndReason.Declined)] [TestCase(LineVerb.HangUp, CallEndReason.HungUp)]
        public void RingingCanDeclineOrCallerCancel(LineVerb verb, CallEndReason reason)
        {
            var m = new LineTest(); m.Ok(0, LineVerb.Call, 1); m.Ok(verb == LineVerb.Decline ? 1 : 0, verb);
            Assert.AreEqual(CallStatus.Idle, m.View().Mine.Phone.Status); Assert.AreEqual(CallStatus.Idle, m.View(1).Mine.Phone.Status);
            var events = m.Engine.DrainEvents().Where(e => e.Type == LineEventType.CallEnded).ToList();
            Assert.AreEqual(2, events.Count); Assert.IsTrue(events.All(e => e.A == (int)reason && e.Audience == LineAudience.Player));
        }

        [Test]
        public void RingTimesOutAtAbsoluteDeadline()
        {
            var m = new LineTest(); m.Ok(0, LineVerb.Call, 1); double until = m.View(1).Mine.Phone.RingUntil;
            m.Advance(until - m.Now - .01); Assert.AreEqual(CallStatus.Ringing, m.View(1).Mine.Phone.Status);
            m.Advance(.01); Assert.AreEqual(CallStatus.Idle, m.View(1).Mine.Phone.Status);
            var ended = m.Engine.DrainEvents().Where(e => e.Type == LineEventType.CallEnded).ToList();
            Assert.IsTrue(ended.All(e => e.Time == until && e.A == (int)CallEndReason.Missed));
        }

        [Test]
        public void CallsEndOnAbsenceAndMeetingsAndRejectAbsentCallees()
        {
            var m = new LineTest(); m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer);
            m.Engine.SetConnected(1, false, m.Now); Assert.AreEqual(CallEndReason.Disconnected, m.View().Mine.CallLog.Last().EndReason);
            Assert.AreEqual(LineFailure.Absent, m.Do(0, LineVerb.Call, 1).Failure);
            m.Engine.SetConnected(1, true, m.Now); m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer);
            m.Until(LinePhase.MeetingDossier); Assert.AreEqual(-1, m.Engine.ConnectedCallId(0));
            Assert.AreEqual(CallEndReason.PhaseEnd, m.View().Mine.CallLog.Last().EndReason);
            Assert.AreEqual(LineFailure.WrongPhase, m.Do(0, LineVerb.Call, 1).Failure);
        }

        [Test]
        public void MailLimitsTemplatesRecipientsAndMorningMemo()
        {
            var m = new LineTest(); Assert.AreEqual(MailKind.Manager, m.View().Mine.Inbox.Single().Kind);
            Assert.AreEqual(LineIds.Manager, m.View().Mine.Inbox.Single().From);
            m.Ok(0, LineVerb.SendMail, 1, 7, 2); var mail = m.View(1).Mine.Inbox.Last();
            Assert.AreEqual(MailKind.Personal, mail.Kind); Assert.AreEqual(0, mail.From); Assert.AreEqual(7, mail.TemplateId);
            Assert.AreEqual(2, mail.About); Assert.IsFalse(mail.ToAll);
            Assert.AreEqual(1, m.View(2).Mine.Inbox.Count);
            for (int i = 1; i < m.Config.MailsPerDay; i++) m.Ok(0, LineVerb.SendMail, -1, i);
            Assert.AreEqual(LineFailure.LimitReached, m.Do(0, LineVerb.SendMail, 1, 0).Failure);
            Assert.AreEqual(LineFailure.BadParam, m.Do(1, LineVerb.SendMail, 0, m.Config.MailTemplates).Failure);
            m.Until(LinePhase.Morning, 1); Assert.AreEqual(0, m.View().Mine.MailsSentToday);
        }

        [Test]
        public void CoffeeHidesRoundAndBlocksSubmitUntilExpiry()
        {
            var m = LineTest.WithTrick(TrickType.KahveDok); m.Ok(0, LineVerb.UseTrick, 1);
            Assert.AreEqual(m.Now + m.Config.CoffeeSeconds, m.View(1).Mine.ComputerBlockedUntil);
            Assert.AreEqual(TrickType.KahveDok, m.View(1).Mine.ComputerBlockedBy);
            Assert.IsTrue(m.View(1).Round == null || (m.View(1).Round.Partner == -1 && m.View(1).Round.TopicId == -1 && m.View(1).Round.ClueInnocent == -1 && m.View(1).Round.Digit == -1));
            Assert.AreEqual(LineFailure.ComputerBlocked, m.Do(1, LineVerb.SubmitChoice, 0, 0).Failure);
            Assert.AreEqual(LineFailure.AlreadyUsed, m.Do(0, LineVerb.UseTrick, 2).Failure);
            m.Advance(m.Config.CoffeeSeconds); m.Ok(1, LineVerb.SubmitChoice, 0, 0);
            Assert.LessOrEqual(m.View(1).Mine.ComputerBlockedUntil, m.Now);
        }

        [Test]
        public void PullPlugDropsCallAndDeadPhoneReportsBusyWithoutActorLeak()
        {
            var m = LineTest.WithTrick(TrickType.FisCek); m.Ok(1, LineVerb.Call, 2); m.Ok(2, LineVerb.Answer);
            m.Ok(0, LineVerb.UseTrick, 1); Assert.AreEqual(-1, m.Engine.ConnectedCallId(2));
            Assert.AreEqual(CallEndReason.LineCut, m.View(1).Mine.CallLog.Last().EndReason);
            Assert.AreEqual(LineFailure.PhoneDead, m.Do(1, LineVerb.Call, 2).Failure);
            Assert.AreEqual(LineFailure.Busy, m.Do(3, LineVerb.Call, 1).Failure);
            foreach (var e in m.Engine.DrainEvents().Where(e => e.Type == LineEventType.TrickHit))
            {
                Assert.AreEqual(-1, e.Actor); Assert.IsFalse(e.IsVisibleTo(3));
            }
            m.Advance(m.Config.PhoneDeadSeconds); m.Ok(1, LineVerb.Call, 2);
        }

        [Test]
        public void InternetBlocksEverybodyElseAndDelaysNewMail()
        {
            var m = LineTest.WithTrick(TrickType.InternetKes); int before = m.View(1).Mine.Inbox.Count;
            m.Ok(0, LineVerb.UseTrick, 1); Assert.AreEqual(before, m.View(1).Mine.Inbox.Count);
            Assert.LessOrEqual(m.View().Mine.ComputerBlockedUntil, m.Now);
            for (int p = 1; p < 4; p++) Assert.AreEqual(m.Now + m.Config.InternetSeconds, m.View(p).Mine.ComputerBlockedUntil);
            m.Ok(0, LineVerb.SendMail, 1, 7, 2);
            Assert.AreEqual(before, m.View(1).Mine.Inbox.Count);
            Assert.IsTrue(m.Engine.DrainEvents().Where(e => e.Type == LineEventType.InternetDown).All(e => e.Actor == -1));
            m.Advance(m.Config.InternetSeconds); Assert.AreEqual(before + 2, m.View(1).Mine.Inbox.Count);
            Assert.AreEqual(MailKind.System, m.View(1).Mine.Inbox[before].Kind);
            Assert.AreEqual(MailKind.Personal, m.View(1).Mine.Inbox[before + 1].Kind);
        }

        [Test]
        public void FakeMailShowsApparentSenderAndDossierTraceOnly()
        {
            var m = LineTest.WithTrick(TrickType.SahteEposta); int ownCount = m.View().Mine.Inbox.Count;
            m.Ok(0, LineVerb.UseTrick, 1, 7, 2); Assert.AreEqual(ownCount, m.View().Mine.Inbox.Count);
            for (int p = 1; p < 4; p++)
            {
                var mail = m.View(p).Mine.Inbox.Last(); Assert.AreEqual(1, mail.From); Assert.IsTrue(mail.ToAll);
                Assert.AreEqual(MailKind.Personal, mail.Kind); Assert.AreEqual(7, mail.TemplateId); Assert.AreEqual(2, mail.About);
            }
            m.Until(LinePhase.MeetingDossier); var trace = m.View().Meeting.Dossier.Single(d => d.Kind == DossierKind.TrickTrace);
            Assert.AreEqual(7, trace.TemplateId); Assert.IsFalse(trace.Named); Assert.AreEqual(-1, trace.Player);
        }

        [Test]
        public void WiretapWindowIsPrivateAndUnnamedTapLeavesNoTrace()
        {
            var m = LineTest.WithTrick(TrickType.HatDinle); m.Ok(0, LineVerb.UseTrick, 1); var buffer = new int[6];
            Assert.AreEqual(1, m.Engine.TappersOf(1, buffer)); Assert.AreEqual(0, buffer[0]);
            Assert.AreEqual(1, m.View().Mine.WiretapTarget); Assert.AreEqual(-1, m.View(1).Mine.WiretapTarget);
            Assert.AreEqual(0, m.Engine.TappersOf(2, buffer));
            m.Advance(m.Config.WiretapSeconds); Assert.AreEqual(0, m.Engine.TappersOf(1, buffer));
            Assert.AreEqual(-1, m.View().Mine.WiretapTarget);
            m.Until(LinePhase.MeetingDossier); Assert.IsFalse(m.View().Meeting.Dossier.Any(d => d.Trick == TrickType.HatDinle));
        }

        [TestCase(0f)] [TestCase(1f)]
        public void ConfirmedTipNamesTraceAndCaughtBlameIsAppliedExactlyOnce(float traceChance)
        {
            var m = LineTest.WithTrick(TrickType.KahveDok, c => c.Profiles.ForEach(p => p.TraceChance = traceChance));
            m.Ok(0, LineVerb.UseTrick, 1); m.Ok(2, LineVerb.TipOff, 0);
            Assert.AreEqual(LineFailure.AlreadyUsed, m.Do(2, LineVerb.TipOff, 1).Failure);
            m.Until(LinePhase.MeetingDossier); var profile = m.Config.Profile(m.View().ManagerProfileId);
            Assert.AreEqual(m.Config.StartBlame + profile.TrickCaughtBlame + m.Config.ConfirmedTipBlame, m.Player(0).Blame);
            var trace = m.View().Meeting.Dossier.Single(d => d.Kind == DossierKind.TrickTrace);
            Assert.IsTrue(trace.Named); Assert.AreEqual(0, trace.Player);
            var tip = m.View().Meeting.Dossier.Single(d => d.Kind == DossierKind.TipResult);
            Assert.AreEqual(0, tip.Player); Assert.AreEqual(1, tip.Arg); Assert.AreEqual(-1, tip.Victim);
        }

        [Test]
        public void FalseTipBlamesAndNamesTipper()
        {
            var m = LineTest.WithTrick(TrickType.KahveDok); m.Ok(2, LineVerb.TipOff, 0);
            m.Until(LinePhase.MeetingDossier); Assert.AreEqual(m.Config.StartBlame + m.Config.FalseTipBlame, m.Player(2).Blame);
            var tip = m.View().Meeting.Dossier.Single(d => d.Kind == DossierKind.TipResult);
            Assert.AreEqual(0, tip.Player); Assert.AreEqual(0, tip.Arg); Assert.AreEqual(2, tip.Victim);
        }

        [TestCase(false)] [TestCase(true)]
        public void BusyAndDeadCalleeGetsPrivateMissedCall(bool dead)
        {
            var m = LineTest.WithTrick(TrickType.FisCek);
            if (dead) m.Ok(0, LineVerb.UseTrick, 1);
            else { m.Ok(1, LineVerb.Call, 2); m.Ok(2, LineVerb.Answer); }
            m.Engine.DrainEvents(); Assert.AreEqual(LineFailure.Busy, m.Do(3, LineVerb.Call, 1).Failure);
            var notice = m.Engine.DrainEvents().Single(e => e.Type == LineEventType.MissedCall);
            Assert.AreEqual(3, notice.Actor); Assert.IsTrue(notice.IsVisibleTo(1));
            Assert.IsFalse(notice.IsVisibleTo(0)); Assert.IsFalse(notice.IsVisibleTo(3));
        }

        [TestCase(WeekLength.Short, 1)] [TestCase(WeekLength.Full, 2)]
        public void WeeklyTrickAndTipAllowancesDoNotResetAtMorning(WeekLength length, int allowance)
        {
            var s = LineTest.MakeSetup(); s.Length = length;
            s.History = new LineSessionHistory { WeeksPlayed = 1 };
            s.ForceTricks = new List<TrickType> { TrickType.KahveDok };
            var m = new LineTest(s);
            for (int day = 0; day < allowance; day++)
            {
                m.Until(LinePhase.RoundPlay, day);
                Assert.AreEqual(allowance - day, m.View().Mine.TricksLeftThisWeek);
                m.Ok(0, LineVerb.UseTrick, 1);
                Assert.AreEqual(LineFailure.AlreadyUsed, m.Do(0, LineVerb.UseTrick, 2).Failure);
                if (day == 0) m.Ok(0, LineVerb.TipOff, 2);
            }
            m.Until(LinePhase.RoundPlay, allowance);
            Assert.AreEqual(0, m.View().Mine.TricksLeftThisWeek);
            Assert.AreEqual(0, m.View().Mine.TipsLeftThisWeek);
            Assert.IsFalse(m.View().Mine.TrickUsedToday); Assert.IsFalse(m.View().Mine.TipUsedToday);
            Assert.AreEqual(LineFailure.LimitReached, m.Do(0, LineVerb.UseTrick, 1).Failure);
            Assert.AreEqual(LineFailure.LimitReached, m.Do(0, LineVerb.TipOff, 1).Failure);
        }

        [Test]
        public void FirstWeekTipLineUnlocksOnThirdDayIndependentlyOfTricks()
        {
            var m = new LineTest(); m.Until(LinePhase.RoundPlay, 1);
            Assert.IsTrue(m.View().Features.TricksUnlocked); Assert.IsFalse(m.View().Features.TipLineUnlocked);
            Assert.AreEqual(LineFailure.NotUnlocked, m.Do(0, LineVerb.TipOff, 1).Failure);
            m.Until(LinePhase.Morning, 2); Assert.IsTrue(m.View().Features.TipLineUnlocked);
            m.Ok(0, LineVerb.TipOff, 1);
        }
    }
}
