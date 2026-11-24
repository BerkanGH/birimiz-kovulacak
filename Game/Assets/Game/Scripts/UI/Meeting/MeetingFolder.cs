using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3a: the folder in front of you at the meeting table (docs/V5_INTERACTION.md §3). E (or Q) picks it up:
    /// the folder comes up to the camera, its cover flips open and this tablet UI is laid exactly over the tablet screen.
    /// Tabs: <b>Eylemler</b> (Soruştur, Kaydı sızdır — your own moves in the meeting), <b>Sohbet</b> (meeting chat and private
    /// texts, one thread), <b>Havale</b> (send prim; sealed for candidates), <b>Dosya</b> (the meeting's public record,
    /// earlier firings; in the results the full reveal of step 3b, <see cref="MeetingReveal"/>: every balance, who robbed
    /// whom, every vote per round, desk openings, transfers, task choices), <b>Kurallar</b>. Esc / E puts it down.
    /// Content comes from <see cref="PlayBlocks"/>; every action is a <see cref="LineClient.Send"/>.
    /// </summary>
    public sealed class MeetingFolder
    {
        public enum Tab { Actions = 0, Pranks = 1, Chat = 2, Transfer = 3, Dossier = 4, Rules = 5 }
        static readonly string[] Ids = { "actions", "pranks", "chat", "transfer", "dossier", "rules" };

        readonly MeetingUi _ui;
        readonly PlayState _st;
        public readonly VisualElement Root;
        readonly Button[] _tabs = new Button[6];
        readonly Label[] _badges = new Label[6];
        readonly Label _clock, _prim, _title;
        readonly ScrollView _scroll;
        readonly PlaySection _body, _composeTargets;
        readonly VisualElement _compose;
        readonly TextField _field;
        readonly Label _composeInfo;
        Tab _tab;
        bool _open, _dirty = true, _scrollBottom;
        int _rulesPage, _seenChat;
        string _revealTab = MeetingReveal.TabStory;
        float _alpha;

        public bool IsOpen => _open;
        public bool Shown => _open && _alpha > 0.01f;
        public Tab Current => _tab;

        public MeetingFolder(MeetingUi ui, VisualElement parent, PlayState st)
        {
            _ui = ui;
            _st = st;
            Root = Box("mt-tablet").AddTo(parent);
            Root.style.position = Position.Absolute;
            var bar = Box("mt-tabs").AddTo(Root);
            Title(T("meeting.folder.brand"), "mt-tabs-brand").NoPick().AddTo(bar);
            for (int i = 0; i < Ids.Length; i++)
            {
                var tab = (Tab)i;
                // the label is a child (a button with children is measured by them, not by its own text)
                var b = new Button(() => { Sfx.Play("click"); SetTab(tab); }).Cls("mt-tab");
                b.RemoveFromClassList(Button.ussClassName);
                Bold(T("meeting.folder.tab." + Ids[i]), "mt-tab-text").NoPick().AddTo(b);
                _badges[i] = Bold("", "mt-tab-badge").NoPick().AddTo(b);
                _badges[i].Show(false);
                b.AddTo(bar);
                _tabs[i] = b;
            }
            Box("grow").NoPick().AddTo(bar);
            _prim = Bold("", "mt-tabs-prim").NoPick().AddTo(bar);
            _prim.style.flexShrink = 0;
            _clock = Title("", "mt-tabs-clock").NoPick().AddTo(bar);
            var x = new Button(() => _ui.CloseAll()) { text = "×" }.Cls("os-win-close");
            x.AddTo(bar);
            _title = Bold("", "mt-tablet-title").NoPick().AddTo(Root);
            _scroll = new ScrollView(ScrollViewMode.Vertical).Cls("mt-tablet-body");
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Root.Add(_scroll);
            _body = new PlaySection(_scroll.contentContainer, "mt-tablet-section");
            _compose = Box("os-compose", "mt-compose").AddTo(Root);
            _composeInfo = Text("", "pl-text", "pl-dim").AddTo(_compose);
            _composeTargets = new PlaySection(_compose, "os-section");
            var row = Box("pl-row").AddTo(_compose);
            _field = new TextField { maxLength = 200 }.Cls("pl-field", "grow").AddTo(row);
            _field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { SendText(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            Btn(T("msg.send"), SendText, "pl-primary").AddTo(row);
            _compose.Show(false);
            Text(F("meeting.folder.foot", OfficeText.Key(GameInput.Interact)), "mt-tablet-foot").NoPick().AddTo(Root);
            Root.Show(false);
        }

        // ------------------------------------------------------------------ open / close

        public void Open(Tab? tab = null)
        {
            bool was = _open;
            _open = true;
            Root.Show(true);
            if (!was) { _alpha = 0f; Root.style.opacity = 0f; }
            SetTab(tab ?? _tab);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            Root.Show(false);
            Root.style.opacity = 0f;
            _alpha = 0f;
            if (_field.focusController != null && _field.focusController.focusedElement == _field) _field.Blur();
        }

        public void SetTab(Tab tab)
        {
            bool changed = tab != _tab;
            _tab = tab;
            for (int i = 0; i < _tabs.Length; i++) _tabs[i].EnableInClassList("mt-tab-on", i == (int)tab);
            _compose.Show(tab == Tab.Chat);
            _dirty = true;
            _body.Invalidate();
            if (changed || tab == Tab.Chat) _scroll.scrollOffset = Vector2.zero;
            _scrollBottom = tab == Tab.Chat;
        }

        public void FocusField() { if (_tab == Tab.Chat) _field.Focus(); }
        public void MarkDirty() { _dirty = true; }
        public void ResetWeek() { _seenChat = 0; _rulesPage = 0; _revealTab = MeetingReveal.TabStory; SetTab(Tab.Actions); }

        /// <summary>v5 step 3b: the results' full reveal on one of its tabs (<see cref="MeetingReveal"/> tab ids).</summary>
        public void ShowReveal(string tab)
        {
            if (!string.IsNullOrEmpty(tab)) _revealTab = tab;
            SetTab(Tab.Dossier);
        }

        /// <summary>The tab that has something for you right now (opening the folder lands there).</summary>
        public static Tab Suggested(LineView v)
        {
            if (v.Phase == LinePhase.Results) return Tab.Dossier;
            if (ActionsAvailable(v)) return Tab.Actions;
            return Tab.Chat;
        }

        static bool ActionsAvailable(LineView v)
        {
            var m = v.Mine;
            bool investigate = v.Features.InvestigationOpen && !m.InvestigationUsed && m.InvestigationTargets.Count > 0;
            return investigate || PlayBlocks.LeakableCount(v) > 0;
        }

        /// <summary>Lays the tablet UI over the folder's screen (panel units); fades in once the folder is up.</summary>
        public void Place(Rect r, bool settled, float dt)
        {
            if (!_open) return;
            Root.style.left = r.x; Root.style.top = r.y; Root.style.width = r.width; Root.style.height = r.height;
            _alpha = Mathf.MoveTowards(_alpha, settled ? 1f : 0f, dt * (settled ? 7f : 20f));
            Root.style.opacity = _alpha;
            Root.pickingMode = _alpha > 0.5f ? PickingMode.Position : PickingMode.Ignore;
        }

        void SendText()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (!PlayBlocks.SendMessage(v, _st, _field.value)) return;
            _field.SetValueWithoutNotify("");
            _field.Focus();
            _scrollBottom = true;
        }

        // ------------------------------------------------------------------ per frame

        public void Tick(LineClient lc, LineView v, double now, bool dirty, bool live)
        {
            UpdateBadges(lc, v);
            if (!_open) return;
            _clock.SetText(UiKit.Clock(Math.Max(0, v.PhaseEndTime - now)));
            _prim.SetText(F("header.my_prim", v.Mine.Prim));
            string titleKey = "meeting.folder.title." + Ids[(int)_tab];
            if (_tab == Tab.Dossier && v.Phase == LinePhase.Results) titleKey = "meeting.folder.title.reveal";
            _title.SetText(T(titleKey));
            if (dirty || _dirty)
            {
                _dirty = false;
                _body.Update(p => Build(p, lc, v, now));
                if (_tab == Tab.Chat)
                {
                    _composeTargets.Update(p => PlayBlocks.BuildComposeTargets(p, lc, v, _st, _composeInfo));
                    _seenChat = CountIncoming(lc);
                }
            }
            if (_scrollBottom && _scroll.contentContainer.layout.height > 1f)
            {
                _scrollBottom = false;
                _scroll.scrollOffset = new Vector2(0f, Mathf.Max(0f, _scroll.contentContainer.layout.height));
            }
            if (live) _body.TickLive(now);
        }

        static int CountIncoming(LineClient lc)
        {
            int n = 0;
            foreach (var msg in lc.Messages) if (msg.From != lc.MySlot) n++;
            return n;
        }

        /// <summary>Unread chat / texts since the chat tab was last open.</summary>
        public int Unread(LineClient lc) { return Math.Max(0, CountIncoming(lc) - _seenChat); }

        void UpdateBadges(LineClient lc, LineView v)
        {
            SetBadge(Tab.Actions, ActionsAvailable(v) ? "!" : null);
            int unread = Unread(lc);
            SetBadge(Tab.Chat, unread > 0 && !(_open && _tab == Tab.Chat) ? unread.ToString() : null);
            // v5 Yaramazlıklar: the tab only when the lobby turned them on
            bool pranks = v.Features.PranksEnabled && v.Pranks != null;
            if (_tabs[(int)Tab.Pranks].IsShown() != pranks) _tabs[(int)Tab.Pranks].Show(pranks);
            if (!pranks && _tab == Tab.Pranks && _open) SetTab(Tab.Actions);
        }

        void SetBadge(Tab tab, string text)
        {
            var b = _badges[(int)tab];
            bool show = !string.IsNullOrEmpty(text);
            if (b.IsShown() != show) { b.Show(show); if (show) UiMotion.Pulse(b, 0.3f, 0.3f); }
            if (show && b.text != text) b.text = text;
        }

        // ------------------------------------------------------------------ tabs

        void Build(VisualElement p, LineClient lc, LineView v, double now)
        {
            switch (_tab)
            {
                case Tab.Actions: BuildActions(p, lc, v, now); break;
                case Tab.Pranks: _ui.Pranks.BuildTab(p, lc, v, now); break;
                case Tab.Chat: BuildChat(p, lc); break;
                case Tab.Transfer: PlayBlocks.BuildTransfer(p, lc, v, _st, false); break;
                case Tab.Dossier:
                    if (v.Phase == LinePhase.Results) MeetingReveal.Build(p, lc, v, _revealTab, t => { _revealTab = t; _dirty = true; _scroll.scrollOffset = Vector2.zero; });
                    else BuildDossier(p, lc, v, now);
                    break;
                case Tab.Rules: BuildRules(p); break;
            }
        }

        /// <summary>Soruştur and Kaydı sızdır: your own moves in this meeting, with the rules and the reasons when closed.</summary>
        static void BuildActions(VisualElement p, LineClient lc, LineView v, double now)
        {
            var m = v.Mine;
            var c = lc.Config;
            var f = v.Features;
            // ---- investigation (meeting 1)
            var inv = Box("pl-focus", "mt-card").AddTo(p);
            Bold(Loc.Upper(T("investigate.title")), "pl-focus-kicker").AddTo(inv);
            bool canInvestigate = f.InvestigationOpen && !m.InvestigationUsed && m.InvestigationTargets.Count > 0;
            if (canInvestigate)
            {
                Big(inv, T("meeting.folder.investigate_how"));
                var row = Wrap(inv);
                foreach (int t in m.InvestigationTargets)
                {
                    int id = t;
                    var b = Btn(F("investigate.btn", Name(id)), () => Send(LineVerb.Investigate, id), "pl-danger", "pl-big").AddTo(row);
                    b.SetEnabled(m.Prim >= c.InvestigationCost);
                }
                Line(inv, F("investigate.rules", c.InvestigationGuiltyBlame), "pl-dim");
                if (m.Prim < c.InvestigationCost) Line(inv, F("investigate.poor", m.Prim), "pl-bad");
                inv.AddToClassList("pl-focus-todo");
            }
            else if (m.InvestigationUsed) Line(inv, T("investigate.used"), "pl-dim");
            else if (f.InvestigationOpen) Line(inv, T("investigate.none"), "pl-dim");
            else Line(inv, T(v.Day == 0 && v.Phase < LinePhase.MeetingVote ? "investigate.none" : "meeting.folder.investigate_closed"), "pl-dim");

            // ---- leaks (meeting discussion and the final discussion)
            var leak = Box("pl-focus", "mt-card").AddTo(p);
            Bold(T("meeting.folder.leak_title"), "pl-focus-kicker").AddTo(leak);
            if (!f.RecordingsEnabled) { Line(leak, T("office.rec_disabled"), "pl-dim"); return; }
            if (m.Recorded.Count == 0) { Line(leak, T("leak.none"), "pl-dim"); return; }
            Line(leak, T(f.LeakOpen ? "meeting.folder.leak_how" : "leak.mine_closed"), f.LeakOpen ? null : "pl-dim");
            foreach (var r in m.Recorded)
            {
                var row = Box("pl-row", "mt-rec").AddTo(leak);
                string other = Name(r.Other);
                double start = r.Start;
                Live(row, "rec" + r.CallId + ":" + r.Leaked, now, t => F("leak.recorded", other, UiKit.Clock(Math.Max(0, t - start))) + (r.Leaked ? " · " + T("leak.leaked") : ""), "grow", r.Leaked ? "pl-dim" : null);
                if (!r.Leaked && f.LeakOpen)
                {
                    int callId = r.CallId;
                    Btn(F("focus.leak_btn", other), () => Send(LineVerb.Leak, a: callId), "pl-danger").AddTo(row);
                }
            }
            if (f.LeakOpen && PlayBlocks.LeakableCount(v) > 0) leak.AddToClassList("pl-focus-todo");
        }

        /// <summary>Meeting chat and private texts in one thread: yours on the right, the meeting chat marked "herkese".</summary>
        static void BuildChat(VisualElement p, LineClient lc)
        {
            var msgs = lc.Messages;
            if (msgs.Count == 0) { Line(p, T("msg.empty"), "pl-dim"); return; }
            int me = lc.MySlot;
            for (int i = Math.Max(0, msgs.Count - 40); i < msgs.Count; i++)
            {
                var m = msgs[i];
                bool mine = m.From == me;
                bool chat = m.Kind == MessageKind.MeetingChat;
                var row = Box("os-msg-row", mine ? "os-msg-row-out" : null).AddTo(p);
                var bubble = Box("os-msg", mine ? "os-msg-out" : m.Kind == MessageKind.System ? "os-msg-sys" : "os-msg-in", chat ? "mt-msg-chat" : null).AddTo(row);
                string who = m.Kind == MessageKind.System ? T("msg.system")
                    : chat ? F(mine ? "meeting.folder.chat_mine" : "meeting.folder.chat_from", Name(m.From))
                    : mine ? F("msg.text_to", Name(m.To)) : F("meeting.folder.text_from", Name(m.From));
                var head = Bold(who, "os-msg-who").AddTo(bubble);
                if (!mine && m.Kind != MessageKind.System) head.style.color = PlayerColor(lc.ColorOf(m.From)) * 0.8f + new Color(0f, 0f, 0f, 0.2f);
                Text(BotSpeech.MessageText(m), "os-msg-text").AddTo(bubble);
            }
        }

        static void BuildDossier(VisualElement p, LineClient lc, LineView v, double now)
        {
            bool any = false;
            if (v.Final != null) { PlayBlocks.BuildFinalInfo(p, lc, v); any = true; }
            if (v.Meeting != null) { PlayBlocks.BuildMeetingInfo(p, lc, v, now); any = true; }
            if (!any) Line(p, T("common.none"), "pl-dim");
        }

        void BuildRules(VisualElement p)
        {
            var cols = Box("os-rules").AddTo(p);
            var list = Box("os-rules-list").AddTo(cols);
            for (int i = 0; i < HowToPlayScreen.Pages; i++)
            {
                int page = i;
                Btn(HowToPlayScreen.PageTitle(i), () => { _rulesPage = page; _dirty = true; _scroll.scrollOffset = Vector2.zero; }, "os-rules-item", i == _rulesPage ? "pl-on" : null).AddTo(list);
            }
            var text = Box("os-rules-text").AddTo(cols);
            Title(HowToPlayScreen.PageTitle(_rulesPage), "os-rules-title").AddTo(text);
            Text(HowToPlayScreen.PageBody(_rulesPage), "os-rules-body").AddTo(text);
        }

        /// <summary>Open the rules tab on a page.</summary>
        public void ShowRules(int page)
        {
            _rulesPage = Mathf.Clamp(page, 0, HowToPlayScreen.Pages - 1);
            SetTab(Tab.Rules);
        }
    }
}
