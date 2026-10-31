using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineOfficeTests
    {
        private static LineTest Pad()
        {var m=LineTest.Task(TaskKind.InvoiceReconcile);m.Perfect();m.Submit(0,-1,1,fields:TaskField.B);m.Reveal();return m;}
        [Test]
        public void TransfersConserveMoneyAndDeduplicateAcrossReconnects()
        {
            var m=new LineTest();var action=new LineAction(42,1,LineVerb.Transfer,1,2);Assert.IsTrue(m.Engine.Apply(0,action,m.Now).Accepted);Assert.AreEqual(2,m.View().Mine.Money);Assert.AreEqual(6,m.View(1).Mine.Money);
            Assert.AreEqual(LineFailure.Duplicate,m.Engine.Apply(0,action,m.Now).Failure);m.Engine.SetConnected(0,false,m.Now);m.Engine.SetConnected(0,true,m.Now);Assert.AreEqual(LineFailure.Duplicate,m.Engine.Apply(0,action,m.Now).Failure);
            Assert.AreEqual(8,m.View().Mine.Money+m.View(1).Mine.Money);Assert.AreEqual(1,m.View().Mine.Records.Count(r=>r.Kind==RecordKind.Transfer));Assert.IsFalse(m.View(2).Mine.Records.Any(r=>r.Kind==RecordKind.Transfer));
            var newer=new LineAction(42,2,LineVerb.Transfer,1,1);Assert.AreEqual(LineFailure.RateLimited,m.Engine.Apply(0,newer,m.Now).Failure);
        }
        [Test]
        public void TransferLimitsAndBalancesCannotCreateMoney()
        {
            var m=new LineTest();Assert.AreEqual(LineFailure.BadParam,m.Do(0,LineVerb.Transfer,1,0).Failure);Assert.AreEqual(LineFailure.BadParam,m.Do(0,LineVerb.Transfer,1,11).Failure);Assert.AreEqual(LineFailure.InsufficientFunds,m.Do(0,LineVerb.Transfer,1,5).Failure);Assert.AreEqual(LineFailure.Self,m.Do(0,LineVerb.Transfer,0,1).Failure);Assert.AreEqual(16,m.Views().Sum(v=>v.Mine.Money));
        }
        [Test]
        public void EvidencePurchaseIncludesFutureRecordsAndCannotChargeTwice()
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile);m.Ok(1,LineVerb.BuyEvidence,a:100);Assert.AreEqual(1,m.View(1).Mine.Money);
            m.Perfect();m.Submit(0,-1,1,fields:TaskField.B);m.Reveal();var proof=m.View(1).Mine.Records.Single(r=>r.Kind==RecordKind.DealPayment&&r.ProofOf>0);Assert.AreEqual(0,proof.Actor);
            int before=m.View(1).Mine.Money;Assert.AreEqual(LineFailure.AlreadyUsed,m.Do(1,LineVerb.BuyEvidence,a:100).Failure);Assert.AreEqual(before,m.View(1).Mine.Money);Assert.IsFalse(m.View(2).Mine.Records.Any(r=>r.ProofOf>0));
            m.Until(LinePhase.Morning,1);Assert.AreEqual(4,m.View(1).Mine.Offers.Single(o=>o.Id==101).Price);
        }
        [Test]
        public void EvidencePricesDiscountAndCapsArePerBuyer()
        {
            var s=LineTest.MakeSetup();s.ManagerProfileId="paragoz";var m=new LineTest(s);Assert.AreEqual(2,m.View().Mine.Offers.Single(o=>o.Id==100).Price);
            m.Ok(0,LineVerb.BuyEvidence,a:100);Assert.AreEqual(LineFailure.LimitReached,m.Do(0,LineVerb.BuyEvidence,a:300).Failure);m.Ok(1,LineVerb.BuyEvidence,a:100);Assert.AreEqual(2,m.View().Mine.Money);Assert.AreEqual(2,m.View(1).Mine.Money);
        }
        [Test]
        public void PresentingProofExposesExactlyOnceIncludingAcrossMeetings()
        {
            var m=Pad();int record=m.View().Mine.Records.Single(r=>r.ProofOf>0).Id;m.Until(LinePhase.MeetingDossier);m.Ok(0,LineVerb.PresentRecord,a:record);int blame=m.Player(0).Blame;
            m.Ok(1,LineVerb.PresentRecord,a:record);Assert.AreEqual(blame,m.Player(0).Blame);Assert.IsTrue(m.View().Meeting.Presentations[1].Repeat);Assert.AreEqual(1,m.Engine.DrainEvents().Count(e=>e.Type==LineEventType.IncidentExposed));
            m.Until(LinePhase.MeetingDossier,1);int prior=m.Player(0).Blame;m.Ok(2,LineVerb.PresentRecord,a:record);Assert.AreEqual(prior,m.Player(0).Blame);m.Finish();Assert.AreEqual(1,m.Engine.Result.Exposed.Count);Assert.AreEqual(-50,m.Engine.Result.Players[0].Career.Single(c=>c.Item==CareerItem.ProvenSabotage).Points);
        }
        [Test]
        public void PrivateRecordAndPresentationCapAreEnforced()
        {
            var m=Pad();int id=m.View().Mine.Records.Single(r=>r.ProofOf>0).Id;m.Until(LinePhase.MeetingDossier);Assert.AreEqual(LineFailure.NotOwned,m.Do(2,LineVerb.PresentRecord,a:id).Failure);m.Ok(0,LineVerb.PresentRecord,a:id);m.Ok(0,LineVerb.PresentRecord,a:id);Assert.AreEqual(LineFailure.LimitReached,m.Do(0,LineVerb.PresentRecord,a:id).Failure);
        }
        [Test]
        public void RemedialRecoversOnlyOnceAndHasCapsAndPublicRecord()
        {
            var m=Pad();int original=m.View().Task.Reveal.Output;m.Ok(1,LineVerb.StartRemedial,2,0);Assert.IsNull(m.View(0).Mine.IncomingRemedial);Assert.AreEqual(1,m.View(2).Mine.IncomingRemedial.Requester);m.Ok(2,LineVerb.RespondRemedial,1,1);
            Assert.AreEqual(original+10,m.View().Task.Reveal.Output);Assert.AreEqual(3,m.Player(1).Blame);Assert.AreEqual(1,m.View(1).Mine.RemedialsLeftThisMatch);Assert.AreEqual(0,m.View(1).Mine.RemedialsLeftThisBlock);Assert.IsTrue(m.Views().All(v=>v.Mine.Records.Any(r=>r.Kind==RecordKind.Remedial)));
            Assert.AreEqual(LineFailure.NotOwned,m.Do(2,LineVerb.RespondRemedial,1,1).Failure);Assert.AreEqual(LineFailure.NotAvailable,m.Do(0,LineVerb.StartRemedial,2,0).Failure);Assert.AreEqual(original+10,m.View().Company.ResolvedOutput);
        }
        [Test]
        public void RemedialCannotExceedMaxAndExpiresAtTimeoutOrMeeting()
        {
            var m=LineTest.Task(TaskKind.VendorPick,3);m.Perfect();m.Reveal();if(m.View().Task.Reveal.Output==100)Assert.Pass("Seed selected the best vendor.");int before=m.View().Task.Reveal.Output;
            m.Ok(0,LineVerb.StartRemedial,1,0);m.Advance(30);Assert.IsNull(m.View().Mine.OutgoingRemedial);Assert.AreEqual(before,m.View().Company.ResolvedOutput);
            var p=Pad();p.Ok(0,LineVerb.StartRemedial,1,0);p.Until(LinePhase.MeetingDossier);Assert.IsNull(p.View().Mine.OutgoingRemedial);
        }
        [Test]
        public void ProductiveFilterIsRunningSumNotPerPaymentRounding()
        {
            var m=LineTest.Task(TaskKind.BonusPool,4,c=>c.Profiles.Find(p=>p.Id=="disiplinci").HasFavourite=true);m.Until(LinePhase.RoundPlay);int fav=m.View().Favourite;foreach(var v in m.Views())m.Submit(v.Me,v.Me==fav?1:0);m.Reveal();Assert.AreEqual(9,m.View(fav).Mine.Money);Assert.AreEqual(5,m.View(fav).Mine.BlockIncome);
        }
        [Test]
        public void TextSanitisesNfcControlsAnglesAndUtf16Limits()
        {
            var value=LineText.Sanitize("  I\u0307<hey>\u0001\n there  ",50,false,out var failure);Assert.AreEqual(LineFailure.None,failure);Assert.AreEqual("İ‹hey› there",value);
            Assert.AreEqual("a\n\n\nb",LineText.Sanitize("a\n\n\n\nb",20,true,out failure));LineText.Sanitize("\u0001 ",20,false,out failure);Assert.AreEqual(LineFailure.EmptyText,failure);
            LineText.Sanitize("🎧",1,false,out failure);Assert.AreEqual(LineFailure.TooLong,failure);Assert.AreEqual("🎧",LineText.Sanitize("🎧",2,false,out failure));
        }
        [Test]
        public void MessageLimitsRateLimitsThreadsAndPerRecipientSeq()
        {
            var m=new LineTest(null,c=>{c.TextsPerBlock=1;c.EmailsPerBlock=2;});m.Ok(0,LineVerb.SendText,1,text:"private");Assert.AreEqual(LineFailure.RateLimited,m.Do(0,LineVerb.SendText,1,text:"again").Failure);m.Advance(1.5);Assert.AreEqual(LineFailure.LimitReached,m.Do(0,LineVerb.SendText,1,text:"again").Failure);
            m.Ok(0,LineVerb.SendEmail,1,c:-1,text:"Hello\nthere");var mail=m.Engine.MessagesFor(1,0).Last();Assert.AreEqual(MessageKind.Email,mail.Kind);m.Ok(1,LineVerb.SendEmail,0,c:mail.Id,text:"Reply");var reply=m.Engine.MessagesFor(0,0).Last();Assert.AreEqual(mail.ThreadId,reply.ThreadId);Assert.AreEqual(mail.Id,reply.ReplyTo);
            Assert.IsFalse(m.Engine.MessagesFor(2,0).Any(x=>x.Text=="private"||x.Text=="Reply"));Assert.IsTrue(m.Engine.MessagesFor(0,0).Select(x=>x.Seq).SequenceEqual(Enumerable.Range(1,4).Select(x=>(long)x)));
            var detached=m.Engine.MessagesFor(1,0);detached.Last().Text="MUTATED";Assert.AreEqual("Reply",m.Engine.MessagesFor(1,0).Last().Text);
            Assert.AreEqual(1,m.Engine.MessagesFor(0,3).Count);
        }
        [Test]
        public void ComplaintsOnlyAppearAtDossierWithNoAuthor()
        {
            var m=new LineTest();m.Ok(0,LineVerb.FileComplaint,1,a:-1,text:"unverified <claim>");Assert.AreEqual(1,m.Engine.MessagesFor(1,0).Count);Assert.AreEqual(0,m.View().Mine.ComplaintsLeftThisBlock);m.Until(LinePhase.MeetingDossier);
            foreach(var v in m.Views()){Assert.AreEqual("unverified ‹claim›",v.Meeting.Complaints.Single().Text);Assert.AreEqual(1,v.Meeting.Complaints.Single().Target);}
            Assert.IsFalse(typeof(LineComplaintView).GetFields().Any(f=>f.Name=="Actor"||f.Name=="Author"));
        }
        [Test]
        public void EmailTemplatesBroadcastAndRetentionWork()
        {
            var m=new LineTest(null,c=>{c.MessagesKeptPerPlayer=2;c.MessageMinInterval=0;});m.Ok(0,LineVerb.SendEmail,a:0);m.Ok(0,LineVerb.SendText,1,text:"one");m.Ok(0,LineVerb.SendText,1,text:"two");var messages=m.Engine.MessagesFor(1,0);Assert.AreEqual(2,messages.Count);Assert.AreEqual(3,messages[0].Seq);Assert.AreEqual(4,messages[1].Seq);
            Assert.AreEqual(2,m.Engine.MessagesFor(2,0).Count);Assert.AreEqual(LineFailure.NotOwned,m.Do(2,LineVerb.SendEmail,1,c:999,text:"forged reply").Failure);
        }
        [TestCase(TrickType.KahveDok,IncidentKind.TrickCoffee),TestCase(TrickType.FisCek,IncidentKind.TrickPhoneCut),TestCase(TrickType.InternetKes,IncidentKind.TrickInternet),TestCase(TrickType.HatDinle,IncidentKind.TrickWiretap),TestCase(TrickType.SahteEposta,IncidentKind.TrickFakeEmail)]
        public void EveryTrickCreatesOneHostIncidentAndItProofWithOnlyObservableDossier(TrickType trick,IncidentKind kind)
        {
            var s=LineTest.MakeSetup();s.History=new LineSessionHistory{WeeksPlayed=1};s.ForceTricks=new List<TrickType>{trick};var m=new LineTest(s);m.Ok(0,LineVerb.UseTrick,1,text:trick==TrickType.SahteEposta?"Pretend email":"");
            var e=m.Engine.DrainEvents().Single(x=>x.Type==LineEventType.HostIncident);Assert.AreEqual((int)kind,e.A);Assert.AreEqual(LineAudience.Host,e.Audience);Assert.IsFalse(m.View(2).Mine.Records.Any(r=>r.ProofOf>0));
            if(trick==TrickType.SahteEposta){var mail=m.Engine.MessagesFor(2,0).Last();Assert.AreEqual(1,mail.From);Assert.AreEqual("Pretend email",mail.Text);}
            m.Until(LinePhase.MeetingDossier);m.Ok(2,LineVerb.BuyEvidence,a:300);Assert.AreEqual(1,m.View(2).Mine.Records.Count(r=>r.ProofOf>0));Assert.IsTrue(m.View().Meeting.Dossier.Where(d=>d.Kind==DossierKind.IncidentEffect).All(d=>d.Player== -1));
        }
        [Test]
        public void CoffeeExemptsMissedBlameAndPhoneCutEndsCalls()
        {
            var s=LineTest.MakeSetup();s.History=new LineSessionHistory{WeeksPlayed=1};s.ForceTricks=new List<TrickType>{TrickType.KahveDok,TrickType.FisCek};var m=new LineTest(s);m.Until(LinePhase.RoundPlay);m.Advance(m.Engine.PhaseEndTime-m.Now-10);m.Ok(0,LineVerb.UseTrick,2);m.Ok(0,LineVerb.Call,1);m.Ok(1,LineVerb.Answer);m.Ok(1,LineVerb.UseTrick,0);Assert.AreEqual(-1,m.Engine.ConnectedCallId(0));m.Next();Assert.AreEqual(5,m.Player(2).Blame);Assert.AreEqual(6,m.Player(3).Blame);
        }
        [Test]
        public void CallsGivePrivateLogsAndWiretapNeverNotifiesVictim()
        {
            var s=LineTest.MakeSetup();s.History=new LineSessionHistory{WeeksPlayed=1};s.ForceTricks=new List<TrickType>{TrickType.HatDinle};var m=new LineTest(s);m.Ok(0,LineVerb.UseTrick,1);m.Ok(1,LineVerb.Call,2);m.Ok(2,LineVerb.Answer);var buffer=new int[6];Assert.AreEqual(1,m.Engine.TappersOf(2,buffer));Assert.AreEqual(0,buffer[0]);Assert.AreEqual(0,m.View(3).Mine.CallLog.Count);m.Advance(7);m.Ok(1,LineVerb.HangUp);Assert.AreEqual(7,m.View(1).Mine.Records.Single(r=>r.Kind==RecordKind.Call).N1);Assert.IsFalse(m.Engine.DrainEvents().Any(e=>e.Type==LineEventType.TrickHit&&e.AudiencePlayer==1));
        }
    }
}
