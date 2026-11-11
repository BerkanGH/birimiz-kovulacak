using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Local selections of a play surface (who the transfer goes to and how much, who the next text goes to) and the
    /// callbacks the shared blocks need. The meeting screen and the office computer each own one.
    /// </summary>
    public sealed class PlayState
    {
        public int TransferTarget = LineIds.None, TransferAmount = 1;
        /// <summary>-2 = nobody chosen, -1 = meeting chat (everyone), else a player id.</summary>
        public int MsgTarget = -2;
        public Action Dirty;
        public Action<string> Toast;
        public Action FocusMessageField;

        public void ResetWeek() { TransferTarget = LineIds.None; TransferAmount = 1; MsgTarget = -2; }
        public void MarkDirty() { Dirty?.Invoke(); }
    }

    /// <summary>
    /// Content blocks shared by the meeting screen (<see cref="PlayUi"/>) and the office computer
    /// (<see cref="OfficeComputer"/>): the task card (instruction, group, choices, default, reveal, whistle), the task
    /// rules with the host's numbers, the intro, the crisis-calls card, the transfer panel, messages and compose
    /// targets, and the call log. Every rule text comes from PlayText / the v5 tables, never duplicated.
    /// </summary>
    public static class PlayBlocks
    {
        // ---- task (TaskPlay: choose; TaskReveal / Break: results and the whistle)

        public static int FocusTask(VisualElement card, LineClient lc, LineView v)
        {
            var t = v.Task;
            if (t == null) { Big(card, T("hint.TaskPlay")); return Info; }
            int me = lc.MySlot;
            var c = lc.Config;
            switch (t.Kind)
            {
                case TaskKind.ClientBonus: Big(card, T("focus.task.client_bonus")); break;
                case TaskKind.PettyCash: Big(card, T("focus.task.petty_cash")); break;
                case TaskKind.WeekendShift: Big(card, F("focus.task.weekend_shift", c.ShiftSkipPay, t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count))); break;
                default: Big(card, T("hint.TaskPlay")); break;
            }
            if (t.Kind == TaskKind.WeekendShift) Line(card, F("task.everyone", t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count)));
            else Line(card, F("task.group", PlayText.Names(t.Group.FindAll(id => id != me))));
            var row = Wrap(card);
            for (int choice = 0; choice <= 1; choice++)
            {
                int ch = choice;
                bool on = t.Submitted && t.MyChoice == ch;
                Btn(PlayText.Choice(t.Kind, ch), () => Send(LineVerb.SubmitTask, a: ch), "pl-choice", on ? "pl-on" : null).AddTo(row);
            }
            Line(card, t.Submitted ? F("task.submitted", PlayText.Choice(t.Kind, t.MyChoice)) : F("task.default", PlayText.Choice(t.Kind, 0)), t.Submitted ? "pl-good" : "pl-warn");
            return t.Submitted ? Done : Todo;
        }

        public static int FocusReveal(VisualElement card, LineClient lc, LineView v)
        {
            var t = v.Task;
            if (t == null) { Big(card, PlayText.Hint(v.Phase)); return Info; }
            int me = lc.MySlot;
            var m = v.Mine;
            var c = lc.Config;
            int state = Info;
            bool witness = v.Phase == LinePhase.Break && v.Features.WhistleOpen && m.WitnessOf.Count > 0;
            if (witness)
            {
                foreach (int thief in m.WitnessOf)
                {
                    int id = thief;
                    Big(card, F("break.witness", Name(id), c.CashWhistleBlame, c.CashWhistleReward), "pl-bad");
                    Btn(F("break.whistle_btn", Name(id)), () => Send(LineVerb.Whistle, id), "pl-danger", "pl-choice").AddTo(card);
                }
                Line(card, T("focus.witness_or"), "pl-dim");
                state = Todo;
            }
            else if (v.Phase == LinePhase.Break && v.Features.WhistleOpen && SoleTaker(t, me)) Big(card, T("focus.thief"), "pl-warn");
            else if (v.Phase == LinePhase.Break) Big(card, T(t.Number == 0 ? "break.next_task" : "break.meeting")); // breaks follow tasks 1 and 2 only
            else Big(card, T("hint.TaskReveal"));
            if (!t.Revealed) { Line(card, T("task.not_revealed"), "pl-dim"); return state; }
            var row = Wrap(card);
            foreach (var ch in t.Choices)
            {
                bool bad = ch.Choice == LineChoice.Take && t.Kind != TaskKind.WeekendShift;
                Bold(Name(ch.Player) + ": " + PlayText.Choice(t.Kind, ch.Choice), "pl-chip", bad ? "pl-chip-bad" : null).AddTo(row);
            }
            if (t.MyPayout >= 0) Bold(F("task.my_payout", t.MyPayout), "pl-good", "pl-sub").AddTo(card);
            return state;
        }

        /// <summary>Petty Cash: I am the only taker in my group (a witness may whistle on me in the break).</summary>
        public static bool SoleTaker(LineTaskView t, int me)
        {
            if (t.Kind != TaskKind.PettyCash || !t.Revealed) return false;
            int takers = 0;
            bool mine = false;
            foreach (var ch in t.Choices)
                if (ch.Choice == LineChoice.Take) { takers++; if (ch.Player == me) mine = true; }
            return mine && takers == 1;
        }

        /// <summary>The task's rules with the host's numbers, then what became public after the reveal.</summary>
        public static void BuildTask(VisualElement p, LineClient lc, LineView v)
        {
            var t = v.Task;
            if (t == null) return;
            Head(p, F("task.title", t.Number + 1, PlayText.Task(t.Kind)));
            Sub(p, T("task.rules_title"));
            Line(p, PlayText.TaskRules(t, lc.Config, v.Players.Count));
            if (!t.Revealed) return;
            if (t.Takers >= 0) Line(p, F("task.public_takers", t.Takers));
            if (t.Kind == TaskKind.WeekendShift) Line(p, F("task.public_roster", PlayText.Names(t.Roster), t.Roster.Count, t.Needed));
        }

        public static void BuildIntro(VisualElement p, LineClient lc, LineView v)
        {
            Head(p, T("intro.title"));
            var c = lc.Config;
            Line(p, F("intro.goal", c.StartPrim));
            Line(p, T("intro.flow"));
            Line(p, F("intro.final", c.CandidatesFor(v.Players.Count), c.FiringsFor(v.Players.Count)));
            Line(p, T("intro.phone"));
            if (v.Features.RecordingsEnabled) Line(p, F("intro.recorder", c.RecorderUses));
            Line(p, F("intro.transfer", c.TransferMax));
        }

        /// <summary>Crisis calls: what to bargain about and how many become candidates.</summary>
        public static void CrisisCalls(VisualElement card, LineClient lc, LineView v, string howKey)
        {
            Big(card, F("focus.crisis_calls", lc.Config.CrisisBlame));
            Line(card, F("crisis.candidates_rule", lc.Config.CandidatesFor(v.Players.Count)));
            if (!string.IsNullOrEmpty(howKey)) Line(card, T(howKey), "pl-dim");
        }

        /// <summary>The how-to page that explains this phase (-1 = none).</summary>
        public static int HowToPage(LineView v)
        {
            switch (v.Phase)
            {
                case LinePhase.Intro: return HowToPlayScreen.PageGoal;
                case LinePhase.TaskPlay:
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                    if (v.Task == null) return HowToPlayScreen.PageFlow;
                    return v.Task.Kind == TaskKind.ClientBonus ? HowToPlayScreen.PageBonus : v.Task.Kind == TaskKind.PettyCash ? HowToPlayScreen.PageCash : HowToPlayScreen.PageShift;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion: return v.Day == 0 ? HowToPlayScreen.PageInvestigation : HowToPlayScreen.PageBlame;
                case LinePhase.MeetingVote:
                case LinePhase.MeetingVerdict: return HowToPlayScreen.PageBlame;
                case LinePhase.CrisisCalls:
                case LinePhase.CrisisVote:
                case LinePhase.CrisisVerdict: return HowToPlayScreen.PageCrisis;
                case LinePhase.FinalDiscussion:
                case LinePhase.FinalDefense:
                case LinePhase.FinalVote:
                case LinePhase.FinalVerdict: return HowToPlayScreen.PageFinal;
                default: return -1;
            }
        }

        // ---- transfers

        /// <summary>Send prim (sealed desks and fired players cannot) and my own transfer log.</summary>
        public static void BuildTransfer(VisualElement p, LineClient lc, LineView v, PlayState st, bool head = true)
        {
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            if (!f.TransfersOpen && m.Transfers.Count == 0) { if (!head) Line(p, T("office.transfer_closed"), "pl-dim"); return; }
            if (head) Head(p, T("transfer.title"));
            var meP = lc.Player(me);
            if (f.TransfersOpen)
            {
                if (meP != null && meP.Fired) Line(p, T("transfer.fired"), "pl-dim");
                else if (meP != null && meP.Candidate)
                {
                    // sealed desk (docs/V5_ENGINE_SPEC.md §18): sending is disabled, receiving still works
                    Line(p, T("transfer.sealed"), "pl-bad");
                    Line(p, T("transfer.sealed_receive"), "pl-dim");
                    Btn(T("transfer.sealed_btn"), null, "pl-primary").AddTo(p).SetEnabled(false);
                }
                else
                {
                    Line(p, F("transfer.rules", lc.Config.TransferMax), "pl-dim");
                    if (v.Final != null && v.Final.Candidates.Count > 0) Line(p, T("transfer.candidates_note"), "pl-warn");
                    var targets = Wrap(p);
                    foreach (var pl in v.Players)
                    {
                        if (pl.Id == me) continue;
                        int id = pl.Id;
                        Btn(pl.Name, () => { st.TransferTarget = id; st.MarkDirty(); }, st.TransferTarget == id ? "pl-on" : null).AddTo(targets);
                    }
                    int max = Math.Max(1, Math.Min(lc.Config.TransferMax, m.Prim));
                    st.TransferAmount = Mathf.Clamp(st.TransferAmount, 1, max);
                    var row = Row(p);
                    Btn("−", () => { st.TransferAmount = Math.Max(1, st.TransferAmount - 1); st.MarkDirty(); }).AddTo(row);
                    Bold(F("transfer.amount", st.TransferAmount), "pl-amount").AddTo(row);
                    Btn("+", () =>
                    {
                        var cur = LineClient.I != null ? LineClient.I.View : null;
                        int top = Math.Max(1, Math.Min(lc.Config.TransferMax, cur != null ? cur.Mine.Prim : 1));
                        st.TransferAmount = Math.Min(top, st.TransferAmount + 1);
                        st.MarkDirty();
                    }).AddTo(row);
                    var send = Btn(st.TransferTarget >= 0 ? F("transfer.send", st.TransferAmount, Name(st.TransferTarget)) : T("transfer.pick"), () =>
                    {
                        if (st.TransferTarget >= 0) Send(LineVerb.Transfer, st.TransferTarget, st.TransferAmount);
                    }, "pl-primary").AddTo(p);
                    send.SetEnabled(st.TransferTarget >= 0 && m.Prim > 0);
                }
            }
            if (m.Transfers.Count > 0)
            {
                Sub(p, T("transfer.log"));
                foreach (var t in m.Transfers)
                    Line(p, t.From == me ? F("transfer.out", Name(t.To), t.Amount) : F("transfer.in", Name(t.From), t.Amount), t.From == me ? "pl-bad" : "pl-good");
            }
        }

        // ---- messages

        public static void BuildMessages(VisualElement p, LineClient lc, LineView v, bool head = true, int keep = 30)
        {
            if (head) Head(p, T("msg.title"));
            var msgs = lc.Messages;
            if (msgs.Count == 0) Line(p, T("msg.empty"), "pl-dim");
            int me = lc.MySlot;
            for (int i = Math.Max(0, msgs.Count - keep); i < msgs.Count; i++)
            {
                var msg = msgs[i];
                string who;
                if (msg.Kind == MessageKind.MeetingChat) who = F("msg.chat_from", Name(msg.From));
                else if (msg.Kind == MessageKind.System) who = T("msg.system");
                else if (msg.From == me) who = F("msg.text_to", Name(msg.To));
                else who = F("msg.text_from", Name(msg.From));
                Line(p, who + " " + msg.Text, msg.Kind == MessageKind.MeetingChat ? null : "pl-private");
            }
        }

        /// <summary>Recipient buttons of the compose box; sets <paramref name="info"/> to the current recipient line.</summary>
        public static void BuildComposeTargets(VisualElement p, LineClient lc, LineView v, PlayState st, Label info)
        {
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            int target = st.MsgTarget == -2 && f.MeetingChatOpen ? -1 : st.MsgTarget;
            if (info != null) info.SetText(target == -1 ? F("msg.to_all", m.MeetingChatLeft) : target >= 0 ? F("msg.to", Name(target), m.TextsLeft) : T("msg.pick_target"));
            var row = Wrap(p);
            if (f.MeetingChatOpen) Btn(T("msg.all"), () => { st.MsgTarget = -1; st.MarkDirty(); }, target == -1 ? "pl-on" : null).AddTo(row);
            if (f.TextsOpen)
                foreach (var pl in v.Players)
                {
                    if (pl.Id == me) continue;
                    int id = pl.Id;
                    Btn(pl.Name, () => { st.MsgTarget = id; st.MarkDirty(); st.FocusMessageField?.Invoke(); }, target == id ? "pl-on" : null).AddTo(row);
                }
        }

        /// <summary>Sends the compose text to the chosen recipient (or the meeting chat); returns true when sent.</summary>
        public static bool SendMessage(LineView v, PlayState st, string raw)
        {
            string text = (raw ?? "").Trim();
            if (v == null || text.Length == 0) return false;
            int target = st.MsgTarget == -2 && v.Features.MeetingChatOpen ? -1 : st.MsgTarget;
            if (target == -1)
            {
                if (!v.Features.MeetingChatOpen) { st.Toast?.Invoke(T("msg.chat_closed")); return false; }
                Send(LineVerb.SendMeetingChat, text: text);
            }
            else if (target >= 0) Send(LineVerb.SendText, target, text: text);
            else { st.Toast?.Invoke(T("msg.pick_target")); return false; }
            return true;
        }

        // ---- calls

        /// <summary>My calls, newest first: direction, colleague, duration or why it did not connect.</summary>
        public static void BuildCallLog(VisualElement p, LineView v, double now, int max, bool head = true)
        {
            var m = v.Mine;
            if (m.CallLog.Count == 0) { if (!head) Line(p, T("office.calllog_empty"), "pl-dim"); return; }
            if (head) Sub(p, T("phone.log"));
            for (int i = m.CallLog.Count - 1, n = 0; i >= 0 && n < max; i--, n++)
            {
                var c = m.CallLog[i];
                string head2 = T(c.Outgoing ? "phone.log_out" : "phone.log_in") + " " + Name(c.Other) + " · ";
                bool missed = !c.Connected && !c.Outgoing && c.EndReason == CallEndReason.Missed;
                if (c.Connected && c.End < 0)
                {
                    double start = c.Start;
                    Live(p, "log" + c.CallId + ":" + start, now, t => head2 + F("phone.log_active", UiKit.Clock(Math.Max(0, t - start))), "pl-dim");
                }
                else Line(p, head2 + (c.Connected ? UiKit.Clock(c.End - c.Start) : T("phone.log_missed." + c.EndReason)), missed ? "pl-bad" : "pl-dim");
            }
        }

        /// <summary>Recorded calls (leak buttons live in the meeting focus card).</summary>
        public static bool RecordingCurrentCall(LineView v)
        {
            var ph = v.Mine.Phone;
            if (ph.Status != CallStatus.Connected) return false;
            foreach (var r in v.Mine.Recorded) if (r.CallId == ph.CallId) return true;
            return false;
        }
    }
}
