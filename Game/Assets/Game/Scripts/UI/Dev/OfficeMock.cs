using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 office scenarios for the UI mock (<c>-uimock "v5.office*"</c>): the seated desk with the HUD strip and a use
    /// prompt, the computer's apps (task, transfer, messages, call log, rules), the desk phone ringing / in a recorded
    /// call (contacts, missed calls, the call pill and the recorder), the cork board, standing in the room with the door
    /// lamp, the whistle in a break, Weekend Shift, and English samples. Each step that changes the view is shot.
    /// Step 2 (task objects): <c>v5.office_cashbox*</c> (the Petty Cash box: choosing, outcomes, witness and whistle,
    /// the sole taker), <c>v5.office_roster*</c> (writing / rubbing out, the revealed roster), <c>v5.office_drawer</c>
    /// (the prim stack growing and shrinking, prim coming in, looking sharply right from the chair).
    /// </summary>
    public static class OfficeMockScenarios
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            LineMock.Register("v5.office_desk", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Intro()); Aim(0f, 15f); })
                .Do(1.6f, () => AimAt(Presenter().Office.Screen.transform.position), true)
                .Do(3.2f, () => { var v = Intro(); v.Mine.Prim = 6; Show(v); AimAt(Presenter().Office.Recorder.position + Vector3.up * 0.05f); }, true)
                .Do(4.8f, () => AimAt(Presenter().Office.CorkSurface.transform.position), true));

            LineMock.Register("v5.office_task", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Task(TaskKind.ClientBonus, 0, 4, LinePhase.TaskPlay, false)); Aim(0f, 15f); }, true)
                .Do(1.6f, () => OfficeUi.I.OpenComputer(OfficeComputer.App.Task), true)
                .Do(3.2f, () => { Texts(); OfficeUi.I.Computer.SetApp(OfficeComputer.App.Messages); }, true)
                .Do(4.8f, () => OfficeUi.I.Computer.SetApp(OfficeComputer.App.Transfer), true)
                .Do(6.4f, () => OfficeUi.I.Computer.SetApp(OfficeComputer.App.Calls), true)
                .Do(8.0f, () => OfficeUi.I.Computer.ShowRules(HowToPlayScreen.PageBonus), true));

            LineMock.Register("v5.office_ring", () => new LineMockScenario()
                .Do(0f, () => { Look(); var v = Task(TaskKind.PettyCash, 1, 5, LinePhase.TaskPlay, true); Ring(v, 2); Show(v); Aim(0f, 15f); })
                .Do(1.2f, () => { var v = Task(TaskKind.PettyCash, 1, 5, LinePhase.TaskPlay, true); Ring(v, 2); Show(v); Transfer(3, 2); }, true)
                .Do(2.8f, () => OfficeUi.I.OpenPhone(), true));

            LineMock.Register("v5.office_call", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Call(true)); })
                .Do(0.8f, () => { Show(Call(true)); OfficeUi.I.OpenPhone(); }, true)
                .Do(3.0f, () => { OfficeUi.I.CloseFocus(); Aim(18f, 22f); }, true)
                .Do(4.6f, () => { Show(Call(false)); AimAt(Presenter().Office.Recorder.position + Vector3.up * 0.04f); }, true));

            LineMock.Register("v5.office_board", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Board()); })
                .Do(1.2f, () => OfficeUi.I.OpenBoard(), true)
                .Do(2.8f, () => { OfficeUi.I.CloseFocus(); Aim(26f, -6f); }, true));

            // layout check of the small notes: I am Mor (bottom row), on the phone; stamps on two notes
            LineMock.Register("v5.office_board_tags", () => new LineMockScenario { Me = 5 }
                .Do(0f, () =>
                {
                    Look();
                    var v = Board();
                    v.Me = 5;
                    v.Players[5].Connected = true;
                    v.LineBoard.Add(new LineBoardCall { CallId = 32, Caller = 5, Callee = 4, Status = CallStatus.Connected, Start = LineMock.Now - 40 });
                    v.Players[2].Candidate = true; v.Players[3].Candidate = true; v.Players[3].Fired = true;
                    Show(v);
                })
                .Do(1.0f, () => OfficeUi.I.OpenBoard(), true));

            LineMock.Register("v5.office_witness", () => new LineMockScenario()
                .Do(0f, () =>
                {
                    Look();
                    var v = Task(TaskKind.PettyCash, 1, 4, LinePhase.Break, true);
                    v.Features.WhistleOpen = true; v.Mine.WitnessOf.Add(1);
                    Show(v);
                })
                .Do(1.0f, () => OfficeUi.I.OpenComputer(OfficeComputer.App.Task), true));

            LineMock.Register("v5.office_shift", () => new LineMockScenario()
                .Do(0f, () =>
                {
                    Look();
                    var v = Task(TaskKind.WeekendShift, 2, 6, LinePhase.TaskPlay, false);
                    v.Task.Submitted = true; v.Task.MyChoice = LineChoice.Volunteer;
                    v.Players[3].Submitted = true; v.Players[4].Submitted = true;
                    Show(v);
                })
                .Do(1.0f, () => OfficeUi.I.OpenComputer(OfficeComputer.App.Task), true));

            LineMock.Register("v5.office_standing", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(BreakBeforeMeeting()); })
                .Do(0.8f, () => Presenter().StandAt(new Vector2(-1.45f, -1.7f), 32f, 12f), true)
                .Do(2.4f, () => Presenter().StandAt(new Vector2(-0.2f, -0.9f), 95f, 6f), true)
                .Do(4.0f, () => Presenter().StandAt(new Vector2(1.45f, -0.6f), -110f, 8f), true)
                .Do(5.6f, () => { Presenter().StandAt(new Vector2(0.25f, 0.4f), 22f, 22f); }, true));

            // ---- step 2: the Petty Cash box
            LineMock.Register("v5.office_cashbox", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Cash(LinePhase.TaskPlay, 0, 0, false)); Aim(0f, 15f); })
                .Do(1.0f, () => AimAt(Presenter().Office.CashBox.position + Vector3.up * 0.09f), true)
                .Do(2.8f, () => OfficeUi.I.OpenObject(OfficeUi.Focus.CashBox), true)
                .Do(4.6f, () => Show(Cash(LinePhase.TaskPlay, LineChoice.Take, 0, true)), 0.55f)
                .Do(6.0f, () => { }, true)
                .Do(7.4f, () => { OfficeUi.I.CloseFocus(); }, 0.2f)
                .Do(8.2f, () => AimAt(Presenter().Office.CashBox.position + Vector3.up * 0.09f), true)
                .Do(9.8f, () => { OfficeUi.I.OpenObject(OfficeUi.Focus.CashBox); }, 0.9f)
                .Do(11.0f, () => Show(Cash(LinePhase.TaskPlay, LineChoice.Leave, 0, true)), true));

            LineMock.Register("v5.office_cashbox_reveal", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Cash(LinePhase.TaskReveal, LineChoice.Take, 0, true)); Aim(0f, 15f); })
                .Do(0.8f, () => OfficeUi.I.OpenObject(OfficeUi.Focus.CashBox), true)
                .Do(2.6f, () => Show(Cash(LinePhase.TaskReveal, 0, LineChoice.Take, true)), true)
                .Do(4.4f, () => Show(Cash(LinePhase.TaskReveal, LineChoice.Take, LineChoice.Take, true)), true)
                .Do(6.2f, () => Show(Cash(LinePhase.TaskReveal, 0, 0, true)), true)
                .Do(8.0f, () => { Show(Cash(LinePhase.TaskReveal, LineChoice.Take, LineChoice.Take, true)); OfficeUi.I.CloseFocus(); AimAt(Presenter().Office.CashBox.position + Vector3.up * 0.09f); }, true));

            LineMock.Register("v5.office_cashbox_witness", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Cash(LinePhase.TaskReveal, 0, LineChoice.Take, true)); Aim(0f, 15f); })
                .Do(0.6f, () => Show(Cash(LinePhase.Break, 0, LineChoice.Take, true)))
                .Do(1.2f, () => AimAt(Presenter().Office.CashBox.position + Vector3.up * 0.09f), true)
                .Do(3.0f, () => OfficeUi.I.OpenObject(OfficeUi.Focus.CashBox), true)
                .Do(4.8f, () =>
                {
                    // v5.1: the whistle is free and earns nothing; the thief keeps the cash (A = 0 taken back)
                    var v = Cash(LinePhase.Break, 0, LineChoice.Take, true);
                    v.Mine.WitnessOf.Clear();
                    LineClient.I.ApplyEvents(new List<LineEvent> { new LineEvent { Type = LineEventType.Whistled, Actor = 0, Target = 1, A = 0, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now } });
                    Show(v);
                }, true)
                .Do(6.6f, () => { OfficeUi.I.CloseFocus(); AimAt(Presenter().Office.CashBox.position + Vector3.up * 0.09f); }, true));

            LineMock.Register("v5.office_cashbox_thief", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Cash(LinePhase.TaskReveal, LineChoice.Take, 0, true)); })
                .Do(0.7f, () => { Show(Cash(LinePhase.Break, LineChoice.Take, 0, true)); Aim(-12f, 21f); }, true)
                .Do(2.4f, () => OfficeUi.I.OpenObject(OfficeUi.Focus.CashBox), true)
                .Do(4.2f, () =>
                {
                    // the witness whistled: v5.1 the notes stay on the desk (A = 0 taken back), the suç comes in meeting 1
                    var v = Cash(LinePhase.Break, LineChoice.Take, 0, true);
                    LineClient.I.ApplyEvents(new List<LineEvent> { new LineEvent { Type = LineEventType.Whistled, Actor = 1, Target = 0, A = 0, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now } });
                    Show(v);
                }, true));

            // ---- step 2: the Weekend Shift roster
            LineMock.Register("v5.office_roster", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Shift(LinePhase.TaskPlay, false, 0, null)); Aim(0f, 15f); })
                .Do(1.0f, () => AimAt(Presenter().Office.Roster.position), true)
                .Do(2.8f, () => OfficeUi.I.OpenObject(OfficeUi.Focus.Roster), true)
                .Do(4.6f, () => Show(Shift(LinePhase.TaskPlay, true, LineChoice.Volunteer, null)), 0.85f)
                .Do(6.4f, () => { }, true)
                .Do(8.0f, () => Show(Shift(LinePhase.TaskPlay, true, LineChoice.Skip, null)), 0.9f)
                .Do(9.8f, () => { }, true));

            LineMock.Register("v5.office_roster_reveal", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Shift(LinePhase.TaskReveal, true, LineChoice.Volunteer, new[] { 0, 2, 4 })); Aim(0f, 15f); })
                .Do(0.8f, () => OfficeUi.I.OpenObject(OfficeUi.Focus.Roster), true)
                .Do(2.6f, () => Show(Shift(LinePhase.TaskReveal, true, LineChoice.Skip, new[] { 3 })), true)
                .Do(4.4f, () => { OfficeUi.I.CloseFocus(); AimAt(Presenter().Office.Roster.position); }, true));

            // ---- step 2: the drawer's prim stack; the last shot looks sharply right from the chair (no long arm)
            LineMock.Register("v5.office_drawer", () => new LineMockScenario()
                .Do(0f, () => { Look(); Show(Prim(6)); Aim(0f, 15f); })
                .Do(0.8f, () => AimAt(Presenter().Office.Drawer.position + Vector3.up * 0.05f), true)
                .Do(2.4f, () => OfficeUi.I.OpenObject(OfficeUi.Focus.Drawer), true)
                .Do(4.0f, () => Show(Prim(23)), true)
                .Do(5.6f, () => { Transfer(3, 4); Show(Prim(27)); }, true)
                .Do(7.2f, () => Show(Prim(0)), true)
                .Do(8.8f, () => Show(Prim(58)), true)
                .Do(10.4f, () => { OfficeUi.I.CloseFocus(); Aim(72f, 24f); }, true)
                .Do(12.0f, () => Aim(-70f, 20f), true));

            LineMock.Register("v5.office_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Look(); var v = Task(TaskKind.PettyCash, 1, 4, LinePhase.TaskPlay, false); LineMock.SetNames(v, true); Show(v); })
                .Do(1.2f, () => OfficeUi.I.OpenComputer(OfficeComputer.App.Task), true)
                .Do(2.8f, () => { var v = Call(true); LineMock.SetNames(v, true); Show(v); OfficeUi.I.OpenPhone(); }, true)
                .Do(4.4f, () => { OfficeUi.I.CloseFocus(); var v = Board(); LineMock.SetNames(v, true); Show(v); OfficeUi.I.OpenBoard(); }, true)
                .Do(6.0f, () => { OfficeUi.I.CloseFocus(); var v = Cash(LinePhase.Break, 0, LineChoice.Take, true); LineMock.SetNames(v, true); Show(v); OfficeUi.I.OpenObject(OfficeUi.Focus.CashBox); }, true)
                .Do(7.8f, () => { OfficeUi.I.CloseFocus(); var v = Shift(LinePhase.TaskReveal, true, LineChoice.Volunteer, new[] { 0, 5 }); LineMock.SetNames(v, true); Show(v); OfficeUi.I.OpenObject(OfficeUi.Focus.Roster); }, true));
        }

        // ------------------------------------------------------------------ views

        static RoomsPresenter Presenter() { return RoomsPresenter.Instance; }

        static void Look() { RoomsPresenter.ForceLookInput = true; }

        static void Aim(float yaw, float pitch) { if (Presenter() != null) { Presenter().SitDown(false); Presenter().SetLook(yaw, pitch); } }

        static void AimAt(Vector3 p) { if (Presenter() != null) Presenter().AimAt(p); }

        static LineView Intro()
        {
            var v = LineMock.BaseView(LinePhase.Intro, 4, 0, 14);
            v.Mine.CallLog.Clear();
            v.Mine.Transfers.Clear();
            return v;
        }

        static LineView Task(TaskKind kind, int number, int players, LinePhase phase, bool submitted)
        {
            bool revealed = phase != LinePhase.TaskPlay;
            var v = LineMock.BaseView(phase, players, number < 2 ? 0 : 1, 38);
            v.TaskNumber = number;
            v.Task = LineMock.TaskView(kind, number, players, revealed, 0, revealed || submitted);
            v.Players[1].Submitted = true;
            return v;
        }

        static void Ring(LineView v, int from)
        {
            v.Mine.Phone.Status = CallStatus.Ringing;
            v.Mine.Phone.Other = from;
            v.Mine.Phone.RingUntil = LineMock.Now + 9;
            v.LineBoard.Add(new LineBoardCall { CallId = 12, Caller = from, Callee = 0, Status = CallStatus.Ringing, Start = LineMock.Now - 3 });
        }

        /// <summary>Crisis calls: I am talking to Sarı (recorded or not); Pembe and Yeşil talk too; a bot; Mor offline; a missed call.</summary>
        static LineView Call(bool recorded)
        {
            var v = LineMock.BaseView(LinePhase.CrisisCalls, 6, 2, 52);
            v.Meeting = new LineMeetingView { Index = 2 };
            foreach (var p in v.Players) p.Blame = new[] { 3, 5, 2, 6, 1, 4 }[p.Id];
            v.Players[5].Connected = false;
            v.LineBoard.Clear();
            v.LineBoard.Add(new LineBoardCall { CallId = 21, Caller = 0, Callee = 1, Status = CallStatus.Connected, Start = LineMock.Now - 37 });
            v.LineBoard.Add(new LineBoardCall { CallId = 22, Caller = 3, Callee = 2, Status = CallStatus.Connected, Start = LineMock.Now - 64 });
            var ph = v.Mine.Phone;
            ph.Status = CallStatus.Connected; ph.Other = 1; ph.CallId = 21; ph.Since = LineMock.Now - 37;
            v.Mine.CallLog.Add(new LineCallRecordView { CallId = 20, Other = 3, Outgoing = false, Connected = false, Start = LineMock.Now - 95, End = LineMock.Now - 83, EndReason = CallEndReason.Missed });
            v.Mine.CallLog.Add(new LineCallRecordView { CallId = 21, Other = 1, Outgoing = true, Connected = true, Start = LineMock.Now - 37, End = -1 });
            if (recorded)
            {
                v.Mine.Recorded.Add(new LineRecordedCallView { CallId = 21, Other = 1, Start = LineMock.Now - 30 });
                v.Mine.RecorderUsesLeft = 0;
            }
            return v;
        }

        static LineView Board()
        {
            var v = LineMock.BaseView(LinePhase.TaskPlay, 6, 1, 33);
            v.TaskNumber = 2;
            v.Task = LineMock.TaskView(TaskKind.WeekendShift, 2, 6, false, 0, false);
            foreach (var p in v.Players) p.Blame = new[] { 2, 4, 0, 7, 3, 1 }[p.Id];
            v.Players[2].Submitted = true; v.Players[4].Submitted = true; v.Players[1].Submitted = true;
            v.Players[5].Connected = false;
            v.LineBoard.Clear();
            v.LineBoard.Add(new LineBoardCall { CallId = 31, Caller = 1, Callee = 3, Status = CallStatus.Connected, Start = LineMock.Now - 22 });
            return v;
        }

        /// <summary>Petty Cash with Sarı (player 1): my choice and Sarı's (revealed after TaskPlay); a witness in the
        /// break when only Sarı took.</summary>
        static LineView Cash(LinePhase phase, int mine, int partner, bool submitted)
        {
            bool revealed = phase != LinePhase.TaskPlay;
            var v = LineMock.BaseView(phase, 4, 0, phase == LinePhase.TaskPlay ? 34 : 21);
            v.TaskNumber = 1;
            var c = LineConfig.CreateDefault();
            var t = new LineTaskView { Kind = TaskKind.PettyCash, Number = 1, Family = LineTasks.Family(TaskKind.PettyCash), PlayEndTime = LineMock.Now + 30,
                                       Submitted = submitted, MyChoice = mine, Revealed = revealed };
            t.Group.Add(0); t.Group.Add(1);
            if (revealed)
            {
                bool caught = mine == LineChoice.Take && partner == LineChoice.Take;
                t.Choices.Add(new LineChoiceView { TaskNumber = 1, Task = TaskKind.PettyCash, Player = 0, Choice = mine, Payout = mine == LineChoice.Take && !caught ? c.CashTake : 0 });
                t.Choices.Add(new LineChoiceView { TaskNumber = 1, Task = TaskKind.PettyCash, Player = 1, Choice = partner, Payout = -1 });
                t.MyPayout = mine == LineChoice.Take && !caught ? c.CashTake : 0;
            }
            v.Task = t;
            v.Players[1].Submitted = true;
            if (phase == LinePhase.Break)
            {
                v.Features.WhistleOpen = true;
                if (mine == 0 && partner == LineChoice.Take) v.Mine.WitnessOf.Add(1);
            }
            return v;
        }

        /// <summary>Weekend Shift, six players: my choice; after the reveal the public roster.</summary>
        static LineView Shift(LinePhase phase, bool submitted, int mine, int[] roster)
        {
            bool revealed = phase != LinePhase.TaskPlay;
            var v = LineMock.BaseView(phase, 6, 1, phase == LinePhase.TaskPlay ? 41 : 6);
            v.TaskNumber = 2;
            var c = LineConfig.CreateDefault();
            var t = new LineTaskView { Kind = TaskKind.WeekendShift, Number = 2, Family = LineTasks.Family(TaskKind.WeekendShift), PlayEndTime = LineMock.Now + 30,
                                       Needed = LineConfig.ShiftNeeded(6), Submitted = submitted, MyChoice = mine, Revealed = revealed };
            for (int i = 0; i < 6; i++) t.Group.Add(i);
            if (revealed && roster != null)
            {
                // v5.1: the skippers of a short roster get nothing
                int skipPay = roster.Length >= t.Needed ? c.ShiftSkipPay : c.ShiftFailSkipPay;
                for (int i = 0; i < 6; i++)
                {
                    bool on = System.Array.IndexOf(roster, i) >= 0;
                    t.Choices.Add(new LineChoiceView { TaskNumber = 2, Task = TaskKind.WeekendShift, Player = i, Choice = on ? 1 : 0, Payout = i == 0 ? (on ? 0 : skipPay) : -1 });
                }
                t.Roster.AddRange(roster);
                t.MyPayout = System.Array.IndexOf(roster, 0) >= 0 ? 0 : skipPay;
            }
            v.Task = t;
            v.Players[2].Submitted = true; v.Players[4].Submitted = true;
            return v;
        }

        /// <summary>A break after task 1 with my prim at <paramref name="prim"/> (the drawer scenario).</summary>
        static LineView Prim(int prim)
        {
            var v = Task(TaskKind.ClientBonus, 0, 4, LinePhase.Break, true);
            v.Mine.Prim = prim;
            return v;
        }

        /// <summary>Break after task 2, a few seconds before meeting 1: the lamp over the door is on.</summary>
        static LineView BreakBeforeMeeting()
        {
            var v = Task(TaskKind.PettyCash, 1, 4, LinePhase.Break, true);
            v.PhaseEndTime = LineMock.Now + 9;
            return v;
        }

        static void Show(LineView v)
        {
            LineMock.SyncMessageSeq(v);
            LineClient.I.ApplyView(v);
        }

        static void Texts()
        {
            var list = new List<LineMessage>
            {
                LineMock.Msg(MessageKind.Text, 1, 0, "Paylaşalım mı? Ben paylaşırım.", ago: 40),
                LineMock.Msg(MessageKind.Text, 0, 1, "Tamam, paylaşıyorum.", ago: 30),
                LineMock.Msg(MessageKind.Text, 2, 0, "Sarı'ya güvenme, geçen sefer hepsini aldı.", ago: 4),
            };
            LineClient.I.ApplyMessages(1, list);
            var v = LineClient.I.View;
            if (v != null) { LineMock.SyncMessageSeq(v); }
        }

        static void Transfer(int from, int amount)
        {
            LineClient.I.ApplyEvents(new List<LineEvent>
            {
                new LineEvent { Type = LineEventType.TransferReceived, Actor = from, Target = 0, A = amount, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now },
            });
        }

        static void English()
        {
            Loc.Load("en");
            if (UIRoot.I != null) UIRoot.I.Rebuild();
        }
    }
}
