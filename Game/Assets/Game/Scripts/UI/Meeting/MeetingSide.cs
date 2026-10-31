using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Right-hand drawer of the meeting screen view: the public meeting chat (a complete alternative to voice) and the
    /// evidence picker (present one of my authenticated records, Features.PresentationOpen / Mine.PresentationsLeft).
    /// Everything player-authored is shown in labels with rich text off (UiKit.Text).
    /// </summary>
    public sealed class MeetingSide : MeetingPanel
    {
        const int MaxLines = 60;

        readonly VisualElement _tabs, _chatPage, _recPage, _inputRow, _recList;
        readonly ScrollView _chatScroll;
        readonly Button _tabChat, _tabRec, _send;
        readonly TextField _input;
        readonly Label _counter, _status, _error, _recInfo, _recEmpty;
        int _tab; // 0 chat, 1 records
        int _processed, _messagesWeek = -1, _lines;
        int _recKey = int.MinValue, _shownCooldown = -2, _shownLeft = -2;
        bool _hooked, _look;
        float _errorUntil;
        int _pendingRecord = int.MinValue;
        float _pendingUntil;

        public MeetingSide(VisualElement parent) : base(parent, "mt-side")
        {
            Root.pickingMode = PickingMode.Position;
            var card = Panel("panel", "side-card").AddTo(Root);
            _tabs = Box("side-tabs").AddTo(card);
            _tabChat = Btn(Loc.T("chat.tab"), () => SetTab(0), "btn-small", "side-tab").AddTo(_tabs);
            _tabRec = Btn(Loc.T("present.tab"), () => SetTab(1), "btn-small", "side-tab").AddTo(_tabs);

            // ---- chat
            _chatPage = Box("side-page").AddTo(card);
            Memo(Loc.T("chat.public_note"), "side-note").NoPick().AddTo(_chatPage);
            _chatScroll = new ScrollView().Cls("side-chat").AddTo(_chatPage);
            _status = Memo("", "side-status").NoPick().AddTo(_chatPage);
            _inputRow = Box("side-input").AddTo(_chatPage);
            _input = new TextField { maxLength = LineConfig.CreateDefault().MeetingChatMaxChars, isDelayed = false }.Cls("field", "grow", "side-field").AddTo(_inputRow);
            _input.RegisterValueChangedCallback(_ => UpdateCounter());
            _input.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.StopPropagation(); Send(); } }, TrickleDown.TrickleDown);
            _send = Btn(Loc.T("chat.send"), Send, "btn-small", "btn-primary").AddTo(_inputRow);
            var cr = Box("side-count-row").NoPick().AddTo(_chatPage);
            _counter = Memo("", "side-counter").NoPick().AddTo(cr);
            _error = Bold("", "side-error").NoPick().AddTo(_chatPage);

            // ---- records (evidence)
            _recPage = Box("side-page").AddTo(card);
            _recInfo = Bold("", "side-rec-info").NoPick().AddTo(_recPage);
            Memo(Loc.T("present.pick_note"), "side-note").NoPick().AddTo(_recPage);
            var sv = new ScrollView().Cls("side-rec-scroll").AddTo(_recPage);
            _recList = sv.contentContainer;
            _recEmpty = Text(Loc.T("present.none"), "empty").NoPick().AddTo(_recPage);

            Root.RegisterCallback<DetachFromPanelEvent>(_ => Unhook());
        }

        void Hook()
        {
            if (_hooked || LineClient.I == null) return;
            _hooked = true;
            LineClient.I.ResultReceived += OnResult;
        }

        void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            if (LineClient.I != null) LineClient.I.ResultReceived -= OnResult;
        }

        void SetTab(int t)
        {
            _tab = t;
            ApplyTab();
        }

        void ApplyTab()
        {
            int t = _look ? 0 : _tab;
            _chatPage.Show(t == 0);
            _recPage.Show(t == 1);
            _tabChat.EnableInClassList("tab-active", t == 0);
            _tabRec.EnableInClassList("tab-active", t == 1);
        }

        /// <summary>Screen view = full drawer; look view (locked cursor) = read-only latest chat lines.</summary>
        public void SetLook(bool look)
        {
            if (look == _look) return;
            _look = look;
            Root.EnableInClassList("mt-side-look", look);
            _tabs.Show(!look);
            _inputRow.Show(!look);
            ApplyTab();
            if (look) _input.Blur();
        }

        protected override void OnShow(LineView v)
        {
            _processed = 0;
            _messagesWeek = -1;
            _lines = 0;
            _chatScroll.contentContainer.Clear();
            _input.SetValueWithoutNotify("");
            _error.text = "";
            _recKey = int.MinValue;
            _tab = 0;
            ApplyTab();
            Hook();
            Refresh(v);
            PullMessages(v);
            UiMotion.SlideIn(Root, new Vector2(60f, 0f), 0.3f, 0.1f);
        }

        protected override void OnHide() { _input.Blur(); }

        public bool InputFocused => Visible && _input.focusController != null && _input.focusController.focusedElement is VisualElement f && (f == _input || _input.Contains(f));

        // ------------------------------------------------------------------ chat

        void PullMessages(LineView v)
        {
            var c = LineClient.I;
            if (c == null) return;
            var all = c.Messages;
            if (v.WeekId != _messagesWeek || all.Count < _processed) { _messagesWeek = v.WeekId; _processed = 0; _lines = 0; _chatScroll.contentContainer.Clear(); }
            bool added = false;
            for (; _processed < all.Count; _processed++)
            {
                var m = all[_processed];
                if (m.Kind != MessageKind.MeetingChat) continue;
                AddLine(m, v);
                added = true;
            }
            if (added) _chatScroll.schedule.Execute(() => _chatScroll.scrollOffset = new Vector2(0f, float.MaxValue)).StartingIn(30);
        }

        void AddLine(LineMessage m, LineView v)
        {
            var host = _chatScroll.contentContainer;
            var row = Box("chat-line").NoPick().AddTo(host);
            if (m.From == v.Me) row.AddToClassList("chat-me");
            Badge(m.From, "badge-xs").AddTo(row);
            var col = Box("col", "grow").NoPick().AddTo(row);
            Bold(MeetingText.Name(m.From), "chat-name").NoPick().AddTo(col);
            Text(m.Text ?? "", "chat-text").NoPick().AddTo(col); // player text: rich text is off in UiKit.Text
            _lines++;
            if (_lines > MaxLines && host.childCount > 0) { host.RemoveAt(0); _lines--; }
        }

        void UpdateCounter()
        {
            int max = _input.maxLength > 0 ? _input.maxLength : LineConfig.CreateDefault().MeetingChatMaxChars;
            _counter.text = Loc.F("chat.chars", (_input.value ?? "").Length, max);
        }

        void Send()
        {
            var c = LineClient.I;
            if (c == null || c.View == null) return;
            string t = (_input.value ?? "").Trim();
            if (t.Length == 0) { ShowError(Loc.T("chat.fail.EmptyText")); return; }
            if (!c.View.Features.MeetingChatOpen) { ShowError(Loc.T("chat.fail.WrongPhase")); return; }
            c.Send(LineVerb.SendMeetingChat, LineIds.None, -1, -1, -1, t);
            _send.SetEnabled(false);
        }

        void OnResult(LineActionResult r, LineAction req)
        {
            if (req == null) return;
            _shownLeft = -2; // re-evaluate the send button
            if (req.Verb == LineVerb.SendMeetingChat)
            {
                if (r.Accepted) { _input.SetValueWithoutNotify(""); UpdateCounter(); _error.text = ""; }
                else ShowError(Loc.TOr("chat.fail." + r.Failure, "chat.fail.generic"));
            }
            else if (req.Verb == LineVerb.PresentRecord)
            {
                _pendingRecord = int.MinValue;
                if (!r.Accepted) ShowError(Loc.TOr("present.fail." + r.Failure, "present.fail.generic"));
                else { _error.text = ""; _recKey = int.MinValue; }
            }
        }

        void ShowError(string s)
        {
            _error.text = s;
            _errorUntil = Time.unscaledTime + 4f;
            UiMotion.Shake(_error, 5f, 0.25f);
        }

        // ------------------------------------------------------------------ records

        void Present(LineRecordView r)
        {
            var c = LineClient.I;
            if (c == null || c.View == null || _pendingRecord != int.MinValue) return;
            _pendingRecord = r.Id;
            _pendingUntil = Time.unscaledTime + 3f;
            c.Send(LineVerb.PresentRecord, LineIds.None, r.Id);
            Sfx.Play("click", 0.6f);
            _recKey = int.MinValue;
        }

        int RecordKey(LineView v)
        {
            int k = v.Mine.Records.Count * 31 + v.Mine.PresentationsLeft * 7 + (v.Features.PresentationOpen ? 1 : 0) + (_pendingRecord != int.MinValue ? 99 : 0);
            foreach (var r in v.Mine.Records) k = k * 17 + r.Id + (r.Presented ? 3 : 0);
            return k;
        }

        void BuildRecords(LineView v)
        {
            _recList.Clear();
            bool open = v.Features.PresentationOpen;
            int left = v.Mine.PresentationsLeft;
            _recInfo.text = open ? Loc.F("present.left", left) : Loc.T("present.closed");
            bool can = open && left > 0 && _pendingRecord == int.MinValue;
            var recs = v.Mine.Records;
            _recEmpty.Show(recs.Count == 0);
            for (int i = recs.Count - 1; i >= 0; i--)
            {
                var r = recs[i];
                RecordCards.PickRow(_recList, r, v, () => Present(r), can);
            }
        }

        // ------------------------------------------------------------------ view

        public override void Refresh(LineView v)
        {
            if (!Visible) return;
            bool chatOpen = v.Features.MeetingChatOpen;
            _input.SetEnabled(chatOpen);
            if (_input.maxLength != MeetingText.Config.MeetingChatMaxChars) _input.maxLength = MeetingText.Config.MeetingChatMaxChars;
            UpdateCounter();
            _tabRec.Show(v.Features.PresentationOpen || v.Mine.Records.Count > 0);
            int k = RecordKey(v);
            if (k != _recKey) { _recKey = k; BuildRecords(v); }
            PullMessages(v);
            UpdateRate(v, LineClient.I != null ? LineClient.I.Now : 0.0, true);
        }

        void UpdateRate(LineView v, double now, bool force)
        {
            int left = v.Mine.MeetingChatLeft;
            int cool = (int)System.Math.Ceiling(System.Math.Max(0.0, v.Mine.NextMessageAt - now));
            if (!force && cool == _shownCooldown && left == _shownLeft) return;
            _shownCooldown = cool; _shownLeft = left;
            bool chatOpen = v.Features.MeetingChatOpen;
            _status.text = !chatOpen ? Loc.T("chat.closed") : left <= 0 ? Loc.T("chat.none_left") : cool > 0 ? Loc.F("chat.wait", cool, left) : Loc.F("chat.left", left);
            _send.SetEnabled(chatOpen && left > 0 && cool <= 0);
        }

        public override void Tick(LineView v, double now)
        {
            if (!Visible) return;
            Hook();
            UpdateRate(v, now, false);
            if (_error.text.Length > 0 && Time.unscaledTime > _errorUntil) _error.text = "";
            if (_pendingRecord != int.MinValue && Time.unscaledTime > _pendingUntil) { _pendingRecord = int.MinValue; _recKey = int.MinValue; }
            if (LineClient.I != null && LineClient.I.Messages.Count != _processed) PullMessages(v);
        }
    }
}
