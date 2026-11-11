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

            LineMock.Register("v5.office_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Look(); var v = Task(TaskKind.PettyCash, 1, 4, LinePhase.TaskPlay, false); LineMock.SetNames(v, true); Show(v); })
                .Do(1.2f, () => OfficeUi.I.OpenComputer(OfficeComputer.App.Task), true)
                .Do(2.8f, () => { var v = Call(true); LineMock.SetNames(v, true); Show(v); OfficeUi.I.OpenPhone(); }, true)
                .Do(4.4f, () => { OfficeUi.I.CloseFocus(); var v = Board(); LineMock.SetNames(v, true); Show(v); OfficeUi.I.OpenBoard(); }, true));
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
