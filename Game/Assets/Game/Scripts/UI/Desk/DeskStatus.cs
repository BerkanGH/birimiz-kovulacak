using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The player's status panel: company productivity (live percentage, target marker, the reason of the last change,
    /// "Measuring..." until the first task resolves), money, blame, warnings, the secret objective with progress, the
    /// break time carried over from early submissions and dirty-trick availability. Two layouts: the full HUD panel and
    /// the compact taskbar strip of the computer.
    /// </summary>
    public sealed class DeskStatus
    {
        readonly bool _compact;
        readonly VisualElement _root, _fill, _marker, _gauge, _objRow, _objFill, _carried, _trick;
        readonly Label _pct, _target, _reason, _money, _blame, _warn, _objName, _objText, _carriedText, _trickText;
        readonly Label _title;
        int _sig = int.MinValue;
        int _lastPermille = int.MinValue;

        public VisualElement Root => _root;
        /// <summary>Coach-arrow anchor: the productivity gauge.</summary>
        public VisualElement Gauge => _gauge;

        public DeskStatus(VisualElement parent, bool compact)
        {
            _compact = compact;
            _root = Box("dk-status").AddTo(parent);
            if (compact) _root.AddToClassList("dk-status-compact");
            var head = Box("dk-status-head").NoPick().AddTo(_root);
            _title = StampText(Loc.Upper(Dk.T("desk.company.title")), "dk-status-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            _pct = MemoBold("", "dk-status-pct").NoPick().AddTo(head);
            _gauge = Box("dk-gauge").NoPick().AddTo(_root);
            _fill = Box("dk-gauge-fill").NoPick().AddTo(_gauge);
            _marker = Box("dk-gauge-target").NoPick().AddTo(_gauge);
            var line = Box("dk-status-line").NoPick().AddTo(_root);
            _target = Bold("", "dk-status-target").NoPick().AddTo(line);
            _reason = Text("", "dk-status-reason").NoPick().AddTo(compact ? line : _root);
            var chips = Box("dk-status-chips").NoPick().AddTo(_root);
            _money = StatChip(chips, "bank_app", "dk-chip-money");
            _blame = StatChip(chips, "blame", "dk-chip-blame");
            _warn = StatChip(chips, "warning_letter", "dk-chip-warn");
            _objRow = Box("dk-obj").NoPick().AddTo(_root);
            Dk.Icon("agenda_target", "dk-obj-icon").AddTo(_objRow);
            var oc = Box("dk-obj-col").NoPick().AddTo(_objRow);
            _objName = Bold("", "dk-obj-name").NoPick().AddTo(oc);
            _objText = Text("", "dk-obj-text").NoPick().AddTo(oc);
            var bar = Bar(0f, out _objFill, "dk-obj-bar");
            bar.pickingMode = PickingMode.Ignore;
            bar.AddTo(oc);
            _carried = Box("dk-tagrow").NoPick().AddTo(_root);
            Dk.Icon("timer", "dk-tagrow-icon").AddTo(_carried);
            _carriedText = Bold("", "dk-tagrow-text").NoPick().AddTo(_carried);
            _trick = Box("dk-tagrow").NoPick().AddTo(_root);
            Dk.Icon("trick_app", "dk-tagrow-icon").AddTo(_trick);
            _trickText = Bold("", "dk-tagrow-text").NoPick().AddTo(_trick);
            if (compact) { _objRow.Show(false); _carried.Show(false); _trick.Show(false); }
        }

        Label StatChip(VisualElement parent, string icon, string cls)
        {
            var c = Box("dk-statchip", cls).NoPick().AddTo(parent);
            Dk.Icon(icon, "dk-statchip-icon").AddTo(c);
            return Bold("", "dk-statchip-text").NoPick().AddTo(c);
        }

        public void Flash() { UiMotion.Pulse(_pct, 0.25f, 0.5f); UiMotion.Pulse(_gauge, 0.05f, 0.4f); }

        public void Update(LineView v)
        {
            var c = v.Company;
            var me = Dk.Player(v.Me);
            int h = c.ProvisionalPermille;
            h = h * 31 + c.TargetPercent;
            h = h * 31 + (int)c.LastChange * 100 + (int)c.LastTask + c.LastDelta * 7;
            h = h * 31 + v.Mine.Money;
            h = h * 31 + (me != null ? me.Blame * 8 + me.Warnings : 0);
            h = h * 31 + (int)v.Mine.Objective.Kind * 100 + v.Mine.Objective.Progress + v.Mine.Objective.Target * 13;
            h = h * 31 + (int)(v.Features.CarriedSeconds * 2);
            h = h * 31 + (int)v.Mine.Trick * 10 + v.Mine.TricksLeftThisWeek + (v.Mine.TrickUsedToday ? 50 : 0) + (v.Features.TricksUnlocked ? 100 : 0);
            h = h * 31 + (c.Closed ? 1 : 0);
            if (h == _sig) return;
            _sig = h;

            bool measuring = c.ProvisionalPermille < 0;
            _pct.SetText(measuring ? Dk.T("desk.company.measuring") : Dk.Pct(c.ProvisionalPermille));
            _pct.EnableInClassList("dk-status-pct-low", !measuring && c.ProvisionalPermille < c.TargetPercent * 10);
            _pct.EnableInClassList("dk-status-pct-ok", !measuring && c.ProvisionalPermille >= c.TargetPercent * 10);
            _gauge.EnableInClassList("dk-gauge-measuring", measuring);
            _fill.style.width = Length.Percent(measuring ? 0f : Mathf.Clamp(c.ProvisionalPermille / 10f, 0f, 100f));
            _fill.EnableInClassList("dk-gauge-fill-low", !measuring && c.ProvisionalPermille < c.TargetPercent * 10);
            _marker.style.left = Length.Percent(Mathf.Clamp(c.TargetPercent, 0, 100));
            _target.SetText(Dk.F("desk.company.target", c.TargetPercent));
            _reason.SetText(Reason(c));
            if (_lastPermille != int.MinValue && _lastPermille != c.ProvisionalPermille) Flash();
            _lastPermille = c.ProvisionalPermille;

            _money.SetText(Dk.F("desk.hud.money", v.Mine.Money));
            _blame.SetText(Dk.F("desk.hud.blame", me != null ? me.Blame : 0));
            _warn.SetText(Dk.F("desk.hud.warnings", me != null ? me.Warnings : 0, Dk.Config.MaxWarningsPerPlayer));
            _warn.parent.EnableInClassList("dk-statchip-hot", me != null && me.Warnings > 0);

            if (_compact) return;
            var o = v.Mine.Objective;
            _objRow.Show(o.Kind != ObjectiveKind.None);
            if (o.Kind != ObjectiveKind.None)
            {
                _objName.SetText(Dk.F("desk.obj.line", Dk.ObjectiveName(o.Kind)));
                _objText.SetText(Dk.ObjectiveDesc(o) + "  " + Dk.ObjectiveProgress(o));
                _objFill.style.width = Length.Percent(o.Progress < 0 || o.Threshold <= 0 ? 0f : Mathf.Clamp01(o.Progress / (float)o.Threshold) * 100f);
            }
            bool carried = v.Phase == LinePhase.Break && v.Features.CarriedSeconds > 0.5f;
            _carried.Show(carried);
            if (carried) _carriedText.SetText(Dk.F("desk.hud.carried", Mathf.RoundToInt(v.Features.CarriedSeconds)));
            bool showTrick = v.Features.TricksUnlocked && v.Mine.Trick != TrickType.None;
            _trick.Show(showTrick);
            if (showTrick)
            {
                string state = v.Mine.TricksLeftThisWeek <= 0 ? Dk.T("desk.hud.trick_none") : v.Mine.TrickUsedToday ? Dk.T("desk.hud.trick_used") : Dk.F("desk.hud.trick_ready", v.Mine.TricksLeftThisWeek);
                _trickText.SetText(Dk.F("desk.hud.trick", Dk.TrickName(v.Mine.Trick), state));
            }
            Dk.NoRich(_root);
        }

        static string Reason(LineCompanyView c)
        {
            switch (c.LastChange)
            {
                case CompanyChange.TaskResolved: return Dk.F("desk.company.reason.task", Dk.TaskName(c.LastTask), Dk.Delta(c.LastDelta));
                case CompanyChange.Remedial: return Dk.F("desk.company.reason.remedial", Dk.Delta(c.LastDelta));
                default: return Dk.T("desk.company.reason.none");
            }
        }
    }
}
