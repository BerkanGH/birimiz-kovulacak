using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>Centered dialog on a dim layer. Esc closes the top-most one (UIRoot).</summary>
    public abstract class Modal
    {
        static readonly List<Modal> _open = new List<Modal>();
        public static bool AnyOpen => _open.Count > 0;

        protected readonly VisualElement Layer;
        protected readonly VisualElement Panel;
        public bool Visible { get; private set; }

        protected Modal(VisualElement parent, string panelKind = "panel")
        {
            Layer = Box("modal-layer").AddTo(parent);
            Layer.style.display = DisplayStyle.None;
            Panel = UiKit.Panel(panelKind, "modal").AddTo(Layer);
            Layer.RegisterCallback<PointerDownEvent>(e => { if (e.target == Layer) Close(); });
        }

        protected void OpenModal()
        {
            if (!Visible) _open.Add(this);
            Visible = true;
            Layer.Show(true);
            Layer.BringToFront();
            Pop(Panel);
        }

        public virtual void Close()
        {
            if (!Visible) return;
            Visible = false;
            _open.Remove(this);
            Layer.Show(false);
        }

        /// <summary>Closes the top-most modal; false if none was open.</summary>
        public static bool CloseTop()
        {
            if (_open.Count == 0) return false;
            _open[_open.Count - 1].Close();
            return true;
        }

        public static void CloseAll()
        {
            for (int i = _open.Count - 1; i >= 0; i--) _open[i].Close();
        }

        public virtual void Tick() { }
    }

    /// <summary>Fake email template picker (card FakeEmail): opened for an affordance with IntParam -1.</summary>
    public sealed class EmailPicker : Modal
    {
        static EmailPicker _i;
        readonly Label _from;
        readonly VisualElement _grid;
        string _target = "";
        int _matchId = -1;

        public EmailPicker(VisualElement parent) : base(parent)
        {
            _i = this;
            Panel.style.width = 1080;
            var head = Box("modal-title-row").AddTo(Panel);
            Icon("sab_fake_email", "icon-l").AddTo(head);
            var hc = Box("col", "grow").AddTo(head);
            Title(Loc.T("email.picker.title"), "h2").AddTo(hc);
            _from = Text("", "t-muted").AddTo(hc);
            _grid = Box("email-grid").AddTo(Panel);
            var actions = Box("modal-actions").AddTo(Panel);
            Text(Loc.T("email.picker.hint"), "t-small", "t-muted", "grow").AddTo(actions);
            Btn(Loc.T("common.cancel"), Close, "btn-ghost").AddTo(actions);
        }

        public static void Open(string pcTargetId)
        {
            if (_i == null) return;
            _i.Show(pcTargetId);
        }

        void Show(string target)
        {
            var v = ClientState.I != null ? ClientState.I.View : null;
            if (v == null) return;
            _target = target ?? "";
            _matchId = v.MatchId;
            int desk = EntityIds.DeskIndexOf(_target);
            int owner = PlayerIds.None;
            foreach (var d in v.Desks) if (d.Index == desk) owner = d.AssignedOwner;
            _from.text = Loc.F("email.picker.from", Loc.PlayerName(v, owner), Loc.Desk(desk));
            _grid.Clear();
            int count = ClientState.I.Config != null && ClientState.I.Config.Email != null ? ClientState.I.Config.Email.TemplateCount : 10;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var b = Btn("", () => Pick(index), "email-option");
                b.RemoveFromClassList("btn-secondary");
                Bold(Loc.F("email.picker.num", (i + 1) % 10), "email-option-num").NoPick().AddTo(b);
                Bold(Describe.EmailSubject(i, v, owner), "email-option-subject").NoPick().AddTo(b);
                Text(Describe.EmailBody(i, v, owner), "email-option-body").NoPick().AddTo(b);
                b.AddTo(_grid);
            }
            OpenModal();
        }

        void Pick(int index)
        {
            var v = ClientState.I.View;
            if (v != null && v.MatchId == _matchId && !string.IsNullOrEmpty(_target))
                ClientState.I.Send(ActionVerb.SendFakeEmail, _target, index);
            Close();
        }

        public override void Tick()
        {
            if (!Visible) return;
            var v = ClientState.I != null ? ClientState.I.View : null;
            if (v == null || v.MatchId != _matchId || !LocalPlayerController.OfficePhase(v.Phase)) { Close(); return; }
            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < 10; i++)
            {
                var key = i == 9 ? kb.digit0Key : kb[Key.Digit1 + i];
                if (key != null && key.wasPressedThisFrame) { Pick(i); return; }
            }
        }
    }

    /// <summary>Yes/no confirmation (accusation, leaving, kicking a bot...).</summary>
    public sealed class ConfirmDialog : Modal
    {
        static ConfirmDialog _i;
        readonly Label _title, _text;
        readonly Button _ok;
        Action _onOk;

        public ConfirmDialog(VisualElement parent) : base(parent)
        {
            _i = this;
            Panel.style.minWidth = 560;
            _title = Title("", "h2").AddTo(Panel);
            _text = Text("", "confirm-text").AddTo(Panel);
            var actions = Box("modal-actions").AddTo(Panel);
            Btn(Loc.T("common.cancel"), Close, "btn-ghost").AddTo(actions);
            _ok = Btn("", () => { var a = _onOk; Close(); a?.Invoke(); }, "btn-primary").AddTo(actions);
        }

        public static void Show(string title, string text, string okText, Action onOk, bool danger = false)
        {
            if (_i == null) { onOk?.Invoke(); return; }
            _i._title.text = title;
            _i._text.text = text;
            _i._ok.text = okText;
            _i._ok.EnableInClassList("btn-danger", danger);
            _i._ok.EnableInClassList("btn-primary", !danger);
            _i._onOk = onOk;
            _i.OpenModal();
        }

        public override void Close()
        {
            _onOk = null;
            base.Close();
        }

        public override void Tick()
        {
            if (!Visible) return;
            if (UiInput.Confirm != null && UiInput.Confirm.WasPressedThisFrame()) { var a = _onOk; Close(); a?.Invoke(); }
        }
    }
}
