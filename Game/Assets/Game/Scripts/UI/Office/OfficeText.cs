using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 office sentences ("v5.office.*" keys): the one-line "Şimdi: …" hint (the same decisions as the meeting
    /// screen's focus card, pointing at the desk object that does it), use prompts for what the crosshair is on, and
    /// key labels. Player names stay in "Label: Name" positions (no Turkish suffixes on names).
    /// </summary>
    public static class OfficeText
    {
        static string T(string key) { return PlayText.T(key); }
        static string F(string key, params object[] a) { return PlayText.F(key, a); }

        public static string Key(UnityEngine.InputSystem.InputAction a) { return a != null ? GameInput.KeyLabel(a) : "?"; }

        /// <summary>What to do now in an office phase; <paramref name="todo"/> = something is still expected of me.</summary>
        public static string Now(LineClient lc, LineView v, out bool todo)
        {
            todo = false;
            var m = v.Mine;
            var t = v.Task;
            int me = lc.MySlot;
            switch (v.Phase)
            {
                case LinePhase.Intro:
                    return F("office.now.intro", Key(GameInput.Phone), Key(GameInput.Computer));
                case LinePhase.TaskPlay:
                    if (t == null) return T("hint.TaskPlay");
                    if (!t.Submitted)
                    {
                        todo = true;
                        // each task has its own object: the computer (Client Bonus), the cash box, the roster
                        if (t.Kind == TaskKind.PettyCash) return F("office.now.choose_cash", Key(GameInput.Interact));
                        if (t.Kind == TaskKind.WeekendShift) return F("office.now.choose_shift", Key(GameInput.Interact));
                        return F("office.now.choose", PlayText.Task(t.Kind), Key(GameInput.Computer), PlayText.Choice(t.Kind, 0));
                    }
                    return F("office.now.chosen", PlayText.Choice(t.Kind, t.MyChoice));
                case LinePhase.TaskReveal:
                {
                    // v5.2: an unstaffed shift blames its skippers at the reveal (no meeting 2): say it here
                    string shortShift = PlayText.ShiftShort(t, lc.Config, v.Players.Count);
                    if (shortShift != null) return shortShift + " " + T("office.now.shift_short_roster");
                }
                    if (t != null && t.MyPayout >= 0)
                        return t.Kind == TaskKind.PettyCash ? F("office.now.reveal_payout_cash", t.MyPayout)
                            : t.Kind == TaskKind.WeekendShift ? F("office.now.reveal_payout_shift", t.MyPayout)
                            : F("office.now.reveal_payout", t.MyPayout, Key(GameInput.Computer));
                    return T("office.now.reveal");
                case LinePhase.Break:
                    if (v.Features.WhistleOpen && m.WitnessOf.Count > 0)
                    {
                        todo = true;
                        return F("office.now.witness", PlayText.Names(m.WitnessOf), Key(GameInput.Interact));
                    }
                    if (v.Features.WhistleOpen && t != null && PlayBlocks.SoleTaker(t, me)) return T("office.now.thief");
                    return T(t != null && t.Number == 0 ? "office.now.break_task" : "office.now.break_meeting");
                case LinePhase.CrisisCalls:
                    return F("office.now.crisis", lc.Config.CrisisBlame, Key(GameInput.Phone));
                default:
                    return PlayText.Hint(v.Phase);
            }
        }

        /// <summary>Name of what the crosshair is on (prompt "E — …").</summary>
        public static string Use(DeskTarget d, LineView v)
        {
            switch (d)
            {
                case DeskTarget.Recorder:
                    if (v != null && PlayBlocks.RecordingCurrentCall(v)) return T("office.use.Recorder_on");
                    return T(v != null && v.Mine.Phone.Status == CallStatus.Connected ? "office.use.Recorder" : "office.use.Recorder_idle");
                case DeskTarget.CashBox:
                    if (v != null && v.Phase == LinePhase.Break && v.Features.WhistleOpen && v.Mine.WitnessOf.Count > 0) return T("office.use.CashBox_whistle");
                    return T(v != null && v.Phase == LinePhase.TaskPlay ? "office.use.CashBox_play" : "office.use.CashBox");
                case DeskTarget.Roster:
                    if (v != null && v.Phase == LinePhase.TaskPlay && v.Task != null && v.Task.Kind == TaskKind.WeekendShift)
                        return T(v.Task.Submitted && v.Task.MyChoice == LineChoice.Volunteer ? "office.use.Roster_signed" : "office.use.Roster_play");
                    return T("office.use.Roster");
                case DeskTarget.Monitor:
                case DeskTarget.Phone:
                case DeskTarget.Board:
                case DeskTarget.Chair:
                case DeskTarget.Mug:
                case DeskTarget.Drawer:
                case DeskTarget.Nameplate:
                    return T("office.use." + d);
                default:
                    return "";
            }
        }

        static readonly System.Collections.Generic.List<int> NoPartners = new System.Collections.Generic.List<int>();

        /// <summary>v5 step 3a: my partners in a pair / trio task (Client Bonus, Petty Cash) while it runs, its reveal and its
        /// break (seat order); empty otherwise (Weekend Shift is everyone's task).</summary>
        public static System.Collections.Generic.List<int> Partners(LineClient lc, LineView v)
        {
            var t = v != null ? v.Task : null;
            if (t == null || lc == null || (t.Kind != TaskKind.ClientBonus && t.Kind != TaskKind.PettyCash)) return NoPartners;
            if (v.Phase != LinePhase.TaskPlay && v.Phase != LinePhase.TaskReveal && v.Phase != LinePhase.Break) return NoPartners;
            var list = new System.Collections.Generic.List<int>();
            foreach (int id in t.Group) if (id != lc.MySlot) list.Add(id);
            list.Sort();
            return list;
        }

        /// <summary>The hotkey that does the same from anywhere (shown next to the prompt), or null.</summary>
        public static string Hotkey(DeskTarget d)
        {
            switch (d)
            {
                case DeskTarget.Monitor: return Key(GameInput.Computer);
                case DeskTarget.Phone: return Key(GameInput.Phone);
                case DeskTarget.Recorder: return Key(GameInput.Record);
                default: return null;
            }
        }
    }
}
