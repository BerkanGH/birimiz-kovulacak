using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 bot talk on the phone (docs/V5_BOT_TALK.md, docs/UI.md "Talking to a bot"): while you are on the phone with a bot
    /// its lines come up in a speech bubble (typed out, with its mumble), with 2–3 question buttons for the current task
    /// or the crisis (keys 1 / 2 / 3, or click) and a quick "send prim" row. Beside the phone panel when the handset is up,
    /// under the call pill otherwise (compact). A refusal ("Şu an çok yoğunum.") stays a moment after the call is gone.
    /// Calls between people are unchanged (voice only): this card never shows for them.
    /// </summary>
    public sealed class BotCallCard
    {
        public enum Place { Phone, Hud, Os }

        public readonly VisualElement Root;
        readonly VisualElement _head, _lines, _questions, _pay;
        readonly Label _name, _hint;
        readonly VisualElement _dot;
        Label _typing;
        BotSpeech.Entry _typingEntry;
        string _typingText = "";
        int _version = -1, _bot = -1;
        string _qsig, _paysig;
        Place _place = Place.Hud;
        bool _shown;

        const float CharsPerSecond = 42f;

        public bool Shown => _shown;

        public BotCallCard(VisualElement parent)
        {
            Root = Box("bc-card", "bc-hud").AddTo(parent);
            Box("bc-tail").NoPick().AddTo(Root);
            _head = Box("bc-head").NoPick().AddTo(Root);
            _dot = Box("bc-dot").NoPick().AddTo(_head);
            _name = Title("", "bc-name").NoPick().AddTo(_head);
            Tag(_head, T("players.bot"), "bc-bot-tag").NoPick();
            Box("grow").NoPick().AddTo(_head);
            _hint = Text("", "bc-hint").NoPick().AddTo(_head);
            _lines = Box("bc-lines").NoPick().AddTo(Root);
            _questions = Box("bc-questions").AddTo(Root);
            _pay = Box("bc-pay").AddTo(Root);
            Root.Show(false);
        }

        /// <summary>The bot I am talking to on the phone (connected), or -1.</summary>
        public static int BotInCall(LineClient lc, LineView v)
        {
            if (lc == null || v == null || v.Mine.Phone.Status != CallStatus.Connected) return -1;
            var p = lc.Player(v.Mine.Phone.Other);
            return p != null && p.IsBot ? p.Id : -1;
        }

        /// <summary>Keys 1 / 2 / 3: ask the bot in my call (false when there is nothing to ask).</summary>
        public static bool AskKey(LineClient lc, LineView v, int index)
        {
            int bot = BotInCall(lc, v);
            var set = LineBotLines.QuestionSetOf(v);
            if (bot < 0 || index < 0 || index >= LineBotLines.Questions(set).Count) return false;
            BotSpeech.Ask(bot, set, index);
            Sfx.Play("click", 0.4f);
            return true;
        }

        public void SetPlace(Place p)
        {
            if (p == _place) return;
            _place = p;
            Root.EnableInClassList("bc-phone", p == Place.Phone);
            Root.EnableInClassList("bc-hud", p == Place.Hud);
            Root.EnableInClassList("bc-os", p == Place.Os);
            _version = -1;
            _qsig = _paysig = null;
        }

        public void Tick(LineClient lc, LineView v, bool allowed)
        {
            int bot = BotInCall(lc, v);
            var tr = BotSpeech.Transcript;
            // a refusal or the bot's goodbye stays a moment after the call is gone
            var ph = v.Mine.Phone;
            bool lingering = bot < 0 && tr.Count > 0 && !tr[tr.Count - 1].Mine && BotSpeech.LastAge < 4.5f
                             && (ph.Status == CallStatus.Idle || (ph.Status == CallStatus.Dialing && ph.Other == tr[tr.Count - 1].Other));
            int who = bot >= 0 ? bot : lingering ? tr[tr.Count - 1].Other : -1;
            bool show = allowed && who >= 0 && LineClient.IsCallsPhase(v.Phase);
            if (show != _shown)
            {
                _shown = show;
                Root.Show(show);
                if (show) { UiMotion.PopIn(Root, 0.22f, 0f, 0.85f); _version = -1; _qsig = _paysig = null; }
            }
            if (!show) return;
            if (who != _bot)
            {
                _bot = who;
                _version = -1;
                var p = lc.Player(who);
                _name.SetText(p != null ? p.Name : Name(who));
                _dot.style.backgroundColor = PlayerColor(lc.ColorOf(who));
            }
            var set = bot >= 0 ? LineBotLines.QuestionSetOf(v) : BotQuestionSet.None;
            _hint.SetText(bot < 0 ? T("office.botcall.ended") : LineBotLines.Questions(set).Count > 0 ? F("office.botcall.keys", LineBotLines.Questions(set).Count) : "");
            if (BotSpeech.Version != _version) { _version = BotSpeech.Version; BuildLines(lc, tr, who); }
            Type();
            BuildQuestions(bot, set);
            BuildPay(lc, v, bot);
        }

        void BuildLines(LineClient lc, IReadOnlyList<BotSpeech.Entry> tr, int who)
        {
            _lines.Clear();
            _typing = null;
            _typingEntry = null;
            int keep = _place == Place.Phone ? 4 : 2;
            var shown = new List<BotSpeech.Entry>();
            for (int i = tr.Count - 1; i >= 0 && shown.Count < keep; i--) if (tr[i].Other == who) shown.Insert(0, tr[i]);
            if (shown.Count == 0)
            {
                Text(T("office.botcall.waiting"), "bc-line", "bc-line-wait").NoPick().AddTo(_lines);
                return;
            }
            for (int i = 0; i < shown.Count; i++)
            {
                var e = shown[i];
                string text = BotSpeech.Text(e.Key, e.Args);
                bool newest = i == shown.Count - 1;
                if (e.Mine)
                {
                    Text(F("office.botcall.me", text), "bc-mine").NoPick().AddTo(_lines);
                    continue;
                }
                var l = Text("", "bc-line", newest ? null : "bc-line-old").NoPick().AddTo(_lines);
                if (newest && Time.unscaledTime - e.At < text.Length / CharsPerSecond)
                {
                    _typing = l;
                    _typingEntry = e;
                    _typingText = text;
                }
                else l.SetText(text);
            }
        }

        /// <summary>The newest line is typed out.</summary>
        void Type()
        {
            if (_typing == null || _typingEntry == null) return;
            int n = Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - _typingEntry.At) * CharsPerSecond), 0, _typingText.Length);
            _typing.SetText(n >= _typingText.Length ? _typingText : _typingText.Substring(0, n) + "▌");
            if (n >= _typingText.Length) _typing = null;
        }

        void BuildQuestions(int bot, BotQuestionSet set)
        {
            var keys = LineBotLines.Questions(set);
            string sig = bot + ":" + set + ":" + Loc.Language + ":" + _place;
            if (sig == _qsig) return;
            _qsig = sig;
            _questions.Clear();
            _questions.Show(bot >= 0 && keys.Count > 0);
            for (int i = 0; i < keys.Count; i++)
            {
                int index = i;
                var b = Btn("", () =>
                {
                    var lc = LineClient.I;
                    AskKey(lc, lc != null ? lc.View : null, index);
                }, "bc-q");
                b.text = "";
                UiKit.KeyCap((i + 1).ToString(), "bc-q-key").NoPick().AddTo(b);
                Bold(T(keys[i]), "bc-q-text").NoPick().AddTo(b);
                b.AddTo(_questions);
            }
        }

        void BuildPay(LineClient lc, LineView v, int bot)
        {
            var me = lc.Me;
            bool can = bot >= 0 && v.Features.TransfersOpen && me != null && !me.Candidate;
            string sig = can + ":" + v.Mine.Prim + ":" + bot + ":" + Loc.Language + ":" + _place;
            if (sig == _paysig) return;
            _paysig = sig;
            _pay.Clear();
            _pay.Show(can && _place == Place.Phone);
            if (!can) return;
            Bold(T("office.botcall.pay"), "bc-pay-label").NoPick().AddTo(_pay);
            foreach (int n in new[] { 1, 2, 3 })
            {
                int amount = n;
                var b = Btn(F("office.botcall.pay_n", n), () => Send(LineVerb.Transfer, bot, amount), "pl-small", "bc-pay-btn").AddTo(_pay);
                b.SetEnabled(v.Mine.Prim >= n);
            }
        }
    }
}
