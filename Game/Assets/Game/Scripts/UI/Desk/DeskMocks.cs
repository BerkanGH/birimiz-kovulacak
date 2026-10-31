using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI mock scenarios for the v4 desk UI (run with <c>-uimock "desk.*" -uimockout &lt;dir&gt;</c>). Every office state in
    /// Turkish (name) and English (name + ".en"): measuring and live productivity, morning call, task intros, the play
    /// view of each of the twelve tasks (role variants, sabotage confirmation), tutorial steps, messages, email (inbox, reply,
    /// compose), complaint, bank, evidence records and shop, remedial (start, incoming, outgoing), dirty tricks, phone
    /// (missed call), break and reveal cards. Views are built when their step runs so times follow the mock clock; the
    /// language is switched for the screenshot without changing the saved preference.
    /// </summary>
    public static class DeskMocks
    {
        static DeskUi Ui => DeskUi.I;
        static double Now => LineMock.Now;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // ---- HUD: measuring vs live productivity
            Reg("hud.measuring", s => s.Show(0.1f, e => Measuring(WithTask(V(LinePhase.RoundPlay, 4, e, 0, 70), TaskKind.InvoiceReconcile))).Do(0.6f, () => Ui.CloseComputer()).Do(0.9f, () => { }, true));
            Reg("hud.live", s => s.Show(0.1f, e => { var v = WithTask(V(LinePhase.RoundPlay, 6, e, 1, 60), TaskKind.BudgetSplit); v.Company.ProvisionalPermille = 520; v.Company.LastDelta = 40; return v; }).Do(0.6f, () => Ui.CloseComputer()).Do(0.9f, () => { }, true));
            Reg("hud.carried_break", s => s.Show(0.1f, e => { var v = V(LinePhase.Break, 4, e, 0, 40); v.Features.CarriedSeconds = 14; return v; }).Do(0.6f, () => Ui.Cards.HideAll()).Do(0.9f, () => { }, true));

            // ---- morning
            Reg("morning.ring.onboard", s => s.Show(0.1f, e => Onboard(V(LinePhase.Morning, 4, e, 0, 36, LineManagerIds.Disiplinci))).Do(1.2f, () => { }, true));
            Reg("morning.call", s => s.Show(0.1f, e => { var v = V(LinePhase.Morning, 6, e, 1, 18, LineManagerIds.Kayirmaci); v.Favourite = 2; return v; }).Do(0.5f, () => Ui.Cards.AnswerManager()).Do(4.5f, () => { }, true));
            Reg("morning.call.last", s => s.Show(0.1f, e => { var v = V(LinePhase.Morning, 4, e, 4, 18, LineManagerIds.Paranoyak); v.Features.IsLastDay = true; v.Company.ProvisionalPermille = 610; return v; }).Do(0.5f, () => Ui.Cards.AnswerManager()).Do(4.5f, () => { }, true));
            Reg("unlock.trick", s => s.Show(0.1f, e => { var v = V(LinePhase.Morning, 5, e, 1, 18); v.FirstWeek = true; return v; }).Do(0.5f, () => Ui.Cards.AnswerManager()).Do(0.9f, () => Ui.Cards.Back()).Do(1.0f, () => { }, true));

            // ---- intro card (known task) and tutorial steps (first-time task)
            Reg("intro.card", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundIntro, 4, e, 1, 10), TaskKind.CustomerSpec)).Do(1.3f, () => { }, true));
            Reg("intro.card.trio", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundIntro, 3, e, 1, 10), TaskKind.BonusPool)).Do(1.3f, () => { }, true));
            Reg("tutorial.invoice", s => s
                .Show(0.1f, e => { var v = WithTask(V(LinePhase.RoundIntro, 4, e, 0, 22), TaskKind.InvoiceReconcile); v.Features.FirstTimeTask = true; v.Features.OnboardingDay = true; v.FirstWeek = true; return v; })
                .Do(0.8f, () => { }, true)
                .Do(0.1f, () => Ui.Computer.Task.MockTutorialStep(1), true)
                .Do(0.1f, () => Ui.Computer.Task.MockTutorialStep(2), true)
                .Do(0.1f, () => Ui.Computer.Task.MockTutorialStep(3), true)
                .Do(0.1f, () => Ui.Computer.Task.MockTutorialStep(4), true));
            Reg("tutorial.archive", s => s
                .Show(0.1f, e => { var v = WithTask(V(LinePhase.RoundIntro, 5, e, 1, 22), TaskKind.ArchiveVisitor, 1); v.Features.FirstTimeTask = true; return v; })
                .Do(0.8f, () => Ui.Computer.Task.MockTutorialStep(2), true)
                .Do(0.1f, () => Ui.Computer.Task.MockTutorialStep(4), true));
            Reg("tutorial.server", s => s
                .Show(0.1f, e => { var v = WithTask(V(LinePhase.RoundIntro, 6, e, 1, 22), TaskKind.ServerCode, 1); v.Features.FirstTimeTask = true; return v; })
                .Do(0.8f, () => Ui.Computer.Task.MockTutorialStep(1), true)
                .Do(0.1f, () => Ui.Computer.Task.MockTutorialStep(3), true));

            // ---- play view of every task
            foreach (var kind in new[] { TaskKind.InvoiceReconcile, TaskKind.BudgetSplit, TaskKind.CustomerSpec, TaskKind.ReportReview, TaskKind.RoomBooking, TaskKind.ArchiveVisitor,
                                          TaskKind.BonusPool, TaskKind.ServerCode, TaskKind.PhishingCheck, TaskKind.ShiftCover, TaskKind.VendorPick, TaskKind.SupplySwap })
            {
                var k = kind;
                int players = k == TaskKind.BonusPool ? 3 : k == TaskKind.ServerCode ? 5 : k == TaskKind.BudgetSplit ? 6 : 4;
                Reg("task." + LineTasks.Id(k), s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, players, e, 1, 58), k)).Do(0.5f, () => Ui.Computer.Task.MockSelect(-1, DefaultPick(k)), true));
            }
            Reg("task.invoice_reconcile.sabotage_confirm", s => s
                .Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 4, e, 1, 58), TaskKind.InvoiceReconcile))
                .Do(0.4f, () => { Ui.Computer.Task.MockSelect(0, 23); Ui.Computer.Task.MockSelect(1, 1); Ui.Computer.Task.MockConfirm(); }, true));
            Reg("task.report_review.waiting", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 4, e, 1, 40), TaskKind.ReportReview)).Do(0.4f, () => Ui.Computer.Task.MockSelect(0, 1), true));
            Reg("task.report_review.locked", s => s.Show(0.1f, e => { var v = WithTask(V(LinePhase.RoundPlay, 4, e, 1, 40), TaskKind.ReportReview, 0, true); v.Task.Controls[0].Enabled = true; v.Task.SubmittedFields = TaskField.A; v.Task.MyA = 0; return v; }).Do(1.0f, () => { }, true));
            Reg("task.archive_visitor.requester", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 5, e, 1, 50), TaskKind.ArchiveVisitor, 2)).Do(0.5f, () => Ui.Computer.Task.MockSelect(0, 1), true));
            Reg("task.server_code.offered", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 4, e, 1, 50), TaskKind.ServerCode, 1)).Do(0.5f, () => Ui.Computer.Task.MockSelect(0, 7140), true));
            Reg("task.phishing_check.insider", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 4, e, 1, 50), TaskKind.PhishingCheck, 1)).Do(0.5f, () => Ui.Computer.Task.MockSelect(0, 2), true));
            Reg("task.vendor_pick.insider", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 5, e, 1, 50), TaskKind.VendorPick, 1)).Do(0.5f, () => Ui.Computer.Task.MockSelect(0, 2), true));
            Reg("task.submitted", s => s.Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 4, e, 1, 30), TaskKind.BonusPool, 0, true)).Do(1.0f, () => { }, true));
            Reg("task.blocked", s => s.Show(0.1f, e => { var v = WithTask(V(LinePhase.RoundPlay, 4, e, 1, 50), TaskKind.InvoiceReconcile); v.Mine.ComputerBlockedUntil = Now + 20; v.Mine.ComputerBlockedBy = TrickType.KahveDok; return v; }).Do(1.0f, () => { }, true));
            Reg("task.offline", s => s.Show(0.1f, e => { var v = WithTask(V(LinePhase.RoundPlay, 4, e, 1, 50), TaskKind.InvoiceReconcile); v.Mine.ComputerBlockedUntil = Now + 20; v.Mine.ComputerBlockedBy = TrickType.InternetKes; return v; }).Do(1.0f, () => { }, true));

            // ---- messages
            Reg("msg.thread", s => s
                .Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 4, e, 1, 50), TaskKind.InvoiceReconcile))
                .Do(0.3f, () => { LineClient.I.ApplyMessages(1, TextThread(Loc.Language == "en")); Ui.OpenMessages(3); Ui.Computer.Messages.MockType(Loc.Language == "en" ? "Mine are 7 and 4" : "Benimkiler 7 ve 4"); }, true));
            Reg("msg.unread", s => s
                .Show(0.1f, e => WithTask(V(LinePhase.RoundPlay, 5, e, 1, 50), TaskKind.BudgetSplit))
                .Do(0.3f, () => { LineClient.I.ApplyMessages(1, TextThread(Loc.Language == "en")); Ui.CloseComputer(); }, false)
                .Do(0.6f, () => Ui.OpenComputer(DeskApp.Task), true));
            Reg("msg.ratelimit", s => s
                .Show(0.1f, e => { var v = V(LinePhase.Break, 4, e, 1, 30); v.Mine.NextMessageAt = Now + 30; v.Mine.TextsLeftThisBlock = 3; return v; })
                .Do(0.3f, () => { LineClient.I.ApplyMessages(1, TextThread(Loc.Language == "en")); Ui.Cards.HideAll(); Ui.OpenMessages(1); Ui.Computer.Messages.MockType("..."); }, true));

            // ---- email
            Reg("mail.inbox", s => s
                .Show(0.1f, e => V(LinePhase.Break, 5, e, 1, 30))
                .Do(0.3f, () => { LineClient.I.ApplyMessages(1, MailThread(Loc.Language == "en")); Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Mail); }, true));
            Reg("mail.reply", s => s
                .Show(0.1f, e => V(LinePhase.Break, 5, e, 1, 30))
                .Do(0.3f, () => { LineClient.I.ApplyMessages(1, MailThread(Loc.Language == "en")); Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Mail); })
                .Do(0.4f, () => Ui.Computer.Mail.MockReply(Loc.Language == "en" ? "Okay, I will pay you the coffee tomorrow." : "Tamam, kahveyi yarın ben ısmarlıyorum."), true));
            Reg("mail.compose", s => s
                .Show(0.1f, e => V(LinePhase.Break, 5, e, 1, 30))
                .Do(0.3f, () => { LineClient.I.ApplyMessages(1, MailThread(Loc.Language == "en")); Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Mail); Ui.Computer.Mail.MockNew(-1, Loc.T("mail.template.2")); }, true));

            // ---- complaint, bank, evidence
            Reg("complaint.preset", s => s.Show(0.1f, e => V(LinePhase.Break, 5, e, 1, 30)).Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Complaint); Ui.Computer.Complaint.MockFill(2, 0, null); }, true));
            Reg("complaint.free", s => s.Show(0.1f, e => V(LinePhase.Break, 4, e, 1, 30)).Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Complaint); Ui.Computer.Complaint.MockFill(1, -1, Loc.Language == "en" ? "Rearranged my desk plants. Again." : "Saksılarımı yine yerinden oynattı."); }, true));
            Reg("bank", s => s.Show(0.1f, e => { var v = V(LinePhase.Break, 5, e, 1, 30); v.Mine.Money = 7; v.Mine.BlockIncome = 5; v.Mine.Records.Add(LineMock.Rec(RecordKind.Transfer, RecordSource.Bank, 2, 0, 2, -1, -1, -1, TaskKind.None, -1)); return v; })
                .Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Bank); Ui.Computer.Bank.MockFill(3, 3); }, true));
            Reg("bank.capped", s => s.Show(0.1f, e => { var v = V(LinePhase.Break, 4, e, 1, 30); v.Mine.Money = 1; v.Mine.BlockIncome = 6; v.Mine.NextTransferAt = Now + 20; return v; })
                .Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Bank); Ui.Computer.Bank.MockFill(1, 1); }, true));
            Reg("evidence.records", s => s.Show(0.1f, e => Records(V(LinePhase.Break, 5, e, 1, 30))).Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Evidence); Ui.Computer.Evidence.MockTab(0); }, true));
            Reg("evidence.shop", s => s.Show(0.1f, e => Records(V(LinePhase.Break, 5, e, 1, 30))).Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Evidence); Ui.Computer.Evidence.MockTab(1); }, true));

            // ---- remedial
            Reg("remedial.start", s => s.Show(0.1f, e => Remedial(V(LinePhase.Break, 4, e, 1, 30), false, false)).Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Remedial); Ui.Computer.Remedial.MockPick(0, 2); }, true));
            Reg("remedial.incoming", s => s.Show(0.1f, e => Remedial(V(LinePhase.Break, 4, e, 1, 30), true, false)).Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Remedial); }, true));
            Reg("remedial.incoming.hud", s => s.Show(0.1f, e => Remedial(V(LinePhase.RoundReveal, 4, e, 1, 8), true, false)).Do(0.3f, () => Ui.Cards.HideAll(), true));
            Reg("remedial.outgoing", s => s.Show(0.1f, e => Remedial(V(LinePhase.Break, 4, e, 1, 30), false, true)).Do(0.3f, () => { Ui.Cards.HideAll(); Ui.OpenComputer(DeskApp.Remedial); }, true));

            // ---- dirty tricks
            Reg("trick.kahve", s => s.Show(0.1f, e => V(LinePhase.RoundPlay, 4, e, 1, 50)).Do(0.3f, () => { Ui.OpenComputer(DeskApp.Trick); Ui.Computer.Trick.MockPick(2, -1, -1); }, true));
            Reg("trick.fake_email", s => s.Show(0.1f, e => { var v = V(LinePhase.RoundPlay, 5, e, 1, 50); v.Mine.Trick = TrickType.SahteEposta; return v; })
                .Do(0.3f, () => { Ui.OpenComputer(DeskApp.Trick); Ui.Computer.Trick.MockPick(2, 4, 3); }, true));
            Reg("trick.used", s => s.Show(0.1f, e => { var v = V(LinePhase.RoundPlay, 4, e, 1, 50); v.Mine.TrickUsedToday = true; v.Mine.TricksLeftThisWeek = 0; return v; }).Do(0.3f, () => Ui.OpenComputer(DeskApp.Trick), true));

            // ---- phone
            Reg("phone.idle", s => s.Show(0.1f, e => V(LinePhase.RoundPlay, 5, e, 1, 50)).Do(0.3f, () => Ui.OpenPhone(), true));
            Reg("phone.missed", s => s.Show(0.1f, e => V(LinePhase.RoundPlay, 5, e, 1, 50))
                .Do(0.3f, () => Ui.CloseComputer())
                .Events(0.4f, Ev(LineEventType.MissedCall, actor: 3))
                .Do(0.5f, () => Ui.OpenPhone(), true));
            Reg("phone.busy", s => s.Show(0.1f, e => V(LinePhase.RoundPlay, 5, e, 1, 50))
                .Do(0.3f, () => Ui.CloseComputer())
                .Events(0.4f, Ev(LineEventType.CallBusy, target: 2))
                .Do(0.5f, () => Ui.OpenPhone(), true));
            Reg("phone.break", s => s.Show(0.1f, e => { var v = V(LinePhase.Break, 4, e, 1, 30); v.Mine.Phone.Status = CallStatus.Connected; v.Mine.Phone.Other = 2; v.Mine.Phone.Since = Now - 12; return v; }).Do(0.5f, () => Ui.OpenPhone(), true));
            Reg("phone.ringing", s => s.Show(0.1f, e => { var v = V(LinePhase.RoundPlay, 4, e, 1, 50); v.Mine.Phone.Status = CallStatus.Ringing; v.Mine.Phone.Other = 2; v.Mine.Phone.RingUntil = Now + 12; return v; }).Do(0.6f, () => Ui.CloseComputer(), true));

            // ---- break and reveal
            Reg("break", s => s.Show(0.1f, e => V(LinePhase.Break, 4, e, 1, 25)).Do(1.3f, () => { }, true));
            Reg("break.carried", s => s.Show(0.1f, e => { var v = V(LinePhase.Break, 4, e, 1, 25); v.Features.CarriedSeconds = 18; return v; }).Do(1.3f, () => { }, true));
            foreach (var kind in new[] { TaskKind.InvoiceReconcile, TaskKind.BudgetSplit, TaskKind.PhishingCheck, TaskKind.VendorPick, TaskKind.RoomBooking, TaskKind.ShiftCover, TaskKind.CustomerSpec })
            {
                var k = kind;
                Reg("reveal." + LineTasks.Id(k), s => s.Show(0.1f, e => Reveal(V(LinePhase.RoundReveal, 5, e, 1, 8), k, k == TaskKind.BudgetSplit)).Do(1.6f, () => { }, true));
            }
            Reg("reveal.low", s => s.Show(0.1f, e => { var v = Reveal(V(LinePhase.RoundReveal, 4, e, 1, 8), TaskKind.InvoiceReconcile, true); v.Task.Reveal.Output = 24; v.Company.ProvisionalPermille = 380; return v; }).Do(1.6f, () => { }, true));
            Reg("reveal.first", s => s.Show(0.1f, e => Measuring(Reveal(V(LinePhase.RoundReveal, 4, e, 0, 8), TaskKind.BonusPool, false))).Do(1.6f, () => { }, true));
        }

        // ------------------------------------------------------------------ plumbing

        sealed class Scn
        {
            public LineMockScenario S;
            public bool En;
            public Scn Show(float at, Func<bool, LineView> make, bool shot = false) { S.Do(at, () => { var v = make(En); v.Now = LineClient.I.Now; LineClient.I.ApplyView(v); }, shot); return this; }
            public Scn Do(float at, Action a, bool shot = false) { S.Do(at, a, shot); return this; }
            public Scn Events(float at, params LineEvent[] ev) { S.Do(at, () => LineClient.I.ApplyEvents(new List<LineEvent>(ev))); return this; }
        }

        /// <summary>Registers "desk.&lt;name&gt;" (Turkish) and "desk.&lt;name&gt;.en".</summary>
        static void Reg(string name, Action<Scn> build)
        {
            foreach (var en in new[] { false, true })
            {
                bool english = en;
                LineMock.Register("desk." + name + (english ? ".en" : ""), () =>
                {
                    var s = new LineMockScenario { Me = 0 };
                    s.Do(0f, () => { Rooms.Hovered = DeskTarget.None; Lang(english ? "en" : "tr"); Reset(); });
                    build(new Scn { S = s, En = english });
                    return s;
                });
            }
        }

        static void Reset()
        {
            var ui = Ui;
            if (ui == null) return;
            ui.State.Reset(-1);
            ui.CloseComputer();
            ui.ClosePhone();
            ui.Cards.HideAll();
            ui.Notes.Clear();
        }

        static void Lang(string want)
        {
            if (Loc.Language == want) return;
            bool had = PlayerPrefs.HasKey(Loc.PrefKey);
            string saved = PlayerPrefs.GetString(Loc.PrefKey, "");
            Loc.SetLanguage(want);
            if (had) PlayerPrefs.SetString(Loc.PrefKey, saved); else PlayerPrefs.DeleteKey(Loc.PrefKey);
            PlayerPrefs.Save();
        }

        static LineEvent Ev(LineEventType t, int actor = -1, int target = -1, int a = -1)
        {
            return new LineEvent { Type = t, Time = Now, Audience = LineAudience.All, Actor = actor, Target = target, A = a };
        }

        // ------------------------------------------------------------------ view builders

        static LineView V(LinePhase phase, int players, bool en, int day = 1, double left = 90, string mgr = LineManagerIds.Paragoz)
        {
            var v = LineMock.BaseView(phase, players, day, left);
            LineMock.SetNames(v, en);
            v.ManagerProfileId = mgr;
            for (int i = 0; i < v.Players.Count; i++) v.Players[i].Blame = new[] { 5, 7, 3, 6, 4, 8 }[i];
            return v;
        }

        static LineView WithTask(LineView v, TaskKind kind, int role = 0, bool submitted = false)
        {
            LineMock.WithTask(v, kind, role, submitted);
            for (int i = 0; i < v.Players.Count; i++) v.Players[i].Submitted = i == 1 || (submitted && i == 0);
            return v;
        }

        static LineView Measuring(LineView v)
        {
            v.Company = new LineCompanyView { ProvisionalPermille = -1, TargetPercent = 65, TasksScheduled = 6, ScheduledMax = 600 };
            return v;
        }

        static LineView Onboard(LineView v)
        {
            v.FirstWeek = true;
            v.Day = 0;
            v.Features.OnboardingDay = true;
            v.Features.TricksUnlocked = false;
            v.Company = new LineCompanyView { ProvisionalPermille = -1, TargetPercent = 65 };
            v.TodayTasks[0] = TaskKind.BonusPool; v.TodayTasks[1] = TaskKind.InvoiceReconcile;
            return v;
        }

        static LineView Reveal(LineView v, TaskKind kind, bool missed)
        {
            v.Task = LineMock.TaskView(kind, v.Players.Count, 0, true);
            v.TodayTasks[0] = kind;
            v.Task.Reveal = LineMock.RevealView(kind, v.Players.Count, kind == TaskKind.BudgetSplit ? 58 : 74, missed);
            v.TodayReveals.Add(v.Task.Reveal);
            return v;
        }

        static LineView Records(LineView v)
        {
            var m = v.Mine;
            m.Records.Add(LineMock.Rec(RecordKind.DealPayment, RecordSource.Bank, 2, 2, 3, -1, -1, 0, TaskKind.InvoiceReconcile, 5, false, 40));
            m.Records.Add(LineMock.Rec(RecordKind.TrickEffect, RecordSource.ITLog, LineIds.None, 0, (int)TrickType.KahveDok, -1, -1, 1, TaskKind.CustomerSpec, -1, false, 15));
            m.Records.Add(LineMock.Rec(RecordKind.AuditFigure, RecordSource.TaskSystem, LineIds.None, 1, 64, -1, -1, 2, TaskKind.ReportReview, -1, true, 8));
            m.Records.Add(LineMock.Rec(RecordKind.BadgeAccess, RecordSource.BadgeLog, 3, LineIds.None, -1, 1, -1, 3, TaskKind.ArchiveVisitor, 7, false, 5));
            m.Money = 6;
            m.Offers[2].Purchased = true;
            m.Offers[3].Available = false;
            return v;
        }

        static LineView Remedial(LineView v, bool incoming, bool outgoing)
        {
            var m = v.Mine;
            m.RemedialOptions.Clear(); m.RemedialOptions.Add(0); m.RemedialOptions.Add(1);
            v.TodayReveals.Add(LineMock.RevealView(TaskKind.InvoiceReconcile, v.Players.Count, 74, false));
            v.TodayReveals[0].Number = 0;
            var second = LineMock.RevealView(TaskKind.CustomerSpec, v.Players.Count, 40, false); second.Number = 1;
            v.TodayReveals.Add(second);
            if (incoming) m.IncomingRemedial = new LineRemedialView { Requester = 2, Cosigner = 0, TaskNumber = 1, Task = TaskKind.CustomerSpec, ExpiresAt = Now + 24 };
            if (outgoing) m.OutgoingRemedial = new LineRemedialView { Requester = 0, Cosigner = 3 % v.Players.Count, TaskNumber = 1, Task = TaskKind.CustomerSpec, ExpiresAt = Now + 21 };
            return v;
        }

        static int DefaultPick(TaskKind k)
        {
            switch (k)
            {
                case TaskKind.InvoiceReconcile: return 23;
                case TaskKind.BudgetSplit: return 3;
                case TaskKind.ServerCode: return 71409;
                case TaskKind.PhishingCheck: return 2;
                case TaskKind.ArchiveVisitor: return 7;
                default: return 1;
            }
        }

        static List<LineMessage> TextThread(bool en)
        {
            var l = new List<LineMessage>();
            string[] a = en ? new[] { "Hi, my lines are 7 and 4. What are yours?", "9 and 3. So the total is 23?", "Yes! Enter it, please.", "On it. Careful, Lee keeps calling me." }
                            : new[] { "Selam, benim kalemlerim 7 ve 4. Seninkiler?", "9 ve 3. Yani toplam 23, değil mi?", "Evet! Gir bakalım.", "Giriyorum. Dikkat et, Turuncu sürekli arıyor." };
            l.Add(LineMock.Msg(MessageKind.Text, 3, 0, a[0], ago: 60));
            l.Add(LineMock.Msg(MessageKind.Text, 0, 3, a[1], ago: 50));
            l.Add(LineMock.Msg(MessageKind.Text, 3, 0, a[2], ago: 40));
            l.Add(LineMock.Msg(MessageKind.Text, 3, 0, a[3], ago: 12));
            l.Add(LineMock.Msg(MessageKind.Text, 1, 0, en ? "Do not forget the meeting tonight <b>bold?</b>" : "Akşamki toplantıyı unutma çok iyi bir İŞ çıkardık ğüşiöç <b>kalın?</b>", ago: 8));
            return l;
        }

        static List<LineMessage> MailThread(bool en)
        {
            var l = new List<LineMessage>();
            var m1 = LineMock.Msg(MessageKind.Manager, LineIds.Manager, 0, en ? "Team: productivity is on my desk. Please keep it there." : "Ekip: verimlilik masamda. Orada kalsın lütfen.", ago: 300);
            l.Add(m1);
            var m2 = LineMock.Msg(MessageKind.Email, 2, 0, en ? "Can you cover for me on the budget? I will owe you a coffee." : "Bütçede beni kollar mısın? Bir kahve borcum olsun.", ago: 120);
            l.Add(m2);
            l.Add(LineMock.Msg(MessageKind.Email, 0, 2, en ? "Maybe. What is in it for me?" : "Belki. Bana ne var?", replyTo: m2.Id, thread: m2.ThreadId, ago: 90));
            l.Add(LineMock.Msg(MessageKind.Email, 2, 0, en ? "Half a coffee. Final offer." : "Yarım kahve. Son teklif.", replyTo: m2.Id, thread: m2.ThreadId, ago: 30));
            l.Add(LineMock.Msg(MessageKind.Email, 4, 0, en ? "Everyone: the printer is cursed. Do not touch it." : "Herkese: yazıcı lanetli. Dokunmayın.", toAll: true, ago: 20));
            l.Add(LineMock.Msg(MessageKind.System, LineIds.System, 0, en ? "IT: the internet will be unstable around noon." : "BT: öğlen civarı internet dalgalanabilir.", ago: 10));
            return l;
        }

    }
}
