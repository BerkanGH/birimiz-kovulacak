using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Centre-stage cards, one at a time: the manager's morning call (rings first, typewriter speech, today's round
    /// cards, the favourite), the round rule card (RoundIntro; long version the first time a type appears), the round
    /// reveal (see <see cref="DeskReveal"/>) and the day-2 unlock cards (Dirty Trick, Manager Line).
    /// </summary>
    public sealed class DeskCards
    {
        enum Kind { None, Morning, Intro, Reveal, Unlock }

        readonly DeskUi _ui;
        readonly VisualElement _dim, _slot;
        Kind _kind;
        bool _mgrRinging;
        double _mgrRingUntil;
        Label _introTimer;
        VisualElement _introFill;
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
            if (v.TodayRounds.Count >= 2) lines.Add(Dk.F("desk.morning.rounds2", Dk.RoundName(v.TodayRounds[0]), Dk.RoundName(v.TodayRounds[1])));
            else if (v.TodayRounds.Count == 1) lines.Add(Dk.F("desk.morning.rounds1", Dk.RoundName(v.TodayRounds[0])));
            if (v.Favourite >= 0) lines.Add(Dk.F("desk.morning.favourite", Dk.Name(v.Favourite)));
            if (v.Features.IsLastDay) lines.Add(Dk.T("desk.morning.last_day"));
            float delay = 0.35f;
            for (int i = 0; i < lines.Count; i++)
            {
                var bubble = Box("dk-bubble").NoPick().AddTo(talk);
                if (i == 0) bubble.AddToClassList("dk-bubble-first");
                var l = Text("", "dk-bubble-text").NoPick().AddTo(bubble);
                bubble.style.opacity = 0f;
                float d = delay;
                string full = lines[i];
                UiMotion.SlideIn(bubble, new Vector2(-24f, 6f), 0.25f, d);
                Typewriter(l, full, d + 0.05f);
                delay += 0.25f + Mathf.Min(1.6f, full.Length * 0.018f);
            }

            // today's work
            var today = Box("dk-morning-today").NoPick().AddTo(card);
            StampText(Loc.Upper(Dk.T("desk.morning.today")), "dk-section-band").NoPick().AddTo(today);
            var row = Box("dk-morning-rounds").NoPick().AddTo(today);
            for (int i = 0; i < v.TodayRounds.Count; i++)
            {
                var rc = MiniRound(v.TodayRounds[i], i).AddTo(row);
                UiMotion.PopIn(rc, 0.3f, 0.5f + i * 0.15f);
            }
            if (v.Favourite >= 0)
            {
                var fav = Box("dk-fav").NoPick().AddTo(row);
                Dk.Icon("favourite", "dk-fav-icon").AddTo(fav);
                Dk.Badge(v.Favourite, "badge-l").AddTo(fav);
                var fc = Box("dk-fav-col").NoPick().AddTo(fav);
                StampText(Loc.Upper(Dk.T("desk.morning.fav_band")), "dk-fav-band").NoPick().AddTo(fc);
                Title(Dk.Name(v.Favourite), "dk-fav-name").NoPick().AddTo(fc);
                Text(Dk.T("desk.morning.fav_rule"), "dk-fav-rule").NoPick().AddTo(fc);
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

        static VisualElement MiniRound(RoundType t, int index)
        {
            var c = Box("dk-mini-round").NoPick();
            Dk.RoundCard(t, "dk-mini-card").AddTo(c);
            var col = Box("dk-mini-round-col").NoPick().AddTo(c);
            Bold(Dk.F("desk.round_n", index + 1), "dk-mini-round-n").NoPick().AddTo(col);
            Title(Dk.RoundName(t), "dk-mini-round-name").NoPick().AddTo(col);
            Text(Dk.RoundRule(t), "dk-mini-round-rule").NoPick().AddTo(col);
            return c;
        }

        static void Typewriter(Label l, string full, float delay)
        {
            int last = -1;
            float secs = Mathf.Clamp(full.Length * 0.018f, 0.25f, 1.6f);
            UiMotion.Tween(l, secs, t =>
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(t * full.Length), 0, full.Length);
                if (n == last) return;
                last = n;
                // invisible tail keeps the bubble's final size from the first frame
                l.text = n >= full.Length ? full : full.Substring(0, n) + "<alpha=#00>" + full.Substring(n);
            }, UiMotion.Ease.Linear, () => l.text = full, delay, "type");
        }

        // ------------------------------------------------------------------ unlocks (first week, day 2)

        int _unlockKind;

        /// <summary>First week only: the Dirty Trick card on its unlock day, the Manager Line card on its own (later) day.</summary>
        void MaybeUnlock(LineView v)
        {
            var st = _ui.State;
            var c = Dk.Config;
            if (!v.FirstWeek) return;
            if (!st.TrickUnlockSeen && v.Features.TricksUnlocked && v.Mine.Trick != TrickType.None && v.Day >= c.UnlockDayFirstWeek) { ShowUnlock(v, 1); return; }
            if (!st.LineUnlockSeen && v.Features.TipLineUnlocked && v.Day >= c.TipUnlockDayFirstWeek) { ShowUnlock(v, 2); return; }
        }

        void ShowUnlock(LineView v, int kind)
        {
            _unlockKind = kind;
            bool trick = kind == 1;
            Open(Kind.Unlock);
            var card = Box("dk-unlock").AddTo(_slot);
            var art = Box("dk-unlock-art").NoPick().AddTo(card);
            var col = Box("dk-unlock-col").NoPick().AddTo(card);
            StampText(Loc.Upper(Dk.T("desk.unlock.band")), "dk-section-band", "dk-unlock-band").NoPick().AddTo(col);
            if (trick)
            {
                if (Dk.Art(art, Dk.TrickArtId(v.Mine.Trick))) Title(Dk.TrickName(v.Mine.Trick), "dk-card-label").NoPick().AddTo(art);
                else { art.AddToClassList("dk-round-art-fallback"); Dk.Icon(Dk.TrickIcon(v.Mine.Trick), "icon-xxl").AddTo(art); }
                Title(Dk.T("desk.unlock.trick_title"), "dk-unlock-title").NoPick().AddTo(col);
                Bold(Dk.F("desk.unlock.trick_yours", Dk.TrickName(v.Mine.Trick)), "dk-unlock-name").NoPick().AddTo(col);
                Text(Dk.TrickHow(v.Mine.Trick), "dk-unlock-text").NoPick().AddTo(col);
                int per = v.Length == WeekLength.Full ? Dk.Config.TricksPerWeekFull : Dk.Config.TricksPerWeekShort;
                Text(Dk.F("desk.unlock.trick_where", per), "dk-unlock-where").NoPick().AddTo(col);
            }
            else
            {
                art.AddToClassList("dk-unlock-art-icon");
                Dk.Icon("manager_line", "dk-unlock-icon").AddTo(art);
                Title(Dk.T("desk.unlock.line_title"), "dk-unlock-title").NoPick().AddTo(col);
                Text(Dk.F("desk.unlock.line_text", Dk.Config.ConfirmedTipBlame, Dk.Config.FalseTipBlame), "dk-unlock-text").NoPick().AddTo(col);
                Text(Dk.T("desk.unlock.line_where"), "dk-unlock-where").NoPick().AddTo(col);
            }
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
        }

        void NextUnlock()
        {
            var v = Dk.View;
            if (_unlockKind == 1) _ui.State.TrickUnlockSeen = true;
            if (_unlockKind == 2) _ui.State.LineUnlockSeen = true;
            HideAll();
            if (v != null) MaybeUnlock(v);
        }

        // ------------------------------------------------------------------ round intro

        public void ShowIntro(LineView v)
        {
            if (v.RoundIndex < 0 || v.RoundIndex >= v.TodayRounds.Count) return;
            var t = v.Round != null ? v.Round.Type : v.TodayRounds[v.RoundIndex];
            bool longCard = v.Features.FirstTimeRound;
            Open(Kind.Intro);
            _phaseStart = v.PhaseStartTime; _phaseEnd = v.PhaseEndTime;
            _ui.State.IntroSeenKey = v.Day * 10 + v.RoundIndex;

            var card = Box("dk-intro").AddTo(_slot);
            if (longCard) card.AddToClassList("dk-intro-long");
            var artBox = Box("dk-intro-art").NoPick().AddTo(card);
            bool hasBack = Dk.Art(artBox, "rounds/round_card_back");
            if (!hasBack) artBox.AddToClassList("dk-intro-art-back");
            var col = Box("dk-intro-col").NoPick().AddTo(card);
            var bandRow = Box("dk-intro-bandrow").NoPick().AddTo(col);
            StampText(Loc.Upper(Dk.F("desk.intro.band", v.RoundIndex + 1, v.TodayRounds.Count)), "dk-section-band").NoPick().AddTo(bandRow);
            var title = Title(Dk.RoundName(t), "dk-intro-title").NoPick().AddTo(col);
            var rule = Text(Dk.RoundRule(t), "dk-intro-rule").NoPick().AddTo(col);
            var ex = Box("dk-intro-example").NoPick().AddTo(col);
            Hand(Dk.RoundExample(t), "dk-intro-example-text").NoPick().AddTo(ex);
            VisualElement more = null;
            if (longCard)
            {
                more = Box("dk-intro-more").NoPick().AddTo(col);
                StampText(Loc.Upper(Dk.T("desk.intro.scoring")), "dk-intro-more-band").NoPick().AddTo(more);
                for (int i = 0; i < 3; i++)
                {
                    string key = "round." + t + ".long." + i;
                    if (!Loc.Has(key)) break;
                    var line = Box("dk-intro-more-line").NoPick().AddTo(more);
                    Box("dk-bullet").NoPick().AddTo(line);
                    Text(Dk.T(key), "dk-intro-more-text").NoPick().AddTo(line);
                }
            }
            var foot = Box("dk-card-foot").AddTo(col);
            var timer = Box("dk-intro-timer").NoPick().AddTo(foot);
            var bar = Box("dk-intro-bar").NoPick().AddTo(timer);
            _introFill = Box("dk-intro-fill").NoPick().AddTo(bar);
            _introTimer = MemoBold("", "dk-intro-secs").NoPick().AddTo(timer);
            Box("spacer").NoPick().AddTo(foot);
            _primaryAction = () => { HideAll(); _ui.OpenComputer(DeskApp.Task); };
            _primary = Btn(Dk.T("desk.intro.open"), _primaryAction, "btn-primary", "dk-primary").AddTo(foot);
            Dk.Key("Enter", "dk-key-s").AddTo(foot);
            _lastSec = -1;

            // motion: the card slides in face down, flips to the round art, the text follows
            UiMotion.SlideIn(card, new Vector2(0, 80f), 0.34f, 0f, UiMotion.Ease.OutBack);
            UiMotion.Flip(artBox, () =>
            {
                artBox.RemoveFromClassList("dk-intro-art-back");
                if (Dk.Art(artBox, Dk.RoundArtId(t))) Title(Dk.RoundName(t), "dk-card-label").NoPick().AddTo(artBox);
                else
                {
                    artBox.style.backgroundImage = StyleKeyword.Null;
                    artBox.AddToClassList("dk-round-art-fallback");
                    Dk.Icon(Dk.RoundIcon(t), "dk-intro-art-icon").AddTo(artBox);
                    Title(Dk.RoundName(t), "dk-intro-art-name").NoPick().AddTo(artBox);
                }
                Dk.Play("card_flip", "pickup");
            }, 0.5f, 0.3f);
            UiMotion.SlideIn(title, new Vector2(40f, 0), 0.3f, 0.55f);
            UiMotion.SlideIn(rule, new Vector2(40f, 0), 0.3f, 0.7f);
            UiMotion.SlideIn(ex, new Vector2(0, 30f), 0.3f, 0.9f, UiMotion.Ease.OutBack);
            if (more != null) UiMotion.SlideIn(more, new Vector2(0, 30f), 0.3f, 1.1f);
            if (longCard)
            {
                var stamp = Dk.Stamp(Dk.T("desk.intro.new_rule"), null, "dk-intro-stamp").AddTo(card);
                UiMotion.Stamp(stamp, 8f, 0.24f, 1.0f, () => Dk.Play("stamp"));
            }
            Dk.Play("phase");
        }

        public void HideIntro() { if (_kind == Kind.Intro) HideAll(); }

        // ------------------------------------------------------------------ reveal

        public void ShowReveal(LineView v)
        {
            var r = v.Round != null ? v.Round.Reveal : null;
            if (r == null && v.TodayReveals.Count > 0) r = v.TodayReveals[v.TodayReveals.Count - 1];
            if (r == null) return;
            Open(Kind.Reveal);
            _reveal.Build(_slot, v, r);
        }

        // ------------------------------------------------------------------ common

        void Open(Kind k)
        {
            _slot.Clear();
            _primary = null;
            _primaryAction = null;
            _introTimer = null;
            _introFill = null;
            _kind = k;
            _dim.Show(true);
            _dim.EnableInClassList("dk-cards-dim-soft", k == Kind.Reveal || k == Kind.Intro);
            _dim.pickingMode = k == Kind.Reveal ? PickingMode.Ignore : PickingMode.Position;
            UiMotion.FadeIn(_dim, 0.2f);
        }

        public void HideAll()
        {
            if (_kind == Kind.None) return;
            _kind = Kind.None;
            _primary = null;
            _primaryAction = null;
            _introTimer = null;
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
            if (_kind == Kind.Intro || _kind == Kind.Reveal) HideAll();
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
            if (_kind == Kind.Reveal) _reveal.Tick();
        }
    }
}
