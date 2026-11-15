using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 bot talk scenarios for the UI mock (docs/V5_BOT_TALK.md): <c>-uimock "v5.office_botcall*"</c> a bot calls during
    /// Client Bonus (ringing, its opening lie under the call pill, the bubble beside the phone with the question buttons,
    /// questions 1 and 3 answered with Greedy's lies), an Honest bot's Weekend Shift promise and the prim it pays after the
    /// reveal, a refusal ("Sonra ararım."), the v5.1 silence deals (a Greedy taker calls its victim with "Soruşturma açma,
    /// 1 prim senin." and pays; I pay an Honest witness who sends it back: "Paranı geri gönderiyorum..."), crisis questions;
    /// <c>"v5.meeting_botchat*"</c> bot lines in
    /// the meeting chat over their heads (accusations, the answer when accused, the folder's chat), the defence subtitle
    /// over the standing bot (and at the bottom when it is out of view), the final-vote lines. "_en" variants.
    /// Seat 0 is me (Mavi); Pembe (2) is a Greedy bot, Yeşil (3) an Honest bot, as LineMock.BaseView makes 2 and 3 bots.
    /// </summary>
    public static class BotTalkMockScenarios
    {
        const int Greedy = 2, Honest = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            // ---- a Greedy bot calls during Client Bonus: ring, its opener, the bubble, two questions
            LineMock.Register("v5.office_botcall", () => Office(false));
            LineMock.Register("v5.office_botcall_en", () => Office(true));

            // ---- an Honest bot: Weekend Shift ("sign up and I'll send you 1 prim"), the reveal, the prim it pays
            LineMock.Register("v5.office_botcall_promise", () => Promise(false));
            LineMock.Register("v5.office_botcall_promise_en", () => Promise(true));

            // ---- a refusal: a Mixed bot says it will call back (Greedy no longer turns its victim down: it pays it)
            LineMock.Register("v5.office_botcall_refuse", () => new LineMockScenario()
                .Do(0f, () => { Look(); var v = Break(); Dial(v, Honest); Show(v); Aim(0f, 15f); OfficeUi.I.OpenPhone(); }, 0.2f)
                .Do(1.0f, () => { BotSpeech.Hear(Honest, "bot.refuse.m", null); }, 0.7f)
                .Do(2.2f, () => { Show(Break()); Ended(Honest, CallEndReason.Declined); }, 0.6f));

            // ---- v5.1 silence deals: Pembe (Greedy) took my Client Bonus share and calls with the silence money
            LineMock.Register("v5.office_botcall_hush", () => Hush(false));
            LineMock.Register("v5.office_botcall_hush_en", () => Hush(true));
            // ---- I took the Petty Cash alone, Yeşil (Honest) is the witness: my 1 prim comes straight back
            LineMock.Register("v5.office_botcall_deal", () => Deal(false));
            LineMock.Register("v5.office_botcall_deal_en", () => Deal(true));

            // ---- crisis calls: I ask an Honest bot who it votes for, and not to vote for me
            LineMock.Register("v5.office_botcall_crisis", () => new LineMockScenario()
                .Do(0f, () => { Look(); var v = Crisis(); Connect(v, Honest, true); Show(v); Aim(0f, 15f); OfficeUi.I.OpenPhone(); BotSpeech.Hear(Honest, "bot.answer.h", Args(0)); })
                .Do(1.6f, () => BotSpeech.Ask(Honest, BotQuestionSet.Crisis, 0))
                .Do(2.4f, () => BotSpeech.Hear(Honest, "bot.crisis.a1.h", Args(Greedy)), true)
                .Do(4.0f, () => BotSpeech.Ask(Honest, BotQuestionSet.Crisis, 1))
                .Do(4.8f, () => BotSpeech.Hear(Honest, "bot.crisis.a2.h", null), true));

            // ---- meeting chat: bot lines over their heads, the answer when accused, the folder's chat
            LineMock.Register("v5.meeting_botchat", () => Meeting(false));
            LineMock.Register("v5.meeting_botchat_en", () => Meeting(true));

            // ---- the defence: a bot on its feet with its line as a subtitle; the final-vote lines
            LineMock.Register("v5.meeting_botchat_defence", () => Defence(false));
            LineMock.Register("v5.meeting_botchat_defence_en", () => Defence(true));
        }

        // ================================================================== office

        static LineMockScenario Office(bool en)
        {
            var s = new LineMockScenario();
            if (en) s.Do(0f, English);
            float t = en ? 0.3f : 0f;
            return s
                .Do(t, () => { Look(); var v = Bonus(en); Ring(v, Greedy); Show(v); Aim(0f, 15f); }, true)
                .Do(t + 1.6f, () => { var v = Bonus(en); Connect(v, Greedy, false); Show(v); BotSpeech.Hear(Greedy, "bot.cb.call.g", null); }, 0.7f)
                .Do(t + 2.6f, () => { }, 0.8f)
                .Do(t + 4.0f, () => OfficeUi.I.OpenPhone(), true)
                .Do(t + 5.6f, () => BotSpeech.Ask(Greedy, BotQuestionSet.ClientBonus, 0))
                .Do(t + 6.4f, () => BotSpeech.Hear(Greedy, "bot.cb.a1.g", null), 0.35f)
                .Do(t + 7.4f, () => { }, 0.5f)
                .Do(t + 8.4f, () => BotSpeech.Ask(Greedy, BotQuestionSet.ClientBonus, 2))
                .Do(t + 9.2f, () => BotSpeech.Hear(Greedy, "bot.cb.a3.g", null), true)
                .Do(t + 10.8f, () => { OfficeUi.I.OpenComputer(OfficeComputer.App.Task); }, true);
        }

        static LineMockScenario Promise(bool en)
        {
            var s = new LineMockScenario();
            if (en) s.Do(0f, English);
            float t = en ? 0.3f : 0f;
            return s
                .Do(t, () => { Look(); var v = Shift(LinePhase.TaskPlay, en); Ring(v, Honest); Show(v); Aim(0f, 15f); })
                .Do(t + 1.0f, () => { var v = Shift(LinePhase.TaskPlay, en); Connect(v, Honest, false); Show(v); OfficeUi.I.OpenPhone();
                    BotSpeech.Hear(Honest, "bot.shift.call.h_skip", Args(v.Task.Needed)); }, 2.4f)
                .Do(t + 4.0f, () => BotSpeech.Ask(Honest, BotQuestionSet.WeekendShift, 1))
                .Do(t + 4.8f, () => BotSpeech.Hear(Honest, "bot.shift.a2.h", Args(2)), true)
                .Do(t + 6.6f, () => BotSpeech.Hear(Honest, "bot.bye.h", null), 1.4f)
                // the reveal: I signed, Yeşil did not; it keeps its word
                .Do(t + 8.4f, () => { OfficeUi.I.CloseFocus(); var v = Shift(LinePhase.TaskReveal, en); Show(v); Ended(Honest, CallEndReason.HungUp); }, 0.4f)
                .Do(t + 9.4f, () =>
                {
                    var v = Shift(LinePhase.TaskReveal, en);
                    v.Mine.Prim += 1;
                    v.Mine.Transfers.Add(new LineTransferView { From = Honest, To = 0, Amount = 1, Time = LineMock.Now, Phase = LinePhase.TaskReveal });
                    Show(v);
                    LineClient.I.ApplyEvents(new List<LineEvent>
                    {
                        new LineEvent { Type = LineEventType.TransferReceived, Actor = Honest, Target = 0, A = 1, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now },
                    });
                }, 0.9f)
                .Do(t + 11.0f, () => OfficeUi.I.OpenComputer(OfficeComputer.App.Transfer), true);
        }

        static LineMockScenario Hush(bool en)
        {
            var s = new LineMockScenario();
            if (en) s.Do(0f, English);
            float t = en ? 0.3f : 0f;
            return s
                .Do(t, () => { Look(); var v = Break(); Names(v, en); Ring(v, Greedy); Show(v); Aim(0f, 15f); }, true)
                .Do(t + 1.2f, () => { var v = Break(); Names(v, en); Connect(v, Greedy, false); Show(v); OfficeUi.I.OpenPhone();
                    BotSpeech.Hear(Greedy, "bot.cb.hush.g", null); }, 1.6f)
                .Do(t + 3.4f, () =>
                {
                    var v = Break();
                    Names(v, en);
                    Connect(v, Greedy, false);
                    v.Mine.Prim += 1;
                    v.Mine.Transfers.Add(new LineTransferView { From = Greedy, To = 0, Amount = 1, Time = LineMock.Now, Phase = LinePhase.Break });
                    Show(v);
                    LineClient.I.ApplyEvents(new List<LineEvent>
                    {
                        new LineEvent { Type = LineEventType.TransferReceived, Actor = Greedy, Target = 0, A = 1, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now },
                    });
                }, 0.9f)
                .Do(t + 5.0f, () => BotSpeech.Hear(Greedy, "bot.bye.g", null), 1.2f);
        }

        static LineMockScenario Deal(bool en)
        {
            var s = new LineMockScenario();
            if (en) s.Do(0f, English);
            float t = en ? 0.3f : 0f;
            return s
                .Do(t, () => { Look(); var v = CashBreak(); Names(v, en); Connect(v, Honest, true); Show(v); Aim(0f, 15f); OfficeUi.I.OpenPhone();
                    BotSpeech.Hear(Honest, "bot.answer.h", Args(0)); }, 1.0f)
                .Do(t + 1.6f, () =>
                {
                    var v = CashBreak();
                    Names(v, en);
                    Connect(v, Honest, true);
                    v.Mine.Prim -= 1;
                    v.Mine.Transfers.Add(new LineTransferView { From = 0, To = Honest, Amount = 1, Time = LineMock.Now, Phase = LinePhase.Break });
                    Show(v);
                }, 0.4f)
                .Do(t + 2.6f, () => BotSpeech.Hear(Honest, "bot.deal.refund.h", null), 1.6f)
                .Do(t + 4.6f, () =>
                {
                    var v = CashBreak();
                    Names(v, en);
                    Connect(v, Honest, true);
                    v.Mine.Transfers.Add(new LineTransferView { From = 0, To = Honest, Amount = 1, Time = LineMock.Now - 3, Phase = LinePhase.Break });
                    v.Mine.Transfers.Add(new LineTransferView { From = Honest, To = 0, Amount = 1, Time = LineMock.Now, Phase = LinePhase.Break });
                    Show(v);
                    LineClient.I.ApplyEvents(new List<LineEvent>
                    {
                        new LineEvent { Type = LineEventType.TransferReceived, Actor = Honest, Target = 0, A = 1, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now },
                    });
                }, 0.9f);
        }

        /// <summary>The Petty Cash break: I took alone, Yeşil (Honest) left the cash and is my witness.</summary>
        static LineView CashBreak()
        {
            var v = LineMock.BaseView(LinePhase.Break, 4, 0, 22);
            v.TaskNumber = 1;
            var t = new LineTaskView { Kind = TaskKind.PettyCash, Number = 1, Family = TaskFamily.Pair, Revealed = true, Submitted = true, MyChoice = LineChoice.Take, MyPayout = 3 };
            t.Group.Add(0); t.Group.Add(Honest);
            t.Choices.Add(new LineChoiceView { TaskNumber = 1, Task = TaskKind.PettyCash, Player = 0, Choice = LineChoice.Take, Payout = 3 });
            t.Choices.Add(new LineChoiceView { TaskNumber = 1, Task = TaskKind.PettyCash, Player = Honest, Choice = LineChoice.Leave, Payout = -1 });
            v.Task = t;
            v.Features.WhistleOpen = true;
            v.Mine.Group.Clear(); v.Mine.Group.AddRange(t.Group);
            v.Mine.CallLog.Clear();
            v.Mine.Transfers.Clear();
            v.Mine.Prim = 12;
            v.LineBoard.Clear();
            return v;
        }

        static void Look() { RoomsPresenter.ForceLookInput = true; BotSpeech.ClearTranscript(); }

        static void Aim(float yaw, float pitch)
        {
            var p = RoomsPresenter.Instance;
            if (p != null) { p.SitDown(false); p.SetLook(yaw, pitch); }
        }

        static List<int> Args(params int[] a) { return new List<int>(a); }

        static void Names(LineView v, bool en) { if (en) LineMock.SetNames(v, true); }

        /// <summary>Client Bonus with Pembe (a bot) as my partner.</summary>
        static LineView Bonus(bool en)
        {
            var v = LineMock.BaseView(LinePhase.TaskPlay, 4, 0, 46);
            v.TaskNumber = 0;
            var t = new LineTaskView { Kind = TaskKind.ClientBonus, Number = 0, Family = TaskFamily.Pair, PlayEndTime = LineMock.Now + 46 };
            t.Group.Add(0); t.Group.Add(Greedy);
            v.Task = t;
            v.Mine.Group.Clear(); v.Mine.Group.AddRange(t.Group);
            v.Mine.CallLog.Clear();
            v.Mine.Transfers.Clear();
            v.Mine.Prim = 6;
            v.LineBoard.Clear();
            Names(v, en);
            return v;
        }

        static LineView Shift(LinePhase phase, bool en)
        {
            bool revealed = phase != LinePhase.TaskPlay;
            var v = LineMock.BaseView(phase, 4, 1, revealed ? 6 : 44);
            v.TaskNumber = 2;
            var t = new LineTaskView { Kind = TaskKind.WeekendShift, Number = 2, Family = TaskFamily.All, PlayEndTime = LineMock.Now + 40, Needed = 2,
                                       Revealed = revealed, Submitted = revealed, MyChoice = revealed ? LineChoice.Volunteer : 0 };
            for (int i = 0; i < 4; i++) t.Group.Add(i);
            if (revealed)
            {
                int[] roster = { 0, 1 };
                for (int i = 0; i < 4; i++)
                {
                    bool on = System.Array.IndexOf(roster, i) >= 0;
                    t.Choices.Add(new LineChoiceView { TaskNumber = 2, Task = TaskKind.WeekendShift, Player = i, Choice = on ? 1 : 0, Payout = i == 0 ? 0 : -1 });
                }
                t.Roster.AddRange(roster);
                t.MyPayout = 0;
            }
            v.Task = t;
            v.Mine.Group.Clear(); v.Mine.Group.AddRange(t.Group);
            v.Mine.CallLog.Clear();
            v.Mine.Transfers.Clear();
            v.Mine.Prim = 8;
            v.LineBoard.Clear();
            Names(v, en);
            return v;
        }

        /// <summary>The break after Client Bonus: Pembe took it all, I shared.</summary>
        static LineView Break()
        {
            var v = LineMock.BaseView(LinePhase.Break, 4, 0, 24);
            v.TaskNumber = 0;
            var t = new LineTaskView { Kind = TaskKind.ClientBonus, Number = 0, Family = TaskFamily.Pair, Revealed = true, Submitted = true, MyChoice = 0, Takers = 1, MyPayout = 0 };
            t.Group.Add(0); t.Group.Add(Greedy);
            t.Choices.Add(new LineChoiceView { TaskNumber = 0, Task = TaskKind.ClientBonus, Player = 0, Choice = 0, Payout = 0 });
            t.Choices.Add(new LineChoiceView { TaskNumber = 0, Task = TaskKind.ClientBonus, Player = Greedy, Choice = 1, Payout = -1 });
            v.Task = t;
            v.Mine.Group.Clear(); v.Mine.Group.AddRange(t.Group);
            v.LineBoard.Clear();
            return v;
        }

        static LineView Crisis()
        {
            var v = LineMock.BaseView(LinePhase.CrisisCalls, 4, 2, 50);
            v.Meeting = new LineMeetingView { Index = 2 };
            foreach (var p in v.Players) p.Blame = new[] { 3, 2, 6, 1 }[p.Id];
            v.LineBoard.Clear();
            v.Mine.CallLog.Clear();
            return v;
        }

        static void Ring(LineView v, int from)
        {
            v.Mine.Phone.Status = CallStatus.Ringing;
            v.Mine.Phone.Other = from;
            v.Mine.Phone.Since = LineMock.Now;
            v.Mine.Phone.RingUntil = LineMock.Now + 10;
            v.LineBoard.Add(new LineBoardCall { CallId = 40, Caller = from, Callee = 0, Status = CallStatus.Ringing, Start = LineMock.Now - 1 });
        }

        static void Dial(LineView v, int to)
        {
            v.Mine.Phone.Status = CallStatus.Dialing;
            v.Mine.Phone.Other = to;
            v.Mine.Phone.Since = LineMock.Now;
            v.Mine.Phone.RingUntil = LineMock.Now + 10;
            v.LineBoard.Add(new LineBoardCall { CallId = 41, Caller = 0, Callee = to, Status = CallStatus.Ringing, Start = LineMock.Now - 1 });
        }

        static void Connect(LineView v, int other, bool outgoing)
        {
            var ph = v.Mine.Phone;
            ph.Status = CallStatus.Connected; ph.Other = other; ph.CallId = 40; ph.Since = LineMock.Now - 2;
            v.Mine.CallLog.Add(new LineCallRecordView { CallId = 40, Other = other, Outgoing = outgoing, Connected = true, Start = LineMock.Now - 2, End = -1 });
            v.LineBoard.Clear();
            v.LineBoard.Add(new LineBoardCall { CallId = 40, Caller = outgoing ? 0 : other, Callee = outgoing ? other : 0, Status = CallStatus.Connected, Start = LineMock.Now - 2 });
        }

        static void Ended(int other, CallEndReason reason)
        {
            LineClient.I.ApplyEvents(new List<LineEvent>
            {
                new LineEvent { Type = LineEventType.CallEnded, Actor = other, A = (int)reason, B = 41, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now },
            });
        }

        static void Show(LineView v)
        {
            LineMock.SyncMessageSeq(v);
            LineClient.I.ApplyView(v);
        }

        static void English()
        {
            Loc.Load("en");
            if (UIRoot.I != null) UIRoot.I.Rebuild();
        }

        // ================================================================== meeting room

        static LineMockScenario Meeting(bool en)
        {
            var s = new LineMockScenario();
            if (en) s.Do(0f, MeetingMockScenarios.English);
            float t = en ? 0.3f : 0f;
            return s
                .Do(t, () => { MeetingMockScenarios.Reset(); var v = MeetingMockScenarios.Discussion(4); Names(v, en); MeetingMockScenarios.Show(v); MeetingMockScenarios.Aim(0f, 6f); })
                // Yeşil (Honest) was robbed by Pembe in Client Bonus
                .Do(t + 0.8f, () => { Chat(Honest, "bot.meet.victim.h", Greedy); MeetingMockScenarios.AimAtSlot(Honest); }, 1.6f)
                // Pembe (Greedy) answers: a made-up counter-accusation
                .Do(t + 3.4f, () => { Chat(Greedy, "bot.meet.accused.g", Honest); MeetingMockScenarios.AimAtSlot(Greedy); }, 0.5f)
                .Do(t + 4.4f, () => { }, 1.0f)
                // both in view
                .Do(t + 6.2f, () => { Chat(Honest, "bot.meet.accused.h", -1); MeetingMockScenarios.Aim(0f, 6f); }, 1.2f)
                .Do(t + 8.0f, () => MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Chat), true);
        }

        static LineMockScenario Defence(bool en)
        {
            var s = new LineMockScenario();
            if (en) s.Do(0f, MeetingMockScenarios.English);
            float t = en ? 0.3f : 0f;
            return s
                .Do(t, () =>
                {
                    MeetingMockScenarios.Reset();
                    var v = MeetingMockScenarios.Defense(4);
                    v.Final.Candidates.Clear(); v.Final.Candidates.AddRange(new[] { Honest, Greedy });
                    v.Final.DefenseOrder.Clear(); v.Final.DefenseOrder.AddRange(new[] { Honest, Greedy });
                    v.Final.Options.Clear(); v.Final.Options.AddRange(new[] { Honest, Greedy });
                    v.Final.Defender = Honest;
                    MeetingMockScenarios.Candidates(v);
                    Names(v, en);
                    MeetingMockScenarios.Show(v);
                    MeetingMockScenarios.AimAtSlot(Honest);
                })
                // Yeşil (Honest) defends on its feet across the table: the subtitle over its head
                .Do(t + 1.6f, () => { AimAtHead(Honest); Chat(Honest, "bot.meet.accused.h", -1); }, 0.6f)
                .Do(t + 2.8f, () => { }, 1.2f)
                // out of view: the defence reads as a subtitle at the bottom
                .Do(t + 4.6f, () => MeetingMockScenarios.Aim(-55f, 4f), 1.0f)
                // the final vote: where the bots' votes go (Honest names the board's leader, Greedy talks drawers)
                .Do(t + 6.4f, () =>
                {
                    MeetingMockScenarios.Fresh();
                    var v = LineMock.BaseView(LinePhase.FinalVote, 4, 2, 20);
                    foreach (var p in v.Players) p.Blame = new[] { 3, 8, 11, 7 }[p.Id];
                    v.Meeting = new LineMeetingView { Index = 2 };
                    v.Final = MeetingMockScenarios.Final(new[] { Honest, Greedy }, 1);
                    MeetingMockScenarios.Candidates(v);
                    v.Mine.Ballots.Add(-1);
                    Names(v, en);
                    MeetingMockScenarios.Show(v);
                    MeetingMockScenarios.Aim(0f, 6f);
                    Chat(Honest, "bot.vote.h", Greedy);
                }, 0.9f)
                .Do(t + 7.8f, () => Chat(Greedy, "bot.vote.g", -1), 1.2f)
                .Do(t + 9.4f, () => MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Chat), true);
        }

        /// <summary>Looks at a colleague's head (standing or seated) with room above it for the subtitle.</summary>
        static void AimAtHead(int slot)
        {
            var room = MeetingMockScenarios.Room();
            Vector3 h;
            if (room != null && room.TryGetHead(slot, Vector3.zero, out h)) MeetingMockScenarios.AimAt(h + Vector3.down * 0.32f);
        }

        /// <summary>A bot posts a line in the meeting chat (the token every client renders in its language).</summary>
        static void Chat(int bot, string key, int arg)
        {
            var line = new BotLine { Key = key, Speaker = bot, Listener = -1 };
            if (arg >= 0) line.Args.Add(arg);
            LineClient.I.ApplyMessages(1, new List<LineMessage> { LineMock.Msg(MessageKind.MeetingChat, bot, -1, LineBotLines.Token(line), true, 0) });
            var v = LineClient.I.View;
            if (v != null) LineMock.SyncMessageSeq(v);
        }
    }
}
