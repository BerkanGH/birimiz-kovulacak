using System;
using System.Collections.Generic;
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
