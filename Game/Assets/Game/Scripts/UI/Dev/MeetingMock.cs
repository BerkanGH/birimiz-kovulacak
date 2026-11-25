using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3a scenarios for the UI mock. <c>-uimock "v5.meeting*"</c>: the seated meeting room (everyone looking at
    /// you, a colleague talking, a name tag, shared gaze between colleagues), the whiteboards (from the seat, zoomed, the
    /// board on wheels from the other row, stamps after the crisis), the folder (actions, chat, transfer, file), the
    /// ballot (on the table, held, two columns marked, folding, flying into the box, the colleagues' papers, a fresh one),
    /// the final vote with a blank ballot, the crisis vote, the defence, and an English sample.
    /// <c>-uimock "v5.office_partner*"</c>: the partner card when a pair / trio task starts, the phone with the partner
    /// pinned and selected, and the 15-second hint.
    /// </summary>
    public static class MeetingMockScenarios
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            LineMock.Register("v5.meeting_look", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Discussion(5)); Aim(0f, 6f); })
                .Do(1.4f, () => { for (int i = 1; i < 5; i++) GazeAt(i, 0); }, true)
                .Do(3.0f, () => { Speak(1); for (int i = 2; i < 5; i++) GazeAt(i, 1); GazeAt(1, 3); Aim(18f, 4f); }, true)
                .Do(4.6f, () => { Speak(-1); AimAtSlot(3); GazeAt(3, 0); }, true)
                .Do(6.2f, () => { GazeAt(3, 4); Gaze(4, 0f, 34f); GazeAt(2, 1); GazeAt(1, 2); Aim(30f, 2f); }, true)
                .Do(7.8f, () => { Aim(0f, 44f); }, true));

            LineMock.Register("v5.meeting_board", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Discussion(5)); AimAtBoard(); }, true)
                .Do(1.6f, () => MeetingUi.I?.OpenBoard(), true)
                .Do(3.2f, () => { MeetingUi.I?.CloseAll(); Show(CrisisVerdict(6)); }, 0.3f)
                .Do(4.0f, () => MeetingUi.I?.OpenBoard(), true));
            // the right row faces the board on wheels
            LineMock.Register("v5.meeting_board_stand", () => new LineMockScenario { Me = 1 }
                .Do(0f, () => { Reset(); var v = Discussion(6); v.Me = 1; Show(v); AimAtBoard(); }, true)
                .Do(1.6f, () => MeetingUi.I?.OpenBoard(), true));

            LineMock.Register("v5.meeting_folder", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Discussion(5)); Aim(0f, 6f); Messages(); })
                .Do(0.8f, () => AimAt(Room().Seats[Room().LocalSeat].Folder.position + Vector3.up * 0.02f), true)
                .Do(2.4f, () => MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Actions), true)
                .Do(4.0f, () => MeetingUi.I?.Folder.SetTab(MeetingFolder.Tab.Chat), true)
                .Do(5.6f, () => MeetingUi.I?.Folder.SetTab(MeetingFolder.Tab.Transfer), true)
                .Do(7.2f, () => MeetingUi.I?.Folder.SetTab(MeetingFolder.Tab.Dossier), true)
                .Do(8.8f, () => { MeetingUi.I?.CloseAll(); GazeAt(2, 0); }, 0.25f));

            LineMock.Register("v5.meeting_ballot", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Vote(5, -1, -1)); Aim(0f, 6f); }, true)
                .Do(1.8f, () => AimAt(PaperSpot()), true)
                .Do(3.4f, () => MeetingUi.I?.OpenBallot(), true)
                .Do(5.0f, () => { MeetingUi.I?.Ballot.MockMark(0, 3); MeetingUi.I?.Ballot.MockMark(1, 1); }, true)
                .Do(6.6f, () => MeetingUi.I?.Ballot.Primary(), 0.22f)
                .Do(7.4f, () => { }, 0.15f)
                .Do(8.4f, () => { Show(Vote(5, 3, 1)); AimAt(Room().BallotBox.position + Vector3.up * 0.12f); Room().OtherDrop(2); }, 0.45f)
                .Do(9.6f, () => { Room().OtherDrop(4); }, 1.4f)
                .Do(11.6f, () => MeetingUi.I?.OpenBallot(), true));

            LineMock.Register("v5.meeting_final", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(FinalVote(5)); Aim(0f, 6f); for (int i = 1; i < 5; i++) GazeAt(i, 2); }, true)
                .Do(1.8f, () => MeetingUi.I?.OpenBallot(), true)
                .Do(3.4f, () => MeetingUi.I?.Ballot.MockMark(0, MeetingBallot.AbstainMark), true)
                .Do(5.0f, () => MeetingUi.I?.Ballot.Primary(), 0.3f)
                .Do(6.8f, () => { var v = FinalVote(5); v.Players[0].Voted = false; Show(v); MeetingUi.I?.OpenBallot(); }, true)
                .Do(8.4f, () => { MeetingUi.I?.CloseAll(); MeetingGaze.Clear(); Show(Defense(5)); Speak(1); Aim(14f, 4f); }, true));

            LineMock.Register("v5.meeting_crisis", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(CrisisVote(6)); Aim(0f, 6f); }, true)
                .Do(1.8f, () => MeetingUi.I?.OpenBallot(), true)
                .Do(3.4f, () => MeetingUi.I?.Ballot.MockMark(0, 2), true)
                .Do(5.0f, () => MeetingUi.I?.Ballot.Primary(), 0.6f));

            LineMock.Register("v5.meeting_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); var v = Discussion(5); LineMock.SetNames(v, true); Show(v); Aim(0f, 6f); AimAtSlot(2); }, true)
                .Do(1.9f, () => { MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Actions); }, true)
                .Do(3.5f, () => { MeetingUi.I?.CloseAll(); var v = FinalVote(5); LineMock.SetNames(v, true); Show(v); }, 0.3f)
                .Do(4.3f, () => MeetingUi.I?.OpenBallot(), true)
                .Do(5.9f, () => { MeetingUi.I?.CloseAll(); var v = CrisisVerdict(6); LineMock.SetNames(v, true); Show(v); MeetingUi.I?.OpenBoard(); }, true));

            // ---- office: partner call (pair / trio tasks)
            LineMock.Register("v5.office_partner", () => new LineMockScenario()
                .Do(0f, () => { OfficeLook(); Show(PairTask(TaskKind.ClientBonus, 0, 4, new[] { 0, 1 }, 2)); OfficeAim(0f, 15f); }, true)
                .Do(1.8f, () => OfficeUi.I?.OpenPhone(), true));
            LineMock.Register("v5.office_partner_trio", () => new LineMockScenario()
                .Do(0f, () => { OfficeLook(); Show(PairTask(TaskKind.PettyCash, 1, 5, new[] { 0, 3, 4 }, 2)); OfficeAim(0f, 15f); }, true)
                .Do(1.8f, () => OfficeUi.I?.OpenPhone(), true));
            LineMock.Register("v5.office_partner_hint", () => new LineMockScenario()
                .Do(0f, () => { OfficeUi.MockHint = true; OfficeLook(); Show(PairTask(TaskKind.ClientBonus, 0, 4, new[] { 0, 1 }, 16)); OfficeAim(0f, 15f); }, true)
                .Do(1.8f, () => { OfficeUi.MockHint = false; OfficeUi.I?.OpenPhone(); }, true));
        }

        // ------------------------------------------------------------------ helpers

        internal static RoomsPresenter Presenter() { return RoomsPresenter.Instance; }
        internal static MeetingRoom Room() { return RoomsPresenter.Instance != null ? RoomsPresenter.Instance.Meeting : null; }

        internal static void Reset()
        {
            RoomsPresenter.ForceLookInput = true;
            LineWorldDriver.MockSpeech = null;
            MeetingGaze.Clear();
            MeetingProjector.MockDeckIndex = -1;
            _start = double.NaN;
        }

        /// <summary>The next <see cref="Show"/> starts a new phase now (a second desk opening is a new FinalVerdict).</summary>
        internal static void Fresh() { _start = double.NaN; }

        static void OfficeLook() { RoomsPresenter.ForceLookInput = true; LineWorldDriver.MockSpeech = null; _start = double.NaN; }
        static void OfficeAim(float yaw, float pitch) { if (Presenter() != null) { Presenter().SitDown(false); Presenter().SetLook(yaw, pitch); } }

        internal static void Aim(float yaw, float pitch) { if (Presenter() != null) Presenter().SetLook(yaw, pitch); }
        internal static void AimAt(Vector3 p) { if (Presenter() != null) Presenter().AimAt(p); }

        internal static void AimAtSlot(int slot)
        {
            Vector3 head;
            if (Room() != null && Room().SeatHead(slot, out head)) AimAt(head + Vector3.down * 0.12f);
        }

        internal static void AimAtBoard()
        {
            var lc = LineClient.I;
            var b = Room() != null && lc != null ? Room().BoardFor(lc.MySlot) : null;
            if (b != null) AimAt(b.bounds.center);
        }

        internal static Vector3 PaperSpot()
        {
            var r = Room();
            int s = r != null ? r.LocalSeat : -1;
            if (s < 0 || r.Seats[s].BallotSpot == null) return Vector3.zero;
            return r.Seats[s].BallotSpot.position;
        }

        /// <summary>A colleague looks somewhere (degrees relative to their seat), held for the whole scenario.</summary>
        internal static void Gaze(int slot, float yaw, float pitch) { MeetingGaze.Set(slot, yaw, pitch, 60f); }

        internal static void GazeAt(int from, int to)
        {
            float y, p;
            if (Room() != null && Room().GazeAngles(from, to, out y, out p)) Gaze(from, y, p);
        }

        internal static void Speak(int slot)
        {
            if (slot < 0) { LineWorldDriver.MockSpeech = null; return; }
            LineWorldDriver.MockSpeech = id => id == slot ? 0.55f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 7f)) : 0f;
        }

        internal static LineView Discussion(int players)
        {
            var v = LineMock.BaseView(LinePhase.MeetingDiscussion, players, 0, 44);
            v.Meeting = LineMock.MeetingView(0, -1);
            v.Meeting.Recordings.Clear();
            // v5.3: I shared the Client Bonus and Sarı took it all: the file is mine. Two files are opened already (the
            // dossier), one more is still in somebody's hands
            LineMock.AddFile(v, LineMock.File(TaskKind.ClientBonus, 0, 1));
            v.Files.Held++;
            LineMock.AddFile(v, LineMock.File(TaskKind.PettyCash, 2, 3, LineFileState.Opened, LinePhase.Break));
            LineMock.AddFile(v, LineMock.File(TaskKind.ClientBonus, 3, 2, LineFileState.Opened, LinePhase.MeetingDossier));
            v.Mine.Recorded.Add(new LineRecordedCallView { CallId = 3, Other = 1, Start = LineMock.Now - 200 });
            foreach (var p in v.Players) p.Blame = new[] { 2, 4, 0, 7, 3, 1 }[p.Id];
            return v;
        }

        internal static LineView Vote(int players, int a, int b)
        {
            var v = LineMock.BaseView(LinePhase.MeetingVote, players, 0, 18);
            v.Meeting = LineMock.MeetingView(0, 2);
            v.Meeting.Recordings.Clear();
            v.Mine.Ballots.Add(a); v.Mine.Ballots.Add(b);
            LineMock.AddFile(v, LineMock.File(TaskKind.ClientBonus, 0, 1));
            foreach (var p in v.Players) p.Blame = new[] { 2, 4, 0, 7, 3, 1 }[p.Id];
            v.Players[1].Voted = true; v.Players[3].Voted = true;
            v.Players[0].Voted = a >= 0 && b >= 0;
            return v;
        }

        internal static LineFinalView Final(int[] candidates, int firings)
        {
            var f = new LineFinalView { FiringsTotal = firings };
            f.Candidates.AddRange(candidates);
            f.DefenseOrder.AddRange(candidates);
            f.Options.AddRange(candidates);
            f.Crisis.Add(new LineBlameGainView { Player = 2, Amount = 4 });
            return f;
        }

        internal static void Candidates(LineView v)
        {
            foreach (var p in v.Players)
            {
                p.Candidate = v.Final.Candidates.Contains(p.Id);
                p.Fired = v.Final.Firings.Exists(f => f.Player == p.Id);
            }
        }

        internal static LineView FinalVote(int players)
        {
            var v = LineMock.BaseView(LinePhase.FinalVote, players, 2, 20);
            foreach (var p in v.Players) p.Blame = new[] { 3, 8, 11, 7, 2, 1 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = Final(new[] { 2, 1, 3 }, 2);
            Candidates(v);
            v.Mine.Ballots.Add(-1);
            v.Players[3].Voted = true; v.Players[4].Voted = true;
            return v;
        }

        internal static LineView Defense(int players)
        {
            var v = LineMock.BaseView(LinePhase.FinalDefense, players, 2, 26);
            foreach (var p in v.Players) p.Blame = new[] { 3, 8, 11, 7, 2, 1 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = Final(new[] { 2, 1, 3 }, 2);
            v.Final.Defender = 1; v.Final.DefenderEndTime = LineMock.Now + 12;
            Candidates(v);
            return v;
        }

        internal static LineView CrisisVote(int players)
        {
            var v = LineMock.BaseView(LinePhase.CrisisVote, players, 2, 22);
            foreach (var p in v.Players) p.Blame = new[] { 3, 5, 6, 4, 2, 1 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = new LineFinalView { FiringsTotal = 2 };
            v.Mine.Ballots.Add(-1);
            v.Players[2].Voted = true; v.Players[5].Voted = true;
            return v;
        }

        internal static LineView CrisisVerdict(int players)
        {
            var v = LineMock.BaseView(LinePhase.CrisisVerdict, players, 2, 8);
            foreach (var p in v.Players) p.Blame = new[] { 3, 5, 10, 7, 2, 1 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = Final(new[] { 2, 3, 1 }, 2);
            Candidates(v);
            return v;
        }

        /// <summary>A pair / trio task that just started (<paramref name="ago"/> seconds ago) with these group members (me = 0).</summary>
        static LineView PairTask(TaskKind kind, int number, int players, int[] group, double ago)
        {
            var v = LineMock.BaseView(LinePhase.TaskPlay, players, 0, 40);
            v.PhaseStartTime = LineMock.Now - ago;
            v.TaskNumber = number;
            var t = new LineTaskView { Kind = kind, Number = number, Family = LineTasks.Family(kind), PlayEndTime = LineMock.Now + 40 };
            t.Group.AddRange(group);
            v.Task = t;
            foreach (int id in group) v.Players[id].IsBot = false;
            v.LineBoard.Clear();
            v.Mine.CallLog.Clear();
            v.Mine.Transfers.Clear();
            return v;
        }

        static double _start = double.NaN;
        static LinePhase _startPhase;

        /// <summary>Applies the view; a later view of the same phase keeps the phase's start time (same vote round, the
        /// papers stay in the box).</summary>
        internal static void Show(LineView v)
        {
            if (!double.IsNaN(_start) && v.Phase == _startPhase) v.PhaseStartTime = _start;
            else { _start = v.PhaseStartTime; _startPhase = v.Phase; }
            LineMock.SyncMessageSeq(v);
            LineClient.I.ApplyView(v);
        }

        static void Messages()
        {
            var list = new List<LineMessage>
            {
                LineMock.Msg(MessageKind.MeetingChat, 1, -1, "Kasada ben almadım, Yeşil'e sorun.", true, 30),
                LineMock.Msg(MessageKind.Text, 2, 0, "Dosyayı açarsan ben de sana oy veririm.", ago: 20),
                LineMock.Msg(MessageKind.MeetingChat, 0, -1, "Bonus'ta paylaştım, kaydım var.", true, 12),
                LineMock.Msg(MessageKind.MeetingChat, 3, -1, "Kayıt sahte olabilir.", true, 4),
            };
            LineClient.I.ApplyMessages(1, list);
            var v = LineClient.I.View;
            if (v != null) LineMock.SyncMessageSeq(v);
        }

        internal static void English()
        {
            Loc.Load("en");
            if (UIRoot.I != null) UIRoot.I.Rebuild();
        }
    }
}
