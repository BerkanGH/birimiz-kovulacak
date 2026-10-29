using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Görev / Task app: the current round. Left: the round card, its one-sentence rule and the clock. Right: your private
    /// info, the choice, and one primary button. Choices are staged locally and sent with the button; resubmitting
    /// overwrites until RoundPlay ends (CONTRACTS_V3 §3). Shared Password uses the keypad (digits on the keyboard work).
    /// </summary>
    public sealed class DeskTaskApp : DeskAppView
    {
        public DeskTaskApp(DeskUi ui) : base(ui, "dk-app-task") { }
        public override DeskApp Id => DeskApp.Task;
        public override string Icon => "task_app";

        int _staticSig = int.MinValue, _dynSig = int.MinValue, _roundKey = int.MinValue;
        int _selTarget = LineIds.None, _selA = -1;
        readonly List<Action<LineView>> _updaters = new List<Action<LineView>>();
        Label _timer;
        int _lastSec = -1;
        Button _primary;
        Label _status;
        Func<LineView, bool> _canSubmit;
        Action _submit;
        string _sendKey = "desk.task.lock";
        readonly Dictionary<int, VisualElement> _tickBadges = new Dictionary<int, VisualElement>();
        // Shared Password keypad
        readonly int[] _entry = new int[8];
        int _entryLen, _codeLen;
        readonly List<Label> _entryCells = new List<Label>();
        VisualElement _keypad;

        /// <summary>Coach-arrow anchors (null when not built).</summary>
        public VisualElement CallTarget { get; private set; }
        public VisualElement SubmitTarget => _primary;

        public override string Subtitle(LineView v)
        {
            if (v.Round == null || v.RoundIndex < 0) return Dk.PhaseName(v.Phase);
            return Dk.F("desk.clock.round", v.RoundIndex + 1, v.TodayRounds.Count, Dk.RoundName(v.Round.Type));
        }

        public override int Badge(LineView v)
        {
            return v.Phase == LinePhase.RoundPlay && v.Round != null && !v.Round.Submitted ? -1 : 0;
        }

        // ------------------------------------------------------------------ refresh

        public override void Refresh(LineView v, bool force)
        {
            int ss = StaticSig(v);
            if (force || ss != _staticSig)
            {
                int key = v.Round != null ? v.Day * 10 + v.Round.Index : -1 - v.Day;
                bool newRound = key != _roundKey;
                if (newRound)
                {
                    _roundKey = key;
                    _entryLen = 0;
                    _selTarget = v.Round != null && v.Round.Submitted ? v.Round.MyTarget : LineIds.None;
                    _selA = v.Round != null && v.Round.Submitted ? v.Round.MyA : -1;
                }
                _staticSig = ss;
                Build(v, force || newRound);
                _dynSig = int.MinValue;
            }
            int ds = DynSig(v);
            if (ds != _dynSig)
            {
                _dynSig = ds;
                RunUpdaters(v);
            }
        }

        void RunUpdaters(LineView v) { for (int i = 0; i < _updaters.Count; i++) _updaters[i](v); }

        static int StaticSig(LineView v)
        {
            var r = v.Round;
            if (r == null) return (int)v.Phase * 7 + v.Day * 131 + v.TodayRounds.Count;
            int h = (int)r.Type;
            h = h * 31 + r.Index;
            h = h * 31 + v.Day;
            h = h * 31 + (r.IsOdd ? 1 : 0) + (r.IsCulprit ? 2 : 0) + (r.IsSaboteur ? 4 : 0);
            h = h * 31 + r.Partner;
            h = h * 31 + r.TopicId * 3 + r.TopicVariant;
            h = h * 31 + r.ClueInnocent;
            h = h * 31 + r.DigitPosition * 10 + r.Digit;
            h = h * 31 + r.CodeLength * 7 + r.PositionOwners.Count;
            h = h * 31 + r.GuessOptions.Count;
            h = h * 31 + v.Players.Count;
            return h;
        }

        static int DynSig(LineView v)
        {
            var r = v.Round;
            int h = (int)v.Phase;
            if (r != null)
            {
                h = h * 31 + (r.Submitted ? 1 : 0);
                h = h * 31 + r.MyTarget;
                h = h * 31 + r.MyA;
                h = h * 31 + r.AttemptsLeft;
                h = h * 31 + r.Attempts.Count;
                h = h * 31 + (r.Solved ? 1 : 0);
            }
            int mask = 0;
            for (int i = 0; i < v.Players.Count; i++)
            {
                if (v.Players[i].Submitted) mask |= 1 << v.Players[i].Id;
                if (v.Players[i].Connected) mask |= 1 << (v.Players[i].Id + 8);
            }
            h = h * 31 + mask;
            h = h * 31 + (Dk.ComputerBlocked(v) ? 1 : 0);
            h = h * 31 + (int)v.Mine.Phone.Status * 10 + v.Mine.Phone.Other;
            return h;
        }

        public override void Tick(LineView v)
        {
            if (_timer == null) return;
            double end = v.Round != null && v.Phase == LinePhase.RoundPlay ? v.PhaseEndTime : v.PhaseEndTime;
            int s = Mathf.CeilToInt((float)(end - Dk.Now));
            if (s < 0) s = 0;
            if (s != _lastSec)
            {
                _lastSec = s;
                _timer.SetText(ClockInt(s));
                bool urgent = v.Phase == LinePhase.RoundPlay && s <= 15;
                _timer.parent.EnableInClassList("dk-task-clock-urgent", urgent);
            }
        }

        /// <summary>UI mock: stage a selection / keypad entry as if clicked.</summary>
        internal void MockSelect(int target, int a, params int[] digits)
        {
            _selTarget = target; _selA = a;
            _entryLen = 0;
            for (int i = 0; i < digits.Length && i < _entry.Length; i++) _entry[_entryLen++] = digits[i];
            var v = Dk.View;
            if (v != null) { Build(v, false); _dynSig = int.MinValue; Refresh(v, false); }
        }

        public void OnSubmitted(int actor)
        {
            VisualElement b;
            if (_tickBadges.TryGetValue(actor, out b)) UiMotion.Pulse(b, 0.25f, 0.3f);
        }

        // ------------------------------------------------------------------ build

        void Build(LineView v, bool animate)
        {
            Root.Clear();
            _updaters.Clear();
            _tickBadges.Clear();
            _timer = null; _primary = null; _status = null; _canSubmit = null; _submit = null; CallTarget = null;
            _lockedStamp = null; _lockedShown = false;
            _keypad = null; _entryCells.Clear();
            _lastSec = -1;
            _sendKey = "desk.task.lock";

            if (v.Round == null) { BuildIdle(v, animate); return; }
            var r = v.Round;
            var page = Box("dk-task").AddTo(Root);
            var side = Box("dk-task-side").NoPick().AddTo(page);
            var card = Dk.RoundCard(r.Type, "dk-task-card").AddTo(side);
            Text(Dk.RoundRule(r.Type), "dk-task-rule").NoPick().AddTo(side);
            var clock = Box("dk-task-clock").NoPick().AddTo(side);
            Text(Dk.T(v.Phase == LinePhase.RoundIntro ? "desk.task.starts_in" : "desk.task.time_left"), "dk-task-clock-label").NoPick().AddTo(clock);
            _timer = MemoBold("", "dk-task-clock-digits").NoPick().AddTo(clock);

            var main = Box("dk-task-main").AddTo(page);
            switch (r.Type)
            {
                case RoundType.PrimHavuzu: Prim(main, v, r); break;
                case RoundType.FarkliEposta: Odd(main, v, r); break;
                case RoundType.KayipRapor: Lost(main, v, r); break;
                case RoundType.IhbarKutusu: Complaint(main, v, r); break;
                case RoundType.TerfiListesi: Promotion(main, v, r); break;
                case RoundType.OrtakSifre: Password(main, v, r); break;
            }
            Footer(main, v, r);

            if (animate)
            {
                UiMotion.Flip(card, null, 0.4f, 0.05f);
                int i = 0;
                foreach (var child in main.Children())
                {
                    UiMotion.SlideIn(child, new Vector2(30f, 0), 0.3f, 0.08f + i * 0.08f);
                    i++;
                }
            }
        }

        void BuildIdle(LineView v, bool animate)
        {
            var page = Box("dk-task-idle").NoPick().AddTo(Root);
            Dk.Heading(page, Dk.T("desk.task.today"), Dk.T(v.Phase == LinePhase.Morning ? "desk.task.idle_morning" : "desk.task.idle"));
            var row = Box("dk-task-idle-cards").NoPick().AddTo(page);
            for (int i = 0; i < v.TodayRounds.Count; i++)
            {
                var col = Box("dk-task-idle-col").NoPick().AddTo(row);
                var c = Dk.RoundCard(v.TodayRounds[i], "dk-idle-card").AddTo(col);
                Bold(Dk.F("desk.round_n", i + 1), "dk-idle-n").NoPick().AddTo(col);
                Text(Dk.RoundRule(v.TodayRounds[i]), "dk-idle-rule").NoPick().AddTo(col);
                if (animate) UiMotion.PopIn(c, 0.3f, 0.1f + i * 0.12f);
            }
            var clock = Box("dk-task-clock", "dk-task-clock-inline").NoPick().AddTo(page);
            Text(Dk.T("desk.task.first_round_in"), "dk-task-clock-label").NoPick().AddTo(clock);
            _timer = MemoBold("", "dk-task-clock-digits").NoPick().AddTo(clock);
        }

        // ------------------------------------------------------------------ shared pieces

        void Select(int target, int a)
        {
            if (target != int.MinValue) _selTarget = target;
            if (a != int.MinValue) _selA = a;
            var v = Dk.View;
            if (v != null) RunUpdaters(v);
        }

        void CallButton(VisualElement parent, LineView v, int who)
        {
            if (who < 0 || who >= 100) return;
            var box = Box("dk-callbtn").AddTo(parent);
            if (CallTarget == null) CallTarget = box;
            var b = Btn("", () => Ui.Call(who), "btn-secondary", "dk-callbtn-btn", "btn-icon").AddTo(box);
            Dk.Icon("call", "btn-icon-img").AddTo(b);
            var bl = Bold(Dk.T("desk.task.call"), "btn-label").NoPick().AddTo(b);
            var tag = Text("", "dk-callbtn-tag").NoPick().AddTo(box);
            _updaters.Add(vv =>
            {
                var ph = vv.Mine.Phone;
                bool withThem = ph.Other == who && ph.Status != CallStatus.Idle;
                bool busy = ph.Status != CallStatus.Idle || Dk.PhoneDead(vv);
                b.SetEnabled(!busy);
                bl.SetText(Dk.T(withThem && ph.Status == CallStatus.Connected ? "desk.task.in_call" : withThem ? "desk.task.calling" : "desk.task.call"));
                tag.SetText(Dk.PhoneDead(vv) ? Dk.T("desk.task.phone_dead") : "");
                box.EnableInClassList("dk-callbtn-live", withThem && ph.Status == CallStatus.Connected);
            });
        }

        void Footer(VisualElement main, LineView v, LineRoundView r)
        {
            Box("spacer").NoPick().AddTo(main);
            var foot = Box("dk-task-foot").AddTo(main);
            var ticks = Box("dk-ticks").NoPick().AddTo(foot);
            Text(Dk.T("desk.task.submitted"), "dk-ticks-label").NoPick().AddTo(ticks);
            var badges = Box("dk-ticks-row").NoPick().AddTo(ticks);
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                var b = Box("dk-tick").NoPick().AddTo(badges);
                Dk.Badge(p.Id, "badge-s").AddTo(b);
                var mark = Dk.Icon("check", "dk-tick-mark").AddTo(b);
                _tickBadges[p.Id] = b;
                int id = p.Id;
                _updaters.Add(vv =>
                {
                    var pp = Dk.Player(id);
                    bool done = pp != null && pp.Submitted;
                    b.EnableInClassList("dk-tick-done", done);
                    b.EnableInClassList("dk-tick-away", pp != null && !pp.Connected);
                });
            }
            Box("spacer").NoPick().AddTo(foot);
            if (_submit == null) return;
            var act = Box("dk-task-act").AddTo(foot);
            _status = Text("", "dk-task-status").NoPick().AddTo(act);
            var brow = Box("dk-task-act-row").AddTo(act);
            _lockedStamp = Dk.Stamp(Dk.T("desk.task.locked_stamp"), null, "dk-locked-stamp").AddTo(brow);
            _lockedStamp.Show(false);
            if (_secondary != null) { _secondary.AddTo(brow); _secondary = null; }
            _primary = Btn(Dk.T(_sendKey), () => { if (_submit != null) _submit(); }, "btn-primary", "btn-big", "dk-primary").AddTo(brow);
            _updaters.Add(UpdateSubmit);
        }

        Button _secondary;
        VisualElement _lockedStamp;
        bool _lockedShown;

        void UpdateSubmit(LineView v)
        {
            if (_primary == null || v.Round == null) return;
            var r = v.Round;
            bool play = v.Phase == LinePhase.RoundPlay;
            bool blocked = Dk.ComputerBlocked(v);
            bool valid = _canSubmit == null || _canSubmit(v);
            bool same = r.Submitted && SameAsSubmitted(r);
            _primary.SetEnabled(play && !blocked && valid && !same);
            _primary.Show(!same);
            string label = r.Submitted ? (same ? Dk.T("desk.task.locked") : Dk.T("desk.task.relock")) : Dk.T(_sendKey);
            _primary.SetText(label);
            string status;
            if (v.Phase == LinePhase.RoundIntro) status = Dk.T("desk.task.status.intro");
            else if (v.Phase == LinePhase.RoundReveal) status = Dk.T("desk.task.status.over");
            else if (blocked) status = Dk.T("desk.task.status.blocked");
            else if (r.Submitted) status = Dk.T(same ? "desk.task.status.sent" : "desk.task.status.changed");
            else status = Dk.T("desk.task.status.default." + r.Type);
            _status?.SetText(status);
            _status?.EnableInClassList("dk-task-status-ok", r.Submitted && same);
            bool locked = r.Submitted && same;
            if (_lockedStamp != null && locked != _lockedShown)
            {
                _lockedShown = locked;
                _lockedStamp.Show(locked);
                if (locked) UiMotion.Stamp(_lockedStamp, -8f, 0.22f, 0f, () => Dk.Play("stamp", null, 0.6f));
            }
        }

        bool SameAsSubmitted(LineRoundView r)
        {
            switch (r.Type)
            {
                case RoundType.PrimHavuzu: return r.MyA == _selA;
                case RoundType.FarkliEposta: return r.MyTarget == _selTarget && (!r.IsOdd || r.MyA == _selA);
                case RoundType.KayipRapor:
                case RoundType.TerfiListesi: return r.MyTarget == _selTarget;
                case RoundType.IhbarKutusu: return r.MyTarget == _selTarget && (_selTarget < 0 || r.MyA == _selA);
                case RoundType.OrtakSifre: return _entryLen == _codeLen && r.MyA == EntryCode();
                default: return false;
            }
        }

        void Submit(int target, int a)
        {
            Dk.Send(LineVerb.SubmitChoice, target, a);
            Dk.Play("send", "mail_drop");
            if (_primary != null) UiMotion.Pulse(_primary, 0.1f, 0.25f);
            Ui.State.ObSubmitted = true;
        }

        // ------------------------------------------------------------------ Bonus Pool

        void Prim(VisualElement main, LineView v, LineRoundView r)
        {
            var partners = new List<int>();
            if (r.Partners.Count > 0) partners.AddRange(r.Partners);
            else if (r.Partner != LineIds.None) partners.Add(r.Partner);
            bool trio = partners.Count >= 2;
            var info = Box("dk-task-info").AddTo(main);
            Dk.Heading(info, Dk.T(trio ? "desk.task.prim.partners" : "desk.task.prim.partner"), Dk.T(trio ? "desk.task.prim.trio_note" : "desk.task.prim.partner_note"));
            var prow = Box("dk-partners").AddTo(info);
            for (int i = 0; i < partners.Count; i++)
            {
                int who = partners[i];
                var row = Box("dk-partner").AddTo(prow);
                if (trio) row.AddToClassList("dk-partner-trio");
                Dk.Badge(who, trio ? "badge-l" : "badge-xl").AddTo(row);
                var col = Box("dk-partner-col").NoPick().AddTo(row);
                Title(Dk.Name(who), "dk-partner-name").NoPick().AddTo(col);
                if (who == LineIds.Intern) Text(Dk.F("desk.task.prim.intern_note", Mathf.RoundToInt(Dk.Config.Rounds.InternShareChance * 100f)), "dk-partner-note").NoPick().AddTo(col);
                else CallButton(row, v, who);
            }

            var choose = Box("dk-task-choice").AddTo(main);
            Dk.Heading(choose, Dk.T("desk.task.prim.choose"), Dk.T("desk.task.prim.secret"));
            var cards = Box("dk-choices").AddTo(choose);
            var rules = Dk.Config.Rounds;
            int greed = Dk.Profile(v).GreedBonus;
            VisualElement share, take;
            if (trio)
            {
                share = ChoiceTile(cards, 0, "share", Dk.T("desk.choice.share"),
                    (Dk.T("desk.task.prim.trio_none"), rules.PrimTrioNobodyTakes), (Dk.T("desk.task.prim.trio_some"), rules.PrimTrioSharer));
                take = ChoiceTile(cards, 1, "take_all", Dk.T("desk.choice.take"),
                    (Dk.T("desk.task.prim.trio_solo"), rules.PrimTrioSoloTaker - greed), (Dk.T("desk.task.prim.trio_pair"), rules.PrimTrioPairTakers), (Dk.T("desk.task.prim.trio_all"), rules.PrimTrioAllTake));
            }
            else
            {
                share = ChoiceTile(cards, 0, "share", Dk.T("desk.choice.share"),
                    (Dk.T("desk.task.prim.if_share"), rules.PrimBothShare), (Dk.T("desk.task.prim.if_take"), rules.PrimSharerLoses));
                take = ChoiceTile(cards, 1, "take_all", Dk.T("desk.choice.take"),
                    (Dk.T("desk.task.prim.if_share"), rules.PrimTakerWins - greed), (Dk.T("desk.task.prim.if_take"), rules.PrimBothTake));
            }
            _updaters.Add(vv =>
            {
                share.EnableInClassList("dk-choice-on", _selA == 0);
                take.EnableInClassList("dk-choice-on", _selA == 1);
            });
            _canSubmit = vv => _selA == 0 || _selA == 1;
            _submit = () => Submit(LineIds.None, _selA);
        }

        VisualElement ChoiceTile(VisualElement parent, int a, string icon, string label, params (string label, int delta)[] lines)
        {
            var t = Dk.Tile(() => Select(int.MinValue, a), "dk-choice", a == 1 ? "dk-choice-take" : "dk-choice-share").AddTo(parent);
            Dk.Icon(icon, "dk-choice-icon").AddTo(t);
            var col = Box("dk-choice-col").NoPick().AddTo(t);
            StampText(Loc.Upper(label), "dk-choice-label").NoPick().AddTo(col);
            for (int i = 0; i < lines.Length; i++) PayLine(col, lines[i].label, lines[i].delta);
            var mark = Dk.Icon("check", "dk-choice-check").AddTo(t);
            mark.pickingMode = PickingMode.Ignore;
            return t;
        }

        static void PayLine(VisualElement parent, string label, int delta)
        {
            var row = Box("dk-payline").NoPick().AddTo(parent);
            Text(label, "dk-payline-label").NoPick().AddTo(row);
            Box("spacer").NoPick().AddTo(row);
            Bold(Dk.F("desk.task.blame_delta", Dk.Delta(delta)), delta < 0 ? "dk-good" : delta > 0 ? "dk-bad" : "dk-neutral").NoPick().AddTo(row);
        }

        // ------------------------------------------------------------------ Odd Email

        void Odd(VisualElement main, LineView v, LineRoundView r)
        {
            var info = Box("dk-task-info", "dk-task-info-row").AddTo(main);
            var mail = Box("dk-email").NoPick().AddTo(info);
            MailRow(mail, Dk.T("desk.mail.from"), Dk.T("desk.task.odd.sender"));
            MailRow(mail, Dk.T("desk.mail.to"), Dk.T("desk.task.odd.to"));
            MailRow(mail, Dk.T("desk.mail.subject"), Dk.T("desk.task.odd.subject"));
            Box("dk-email-rule").NoPick().AddTo(mail);
            Memo(Dk.Topic(r.TopicId), "dk-email-body").NoPick().AddTo(mail);
            if (r.IsOdd)
            {
                var st = Dk.Stamp(Dk.T("desk.task.odd.stamp"), null, "dk-email-stamp").AddTo(mail);
                UiMotion.Stamp(st, -9f, 0.24f, 0.5f, () => Dk.Play("stamp"));
                var note = Box("dk-sticky", "dk-task-sticky").NoPick().AddTo(info);
                Dk.Skin(note, "onboarding/sticky_note_9slice");
                Hand(Dk.T("desk.task.odd.you_note"), "dk-sticky-text").NoPick().AddTo(note);
            }

            var vote = Box("dk-task-choice").AddTo(main);
            Dk.Heading(vote, Dk.T("desk.task.odd.who"), Dk.T(r.IsOdd ? "desk.task.odd.who_hint_odd" : "desk.task.odd.who_hint"));
            var upd = Dk.Picker(vote, v, id => Select(id, int.MinValue), false, "badge-m");
            _updaters.Add(vv => upd(_selTarget));
            if (r.IsOdd && r.GuessOptions.Count > 0)
            {
                var guess = Box("dk-task-choice").AddTo(main);
                Dk.Heading(guess, Dk.T("desk.task.odd.guess"), Dk.T("desk.task.odd.guess_hint"));
                var grid = Box("dk-guess-grid").AddTo(guess);
                var tiles = new List<(int, VisualElement)>();
                for (int i = 0; i < r.GuessOptions.Count; i++)
                {
                    int topic = r.GuessOptions[i];
                    var t = Dk.Tile(() => Select(int.MinValue, topic), "dk-guess").AddTo(grid);
                    Memo(Dk.Topic(topic), "dk-guess-text").NoPick().AddTo(t);
                    tiles.Add((topic, t));
                }
                _updaters.Add(vv => { foreach (var (topic, t) in tiles) t.EnableInClassList("dk-guess-on", _selA == topic); });
            }
            _canSubmit = vv => _selTarget >= 0;
            _submit = () => Submit(_selTarget, r.IsOdd ? _selA : -1);
        }

        static void MailRow(VisualElement mail, string k, string val)
        {
            var row = Box("dk-email-row").NoPick().AddTo(mail);
            Text(k, "dk-email-k").NoPick().AddTo(row);
            Bold(val, "dk-email-v").NoPick().AddTo(row);
        }

        // ------------------------------------------------------------------ Lost Report

        void Lost(VisualElement main, LineView v, LineRoundView r)
        {
            var info = Box("dk-task-info", "dk-task-info-row").AddTo(main);
            var slip = Box("dk-evidence").NoPick().AddTo(info);
            Box("dk-tape").NoPick().AddTo(slip);
            StampText(Loc.Upper(Dk.T("desk.task.lost.slip_head")), "dk-evidence-head").NoPick().AddTo(slip);
            var who = Box("dk-evidence-who").NoPick().AddTo(slip);
            Dk.Badge(r.ClueInnocent, "badge-l").AddTo(who);
            Hand(Dk.F("desk.task.lost.clue", Dk.Name(r.ClueInnocent)), "dk-evidence-text").NoPick().AddTo(who);
            Text(Dk.T("desk.task.lost.true"), "dk-evidence-sub").NoPick().AddTo(slip);
            Dk.SetTilt(slip, -1.2f);
            if (r.IsCulprit)
            {
                var st = Dk.Stamp(Dk.T("desk.task.lost.stamp"), null, "dk-evidence-stamp").AddTo(slip);
                UiMotion.Stamp(st, 8f, 0.24f, 0.5f, () => Dk.Play("stamp"));
                var note = Box("dk-sticky", "dk-task-sticky").NoPick().AddTo(info);
                Dk.Skin(note, "onboarding/sticky_note_9slice");
                Hand(Dk.T("desk.task.lost.culprit_note"), "dk-sticky-text").NoPick().AddTo(note);
                if (r.NoAlibi.Count > 0)
                {
                    Hand(Dk.T("desk.task.lost.no_alibi"), "dk-sticky-text", "dk-sticky-strong").NoPick().AddTo(note);
                    var who2 = Box("dk-noalibi").NoPick().AddTo(note);
                    for (int i = 0; i < r.NoAlibi.Count; i++)
                    {
                        var w = Box("dk-noalibi-who").NoPick().AddTo(who2);
                        Dk.Badge(r.NoAlibi[i], "badge-s").AddTo(w);
                        Hand(Dk.Name(r.NoAlibi[i]), "dk-noalibi-name").NoPick().AddTo(w);
                    }
                }
            }
            else
            {
                var note = Box("dk-task-tip").NoPick().AddTo(info);
                Text(Dk.T("desk.task.lost.tip"), "dk-task-tip-text").NoPick().AddTo(note);
                CallButton(note, v, r.ClueInnocent);
            }

            var vote = Box("dk-task-choice").AddTo(main);
            Dk.Heading(vote, Dk.T("desk.task.lost.who"), Dk.T("desk.task.lost.who_hint"));
            var upd = Dk.Picker(vote, v, id => Select(id, int.MinValue));
            _updaters.Add(vv => upd(_selTarget));
            _canSubmit = vv => _selTarget >= 0;
            _submit = () => Submit(_selTarget, -1);
        }

        // ------------------------------------------------------------------ Complaint Box

        void Complaint(VisualElement main, LineView v, LineRoundView r)
        {
            var form = Box("dk-form").AddTo(main);
            var head = Box("dk-form-head").NoPick().AddTo(form);
            StampText(Loc.Upper(Dk.T("desk.task.complaint.form")), "dk-form-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            Memo(Dk.T("desk.task.complaint.form_no"), "dk-form-no").NoPick().AddTo(head);
            Dk.Heading(form, Dk.T("desk.task.complaint.who"), Dk.T("desk.task.complaint.anon"));
            var upd = Dk.Picker(form, v, id => Select(id, int.MinValue), false, "badge-m");
            _updaters.Add(vv => upd(_selTarget));
            Dk.Heading(form, Dk.T("desk.task.complaint.why"));
            var grid = Box("dk-reasons").AddTo(form);
            int n = Dk.Config.Rounds.ComplaintReasons;
            var boxes = new List<VisualElement>();
            for (int i = 0; i < n; i++)
            {
                int reason = i;
                var t = Dk.Tile(() => Select(int.MinValue, reason), "dk-reason").AddTo(grid);
                Box("dk-checkbox").NoPick().AddTo(t);
                Text(Dk.ComplaintReason(i), "dk-reason-text").NoPick().AddTo(t);
                boxes.Add(t);
            }
            _updaters.Add(vv => { for (int i = 0; i < boxes.Count; i++) boxes[i].EnableInClassList("dk-reason-on", _selA == i); });
            _canSubmit = vv => _selTarget >= 0 && _selA >= 0;
            _submit = () => Submit(_selTarget, _selA);
            _secondary = Btn(Dk.T("desk.task.abstain"), () => { _selTarget = LineIds.None; _selA = -1; Submit(LineIds.None, -1); }, "btn-ghost", "dk-secondary");
        }

        // ------------------------------------------------------------------ Promotion List

        void Promotion(VisualElement main, LineView v, LineRoundView r)
        {
            var info = Box("dk-task-info").AddTo(main);
            var memo = Box("dk-memo").NoPick().AddTo(info);
            StampText(Loc.Upper(Dk.T("desk.task.promo.memo_head")), "dk-memo-head").NoPick().AddTo(memo);
            Memo(Dk.T("desk.task.promo.memo"), "dk-memo-text").NoPick().AddTo(memo);
            var vote = Box("dk-task-choice").AddTo(main);
            Dk.Heading(vote, Dk.T("desk.task.promo.who"), Dk.T("desk.task.promo.public"));
            var upd = Dk.Picker(vote, v, id => Select(id, int.MinValue));
            _updaters.Add(vv => upd(_selTarget));
            _canSubmit = vv => _selTarget >= 0;
            _submit = () => Submit(_selTarget, -1);
            _secondary = Btn(Dk.T("desk.task.abstain"), () => { _selTarget = LineIds.None; Submit(LineIds.None, -1); }, "btn-ghost", "dk-secondary");
        }

        // ------------------------------------------------------------------ Shared Password (v3.1)

        void Password(VisualElement main, LineView v, LineRoundView r)
        {
            _codeLen = Mathf.Clamp(r.CodeLength > 0 ? r.CodeLength : v.Players.Count, 1, _entry.Length);
            if (_entryLen == 0 && r.Submitted && r.MyA >= 0) LoadEntry(r.MyA);
            var top = Box("dk-task-info", "dk-task-info-row").AddTo(main);
            // the two digits I know
            var mine = DigitCard(top, Dk.T("desk.task.code.yours"), r.DigitPosition, r.Digit);
            if (r.IsSaboteur)
            {
                var st = Dk.Stamp(Dk.T("desk.task.code.saboteur"), null, "dk-digit-stamp").AddTo(mine);
                UiMotion.Stamp(st, -10f, 0.24f, 0.5f, () => Dk.Play("stamp"));
            }
            int neighbour = r.NeighborPosition >= 0 && r.NeighborPosition < r.PositionOwners.Count ? r.PositionOwners[r.NeighborPosition] : LineIds.None;
            if (r.NeighborPosition >= 0)
                DigitCard(top, Dk.F("desk.task.code.neighbour", Dk.Name(neighbour)), r.NeighborPosition, r.NeighborDigit).AddToClassList("dk-digit-card-neighbour");
            // public owners row
            var pub = Box("dk-owners").AddTo(top);
            Dk.Heading(pub, Dk.T("desk.task.code.owners"), Dk.T(r.IsSaboteur ? "desk.task.code.saboteur_note" : "desk.task.code.owners_hint"));
            var orow = Box("dk-owners-row").AddTo(pub);
            for (int i = 0; i < _codeLen; i++)
            {
                int owner = i < r.PositionOwners.Count ? r.PositionOwners[i] : -1;
                var cell = Box("dk-owner").AddTo(orow);
                Bold(Num(i + 1), "dk-owner-n").NoPick().AddTo(cell);
                Dk.Badge(owner, "badge-s").AddTo(cell);
                Text(owner >= 0 ? Dk.Name(owner) : "?", "dk-owner-name").NoPick().AddTo(cell);
                string known = i == r.DigitPosition && r.Digit >= 0 ? Num(r.Digit) : i == r.NeighborPosition && r.NeighborDigit >= 0 ? Num(r.NeighborDigit) : "?";
                MemoBold(known, "dk-owner-digit").NoPick().AddTo(cell);
                if (i == r.DigitPosition) cell.AddToClassList("dk-owner-me");
                else if (i == r.NeighborPosition) cell.AddToClassList("dk-owner-known");
                if (owner >= 0 && owner != v.Me && owner < 100)
                {
                    int who = owner;
                    var call = Dk.Tile(() => Ui.Call(who), "dk-owner-call").AddTo(cell);
                    Dk.Icon("call", "dk-owner-call-icon").AddTo(call);
                }
            }

            var lower = Box("dk-code-lower").AddTo(main);
            _keypad = Box("dk-keypad").AddTo(lower);
            var display = Box("dk-keypad-display").NoPick().AddTo(_keypad);
            Dk.Skin(display, "deskphone/lcd_frame");
            for (int i = 0; i < _codeLen; i++) _entryCells.Add(MemoBold("_", "dk-keypad-cell").NoPick().AddTo(display));
            var keys = Box("dk-keys-grid").AddTo(_keypad);
            int[] order = { 1, 2, 3, 4, 5, 6, 7, 8, 9, -1, 0, -2 };
            for (int i = 0; i < order.Length; i++)
            {
                int k = order[i];
                if (k == -2) { var del = Key("«", () => Digit(-1)).AddTo(keys); del.AddToClassList("dk-key-del"); continue; }
                if (k == -1) { var clr = Key(Dk.T("desk.task.code.clear"), () => { _entryLen = 0; UpdateEntry(); }).AddTo(keys); clr.AddToClassList("dk-key-del"); continue; }
                Key(Num(k), () => Digit(k)).AddTo(keys);
            }
            var how = Box("dk-code-how").AddTo(lower);
            Dk.Heading(how, Dk.T("desk.task.code.how_head"));
            Text(Dk.F("desk.task.code.how", v.Players.Count / 2 + 1, v.Players.Count), "dk-code-how-text").NoPick().AddTo(how);
            var locked = Box("dk-code-locked").NoPick().AddTo(how);
            Text(Dk.T("desk.task.code.locked_label"), "dk-code-locked-label").NoPick().AddTo(locked);
            var lockedCode = MemoBold("", "dk-code-locked-code").NoPick().AddTo(locked);
            _updaters.Add(vv =>
            {
                var rr = vv.Round;
                if (rr == null) return;
                bool has = rr.Submitted && rr.MyA >= 0;
                locked.EnableInClassList("dk-code-locked-on", has);
                lockedCode.EnableInClassList("dk-code-locked-none", !has);
                lockedCode.SetText(has ? rr.MyA.ToString().PadLeft(_codeLen, '0') : Dk.T("desk.task.code.none_locked"));
            });
            UpdateEntry();
            _canSubmit = vv => _entryLen == _codeLen;
            _submit = () => Submit(LineIds.None, EntryCode());
        }

        static VisualElement DigitCard(VisualElement parent, string head, int position, int digit)
        {
            var c = Box("dk-digit-card").NoPick().AddTo(parent);
            StampText(Loc.Upper(head), "dk-digit-head").NoPick().AddTo(c);
            Title(digit >= 0 ? Num(digit) : "?", "dk-digit-big").NoPick().AddTo(c);
            Bold(Dk.F("desk.task.code.position", position + 1), "dk-digit-pos").NoPick().AddTo(c);
            return c;
        }

        Button Key(string label, Action onClick)
        {
            var b = Dk.Tile(onClick, "dk-pkey");
            Dk.Skin(b, "deskphone/key");
            MemoBold(label, "dk-pkey-text").NoPick().AddTo(b);
            return b;
        }

        int EntryCode()
        {
            int code = 0;
            for (int i = 0; i < _entryLen; i++) code = code * 10 + _entry[i];
            return code;
        }

        void LoadEntry(int code)
        {
            for (int i = _codeLen - 1; i >= 0; i--) { _entry[i] = code % 10; code /= 10; }
            _entryLen = _codeLen;
        }

        public override void Digit(int d)
        {
            var v = Dk.View;
            if (v == null || v.Round == null || v.Round.Type != RoundType.OrtakSifre || _keypad == null) return;
            if (d < 0) { if (_entryLen > 0) _entryLen--; }
            else if (_entryLen < _codeLen) { _entry[_entryLen++] = d; Sfx.Play("click", 0.5f); }
            else { UiMotion.Shake(_keypad, 6f, 0.25f); return; }
            UpdateEntry();
        }

        void UpdateEntry()
        {
            for (int i = 0; i < _entryCells.Count; i++)
            {
                _entryCells[i].SetText(i < _entryLen ? Num(_entry[i]) : "_");
                _entryCells[i].EnableInClassList("dk-keypad-cell-on", i < _entryLen);
            }
            var v = Dk.View;
            if (v != null) UpdateSubmit(v);
        }
    }
}
