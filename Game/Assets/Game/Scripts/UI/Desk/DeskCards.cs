using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Centre-stage cards, one at a time: the manager's morning call (rings first, speech bubbles, today's tasks, company
    /// status, the favourite), the task intro card (RoundIntro; the tutorial opens instead for a first-time task), the
    /// task reveal (see <see cref="DeskReveal"/>), the break card (what you can do now) and the dirty-trick unlock card.
    /// </summary>
    public sealed class DeskCards
    {
        enum Kind { None, Morning, Intro, Reveal, Unlock, Break }

        readonly DeskUi _ui;
        readonly VisualElement _dim, _slot;
        Kind _kind;
        bool _mgrRinging;
        double _mgrRingUntil;
        Label _introTimer;
        VisualElement _introFill;
        Label _breakTimer;
        VisualElement _breakFill;
        Button _primary;
        Action _primaryAction;
        int _lastSec = -1;
        readonly DeskReveal _reveal;
        double _phaseStart, _phaseEnd;

        public DeskCards(DeskUi ui, VisualElement layer)
        {
            _ui = ui;
            _dim = Box("dk-cards-dim").AddTo(layer);
            _slot = Box("dk-cards-slot").NoPick().AddTo(_dim);
            _dim.Show(false);
            _reveal = new DeskReveal();
        }

        public bool Visible => _kind != Kind.None;
        public bool WantsCursor => _kind == Kind.Morning || _kind == Kind.Intro || _kind == Kind.Unlock;
        public bool BlocksDesk => _kind == Kind.Unlock;
        public bool ManagerRinging => _mgrRinging;
        public double ManagerRingUntil => _mgrRingUntil;
        public bool IntroOpen => _kind == Kind.Intro;
        public bool MorningOpen => _kind == Kind.Morning;
        /// <summary>Coach-arrow anchor: the visible card's primary button.</summary>
        public VisualElement PrimaryButton => _primary;

        // ------------------------------------------------------------------ morning

        public void MorningRing(LineView v)
        {
            var st = _ui.State;
            if (st.MorningAnsweredDay == v.Day)
            {
                if (st.MorningClosedDay != v.Day) ShowMorning(v);
                else MaybeUnlock(v);
                return;
            }
            st.MorningRangDay = v.Day;
            _mgrRinging = true;
            _mgrRingUntil = Dk.Now + (v.Features.OnboardingDay ? 14.0 : 3.5);
        }

        public void AnswerManager()
        {
            var v = Dk.View;
            if (!_mgrRinging || v == null) return;
            _mgrRinging = false;
            _ui.State.MorningAnsweredDay = v.Day;
            Dk.Play("call_connect");
            ShowMorning(v);
        }

        void ShowMorning(LineView v)
        {
            Open(Kind.Morning);
            var card = Box("dk-morning").AddTo(_slot);
            // call header: a phone line, not a generic title bar
            var head = Box("dk-morning-head").NoPick().AddTo(card);
            Box("dk-live-dot").NoPick().AddTo(head);
            StampText(Loc.Upper(Dk.T("desk.morning.line")), "dk-morning-line").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            StampText(Loc.Upper(Dk.DayName(v.Day)), "dk-morning-day").NoPick().AddTo(head);

            var body = Box("dk-morning-body").NoPick().AddTo(card);
            var who = Box("dk-morning-who").NoPick().AddTo(body);
            var portrait = ManagerPortrait(v.ManagerProfileId, false, "badge-xl").AddTo(who);
            Title(Dk.ManagerName(v.ManagerProfileId), "dk-morning-name").NoPick().AddTo(who);
            Bold(Dk.ManagerTitle(v.ManagerProfileId), "dk-morning-title").NoPick().AddTo(who);

            var talk = Box("dk-morning-talk").NoPick().AddTo(body);
            var lines = new List<string>();
            lines.Add(Dk.T("desk.morning." + (string.IsNullOrEmpty(v.ManagerProfileId) ? "default" : v.ManagerProfileId) + "." + (v.Day % 3)));
            if (v.Features.OnboardingDay) lines.Add(Dk.T("desk.morning.onboarding"));
            if (v.TodayTasks.Count >= 2) lines.Add(Dk.F("desk.morning.tasks2", Dk.TaskName(v.TodayTasks[0]), Dk.TaskName(v.TodayTasks[1])));
            else if (v.TodayTasks.Count == 1) lines.Add(Dk.F("desk.morning.tasks1", Dk.TaskName(v.TodayTasks[0])));
            if (v.Company.ProvisionalPermille < 0) lines.Add(Dk.F("desk.morning.company_new", v.Company.TargetPercent));
            else lines.Add(Dk.F("desk.morning.company", Dk.Pct(v.Company.ProvisionalPermille), v.Company.TargetPercent));
            if (v.Favourite >= 0) lines.Add(Dk.F("desk.morning.favourite", Dk.Name(v.Favourite)));
            if (v.Features.IsLastDay) lines.Add(Dk.T("desk.morning.last_day"));
            float delay = 0.35f;
            for (int i = 0; i < lines.Count; i++)
            {
                var bubble = Box("dk-bubble").NoPick().AddTo(talk);
                if (i == 0) bubble.AddToClassList("dk-bubble-first");
                Text(lines[i], "dk-bubble-text").Plain().NoPick().AddTo(bubble);
                bubble.style.opacity = 0f;
                UiMotion.SlideIn(bubble, new Vector2(-24f, 6f), 0.25f, delay);
                delay += 0.25f + Mathf.Min(1.2f, lines[i].Length * 0.012f);
            }

            // today's work
            var today = Box("dk-morning-today").NoPick().AddTo(card);
            StampText(Loc.Upper(Dk.T("desk.morning.today")), "dk-section-band").NoPick().AddTo(today);
            var row = Box("dk-morning-rounds").NoPick().AddTo(today);
            for (int i = 0; i < v.TodayTasks.Count; i++)
            {
                var rc = MiniTask(v.TodayTasks[i], i).AddTo(row);
                UiMotion.PopIn(rc, 0.3f, 0.5f + i * 0.15f);
            }
            if (v.Favourite >= 0)
            {
                var fav = Box("dk-fav").NoPick().AddTo(row);
                Dk.Icon("favourite", "dk-fav-icon").AddTo(fav);
                Dk.Badge(v.Favourite, "badge-l").AddTo(fav);
                var fc = Box("dk-fav-col").NoPick().AddTo(fav);
                StampText(Loc.Upper(Dk.T("desk.morning.fav_band")), "dk-fav-band").NoPick().AddTo(fc);
                Title(Dk.Name(v.Favourite), "dk-fav-name").Plain().NoPick().AddTo(fc);
                Text(Dk.F("desk.morning.fav_rule", Dk.Profile(v).FavouriteIncome), "dk-fav-rule").NoPick().AddTo(fc);
                UiMotion.PopIn(fav, 0.3f, 0.8f);
            }

            var foot = Box("dk-card-foot").AddTo(card);
            Text(Dk.T("desk.morning.memo_hint"), "dk-card-foot-hint").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _primaryAction = CloseMorning;
            _primary = Btn(Dk.T("desk.morning.ok"), CloseMorning, "btn-primary", "dk-primary").AddTo(foot);
            Dk.Key("Enter", "dk-key-s").AddTo(foot);

            UiMotion.SlideIn(card, new Vector2(0, 60f), 0.36f, 0f, UiMotion.Ease.OutBack);
            UiMotion.Pulse(portrait, 0.1f, 0.4f);
        }

        void CloseMorning()
        {
            var v = Dk.View;
            if (v != null) _ui.State.MorningClosedDay = v.Day;
            HideAll();
            if (v != null) MaybeUnlock(v);
        }

        static VisualElement MiniTask(TaskKind t, int index)
        {
            var c = Box("dk-mini-round").NoPick();
            Dk.TaskCard(t, "dk-mini-card").AddTo(c);
            var col = Box("dk-mini-round-col").NoPick().AddTo(c);
            Bold(Dk.F("desk.round_n", index + 1), "dk-mini-round-n").NoPick().AddTo(col);
            Title(Dk.TaskName(t), "dk-mini-round-name").NoPick().AddTo(col);
            Text(Dk.TaskRule(t), "dk-mini-round-rule").NoPick().AddTo(col);
            return c;
        }

        // ------------------------------------------------------------------ unlocks (first week, day 2)

        /// <summary>Dirty Trick card the first time tricks are unlocked for this player.</summary>
        void MaybeUnlock(LineView v)
        {
            var st = _ui.State;
            if (!st.TrickUnlockSeen && v.Features.TricksUnlocked && v.Mine.Trick != TrickType.None && v.FirstWeek) ShowUnlock(v);
        }

        void ShowUnlock(LineView v)
        {
            Open(Kind.Unlock);
            var card = Box("dk-unlock").AddTo(_slot);
            var art = Box("dk-unlock-art").NoPick().AddTo(card);
            var col = Box("dk-unlock-col").NoPick().AddTo(card);
            StampText(Loc.Upper(Dk.T("desk.unlock.band")), "dk-section-band", "dk-unlock-band").NoPick().AddTo(col);
            var t = v.Mine.Trick;
            if (Dk.Art(art, Dk.TrickArtId(t))) Title(Dk.TrickName(t), "dk-card-label").NoPick().AddTo(art);
            else { art.AddToClassList("dk-round-art-fallback"); Dk.Icon(Dk.TrickIcon(t), "icon-xxl").AddTo(art); }
            Title(Dk.T("desk.unlock.trick_title"), "dk-unlock-title").NoPick().AddTo(col);
            Bold(Dk.F("desk.unlock.trick_yours", Dk.TrickName(t)), "dk-unlock-name").NoPick().AddTo(col);
            Text(Dk.TrickHow(t), "dk-unlock-text").NoPick().AddTo(col);
            int per = v.Length == WeekLength.Full ? Dk.Config.TricksPerWeekFull : Dk.Config.TricksPerWeekShort;
            Text(Dk.F("desk.unlock.trick_where", per), "dk-unlock-where").NoPick().AddTo(col);
            Text(Dk.F("desk.trick.warning", Dk.Config.CareerSabotagePenalty, Dk.Config.ExposedSabotageBlame), "dk-unlock-where", "dk-bad").NoPick().AddTo(col);
            Box("spacer").NoPick().AddTo(col);
            var foot = Box("dk-card-foot").AddTo(col);
            Box("spacer").NoPick().AddTo(foot);
            _primaryAction = NextUnlock;
            _primary = Btn(Dk.T("desk.unlock.ok"), NextUnlock, "btn-primary", "dk-primary").AddTo(foot);
            Dk.Key("Enter", "dk-key-s").AddTo(foot);
            UiMotion.PopIn(card, 0.32f, 0f, 0.8f);
            UiMotion.Flip(art, null, 0.5f, 0.1f);
            var stamp = Dk.Stamp(Dk.T("desk.unlock.stamp"), Loc.Language == "tr" ? "stamps/stamp_onaylandi" : "stamps_v3/stamp_approved", "dk-unlock-stamp").AddTo(card);
            UiMotion.Stamp(stamp, -12f, 0.24f, 0.55f, () => Dk.Play("stamp"));
            Dk.NoRich(card);
        }

        void NextUnlock()
        {
            _ui.State.TrickUnlockSeen = true;
            HideAll();
        }

        // ------------------------------------------------------------------ round intro

        public void ShowIntro(LineView v)
        {
            if (v.RoundIndex < 0 || v.RoundIndex >= v.TodayTasks.Count) return;
            var t = v.Task != null ? v.Task.Kind : v.TodayTasks[v.RoundIndex];
            Open(Kind.Intro);
            _phaseStart = v.PhaseStartTime; _phaseEnd = v.PhaseEndTime;
            _ui.State.IntroSeenKey = v.Day * 10 + v.RoundIndex;

            var card = Box("dk-intro").AddTo(_slot);
            var artBox = Dk.TaskCard(t, "dk-intro-card").AddTo(card);
            var col = Box("dk-intro-col").NoPick().AddTo(card);
            var bandRow = Box("dk-intro-bandrow").NoPick().AddTo(col);
            StampText(Loc.Upper(Dk.F("desk.intro.band", v.RoundIndex + 1, v.TodayTasks.Count)), "dk-section-band").NoPick().AddTo(bandRow);
            var title = Title(Dk.TaskName(t), "dk-intro-title").NoPick().AddTo(col);
            var rule = Text(Dk.TaskRule(t), "dk-intro-rule").NoPick().AddTo(col);
            if (v.Task != null)
            {
                string role = Dk.RoleText(t, v.Task.Role);
                var chips = Box("dk-intro-chips").NoPick().AddTo(col);
                if (role.Length > 0) Tag(Dk.F("desk.task.role", role), "tag-green").AddTo(chips);
                if (v.Task.Group.Count > 0)
                {
                    string names = "";
                    for (int i = 0; i < v.Task.Group.Count; i++) names += (i > 0 ? ", " : "") + Dk.Name(v.Task.Group[i]);
                    Tag(Dk.F(v.Task.Family == TaskFamily.Pair ? "desk.intro.with" : "desk.intro.with_all", names), "tag-grey").Plain().AddTo(chips);
                }
            }
            var ex = Box("dk-intro-example").NoPick().AddTo(col);
            Hand(Dk.T("task." + Dk.TaskId(t) + ".tut.1.example"), "dk-intro-example-text").NoPick().AddTo(ex);
            var foot = Box("dk-card-foot").AddTo(col);
            var timer = Box("dk-intro-timer").NoPick().AddTo(foot);
            var bar = Box("dk-intro-bar").NoPick().AddTo(timer);
            _introFill = Box("dk-intro-fill").NoPick().AddTo(bar);
            _introTimer = MemoBold("", "dk-intro-secs").NoPick().AddTo(timer);
            Box("spacer").NoPick().AddTo(foot);
            Btn(Dk.T("desk.intro.how"), () => { HideAll(); _ui.OpenComputer(DeskApp.Task); _ui.Computer.Task.OpenTutorial("reopen"); }, "btn-secondary").AddTo(foot);
            _primaryAction = () => { HideAll(); _ui.OpenComputer(DeskApp.Task); };
            _primary = Btn(Dk.T("desk.intro.open"), _primaryAction, "btn-primary", "dk-primary").AddTo(foot);
            Dk.Key("Enter", "dk-key-s").AddTo(foot);
            _lastSec = -1;

            UiMotion.SlideIn(card, new Vector2(0, 80f), 0.34f, 0f, UiMotion.Ease.OutBack);
            UiMotion.Flip(artBox, null, 0.5f, 0.3f);
            UiMotion.SlideIn(title, new Vector2(40f, 0), 0.3f, 0.55f);
            UiMotion.SlideIn(rule, new Vector2(40f, 0), 0.3f, 0.7f);
            UiMotion.SlideIn(ex, new Vector2(0, 30f), 0.3f, 0.9f, UiMotion.Ease.OutBack);
            Dk.NoRich(card);
            Dk.Play("phase");
        }

        public void HideIntro() { if (_kind == Kind.Intro) HideAll(); }

        // ------------------------------------------------------------------ reveal

        public void ShowReveal(LineView v)
        {
            var r = v.Task != null ? v.Task.Reveal : null;
            if (r == null && v.TodayReveals.Count > 0) r = v.TodayReveals[v.TodayReveals.Count - 1];
            if (r == null) return;
            Open(Kind.Reveal);
            _reveal.Build(_slot, v, r);
        }

        // ------------------------------------------------------------------ break

        public void ShowBreak(LineView v)
        {
            Open(Kind.Break);
            _lastSec = -1;
            _phaseStart = v.PhaseStartTime; _phaseEnd = v.PhaseEndTime;
            var card = Box("dk-break").AddTo(_slot);
            var head = Box("dk-break-head").NoPick().AddTo(card);
            StampText(Loc.Upper(Dk.T(v.Features.IsLastDay && v.TodayTasks.Count > 0 && v.RoundIndex >= 0 ? "desk.break.band_closing" : "desk.break.band")), "dk-section-band").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            _breakTimer = MemoBold("", "dk-break-timer").NoPick().AddTo(head);
            var bar = Box("dk-intro-bar").NoPick().AddTo(card);
            _breakFill = Box("dk-intro-fill").NoPick().AddTo(bar);
            Title(Dk.T("desk.break.title"), "dk-break-title").NoPick().AddTo(card);
            if (v.Features.CarriedSeconds > 0.5f)
                Bold(Dk.F("desk.break.carried", Mathf.RoundToInt(v.Features.CarriedSeconds)), "dk-break-carried").NoPick().AddTo(card);
            Text(Dk.T("desk.break.sub"), "dk-break-sub").NoPick().AddTo(card);
            var grid = Box("dk-break-grid").AddTo(card);
            BreakAction(grid, "call", Dk.T("desk.break.calls"), Dk.T("desk.break.calls_sub"), () => { HideAll(); _ui.OpenPhone(); }, v.Features.CallsOpen);
            BreakAction(grid, "message_app", Dk.T("desk.break.texts"), Dk.T("desk.break.texts_sub"), () => { HideAll(); _ui.OpenComputer(DeskApp.Messages); }, true);
            BreakAction(grid, "bank_app", Dk.T("desk.break.transfers"), Dk.T("desk.break.transfers_sub"), () => { HideAll(); _ui.OpenComputer(DeskApp.Bank); }, v.Features.TransfersOpen);
            BreakAction(grid, "record_app", Dk.T("desk.break.evidence"), Dk.T("desk.break.evidence_sub"), () => { HideAll(); _ui.OpenComputer(DeskApp.Evidence); }, v.Features.EvidenceOpen);
            BreakAction(grid, "remedial", Dk.T("desk.break.remedial"), Dk.T("desk.break.remedial_sub"), () => { HideAll(); _ui.OpenComputer(DeskApp.Remedial); }, v.Features.RemedialOpen);
            _ui.State.ObBreakSeen = true;
            UiMotion.SlideIn(card, new Vector2(0, 60f), 0.34f, 0f, UiMotion.Ease.OutBack);
            Dk.NoRich(card);
            Dk.Play("phase");
        }

        void BreakAction(VisualElement parent, string icon, string title, string sub, Action act, bool enabled)
        {
            var t = Dk.Tile(act, "dk-break-act").AddTo(parent);
            Dk.Icon(icon, "dk-break-act-icon").NoPick().AddTo(t);
            var col = Box("dk-break-act-col").NoPick().AddTo(t);
            Bold(title, "dk-break-act-title").NoPick().AddTo(col);
            Text(sub, "dk-break-act-sub").NoPick().AddTo(col);
            t.SetEnabled(enabled);
        }

        // ------------------------------------------------------------------ common

        void Open(Kind k)
        {
            _slot.Clear();
            _primary = null;
            _primaryAction = null;
            _introTimer = null;
            _introFill = null;
            _breakTimer = null;
            _breakFill = null;
            _kind = k;
            _dim.Show(true);
            _dim.EnableInClassList("dk-cards-dim-soft", k == Kind.Reveal || k == Kind.Intro || k == Kind.Break);
            _slot.EnableInClassList("dk-cards-slot-low", k == Kind.Break);
            _dim.pickingMode = k == Kind.Reveal || k == Kind.Break ? PickingMode.Ignore : PickingMode.Position;
            UiMotion.FadeIn(_dim, 0.2f);
        }

        public void HideAll()
        {
            if (_kind == Kind.None) return;
            _kind = Kind.None;
            _primary = null;
            _primaryAction = null;
            _introTimer = null;
            _breakTimer = null;
            _dim.Show(false);
            _slot.Clear();
        }

        /// <summary>Enter: the card's primary action.</summary>
        public void Primary()
        {
            if (_primary != null && _primary.enabledInHierarchy && _primaryAction != null)
            {
                Sfx.Play("click");
                UiMotion.Pulse(_primary, 0.08f, 0.2f);
                _primaryAction();
            }
        }

        public void Back()
        {
            switch (_kind)
            {
                case Kind.Morning: CloseMorning(); break;
                case Kind.Unlock: NextUnlock(); break;
                default: HideAll(); break;
            }
        }

        /// <summary>The computer took the screen: transient cards step aside (unlock lessons stay on top).</summary>
        public void OnComputerOpened()
        {
            if (_kind == Kind.Intro || _kind == Kind.Reveal || _kind == Kind.Break) HideAll();
            else if (_kind == Kind.Morning) CloseMorning();
        }

        public void Tick(LineView v, bool changed)
        {
            if (_mgrRinging)
            {
                if (v.Phase != LinePhase.Morning) _mgrRinging = false;
                else if (Dk.Now >= _mgrRingUntil) AnswerManager();
            }
            if (_kind == Kind.Morning && v.Phase != LinePhase.Morning) { _ui.State.MorningClosedDay = v.Day; HideAll(); }
            if (_kind == Kind.Unlock && !LineClient.IsOfficePhase(v.Phase)) HideAll();
            if (_kind == Kind.Intro && _introTimer != null)
            {
                double left = _phaseEnd - Dk.Now;
                int s = Mathf.CeilToInt((float)left);
                if (s < 0) s = 0;
                if (s != _lastSec) { _lastSec = s; _introTimer.SetText(ClockInt(s)); }
                float span = (float)(_phaseEnd - _phaseStart);
                if (_introFill != null && span > 0.01f) _introFill.style.width = Length.Percent(Mathf.Clamp01((float)(left / span)) * 100f);
            }
            if (_kind == Kind.Break && _breakTimer != null)
            {
                double left = _phaseEnd - Dk.Now;
                int s = Mathf.CeilToInt((float)left);
                if (s < 0) s = 0;
                if (s != _lastSec) { _lastSec = s; _breakTimer.SetText(ClockInt(s)); }
                float span = (float)(_phaseEnd - _phaseStart);
                if (_breakFill != null && span > 0.01f) _breakFill.style.width = Length.Percent(Mathf.Clamp01((float)(left / span)) * 100f);
            }
            if (_kind == Kind.Break && v.Phase != LinePhase.Break) HideAll();
            if (_kind == Kind.Reveal) _reveal.Tick();
        }
    }
}
