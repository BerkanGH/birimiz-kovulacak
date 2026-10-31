using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The 5-step visual tutorial of a task (Goal, Your information, Who to contact, What to do, Consequences): one short
    /// sentence, an example and a highlighted area of the task app per step. It is a docked column next to the task
    /// (never covers the screen, never pauses anything), opens automatically for a first-time task and can be reopened
    /// from the "?" button. The illustration is Resources "ui/tasks/task_&lt;id&gt;" when it exists, else a styled card
    /// with the task icon and colour.
    /// </summary>
    public sealed class DeskTutorial
    {
        public const int Steps = 5;

        readonly VisualElement _root, _art, _dots, _bodyBox;
        readonly Label _stepLabel, _title, _body, _example, _hl, _name;
        readonly Button _back, _next;
        TaskKind _kind;
        int _step;
        int _seenMask;
        double _openedAt;
        Action<int> _onStep;
        Action _onClose;

        public VisualElement Root => _root;
        public bool IsOpen { get; private set; }
        public int Step => _step;

        public DeskTutorial(VisualElement parent)
        {
            _root = Box("dk-tut").AddTo(parent);
            _root.Show(false);
            var head = Box("dk-tut-head").AddTo(_root);
            StampText(Loc.Upper(Dk.T("desk.tut.band")), "dk-tut-band").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            _stepLabel = MemoBold("", "dk-tut-step").NoPick().AddTo(head);
            Btn(Dk.T("desk.tut.close"), () => Close(false), "btn-ghost", "btn-small", "dk-tut-x").AddTo(head);
            _name = Title("", "dk-tut-name").NoPick().AddTo(_root);
            _art = Box("dk-tut-art").NoPick().AddTo(_root);
            _dots = Box("dk-tut-dots").NoPick().AddTo(_root);
            for (int i = 0; i < Steps; i++) Box("dk-tut-dot").NoPick().AddTo(_dots);
            _bodyBox = Box("dk-tut-bodybox").NoPick().AddTo(_root);
            _title = Title("", "dk-tut-title").NoPick().AddTo(_bodyBox);
            _body = Text("", "dk-tut-body").NoPick().AddTo(_bodyBox);
            var ex = Box("dk-tut-example").NoPick().AddTo(_bodyBox);
            StampText(Loc.Upper(Dk.T("desk.tut.example")), "dk-tut-example-band").NoPick().AddTo(ex);
            _example = Hand("", "dk-tut-example-text").NoPick().AddTo(ex);
            var hlRow = Box("dk-tut-hlrow").NoPick().AddTo(_bodyBox);
            Dk.Icon("hint_arrow", "dk-tut-hl-icon").AddTo(hlRow);
            _hl = Bold("", "dk-tut-hl").NoPick().AddTo(hlRow);
            Box("spacer").NoPick().AddTo(_root);
            var foot = Box("dk-tut-foot").AddTo(_root);
            _back = Btn(Dk.T("desk.tut.back"), () => Go(_step - 1, "back"), "btn-ghost", "btn-small").AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _next = Btn(Dk.T("desk.tut.next"), OnNext, "btn-primary", "dk-tut-next").AddTo(foot);
        }

        /// <param name="onStep">Called with the step (0..4) whenever it changes; -1 when the tutorial closes (highlight off).</param>
        public void Open(TaskKind kind, string action, Action<int> onStep, Action onClose)
        {
            _kind = kind;
            _onStep = onStep;
            _onClose = onClose;
            _seenMask = 0;
            _openedAt = Dk.Now;
            bool was = IsOpen;
            IsOpen = true;
            _root.Show(true);
            _name.SetText(Dk.TaskName(kind));
            BuildArt();
            Go(0, action, true);
            if (!was) UiMotion.SlideIn(_root, new Vector2(40f, 0f), 0.28f, 0f, UiMotion.Ease.OutCubic);
        }

        void BuildArt()
        {
            _art.Clear();
            _art.style.backgroundImage = StyleKeyword.Null;
            _art.RemoveFromClassList("dk-tut-art-fallback");
            if (UiArt.Apply(_art, Dk.TaskArtId(_kind))) return;
            _art.AddToClassList("dk-tut-art-fallback");
            _art.style.backgroundColor = Dk.TaskColor(_kind);
            Dk.Icon(Dk.TaskIcon(_kind), "dk-tut-art-icon").AddTo(_art);
        }

        void OnNext()
        {
            if (_step >= Steps - 1) Close(true);
            else Go(_step + 1, "next");
        }

        void Go(int step, string action, bool first = false)
        {
            if (step < 0 || step >= Steps) return;
            _step = step;
            _seenMask |= 1 << step;
            string id = LineTasks.Id(_kind);
            string pre = "task." + id + ".tut." + (step + 1);
            _title.SetText(Dk.T(pre + ".title"));
            _body.SetText(Dk.T(pre + ".body"));
            _example.SetText(Dk.T(pre + ".example"));
            _hl.SetText(Dk.T("desk.tut.hl." + (step + 1)));
            _stepLabel.SetText((step + 1) + "/" + Steps);
            for (int i = 0; i < Steps; i++)
            {
                _dots[i].EnableInClassList("dk-tut-dot-on", i == step);
                _dots[i].EnableInClassList("dk-tut-dot-seen", i != step && (_seenMask & (1 << i)) != 0);
            }
            _back.SetEnabled(step > 0);
            _next.SetText(Dk.T(step >= Steps - 1 ? "desk.tut.done" : "desk.tut.next"));
            if (!first) UiMotion.SlideIn(_bodyBox, new Vector2(24f, 0f), 0.2f);
            Dk.NoRich(_root);
            TelemetryHooks.TutorialStep(_kind, step + 1, action);
            _onStep?.Invoke(step);
        }

        /// <summary>UI mock: jump to a step.</summary>
        internal void MockStep(int step) { Go(step, "next"); }

        public void Close(bool completed)
        {
            if (!IsOpen) return;
            IsOpen = false;
            int seen = 0;
            for (int i = 0; i < Steps; i++) if ((_seenMask & (1 << i)) != 0) seen++;
            TelemetryHooks.TutorialClosed(_kind, seen, Math.Max(0, Dk.Now - _openedAt), completed || (_seenMask & (1 << (Steps - 1))) != 0);
            _root.Show(false);
            _onStep?.Invoke(-1);
            _onClose?.Invoke();
        }

        /// <summary>Silent close (the task changed or the screen went away).</summary>
        public void Dismiss()
        {
            if (!IsOpen) return;
            Close(false);
        }
    }
}
