using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI mock scenarios for the desk UI (run with <c>-uimock "desk.*" -uimockout &lt;dir&gt;</c>). Every state of the office
    /// phases at 3–6 players, Turkish and English. Views are built when their step runs so times are relative to the
    /// mock clock. Language switches restore the saved preference immediately (no lasting side effect).
    /// </summary>
    public static class DeskMocks
    {
        static readonly string[] TrNames = { "Mavi", "Sarı", "Pembe", "Yeşil", "Turuncu", "Mor" };
        static readonly string[] EnNames = { "Alex", "Sam", "Jo", "Kim", "Lee", "Max" };
        static readonly int[] Blames = { 5, 7, 3, 6, 4, 8 };

        static DeskUi Ui => DeskUi.I;
        static double Now => LineMock.Now;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // ---- office HUD, notifications, hover hint
            Reg("a_office.6p", false, s => s
                .Show(0.1f, () => WithInbox(Play(V(LinePhase.RoundPlay, 6), RoundType.PrimHavuzu, 0)))
                .Do(0.4f, () => { Ui.CloseComputer(); Ui.Hud.ForceHover(DeskTarget.Monitor); })
                .Events(0.6f, Ev(LineEventType.MailReceived, a: 12), Ev(LineEventType.FavouriteNamed, target: 2), Ev(LineEventType.CallBusy, target: 3))
                .Do(0.9f, () => { }, true));
            Reg("a_office.3p.en", true, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 3, true, left: 8), RoundType.OrtakSifre, 0))
                .Do(0.4f, () => { Ui.CloseComputer(); Ui.Hud.ForceHover(DeskTarget.Phone); })
                .Events(0.6f, Ev(LineEventType.MissedCall, actor: 2), Ev(LineEventType.TrickHit, a: (int)TrickType.FisCek))
                .Do(1.0f, () => { }, true));

            // ---- morning call
            Reg("b_morning.ring.onboard.3p", false, s => s
                .Show(0.1f, () => Onboard(V(LinePhase.Morning, 3, day: 0, left: 36, mgr: LineManagerIds.Disiplinci)))
                .Do(1.2f, () => { }, true));
            Reg("b_morning.call.6p", false, s => s
                .Show(0.1f, () => { var v = V(LinePhase.Morning, 6, day: 1, left: 18, mgr: LineManagerIds.Kayirmaci); v.Favourite = 2; return v; })
                .Do(0.5f, () => Ui.Cards.AnswerManager())
                .Do(4.5f, () => { }, true));
            Reg("b_morning.call.4p.en", true, s => s
                .Show(0.1f, () => { var v = V(LinePhase.Morning, 4, true, day: 4, left: 18, mgr: LineManagerIds.Paranoyak); v.Features.IsLastDay = true; SetRounds(v, RoundType.OrtakSifre, RoundType.KayipRapor); return v; })
                .Do(0.5f, () => Ui.Cards.AnswerManager())
                .Do(4.5f, () => { }, true));

            // ---- unlock cards (first week)
            Reg("c_unlock.trick.5p", false, s => s
                .Show(0.1f, () => FirstWeek(V(LinePhase.Morning, 5, day: 1, left: 18), tips: false))
                .Do(0.5f, () => Ui.Cards.AnswerManager())
                .Do(0.9f, () => Ui.Cards.Back())
                .Do(1.0f, () => { }, true));
            Reg("c_unlock.line.4p.en", true, s => s
                .Show(0.1f, () => FirstWeek(V(LinePhase.Morning, 4, true, day: 2, left: 18), tips: true))
                .Do(0.3f, () => Ui.State.TrickUnlockSeen = true)
                .Do(0.5f, () => Ui.Cards.AnswerManager())
                .Do(0.9f, () => Ui.Cards.Back())
                .Do(1.0f, () => { }, true));

            // ---- round intro (rule cards)
            Reg("d_intro.prim.long.onboard.4p", false, s => s
                .Show(0.1f, () => { var v = Onboard(V(LinePhase.RoundIntro, 4, day: 0, left: 14)); v.Features.FirstTimeRound = true; return Intro(v, RoundType.PrimHavuzu, 0); })
                .Do(0.3f, () => { Ui.State.MorningAnsweredDay = 0; Ui.State.ObOpenedComputer = true; })
                .Do(1.4f, () => { }, true));
            Reg("d_intro.code.6p.en", true, s => s
                .Show(0.1f, () => Intro(V(LinePhase.RoundIntro, 6, true, left: 6), RoundType.OrtakSifre, 0))
                .Do(1.2f, () => { }, true));
            Reg("d_intro.odd.long.5p", false, s => s
                .Show(0.1f, () => { var v = V(LinePhase.RoundIntro, 5, left: 15); v.Features.FirstTimeRound = true; return Intro(v, RoundType.FarkliEposta, 1); })
                .Do(1.4f, () => { }, true));

            // ---- round play (Task app)
            Reg("e_play.prim.pair.6p", false, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 6, left: 118), RoundType.PrimHavuzu, 0, r => { r.Partners.Add(3); r.Partner = 3; }, submitted: new[] { 1, 2, 4 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(-1, 0), true));
            Reg("e_play.prim.trio.5p.en", true, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 5, true, left: 64), RoundType.PrimHavuzu, 0, r => { r.Partners.Add(2); r.Partners.Add(4); r.Partner = 2; r.Submitted = true; r.MyA = 1; }, submitted: new[] { 0, 3 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(-1, 1), true));
            Reg("e_play.odd.majority.4p", false, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 4, left: 96), RoundType.FarkliEposta, 1, r => { r.TopicId = 4; }, submitted: new[] { 2 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(3, -1), true));
            Reg("e_play.odd.oddone.6p.en", true, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 6, true, left: 77), RoundType.FarkliEposta, 1, r => { r.TopicId = 21; r.IsOdd = true; r.GuessOptions.AddRange(new[] { 6, 4, 7, 5 }); }, submitted: new[] { 1, 5 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(2, 4), true));
            Reg("e_play.lost.culprit.6p", false, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 6, left: 88), RoundType.KayipRapor, 1, r => { r.IsCulprit = true; r.ClueInnocent = 3; r.NoAlibi.Add(2); r.NoAlibi.Add(4); }, submitted: new[] { 3 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(4, -1), true));
            Reg("e_play.lost.innocent.4p.en", true, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 4, true, left: 54), RoundType.KayipRapor, 1, r => { r.ClueInnocent = 2; }))
                .Do(0.5f, () => { }, true));
            Reg("e_play.complaint.5p", false, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 5, left: 102), RoundType.IhbarKutusu, 1, null, submitted: new[] { 2, 4 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(3, 2), true));
            Reg("e_play.promo.3p.en", true, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 3, true, left: 41), RoundType.TerfiListesi, 1, r => { r.Submitted = true; r.MyTarget = 2; }, submitted: new[] { 0, 1 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(2, -1), true));
            Reg("e_play.code.6p", false, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 6, left: 131), RoundType.OrtakSifre, 0, r => Code(r, 6, false, false), submitted: new[] { 3 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(-1, -1, 4, 7, 3), true));
            Reg("e_play.code.saboteur.4p.en", true, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 4, true, left: 23), RoundType.OrtakSifre, 0, r => Code(r, 4, true, true), submitted: new[] { 0, 1, 2 }))
                .Do(0.5f, () => Ui.Computer.Task.MockSelect(-1, -1, 5, 8, 2, 1), true));
            Reg("e_play.idle.morning.5p", false, s => s
                .Show(0.1f, () => V(LinePhase.Morning, 5, left: 12))
                .Do(0.4f, () => { Ui.Cards.AnswerManager(); Ui.Cards.Back(); Ui.OpenComputer(DeskApp.Task); })
                .Do(0.6f, () => { }, true));

            // ---- reveal
            Reg("f_reveal.prim.6p", false, s => s.Show(0.1f, () => Reveal(V(LinePhase.RoundReveal, 6, left: 12), RevealPrim(false))).Do(4.8f, () => { }, true));
            Reg("f_reveal.prim.trio.5p.en", true, s => s.Show(0.1f, () => Reveal(V(LinePhase.RoundReveal, 5, true, left: 12), RevealPrim(true))).Do(4.8f, () => { }, true));
            Reg("f_reveal.odd.6p", false, s => s.Show(0.1f, () => Reveal(V(LinePhase.RoundReveal, 6, left: 12), RevealOdd())).Do(4.8f, () => { }, true));
            Reg("f_reveal.lost.5p.en", true, s => s.Show(0.1f, () => Reveal(V(LinePhase.RoundReveal, 5, true, left: 12), RevealLost())).Do(4.8f, () => { }, true));
            Reg("f_reveal.complaint.6p", false, s => s.Show(0.1f, () => Reveal(V(LinePhase.RoundReveal, 6, left: 12), RevealComplaint())).Do(4.8f, () => { }, true));
            Reg("f_reveal.promo.4p", false, s => s.Show(0.1f, () => Reveal(V(LinePhase.RoundReveal, 4, left: 12), RevealPromo())).Do(4.8f, () => { }, true));
            Reg("f_reveal.code.6p", false, s => s.Show(0.1f, () => Reveal(Codeview(V(LinePhase.RoundReveal, 6, left: 12), 6), RevealCode(6, true))).Do(4.8f, () => { }, true));
            Reg("f_reveal.code.4p.en", true, s => s.Show(0.1f, () => Reveal(Codeview(V(LinePhase.RoundReveal, 4, true, left: 12), 4), RevealCode(4, false))).Do(4.8f, () => { }, true));

            // ---- other apps
            Reg("g_mail.inbox.6p", false, s => s
                .Show(0.1f, () => WithInbox(Play(V(LinePhase.RoundPlay, 6, left: 70), RoundType.PrimHavuzu, 0)))
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Mail))
                .Do(0.6f, () => Ui.Computer.Mail.Select(13), true));
            Reg("g_mail.compose.4p.en", true, s => s
                .Show(0.1f, () => WithInbox(Play(V(LinePhase.RoundPlay, 4, true, left: 70), RoundType.FarkliEposta, 1)))
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Mail))
                .Do(0.6f, () => Ui.Computer.Mail.Compose(LineIds.None, 6, 2), true));
            Reg("h_record.6p", false, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 6, mgr: LineManagerIds.Kayirmaci, left: 70), RoundType.OrtakSifre, 0); v.Favourite = 2; v.TodayReveals.Add(RevealPrim(false)); return v; })
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Record))
                .Do(0.6f, () => { }, true));
            Reg("h_record.3p.en", true, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 3, true, day: 2, left: 70), RoundType.TerfiListesi, 1); v.DayCount = 3; v.Features.IsLastDay = true; return v; })
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Record))
                .Do(0.6f, () => { }, true));
            Reg("i_trick.coffee.armed.6p", false, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 6, left: 70), RoundType.PrimHavuzu, 0))
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Trick))
                .Do(0.6f, () => Ui.Computer.Trick.MockPick(3, -1, -1, true), true));
            Reg("i_trick.fake.4p.en", true, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 4, true, left: 70), RoundType.KayipRapor, 1); v.Mine.Trick = TrickType.SahteEposta; return v; })
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Trick))
                .Do(0.6f, () => Ui.Computer.Trick.MockPick(2, 4, 3, false), true));
            Reg("i_trick.locked.3p", false, s => s
                .Show(0.1f, () => { var v = FirstWeek(Play(V(LinePhase.RoundPlay, 3, day: 0, left: 70), RoundType.PrimHavuzu, 0), false); v.Features.TricksUnlocked = false; v.Mine.Trick = TrickType.FisCek; return v; })
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Trick))
                .Do(0.6f, () => { }, true));
            Reg("i_trick.used.5p.en", true, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 5, true, left: 70), RoundType.OrtakSifre, 0); v.Mine.Trick = TrickType.HatDinle; v.Mine.TrickUsedToday = true; v.Mine.TricksLeftThisWeek = 1; return v; })
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Trick))
                .Do(0.6f, () => { }, true));
            Reg("j_agenda.5p", false, s => s
                .Show(0.1f, () => Play(V(LinePhase.RoundPlay, 5, day: 2, left: 70), RoundType.OrtakSifre, 0))
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Agenda))
                .Do(0.6f, () => { }, true));
            Reg("j_agenda.3p.en", true, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 3, true, day: 1, left: 70), RoundType.FarkliEposta, 1); v.DayCount = 3; v.Length = WeekLength.Short; return v; })
                .Do(0.4f, () => Ui.OpenComputer(DeskApp.Agenda))
                .Do(0.6f, () => { }, true));
            Reg("k_blocked.coffee.6p", false, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 6, left: 70), RoundType.PrimHavuzu, 0); v.Mine.ComputerBlockedUntil = Now + 32; v.Mine.ComputerBlockedBy = TrickType.KahveDok; return v; })
                .Do(0.6f, () => { }, true));
            Reg("k_blocked.offline.4p.en", true, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 4, true, left: 70), RoundType.KayipRapor, 1); v.Mine.ComputerBlockedUntil = Now + 21; v.Mine.ComputerBlockedBy = TrickType.InternetKes; return v; })
                .Do(0.6f, () => { }, true));

            // ---- desk phone
            Reg("l_phone.idle.6p", false, s => s
                .Show(0.1f, () => WithLog(Play(V(LinePhase.RoundPlay, 6, left: 70), RoundType.PrimHavuzu, 0)))
                .Do(0.4f, () => Ui.OpenPhone())
                .Do(0.6f, () => { }, true));
            Reg("l_phone.dialing.4p", false, s => s
                .Show(0.1f, () => { var v = WithLog(Play(V(LinePhase.RoundPlay, 4, left: 70), RoundType.FarkliEposta, 1)); Phone(v, CallStatus.Dialing, 3, ring: 11); return v; })
                .Do(0.4f, () => Ui.OpenPhone())
                .Do(0.6f, () => { }, true));
            Reg("l_phone.connected.5p.en", true, s => s
                .Show(0.1f, () => { var v = WithLog(Play(V(LinePhase.RoundPlay, 5, true, left: 70), RoundType.PrimHavuzu, 0)); Phone(v, CallStatus.Connected, 1, since: 42); return v; })
                .Do(0.4f, () => Ui.OpenPhone())
                .Do(0.6f, () => { }, true));
            Reg("l_phone.busy.3p", false, s => s
                .Show(0.1f, () => WithLog(Play(V(LinePhase.RoundPlay, 3, left: 70), RoundType.OrtakSifre, 0)))
                .Do(0.4f, () => Ui.OpenPhone())
                .Events(0.6f, Ev(LineEventType.CallBusy, target: 2))
                .Do(0.7f, () => { }, true));
            Reg("l_phone.dead.6p.en", true, s => s
                .Show(0.1f, () => { var v = WithLog(Play(V(LinePhase.RoundPlay, 6, true, left: 70), RoundType.PrimHavuzu, 0)); v.Mine.PhoneDeadUntil = Now + 31; return v; })
                .Do(0.4f, () => Ui.OpenPhone())
                .Do(0.6f, () => { }, true));
            Reg("l_phone.tipoff.confirm.5p", false, s => s
                .Show(0.1f, () => WithLog(Play(V(LinePhase.RoundPlay, 5, day: 3, left: 70), RoundType.KayipRapor, 1)))
                .Do(0.4f, () => Ui.OpenPhone())
                .Do(0.6f, () => Ui.Phone.MockIvr(2, 4), true));
            Reg("l_phone.tipoff.menu.4p.en", true, s => s
                .Show(0.1f, () => WithLog(Play(V(LinePhase.RoundPlay, 4, true, day: 3, left: 70), RoundType.KayipRapor, 1)))
                .Do(0.4f, () => Ui.OpenPhone())
                .Do(0.6f, () => Ui.Phone.MockIvr(1, -1), true));

            // ---- calls without the phone open
            Reg("m_call.incoming.6p", false, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 6, left: 70), RoundType.PrimHavuzu, 0); Phone(v, CallStatus.Ringing, 2, ring: 12); return v; })
                .Do(0.4f, () => Ui.CloseComputer())
                .Do(0.9f, () => { }, true));
            Reg("m_call.incall.task.4p.en", true, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 4, true, left: 88), RoundType.PrimHavuzu, 0, r => { r.Partners.Add(1); r.Partner = 1; }); Phone(v, CallStatus.Connected, 1, since: 17); v.Mine.CallLog.Add(new LineCallRecordView { CallId = 3, Other = 1, Outgoing = true, Connected = true, Start = Now - 17 }); return v; })
                .Do(0.6f, () => Ui.Computer.Task.MockSelect(-1, 0), true));
            Reg("m_call.wiretap.5p", false, s => s
                .Show(0.1f, () => { var v = Play(V(LinePhase.RoundPlay, 5, left: 70), RoundType.OrtakSifre, 0); v.Mine.Trick = TrickType.HatDinle; v.Mine.WiretapTarget = 3; v.Mine.WiretapUntil = Now + 44; return v; })
                .Do(0.4f, () => Ui.CloseComputer())
                .Do(0.6f, () => { }, true));

            // ---- onboarding steps (first day)
            Reg("n_onboard.call_partner.4p", false, s => s
                .Show(0.1f, () => Onboard(Play(V(LinePhase.RoundPlay, 4, day: 0, left: 120), RoundType.PrimHavuzu, 0, r => { r.Partners.Add(2); r.Partner = 2; })))
                .Do(0.3f, () => { Ui.State.MorningAnsweredDay = 0; Ui.State.IntroSeenKey = 0; })
                .Do(0.8f, () => { }, true));
            Reg("n_onboard.office.3p.en", true, s => s
                .Show(0.1f, () => Onboard(V(LinePhase.Morning, 3, true, day: 0, left: 22)))
                .Do(0.3f, () => { Ui.Cards.AnswerManager(); })
                .Do(0.6f, () => { Ui.Cards.Back(); })
                .Do(0.9f, () => { }, true));

        }

        // ------------------------------------------------------------------ scenario plumbing

        static void Reg(string name, bool en, Action<LineMockScenario> build)
        {
            LineMock.Register("desk." + name, () =>
            {
                var s = new LineMockScenario { Me = 0 };
                s.Do(0f, () => { Rooms.Hovered = DeskTarget.None; Lang(en ? "en" : "tr"); });
                build(s);
                return s;
            });
        }

        /// <summary>Switches the language for the screenshot without changing the saved preference.</summary>
        static void Lang(string want)
        {
            if (Loc.Language == want) return;
            bool had = PlayerPrefs.HasKey(Loc.PrefKey);
            string saved = PlayerPrefs.GetString(Loc.PrefKey, "");
            Loc.SetLanguage(want);
            if (had) PlayerPrefs.SetString(Loc.PrefKey, saved); else PlayerPrefs.DeleteKey(Loc.PrefKey);
            PlayerPrefs.Save();
            Ui?.MarkLocDirty();
        }

        static LineMockScenario Show(this LineMockScenario s, float at, Func<LineView> make, bool shot = false)
        {
            return s.Do(at, () =>
            {
                var v = make();
                v.Now = LineClient.I.Now;
                LineClient.I.ApplyView(v);
            }, shot);
        }

        static LineMockScenario Events(this LineMockScenario s, float at, params LineEvent[] ev)
        {
            return s.Do(at, () => LineClient.I.ApplyEvents(new List<LineEvent>(ev)));
        }

        static LineEvent Ev(LineEventType t, int actor = -1, int target = -1, int a = -1)
        {
            return new LineEvent { Type = t, Time = Now, Audience = LineAudience.All, Actor = actor, Target = target, A = a };
        }

        // ------------------------------------------------------------------ view builders

        static LineView V(LinePhase phase, int players, bool en = false, int day = 1, double left = 90, string mgr = LineManagerIds.Paragoz)
        {
            var v = LineMock.BaseView(phase, players, day, left);
            v.ManagerProfileId = mgr;
            for (int i = 0; i < v.Players.Count; i++)
            {
                v.Players[i].Name = (en ? EnNames : TrNames)[i];
                v.Players[i].Blame = Blames[i];
                v.Players[i].Ihtars = i == 1 ? 1 : 0;
            }
            v.Mine.TricksLeftThisWeek = 2;
            v.Mine.TipsLeftThisWeek = 1;
            v.Mine.AgendaTarget = players > 3 ? 3 : 2;
            v.Mine.Trick = TrickType.KahveDok;
            SetRounds(v, RoundType.PrimHavuzu, players >= 4 ? RoundType.KayipRapor : RoundType.FarkliEposta);
            return v;
        }

        static void SetRounds(LineView v, RoundType a, RoundType b)
        {
            v.TodayRounds.Clear();
            v.TodayRounds.Add(a);
            v.TodayRounds.Add(b);
        }

        static LineView Onboard(LineView v)
        {
            v.FirstWeek = true;
            v.Day = 0;
            v.Features.OnboardingDay = true;
            v.Features.TricksUnlocked = false;
            v.Features.TipLineUnlocked = false;
            SetRounds(v, RoundType.PrimHavuzu, RoundType.FarkliEposta);
            return v;
        }

        static LineView FirstWeek(LineView v, bool tips)
        {
            v.FirstWeek = true;
            v.Features.TricksUnlocked = true;
            v.Features.TipLineUnlocked = tips;
            return v;
        }

        static LineRoundView NewRound(LineView v, RoundType t, int index)
        {
            v.RoundIndex = index;
            if (index < v.TodayRounds.Count) v.TodayRounds[index] = t;
            var r = new LineRoundView { Type = t, Index = index, PlayEndTime = v.PhaseEndTime, AttemptsLeft = 0 };
            if (t == RoundType.PrimHavuzu) { r.Partner = v.Players.Count > 3 ? 3 : 1; }
            if (t == RoundType.FarkliEposta) { r.TopicId = 4; }
            if (t == RoundType.KayipRapor) { r.ClueInnocent = 2; }
            if (t == RoundType.OrtakSifre) Code(r, v.Players.Count, false, false);
            if (t == RoundType.PrimHavuzu && r.Partners.Count == 0) r.Partners.Add(r.Partner);
            return r;
        }

        static LineView Intro(LineView v, RoundType t, int index)
        {
            v.Round = NewRound(v, t, index);
            return v;
        }

        static LineView Play(LineView v, RoundType t, int index, Action<LineRoundView> tweak = null, params int[] submitted)
        {
            var r = NewRound(v, t, index);
            if (tweak != null)
            {
                if (t == RoundType.PrimHavuzu) { r.Partners.Clear(); }
                tweak(r);
                if (t == RoundType.PrimHavuzu && r.Partners.Count == 0) r.Partners.Add(r.Partner);
            }
            v.Round = r;
            for (int i = 0; i < submitted.Length; i++)
                for (int k = 0; k < v.Players.Count; k++) if (v.Players[k].Id == submitted[i]) v.Players[k].Submitted = true;
            if (r.Submitted) v.Players[0].Submitted = true;
            return v;
        }

        static void Code(LineRoundView r, int n, bool saboteur, bool submitted)
        {
            int[] owners6 = { 3, 0, 5, 1, 4, 2 };
            int[] owners4 = { 2, 0, 3, 1 };
            int[] owners5 = { 4, 0, 3, 1, 2 };
            int[] owners3 = { 1, 0, 2 };
            int[] owners = n >= 6 ? owners6 : n == 5 ? owners5 : n == 4 ? owners4 : owners3;
            int[] digits = n >= 6 ? new[] { 4, 7, 3, 9, 1, 6 } : n == 5 ? new[] { 2, 7, 8, 0, 4 } : n == 4 ? new[] { 5, 8, 2, 1 } : new[] { 9, 3, 6 };
            r.CodeLength = n;
            r.PositionOwners.Clear();
            r.PositionOwners.AddRange(owners);
            r.DigitPosition = Array.IndexOf(owners, 0);
            r.Digit = digits[r.DigitPosition];
            r.NeighborPosition = (r.DigitPosition + 1) % n;
            r.NeighborDigit = digits[r.NeighborPosition];
            r.IsSaboteur = saboteur;
            if (submitted)
            {
                int code = 0;
                for (int i = 0; i < n; i++) code = code * 10 + digits[i];
                r.Submitted = true;
                r.MyA = code;
            }
        }

        static LineView Codeview(LineView v, int n)
        {
            var r = NewRound(v, RoundType.OrtakSifre, 0);
            Code(r, n, false, true);
            v.Round = r;
            return v;
        }

        static LineView Reveal(LineView v, LineRoundReveal rv)
        {
            if (v.Round == null || v.Round.Type != rv.Type) v.Round = NewRound(v, rv.Type, rv.Index);
            v.RoundIndex = rv.Index;
            if (rv.Index < v.TodayRounds.Count) v.TodayRounds[rv.Index] = rv.Type;
            v.Round.Reveal = rv;
            v.TodayReveals.Add(rv);
            return v;
        }

        static LineView WithInbox(LineView v)
        {
            var inbox = v.Mine.Inbox;
            inbox.Add(new LineMailView { Id = 10, Kind = MailKind.Manager, From = LineIds.Manager, TemplateId = 0, ToAll = true, Time = Now - 300 });
            inbox.Add(new LineMailView { Id = 11, Kind = MailKind.Personal, From = 2, TemplateId = 2, About = -1, ToAll = true, Time = Now - 190 });
            inbox.Add(new LineMailView { Id = 12, Kind = MailKind.System, From = LineIds.System, TemplateId = (int)TrickType.KahveDok, Time = Now - 95 });
            inbox.Add(new LineMailView { Id = 13, Kind = MailKind.Personal, From = v.Players.Count > 3 ? 3 : 1, TemplateId = 4, About = v.Players.Count > 4 ? 4 : 2, ToAll = true, Time = Now - 20 });
            Ui?.State.ReadMail.Add(10);
            return v;
        }

        static LineView WithLog(LineView v)
        {
            var log = v.Mine.CallLog;
            log.Add(new LineCallRecordView { CallId = 1, Other = 1, Outgoing = true, Connected = true, Start = Now - 400, End = Now - 352, EndReason = CallEndReason.HungUp });
            log.Add(new LineCallRecordView { CallId = 2, Other = 2, Outgoing = false, Connected = false, Start = Now - 260, End = Now - 245, EndReason = CallEndReason.Missed });
            if (v.Players.Count > 3) log.Add(new LineCallRecordView { CallId = 3, Other = 3, Outgoing = true, Connected = true, Start = Now - 150, End = Now - 64, EndReason = CallEndReason.HungUp });
            log.Add(new LineCallRecordView { CallId = 4, Other = v.Players.Count - 1, Outgoing = true, Connected = false, Start = Now - 40, End = Now - 38, EndReason = CallEndReason.Declined });
            return v;
        }

        static void Phone(LineView v, CallStatus status, int other, double ring = 0, double since = 0)
        {
            v.Mine.Phone.Status = status;
            v.Mine.Phone.Other = other;
            v.Mine.Phone.RingUntil = Now + ring;
            v.Mine.Phone.Since = Now - since;
            v.Mine.Phone.CallId = status == CallStatus.Connected ? 9 : -1;
        }

        // ------------------------------------------------------------------ reveals

        static void Delta(LineRoundReveal r, int player, int d) { r.Deltas.Add(new LineBlameDelta { Player = player, Delta = d, Reason = BlameReason.Round, Round = r.Type }); }

        static LineRoundReveal RevealPrim(bool trio)
        {
            var r = new LineRoundReveal { Type = RoundType.PrimHavuzu, Index = 0 };
            if (!trio)
            {
                r.Pairs.Add(new LinePairReveal { A = 0, B = 3, ChoiceA = 0, ChoiceB = 1, C = -1, ChoiceC = -1 });
                r.Pairs.Add(new LinePairReveal { A = 1, B = 4, ChoiceA = 0, ChoiceB = 0, C = -1, ChoiceC = -1 });
                r.Pairs.Add(new LinePairReveal { A = 2, B = 5, ChoiceA = 1, ChoiceB = 1, C = -1, ChoiceC = -1 });
                Delta(r, 0, 2); Delta(r, 3, -4); Delta(r, 1, -1); Delta(r, 4, -1); Delta(r, 2, 1); Delta(r, 5, 1);
            }
            else
            {
                r.Pairs.Add(new LinePairReveal { A = 0, B = 2, ChoiceA = 0, ChoiceB = 0, C = -1, ChoiceC = -1 });
                r.Pairs.Add(new LinePairReveal { A = 1, B = 3, ChoiceA = 0, ChoiceB = 1, C = 4, ChoiceC = 0 });
                Delta(r, 0, -1); Delta(r, 2, -1); Delta(r, 3, -4); Delta(r, 1, 2); Delta(r, 4, 2);
            }
            return r;
        }

        static LineRoundReveal RevealOdd()
        {
            var r = new LineRoundReveal { Type = RoundType.FarkliEposta, Index = 1, Hidden = 4, TopicId = 4, OddGuess = 5, OddGuessCorrect = false, Winner = 4 };
            int[] votes = { 4, 4, 3, 4, 2, 1 };
            for (int i = 0; i < votes.Length; i++) r.Votes.Add(new LineVoteView { Voter = i, Target = votes[i] });
            r.Counts.AddRange(new[] { 0, 1, 1, 1, 3, 0 });
            Delta(r, 4, 3); Delta(r, 0, -1); Delta(r, 1, -1); Delta(r, 3, -1);
            return r;
        }

        static LineRoundReveal RevealLost()
        {
            var r = new LineRoundReveal { Type = RoundType.KayipRapor, Index = 1, Hidden = 2, Winner = 3 };
            r.Clues.AddRange(new[] { 3, 0, 4, 1, 0 });
            int[] votes = { 2, 3, 3, 2, 3 };
            for (int i = 0; i < votes.Length; i++) r.Votes.Add(new LineVoteView { Voter = i, Target = votes[i] });
            r.Counts.AddRange(new[] { 0, 0, 2, 3, 0 });
            Delta(r, 2, -2); Delta(r, 3, 2);
            return r;
        }

        static LineRoundReveal RevealComplaint()
        {
            var r = new LineRoundReveal { Type = RoundType.IhbarKutusu, Index = 1, Winner = 3 };
            r.Complaints.Add(new LineComplaintView { Target = 3, Reason = 0 });
            r.Complaints.Add(new LineComplaintView { Target = 3, Reason = 2 });
            r.Complaints.Add(new LineComplaintView { Target = 1, Reason = 4 });
            r.Complaints.Add(new LineComplaintView { Target = 3, Reason = 5 });
            r.Complaints.Add(new LineComplaintView { Target = 5, Reason = 1 });
            r.Counts.AddRange(new[] { 0, 1, 0, 3, 0, 1 });
            Delta(r, 3, 2); Delta(r, 0, -1); Delta(r, 2, -1); Delta(r, 4, -1);
            return r;
        }

        static LineRoundReveal RevealPromo()
        {
            var r = new LineRoundReveal { Type = RoundType.TerfiListesi, Index = 1, Winner = 2 };
            int[] votes = { 2, 2, 1, 2 };
            for (int i = 0; i < votes.Length; i++) r.Votes.Add(new LineVoteView { Voter = i, Target = votes[i] });
            r.Counts.AddRange(new[] { 0, 1, 3, 0 });
            Delta(r, 2, -3); Delta(r, 0, -1); Delta(r, 1, -1); Delta(r, 3, -1);
            return r;
        }

        static LineRoundReveal RevealCode(int n, bool solved)
        {
            var r = new LineRoundReveal { Type = RoundType.OrtakSifre, Index = 0, Solved = solved };
            if (n >= 6)
            {
                r.Code = 473916; r.Hidden = 4;
                r.CodesByPlayer.AddRange(new[] { 473916, 473916, 473196, 473916, 471916, 473916 });
                for (int i = 0; i < 6; i++) Delta(r, i, i == 4 ? 2 : -1);
            }
            else
            {
                r.Code = 5821; r.Hidden = 1;
                r.CodesByPlayer.AddRange(new[] { 5821, 5281, 5821, -1 });
                for (int i = 0; i < 4; i++) Delta(r, i, i == 1 ? -2 : 1);
            }
            return r;
        }
    }
}
