using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>Shared pieces of the three communication apps (text is a complete alternative to voice).</summary>
    static class Comms
    {
        public static double RateLeft(LineView v) { return Math.Max(0.0, v.Mine.NextMessageAt - Dk.Now); }

        public static bool IsMine(LineMessage m, int me) { return m.From == me; }

        public static bool ForMe(LineMessage m, int me) { return m.ToAll ? m.From != me : m.To == me; }

        public static string Snippet(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\n', ' ').Trim();
            return s.Length > n ? s.Substring(0, n - 1) + "..." : s;
        }

        /// <summary>Enter sends (the multi-line field would otherwise add a line break); Shift+Enter keeps the break.</summary>
        public static void EnterSends(Dk.TextBox box, Action send)
        {
            box.Field.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !e.shiftKey)
                {
                    e.StopImmediatePropagation();
                    e.PreventDefault();
                    send();
                }
            }, TrickleDown.TrickleDown);
        }

        public static void ScrollEnd(ScrollView sv)
        {
            sv.schedule.Execute(() => sv.verticalScroller.value = sv.verticalScroller.highValue).StartingIn(30);
        }

        /// <summary>Why the send button is off, or "" when sending is possible.</summary>
        public static string Blocker(LineView v, int left, string leftKey)
        {
            if (Dk.ComputerBlocked(v)) return Dk.T("desk.msg.blocked");
            if (left <= 0) return Dk.T(leftKey);
            double r = RateLeft(v);
            if (r > 0.05) return Dk.F("desk.msg.rate", Mathf.CeilToInt((float)r));
            return "";
        }
    }

    // =================================================================================================================
    // Messages: private text threads
    // =================================================================================================================

    public sealed class DeskMessagesApp : DeskAppView
    {
        public DeskMessagesApp(DeskUi ui) : base(ui, "dk-app-msg") { Build(); }
        public override DeskApp Id => DeskApp.Messages;
        public override string Icon => "message_app";

        VisualElement _list, _convoHead, _composer;
        ScrollView _convo, _listScroll;
        Dk.TextBox _box;
        Button _send;
        Label _status, _left;
        int _sig = int.MinValue, _lastRate = -1;
        string _err = "";
        readonly List<int> _order = new List<int>();

        public override string Subtitle(LineView v) { return Dk.F("desk.msg.left", v.Mine.TextsLeftThisBlock); }

        int Me => Dk.Me;

        void Build()
        {
            var row = Box("dk-msg-row").AddTo(Root);
            _listScroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-msg-list").AddTo(row);
            _list = _listScroll.contentContainer;
            var right = Box("dk-msg-right").AddTo(row);
            _convoHead = Box("dk-msg-head").AddTo(right);
            _convo = new ScrollView(ScrollViewMode.Vertical).Cls("dk-msg-convo").AddTo(right);
            _composer = Box("dk-composer").AddTo(right);
            _box = Dk.TextInput(_composer, Dk.Config.TextMaxChars, true, Dk.T("desk.msg.placeholder"), "dk-composer-box");
            _box.Changed = UpdateSend;
            var foot = Box("dk-composer-foot").AddTo(_composer);
            _status = Text("", "dk-composer-status").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _send = Btn(Dk.T("desk.msg.send"), Send, "btn-primary", "dk-composer-send").AddTo(foot);
            Comms.EnterSends(_box, Send);
        }

        internal void MockType(string t) { _box.SetValue(t); UpdateSend(); }

        public int Unread(LineView v)
        {
            var c = LineClient.I;
            if (c == null) return 0;
            int n = 0;
            var msgs = c.Messages;
            for (int i = 0; i < msgs.Count; i++)
            {
                var m = msgs[i];
                if (m.Kind == MessageKind.Text && m.From != v.Me && !Ui.State.ReadIds.Contains(m.Id)) n++;
            }
            return n;
        }

        public override int Badge(LineView v) { return Unread(v); }

        int UnreadOf(int who)
        {
            int n = 0;
            var msgs = LineClient.I.Messages;
            for (int i = 0; i < msgs.Count; i++)
                if (msgs[i].Kind == MessageKind.Text && msgs[i].From == who && !Ui.State.ReadIds.Contains(msgs[i].Id)) n++;
            return n;
        }

        /// <summary>Opens a colleague's thread (and optionally focuses the input).</summary>
        public void Select(int who, bool focus)
        {
            var v = Dk.View;
            if (v == null) return;
            if (Ui.State.MsgThread != who) { _err = ""; _box.Clear(); }
            Ui.State.MsgThread = who;
            int unread = who >= 0 ? UnreadOf(who) : 0;
            if (who >= 0) TelemetryHooks.MessageThreadOpened(MessageKind.Text, unread);
            _sig = int.MinValue;
            Refresh(v, true);
            if (focus) _box.Field.schedule.Execute(() => _box.Focus()).StartingIn(80);
        }

        public override void OnShown()
        {
            var v = Dk.View;
            if (v == null) return;
            var st = Ui.State;
            if (st.MsgThread < 0 || Dk.Player(st.MsgThread) == null || st.MsgThread == v.Me) st.MsgThread = FirstThread(v);
            _sig = int.MinValue;
            Refresh(v, true);
        }

        int FirstThread(LineView v)
        {
            // newest unread thread, else the newest thread, else the first colleague
            var msgs = LineClient.I.Messages;
            int best = LineIds.None; double bestT = -1; bool bestUnread = false;
            for (int i = 0; i < msgs.Count; i++)
            {
                var m = msgs[i];
                if (m.Kind != MessageKind.Text) continue;
                int who = Dk.ThreadPartner(m, v.Me);
                if (who < 0 || who == v.Me) continue;
                bool unread = m.From != v.Me && !Ui.State.ReadIds.Contains(m.Id);
                if ((unread && !bestUnread) || (unread == bestUnread && m.Time > bestT)) { best = who; bestT = m.Time; bestUnread = unread; }
            }
            if (best >= 0) return best;
            for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Id != v.Me) return v.Players[i].Id;
            return LineIds.None;
        }

        public override void Refresh(LineView v, bool force)
        {
            var c = LineClient.I;
            if (c == null) return;
            var st = Ui.State;
            if (st.MsgThread < 0 || Dk.Player(st.MsgThread) == null || st.MsgThread == v.Me) st.MsgThread = FirstThread(v);
            // reading = visible thread: mark it read
            var msgs = c.Messages;
            bool markedAny = false;
            for (int i = 0; i < msgs.Count; i++)
                if (msgs[i].Kind == MessageKind.Text && msgs[i].From == st.MsgThread && st.ReadIds.Add(msgs[i].Id)) markedAny = true;
            int sig = msgs.Count * 31 + (int)c.LastMessageSeq;
            sig = sig * 31 + st.MsgThread + (markedAny ? 1 : 0) + st.ReadIds.Count * 7;
            for (int i = 0; i < v.Players.Count; i++) sig = sig * 3 + (v.Players[i].Connected ? 1 : 0);
            sig = sig * 31 + (Dk.ComputerBlocked(v) ? 1 : 0);
            if (!force && sig == _sig) return;
            _sig = sig;
            BuildList(v);
            BuildConvo(v);
            UpdateSend();
        }

        void BuildList(LineView v)
        {
            _list.Clear();
            _order.Clear();
            var msgs = LineClient.I.Messages;
            var last = new Dictionary<int, LineMessage>();
            for (int i = 0; i < msgs.Count; i++)
            {
                var m = msgs[i];
                if (m.Kind != MessageKind.Text) continue;
                int who = Dk.ThreadPartner(m, v.Me);
                if (who >= 0) last[who] = m;
            }
            for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Id != v.Me) _order.Add(v.Players[i].Id);
            _order.Sort((a, b) =>
            {
                LineMessage ma, mb;
                double ta = last.TryGetValue(a, out ma) ? ma.Time : -1, tb = last.TryGetValue(b, out mb) ? mb.Time : -1;
                int c = tb.CompareTo(ta);
                return c != 0 ? c : a.CompareTo(b);
            });
            Dk.Heading(_list, Dk.T("desk.msg.threads"));
            for (int i = 0; i < _order.Count; i++)
            {
                int who = _order[i];
                var p = Dk.Player(who);
                var row = Dk.Tile(() => Select(who, true), "dk-thread").AddTo(_list);
                row.EnableInClassList("dk-thread-on", who == Ui.State.MsgThread);
                Dk.Badge(who, "badge-s").AddTo(row);
                var col = Box("dk-thread-col").NoPick().AddTo(row);
                Bold(p != null ? p.Name : Dk.Name(who), "dk-thread-name").Plain().NoPick().AddTo(col);
                LineMessage lm;
                string prev = last.TryGetValue(who, out lm) ? (lm.From == v.Me ? Dk.T("desk.msg.you") + " " : "") + Comms.Snippet(lm.Text, 34) : Dk.T("desk.msg.no_texts");
                Text(prev, "dk-thread-prev").Plain().NoPick().AddTo(col);
                int unread = UnreadOf(who);
                if (unread > 0 && who != Ui.State.MsgThread) Bold(Num(unread), "dk-badge").NoPick().AddTo(row);
                if (p != null && !p.Connected) Tag(Dk.T("desk.away"), "tag-grey").AddTo(row);
            }
            Dk.NoRich(_list);
        }

        void BuildConvo(LineView v)
        {
            int who = Ui.State.MsgThread;
            _convoHead.Clear();
            var cc = _convo.contentContainer;
            cc.Clear();
            if (who < 0) { Text(Dk.T("desk.msg.pick"), "dk-empty").AddTo(cc); _composer.Show(false); return; }
            _composer.Show(true);
            var p = Dk.Player(who);
            Dk.Badge(who, "badge-m").AddTo(_convoHead);
            var hc = Box("dk-msg-head-col").NoPick().AddTo(_convoHead);
            Title(p != null ? p.Name : Dk.Name(who), "dk-msg-head-name").Plain().NoPick().AddTo(hc);
            Text(Dk.T("desk.msg.private"), "dk-msg-head-sub").NoPick().AddTo(hc);
            Box("spacer").NoPick().AddTo(_convoHead);
            _left = Bold(Dk.F("desk.msg.left", v.Mine.TextsLeftThisBlock), "dk-msg-left").NoPick().AddTo(_convoHead);
            var callBtn = IconBtn("call", Dk.T("desk.task.call"), () => Ui.Call(who), "btn-secondary", "btn-small").AddTo(_convoHead);
            callBtn.SetEnabled(v.Mine.Phone.Status == CallStatus.Idle && !Dk.PhoneDead(v) && p != null && p.Connected);

            var msgs = LineClient.I.Messages;
            int shown = 0;
            for (int i = 0; i < msgs.Count; i++)
            {
                var m = msgs[i];
                if (m.Kind != MessageKind.Text || Dk.ThreadPartner(m, v.Me) != who) continue;
                shown++;
                bool mine = m.From == v.Me;
                var line = Box("dk-bubble-row", mine ? "dk-bubble-row-me" : "dk-bubble-row-them").AddTo(cc);
                var b = Box("dk-msgbubble", mine ? "dk-msgbubble-me" : "dk-msgbubble-them").AddTo(line);
                Text(m.Text, "dk-msgbubble-text").Plain().AddTo(b);
                Text(Dk.Ago(m.Time), "dk-msgbubble-time").NoPick().AddTo(b);
            }
            if (shown == 0) Text(Dk.F("desk.msg.empty", p != null ? p.Name : Dk.Name(who)), "dk-empty").Plain().AddTo(cc);
            Dk.NoRich(_convoHead);
            Comms.ScrollEnd(_convo);
        }

        public override void Tick(LineView v)
        {
            int rate = Mathf.CeilToInt((float)Comms.RateLeft(v));
            if (rate != _lastRate) { _lastRate = rate; UpdateSend(); }
        }

        void UpdateSend()
        {
            var v = Dk.View;
            if (v == null || _send == null) return;
            string blocker = Comms.Blocker(v, v.Mine.TextsLeftThisBlock, "desk.msg.limit");
            bool empty = _box.Value.Trim().Length == 0;
            var p = Dk.Player(Ui.State.MsgThread);
            _send.SetEnabled(blocker.Length == 0 && !empty && p != null);
            _box.SetEnabled(!Dk.ComputerBlocked(v));
            _status.SetText(_err.Length > 0 ? _err : blocker.Length > 0 ? blocker : p != null && !p.Connected ? Dk.T("desk.msg.away") : Dk.F("desk.msg.hint", Dk.Config.TextMaxChars));
            _status.EnableInClassList("dk-composer-status-bad", _err.Length > 0);
            if (_left != null) _left.SetText(Dk.F("desk.msg.left", v.Mine.TextsLeftThisBlock));
        }

        void Send()
        {
            var v = Dk.View;
            if (v == null) return;
            string text = _box.Value.Trim();
            int who = Ui.State.MsgThread;
            if (text.Length == 0 || who < 0 || Comms.Blocker(v, v.Mine.TextsLeftThisBlock, "desk.msg.limit").Length > 0) { Dk.Play("deny"); return; }
            _err = "";
            Dk.Send(LineVerb.SendText, who, text: text);
            Ui.State.ObCalled = true;
            _box.Field.Focus();
        }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.SendText) return false;
            if (r.Accepted) { _box.Clear(); _err = ""; Dk.Play("toast", null, 0.5f); UpdateSend(); return true; }
            _err = Dk.T("desk.fail." + r.Failure);
            UpdateSend();
            return true;
        }
    }

    // =================================================================================================================
    // Mail: inbox with threads, compose to one or everyone, template shortcut, reply inside the thread
    // =================================================================================================================

    public sealed class DeskMailApp : DeskAppView
    {
        enum Mode { None, New, Reply }

        public DeskMailApp(DeskUi ui) : base(ui, "dk-app-mail") { Build(); }
        public override DeskApp Id => DeskApp.Mail;
        public override string Icon => "mail_app";

        VisualElement _list, _readHead, _compose, _tplRow, _toRow, _toLine;
        ScrollView _read, _listScroll;
        Dk.TextBox _box;
        Button _send, _cancel, _newBtn;
        Label _status, _toName;
        Action<int> _toUpdate;
        int _sig = int.MinValue, _lastRate = -1;
        int _thread = -1;
        Mode _mode;
        int _to = LineIds.None;
        bool _toEveryone;
        int _replyId = -1;
        int _tpl = -1;
        string _err = "";
        bool _toBuilt;

        public override string Subtitle(LineView v) { return Dk.F("desk.mail.left", v.Mine.EmailsLeftThisBlock); }

        void Build()
        {
            var row = Box("dk-msg-row").AddTo(Root);
            var left = Box("dk-mail-left").AddTo(row);
            _newBtn = IconBtn("mail_app", Dk.T("desk.mail.new"), StartNew, "btn-primary", "dk-mail-new").AddTo(left);
            _listScroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-msg-list").AddTo(left);
            _list = _listScroll.contentContainer;
            var right = Box("dk-msg-right").AddTo(row);
            _readHead = Box("dk-mail-head").AddTo(right);
            _read = new ScrollView(ScrollViewMode.Vertical).Cls("dk-msg-convo").AddTo(right);
            _compose = Box("dk-composer", "dk-mail-compose").AddTo(right);
            _toLine = Box("dk-mail-to").AddTo(_compose);
            Bold(Dk.T("desk.mail.to"), "dk-mail-to-label").NoPick().AddTo(_toLine);
            _toName = Bold("", "dk-mail-to-name").Plain().NoPick().AddTo(_toLine);
            _toRow = Box("dk-mail-torow").AddTo(_compose);
            _tplRow = Box("dk-mail-tpl").AddTo(_compose);
            Bold(Dk.T("desk.mail.template"), "dk-mail-tpl-label").NoPick().AddTo(_tplRow);
            int n = Mathf.Max(0, Dk.Config.MailTemplates);
            var chips = Box("dk-chips").AddTo(_tplRow);
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var chip = Dk.Tile(() => PickTemplate(idx), "dk-chip", "dk-chip-tpl").AddTo(chips);
                Bold(Dk.T("mail.template." + i + ".title"), "dk-chip-text").NoPick().AddTo(chip);
            }
            _box = Dk.TextInput(_compose, Dk.Config.EmailMaxChars, true, Dk.T("desk.mail.placeholder"), "dk-composer-box", "dk-mail-box");
            _box.Changed = OnTextChanged;
            var foot = Box("dk-composer-foot").AddTo(_compose);
            _status = Text("", "dk-composer-status").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _cancel = Btn(Dk.T("desk.cancel"), Cancel, "btn-ghost").AddTo(foot);
            _send = Btn(Dk.T("desk.mail.send"), Send, "btn-primary", "dk-composer-send").AddTo(foot);
            _compose.Show(false);
        }

        // ---- data

        static List<LineMessage> Inbox(LineView v)
        {
            var res = new List<LineMessage>();
            var msgs = LineClient.I.Messages;
            for (int i = 0; i < msgs.Count; i++)
            {
                var m = msgs[i];
                if (m.Kind == MessageKind.Email || m.Kind == MessageKind.Manager || m.Kind == MessageKind.System) res.Add(m);
            }
            return res;
        }

        public int Unread(LineView v)
        {
            var c = LineClient.I;
            if (c == null) return 0;
            int n = 0;
            var msgs = c.Messages;
            for (int i = 0; i < msgs.Count; i++)
            {
                var m = msgs[i];
                bool mailKind = m.Kind == MessageKind.Email || m.Kind == MessageKind.Manager || m.Kind == MessageKind.System;
                if (mailKind && m.From != v.Me && !Ui.State.ReadIds.Contains(m.Id)) n++;
            }
            return n;
        }

        public override int Badge(LineView v) { return Unread(v); }

        /// <summary>Opens the thread containing this mail id (from a notification).</summary>
        public void Select(int mailId)
        {
            var v = Dk.View;
            if (v == null) return;
            var msgs = LineClient.I.Messages;
            for (int i = 0; i < msgs.Count; i++) if (msgs[i].Id == mailId) { _thread = msgs[i].ThreadId; break; }
            _mode = Mode.None;
            _sig = int.MinValue;
            Refresh(v, true);
        }

        public override void OnShown()
        {
            var v = Dk.View;
            if (v == null) return;
            _sig = int.MinValue;
            Refresh(v, true);
        }

        // ---- refresh

        public override void Refresh(LineView v, bool force)
        {
            var c = LineClient.I;
            if (c == null) return;
            var inbox = Inbox(v);
            if (_thread < 0 && inbox.Count > 0) _thread = NewestThread(inbox);
            bool markedAny = false;
            if (_mode != Mode.New)
                for (int i = 0; i < inbox.Count; i++)
                    if (inbox[i].ThreadId == _thread && st.ReadIds.Add(inbox[i].Id)) markedAny = true;
            int sig = inbox.Count * 31 + (int)c.LastMessageSeq;
            sig = sig * 31 + _thread + (int)_mode * 7 + (markedAny ? 1 : 0) + st.ReadIds.Count * 5 + _replyId;
            sig = sig * 31 + (Dk.ComputerBlocked(v) ? 1 : 0);
            if (!force && sig == _sig) return;
            _sig = sig;
            BuildList(v, inbox);
            BuildRead(v, inbox);
            UpdateSend();
        }

        DeskState st => Ui.State;

        int NewestThread(List<LineMessage> inbox)
        {
            int best = -1; double t = -1;
            for (int i = 0; i < inbox.Count; i++) if (inbox[i].Time >= t) { t = inbox[i].Time; best = inbox[i].ThreadId; }
            return best;
        }

        static string FromName(LineMessage m) { return Dk.Name(m.From); }

        void BuildList(LineView v, List<LineMessage> inbox)
        {
            _list.Clear();
            var threads = new List<int>();
            var lastOf = new Dictionary<int, LineMessage>();
            for (int i = 0; i < inbox.Count; i++)
            {
                var m = inbox[i];
                LineMessage prev;
                if (!lastOf.TryGetValue(m.ThreadId, out prev)) threads.Add(m.ThreadId);
                if (prev == null || m.Time >= prev.Time) lastOf[m.ThreadId] = m;
            }
            threads.Sort((a, b) => lastOf[b].Time.CompareTo(lastOf[a].Time));
            Dk.Heading(_list, Dk.T("desk.mail.inbox"));
            if (threads.Count == 0) Text(Dk.T("desk.mail.empty"), "dk-empty").AddTo(_list);
            for (int i = 0; i < threads.Count; i++)
            {
                int tid = threads[i];
                var m = lastOf[tid];
                int unread = 0, count = 0;
                for (int k = 0; k < inbox.Count; k++)
                    if (inbox[k].ThreadId == tid) { count++; if (inbox[k].From != v.Me && !st.ReadIds.Contains(inbox[k].Id)) unread++; }
                var row = Dk.Tile(() => { _thread = tid; _mode = Mode.None; _sig = int.MinValue; Refresh(Dk.View, true); }, "dk-thread", "dk-mailrow").AddTo(_list);
                row.EnableInClassList("dk-thread-on", tid == _thread && _mode != Mode.New);
                row.EnableInClassList("dk-mailrow-unread", unread > 0);
                Dk.Badge(m.From, "badge-s").AddTo(row);
                var col = Box("dk-thread-col").NoPick().AddTo(row);
                Bold(m.From == v.Me ? Dk.F("desk.mail.to_line", m.ToAll ? Dk.T("desk.mail.everyone") : Dk.Name(m.To)) : FromName(m), "dk-thread-name").Plain().NoPick().AddTo(col);
                Text(Comms.Snippet(m.Text, 38), "dk-thread-prev").Plain().NoPick().AddTo(col);
                if (count > 1) Tag(Num(count), "tag-grey").AddTo(row);
                if (unread > 0) Bold(Num(unread), "dk-badge").NoPick().AddTo(row);
            }
            Dk.NoRich(_list);
        }

        void BuildRead(LineView v, List<LineMessage> inbox)
        {
            _readHead.Clear();
            var cc = _read.contentContainer;
            cc.Clear();
            bool composing = _mode != Mode.None;
            _compose.Show(composing);
            _newBtn.SetEnabled(true);
            _read.Show(_mode != Mode.New);
            _readHead.Show(_mode != Mode.New);
            if (_mode == Mode.New)
            {
                BuildComposeTargets(v);
                return;
            }
            var thread = new List<LineMessage>();
            for (int i = 0; i < inbox.Count; i++) if (inbox[i].ThreadId == _thread) thread.Add(inbox[i]);
            if (thread.Count == 0) { Text(Dk.T("desk.mail.pick"), "dk-empty").AddTo(cc); return; }
            thread.Sort((a, b) => a.Time.CompareTo(b.Time));
            var first = thread[0];
            Title(Comms.Snippet(first.Text, 46), "dk-mail-subject").Plain().NoPick().AddTo(_readHead);
            if (thread.Count > 1) Tag(Dk.F("desk.mail.in_thread", thread.Count), "tag-grey").AddTo(_readHead);
            for (int i = 0; i < thread.Count; i++)
            {
                var m = thread[i];
                bool mine = m.From == v.Me;
                var card = Box("dk-mailcard").AddTo(cc);
                if (mine) card.AddToClassList("dk-mailcard-me");
                if (m.Kind == MessageKind.Manager) card.AddToClassList("dk-mailcard-mgr");
                if (m.Kind == MessageKind.System) card.AddToClassList("dk-mailcard-sys");
                var head = Box("dk-mailcard-head").NoPick().AddTo(card);
                Dk.Badge(m.From, "badge-s").AddTo(head);
                var hc = Box("dk-mailcard-col").NoPick().AddTo(head);
                Bold(mine ? Dk.T("desk.msg.you") : FromName(m), "dk-mailcard-from").Plain().NoPick().AddTo(hc);
                Text(Dk.F("desk.mail.to_line", m.ToAll ? Dk.T("desk.mail.everyone") : Dk.Name(m.To)) + " · " + Dk.Ago(m.Time), "dk-mailcard-meta").Plain().NoPick().AddTo(hc);
                Box("spacer").NoPick().AddTo(head);
                if (m.Kind == MessageKind.Email && !mine)
                {
                    var rid = m;
                    Btn(Dk.T("desk.mail.reply"), () => StartReply(rid), "btn-secondary", "btn-small", "dk-mail-reply").AddTo(head);
                }
                Text(m.Text, "dk-mailcard-text").Plain().AddTo(card);
            }
            if (_mode == Mode.Reply) BuildReplyTarget();
            Dk.NoRich(_readHead);
            Comms.ScrollEnd(_read);
        }

        // ---- compose

        internal void MockReply(string text)
        {
            var v = Dk.View;
            if (v == null) return;
            var inbox = Inbox(v);
            for (int i = inbox.Count - 1; i >= 0; i--)
                if (inbox[i].Kind == MessageKind.Email && inbox[i].From != v.Me) { _thread = inbox[i].ThreadId; StartReply(inbox[i]); _box.SetValue(text); UpdateSend(); return; }
        }

        internal void MockNew(int to, string text)
        {
            StartNew();
            _to = to; _toEveryone = to < 0; _toBuilt = false;
            _sig = int.MinValue;
            Refresh(Dk.View, true);
            _box.SetValue(text);
            UpdateSend();
        }

        void StartNew()
        {
            _mode = Mode.New;
            _to = LineIds.None;
            _toEveryone = false;
            _replyId = -1;
            _tpl = -1;
            _err = "";
            _box.Clear();
            _toBuilt = false;
            _sig = int.MinValue;
            Refresh(Dk.View, true);
        }

        void StartReply(LineMessage m)
        {
            _mode = Mode.Reply;
            _to = m.From;
            _toEveryone = false;
            _replyId = m.Id;
            _tpl = -1;
            _err = "";
            _box.Clear();
            _sig = int.MinValue;
            Refresh(Dk.View, true);
            _box.Field.schedule.Execute(() => _box.Focus()).StartingIn(80); // the reply is editable right away
        }

        void BuildComposeTargets(LineView v)
        {
            _toRow.Clear();
            _toRow.Show(true);
            _toLine.Show(false);
            _toUpdate = Dk.ChipPicker(_toRow, v, id => { _to = id; _toEveryone = id == LineIds.None; _err = ""; _toUpdate(id); UpdateSend(); }, false, null, true);
            _toUpdate(_toEveryone ? LineIds.None : _to);
            _tplRow.Show(true);
            _box.Root.Show(true);
            Dk.NoRich(_toRow);
        }

        void BuildReplyTarget()
        {
            _toRow.Clear();
            _toRow.Show(false);
            _toLine.Show(true);
            _toName.SetText(Dk.F("desk.mail.replying", Dk.Name(_to)));
            _tplRow.Show(true);
            _box.Root.Show(true);
        }

        void PickTemplate(int i)
        {
            _tpl = i;
            _box.SetValue(Dk.T("mail.template." + i));
            _err = "";
            _box.Field.Focus();
            UpdateSend();
        }

        void OnTextChanged()
        {
            if (_tpl >= 0 && _box.Value != Dk.T("mail.template." + _tpl)) _tpl = -1;
            if (_err.Length > 0) _err = "";
            UpdateSend();
        }

        void Cancel()
        {
            _mode = Mode.None;
            _box.Clear();
            _err = "";
            _sig = int.MinValue;
            Refresh(Dk.View, true);
        }

        bool Ready(LineView v, out string why)
        {
            why = Comms.Blocker(v, v.Mine.EmailsLeftThisBlock, "desk.mail.limit");
            if (why.Length > 0) return false;
            if (_mode == Mode.None) return false;
            if (_mode == Mode.New && !_toEveryone && _to < 0) { why = Dk.T("desk.mail.pick_to"); return false; }
            if (_box.Value.Trim().Length == 0) return false;
            return true;
        }

        void UpdateSend()
        {
            var v = Dk.View;
            if (v == null || _send == null) return;
            string why;
            bool ok = Ready(v, out why);
            _send.SetEnabled(ok);
            _status.SetText(_err.Length > 0 ? _err : why.Length > 0 ? why : Dk.F("desk.mail.hint", Dk.Config.EmailMaxChars));
            _status.EnableInClassList("dk-composer-status-bad", _err.Length > 0);
            _box.SetEnabled(!Dk.ComputerBlocked(v));
        }

        public override void Tick(LineView v)
        {
            int rate = Mathf.CeilToInt((float)Comms.RateLeft(v));
            if (rate != _lastRate) { _lastRate = rate; UpdateSend(); }
        }

        void Send()
        {
            var v = Dk.View;
            if (v == null) return;
            string why;
            if (!Ready(v, out why)) { Dk.Play("deny"); return; }
            _err = "";
            Dk.Send(LineVerb.SendEmail, _toEveryone ? LineIds.None : _to, _tpl, -1, _replyId, _box.Value.Trim());
        }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.SendEmail) return false;
            if (r.Accepted)
            {
                _box.Clear(); _err = ""; _tpl = -1; _mode = Mode.None; _replyId = -1;
                Dk.Play("toast", null, 0.5f);
                _sig = int.MinValue;
                var v = Dk.View;
                if (v != null) Refresh(v, true);
                return true;
            }
            _err = Dk.T("desk.fail." + r.Failure);
            UpdateSend();
            return true;
        }

        public override bool Back()
        {
            if (_mode != Mode.None) { Cancel(); return true; }
            return false;
        }
    }

    // =================================================================================================================
    // Complaint: anonymous, unverified
    // =================================================================================================================

    public sealed class DeskComplaintApp : DeskAppView
    {
        public DeskComplaintApp(DeskUi ui) : base(ui, "dk-app-complaint") { Build(); }
        public override DeskApp Id => DeskApp.Complaint;
        public override string Icon => "complaint_app";

        VisualElement _pickHost, _presetHost, _preview;
        readonly List<VisualElement> _presetTiles = new List<VisualElement>();
        Dk.TextBox _box;
        Button _send;
        Label _status, _previewText;
        Action<int> _pickUpdate;
        int _target = LineIds.None, _preset = -1;
        int _sig = int.MinValue;
        string _err = "";

        public override string Subtitle(LineView v) { return Dk.F("desk.complaint.left", v.Mine.ComplaintsLeftThisBlock); }

        void Build()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-complaint-scroll").AddTo(Root);
            var c = scroll.contentContainer;
            var banner = Box("dk-anon").NoPick().AddTo(c);
            Dk.Icon("complaint", "dk-anon-icon").AddTo(banner);
            var bc = Box("dk-anon-col").NoPick().AddTo(banner);
            StampText(Loc.Upper(Dk.T("desk.complaint.band")), "dk-anon-band").NoPick().AddTo(bc);
            Text(Dk.T("desk.complaint.rule"), "dk-anon-text").NoPick().AddTo(bc);
            Dk.Heading(c, Dk.T("desk.complaint.who"));
            _pickHost = Box("dk-complaint-pick").AddTo(c);
            Dk.Heading(c, Dk.T("desk.complaint.what"), Dk.T("desk.complaint.what_hint"));
            _presetHost = Box("dk-presets").AddTo(c);
            Dk.Heading(c, Dk.T("desk.complaint.or"));
            _box = Dk.TextInput(c, Dk.Config.ComplaintMaxChars, true, Dk.T("desk.complaint.placeholder"), "dk-composer-box", "dk-complaint-box");
            _box.Changed = () => { if (_box.Value.Length > 0) _preset = -1; _err = ""; UpdatePresets(); UpdateSend(); };
            _preview = Box("dk-complaint-preview").NoPick().AddTo(c);
            StampText(Loc.Upper(Dk.T("desk.complaint.preview")), "dk-tut-example-band").NoPick().AddTo(_preview);
            _previewText = Hand("", "dk-complaint-preview-text").Plain().NoPick().AddTo(_preview);
            var foot = Box("dk-composer-foot").AddTo(c);
            _status = Text("", "dk-composer-status").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _send = Btn(Dk.T("desk.complaint.send"), Send, "btn-danger", "dk-composer-send").AddTo(foot);
        }

        internal void MockFill(int target, int preset, string text)
        {
            _target = target; _preset = preset;
            _sig = int.MinValue;
            var v = Dk.View;
            if (v != null) Refresh(v, true);
            if (text != null) { _box.SetValue(text); _preset = -1; }
            UpdateSend();
        }

        public override void OnShown() { _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); }

        public override void Refresh(LineView v, bool force)
        {
            int sig = v.Players.Count * 31 + v.Mine.ComplaintsLeftThisBlock + (v.Features.ComplaintsOpen ? 100 : 0) + (Dk.ComputerBlocked(v) ? 1000 : 0);
            if (!force && sig == _sig) { UpdateSend(); return; }
            _sig = sig;
            _pickHost.Clear();
            _pickUpdate = Dk.Picker(_pickHost, v, id => { _target = id; _err = ""; _pickUpdate(id); UpdateSend(); }, false, "badge-m");
            _pickUpdate(_target);
            _presetHost.Clear();
            _presetTiles.Clear();
            int n = Mathf.Max(0, Dk.Config.ComplaintPresets);
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var tile = Dk.Tile(() => { _preset = _preset == idx ? -1 : idx; if (_preset >= 0) _box.Clear(); _err = ""; UpdatePresets(); UpdateSend(); }, "dk-preset").AddTo(_presetHost);
                Box("dk-opt-mark").NoPick().AddTo(tile);
                Text(Dk.T("complaint.preset." + i), "dk-preset-text").NoPick().AddTo(tile);
                _presetTiles.Add(tile);
            }
            UpdatePresets();
            UpdateSend();
        }

        void UpdatePresets()
        {
            for (int i = 0; i < _presetTiles.Count; i++) _presetTiles[i].EnableInClassList("dk-opt-on", i == _preset);
            string t = _preset >= 0 ? Dk.T("complaint.preset." + _preset) : _box.Value.Trim();
            _previewText.SetText(t.Length > 0 ? (_target >= 0 ? Dk.F("desk.complaint.about", Dk.Name(_target)) + " " : "") + t : Dk.T("desk.complaint.preview_none"));
        }

        void UpdateSend()
        {
            var v = Dk.View;
            if (v == null || _send == null) return;
            string why = "";
            if (!v.Features.ComplaintsOpen) why = Dk.T("desk.complaint.closed");
            else if (v.Mine.ComplaintsLeftThisBlock <= 0) why = Dk.T("desk.complaint.limit");
            else if (Dk.ComputerBlocked(v)) why = Dk.T("desk.msg.blocked");
            else if (_target < 0) why = Dk.T("desk.complaint.pick_target");
            else if (_preset < 0 && _box.Value.Trim().Length == 0) why = Dk.T("desk.complaint.pick_text");
            _send.SetEnabled(why.Length == 0);
            _status.SetText(_err.Length > 0 ? _err : why.Length > 0 ? why : Dk.T("desk.complaint.ready"));
            _status.EnableInClassList("dk-composer-status-bad", _err.Length > 0);
            UpdatePresets();
        }

        void Send()
        {
            var v = Dk.View;
            if (v == null || _target < 0) return;
            _err = "";
            if (_preset >= 0) Dk.Send(LineVerb.FileComplaint, _target, _preset);
            else Dk.Send(LineVerb.FileComplaint, _target, -1, -1, -1, _box.Value.Trim());
        }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.FileComplaint) return false;
            if (r.Accepted) { _box.Clear(); _preset = -1; _target = LineIds.None; _err = ""; _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); return false; }
            _err = Dk.T("desk.fail." + r.Failure);
            UpdateSend();
            return true;
        }
    }
}
