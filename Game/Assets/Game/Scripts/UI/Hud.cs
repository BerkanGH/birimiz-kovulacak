using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using EventType = BirimizKovulacak.Core.EventType;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>In-office HUD: phase clock, shared quota and open orders, coworkers, prompts, progress and feed.</summary>
    public sealed class Hud : UiKit.Screen
    {
        public enum ToastKind { Info, Good, Warning, Manager, Private }
        static Hud _i;

        readonly Label _phase, _timer, _quotaText, _carry, _banner;
        readonly VisualElement _quotaFill, _orders, _players, _prompts, _progress, _progressFill, _feed, _bannerRoot, _hints;
        readonly List<(VisualElement el, float until)> _toasts = new List<(VisualElement, float)>();
        readonly HashSet<string> _recent = new HashSet<string>();
        float _clearRecentAt;
        int _lastPhaseSerial = -1;

        public Hud(VisualElement parent) : base(parent, "hud")
        {
            _i = this;
            Root.pickingMode = PickingMode.Ignore;

            var top = Box("hud-top").AddTo(Root);
            var pb = Box("phase-box").AddTo(top);
            _phase = Bold("", "phase-name").AddTo(pb);
            _timer = Title("", "timer").AddTo(pb);

            var q = Box("quota", "card-dark").AddTo(Root);
            _quotaText = Bold("", "light").AddTo(q);
            Bar(0, out _quotaFill).AddTo(q);
            _orders = Box("orders").AddTo(q);

            _players = Box("players", "card-dark").AddTo(Root);

            _bannerRoot = Box("banner").AddTo(Root);
            var bb = Box("banner-box").AddTo(_bannerRoot);
            _banner = Title("", "banner-text").AddTo(bb);
            _bannerRoot.Show(false);

            var pa = Box("prompt-area").AddTo(Root);
            _prompts = Box("col").AddTo(pa);
            _progress = Box("progress").AddTo(pa);
            _progressFill = Box("progress-fill").AddTo(_progress);
            _progress.Show(false);

            var carry = Box("carry", "card-dark").AddTo(Root);
            _carry = Bold("", "light").AddTo(carry);
            _hints = Box("hints").AddTo(Root);

            _feed = Box("feed").AddTo(Root);
            foreach (var e in Root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;

            if (ClientState.I != null)
            {
                ClientState.I.EventReceived += OnEvent;
                ClientState.I.ResultReceived += OnResult;
            }
        }

        public static void Toast(string text, ToastKind kind = ToastKind.Info, float seconds = 5f)
        {
            if (_i == null || string.IsNullOrEmpty(text)) return;
            if (_i._recent.Contains(text)) return;
            _i._recent.Add(text);
            _i._clearRecentAt = Time.time + 1.5f;
            var t = Text(text, "toast");
            switch (kind)
            {
                case ToastKind.Good: t.AddToClassList("toast-good"); break;
                case ToastKind.Warning: t.AddToClassList("toast-warning"); break;
                case ToastKind.Manager: t.AddToClassList("toast-manager"); break;
                case ToastKind.Private: t.AddToClassList("toast-private"); break;
            }
            t.pickingMode = PickingMode.Ignore;
            _i._feed.Insert(0, t);
            _i._toasts.Add((t, Time.time + seconds));
            while (_i._feed.childCount > 7) _i._feed.RemoveAt(_i._feed.childCount - 1);
        }

        void OnEvent(GameEvent e)
        {
            var v = ClientState.I.View;
            if (v == null) return;
            ToastKind kind;
            string s = Describe.Event(e, v, out kind);
            if (s != null) Toast(s, kind, kind == ToastKind.Manager ? 7f : 5f);
            switch (e.Type)
            {
                case EventType.TaskCompleted: Sfx.Play("complete", 0.6f); break;
                case EventType.ManagerObservation: Sfx.Play("manager_hm"); CameraRig.I?.Shake(0.4f); break;
                case EventType.EvidenceCreated: Sfx.Play("evidence"); break;
                case EventType.NoteAdded: Sfx.Play("toast"); break;
                case EventType.InteractionCompleted: Sfx.Play("place", 0.6f); break;
                case EventType.AccusationFiled: Sfx.Play("evidence", 0.7f); break;
                case EventType.BallotCast: Sfx.Play("vote"); break;
            }
        }

        void OnResult(ActionResult r, ActionRequest req)
        {
            if (r.Accepted)
            {
                if (req != null && (req.Verb == ActionVerb.PickUp)) Sfx.Play("pickup", 0.7f);
                return;
            }
            if (r.Failure == FailureCode.Duplicate || r.Failure == FailureCode.WrongMatch) return;
            Toast(Loc.Failure(r.Failure), ToastKind.Warning, 3f);
            Sfx.Play("deny");
        }

        public override void Tick()
        {
            if (Time.time > _clearRecentAt) _recent.Clear();
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (Time.time > _toasts[i].until) { _toasts[i].el.RemoveFromHierarchy(); _toasts.RemoveAt(i); }
            if (!Visible) return;
            var cs = ClientState.I;
            var v = cs.View;
            if (v == null) return;
            double now = cs.Now;
            double remaining = v.PhaseEndTime - now;

            if (v.PhaseSerial != _lastPhaseSerial)
            {
                _lastPhaseSerial = v.PhaseSerial;
                if (v.Phase != MatchPhase.Briefing) Sfx.Play("phase");
                Toast(Loc.F("feed.phase", Loc.Phase(v.Phase)), ToastKind.Info, 4f);
            }

            _phase.text = Loc.Phase(v.Phase);
            _timer.text = Clock(remaining);
            _timer.EnableInClassList("timer-urgent", remaining < 10 && v.Phase != MatchPhase.Results);

            // Manager warning banner
            bool warn = (v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection) && v.Manager.State == ManagerState.Warning;
            _bannerRoot.Show(warn || (v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection));
            if (warn) _banner.text = Loc.F("hud.manager_coming", Clock(v.Manager.ArrivesAt - now));
            else _banner.text = Loc.T(v.Phase == MatchPhase.Raid ? "hud.raid" : "hud.final_inspection");

            // Quota
            var q = v.Quota;
            _quotaText.text = Loc.F("hud.quota", q.Points.ToString("0"), q.Target.ToString("0"));
            _quotaFill.style.width = Length.Percent(Mathf.Clamp01(q.Target > 0 ? q.Points / q.Target : 0) * 100f);
            _quotaFill.EnableInClassList("good", q.Points >= q.Target);
            _orders.Clear();
            Order(Loc.F("hud.order_coffee", q.CoffeeOrders), q.CoffeeOrders > 0, false);
            Order(Loc.F("hud.order_report", q.ReportOrders), q.ReportOrders > 0, false);
            if (q.PhoneRinging) Order(Loc.F("hud.phone_ringing", Clock(q.PhoneRingEnds - now)), true, true);
            if (q.DirtyDesks > 0) Order(Loc.F("hud.dirty_desks", q.DirtyDesks), true, false);

            // Players
            _players.Clear();
            Bold(Loc.T("hud.coworkers"), "light", "small").AddTo(_players);
            foreach (var p in v.Players)
            {
                var row = Box("player-row").AddTo(_players);
                if (!p.Connected) row.AddToClassList("offline");
                Chip(p.Id).AddTo(row);
                string name = p.Name + (p.Id == cs.MySlot ? " " + Loc.T("hud.you") : "") + (p.Connected ? "" : " " + Loc.F("hud.offline", Clock(p.DisconnectDeadline - now)));
                Text(name, "player-name").AddTo(row);
                Text(p.Performance.ToString("0"), "player-perf").AddTo(row);
                Pips(p.SuspicionLevel).AddTo(row);
            }

            // Prompts / progress
            _prompts.Clear();
            var me = v.Me;
            var active = me != null ? me.ActiveInteraction : null;
            if (active != null && active.Verb != ActionVerb.None)
            {
                _progress.Show(true);
                double total = active.EndTime - active.StartTime;
                float f = total <= 0 ? 1f : (float)((now - active.StartTime) / total);
                _progressFill.style.width = Length.Percent(Mathf.Clamp01(f) * 100f);
                var pr = Box("prompt").AddTo(_prompts);
                Text(Loc.T("verb." + VerbKey(active.Verb)) + " …", "prompt-text").AddTo(pr);
                Text(Loc.T("hud.move_to_cancel"), "prompt-time").AddTo(pr);
            }
            else
            {
                _progress.Show(false);
                var lp = LocalPlayerController.I;
                if (lp != null && lp.Options.Count > 0 && LocalPlayerController.OfficePhase(v.Phase))
                {
                    string[] keys = { GameInput.KeyLabel(GameInput.Interact), GameInput.KeyLabel(GameInput.Secondary) };
                    for (int i = 0; i < lp.Options.Count && i < 2; i++)
                    {
                        var o = lp.Options[i];
                        var pr = Box("prompt").AddTo(_prompts);
                        if (o.BlockedBy != FailureCode.None) pr.AddToClassList("prompt-blocked");
                        Bold(keys[i], "key").AddTo(pr);
                        Text(Loc.T(o.LabelKey), "prompt-text").AddTo(pr);
                        if (o.Seconds > 0) Text(Loc.F("hud.seconds", o.Seconds.ToString("0.#")), "prompt-time").AddTo(pr);
                        if (o.BlockedBy != FailureCode.None) Text("(" + Loc.Failure(o.BlockedBy) + ")", "prompt-time").AddTo(pr);
                        if (o.IsSabotage) Text(Loc.T("hud.sabotage_tag"), "tag-sabotage").AddTo(pr);
                    }
                }
            }

            // Carry + hints
            var mine = cs.Me;
            string carrying = "";
            if (mine != null && !string.IsNullOrEmpty(mine.CarryingItemId))
                foreach (var it in v.Items) if (it.Id == mine.CarryingItemId) carrying = ItemLabel(it, v);
            _carry.text = string.IsNullOrEmpty(carrying) ? Loc.T("hud.hands_empty") : Loc.F("hud.carrying", carrying);
            _hints.Clear();
            Hint(Loc.F("hint.move", "WASD"));
            Hint(Loc.F("hint.drop", GameInput.KeyLabel(GameInput.Drop)));
            Hint(Loc.F("hint.work_pose", GameInput.KeyLabel(GameInput.WorkPose)) + (mine != null && mine.WorkPose ? " (AÇIK)" : ""));
            Hint(Loc.F("hint.slow", GameInput.KeyLabel(GameInput.Slow)));
            Hint(Loc.F("hint.phone", GameInput.KeyLabel(GameInput.Phone)));
        }

        public static string ItemLabel(ItemView it, PlayerView v)
        {
            string s = Loc.Item(it.Variant);
            if (it.Kind == ItemKind.Mug) s += " (" + Loc.T("mug." + it.Fill) + ")";
            if (it.Kind == ItemKind.Report) s += " — " + Loc.PlayerName(v, it.ReportOwner);
            if (it.Kind == ItemKind.Nameplate) s += " — " + Loc.PlayerName(v, it.NameplateOf);
            return s;
        }

        void Hint(string s) { var l = Text(s, "hint"); l.pickingMode = PickingMode.Ignore; _hints.Add(l); }

        void Order(string text, bool active, bool alert)
        {
            var o = Text(text, "order").AddTo(_orders);
            if (alert) o.AddToClassList("alert");
            if (!active) o.style.opacity = 0.5f;
        }

        public static string VerbKey(ActionVerb v)
        {
            switch (v)
            {
                case ActionVerb.PickUp: return "take";
                case ActionVerb.Place: return "place";
                case ActionVerb.BrewCoffee: return "brew_coffee";
                case ActionVerb.WriteReport: return "write_report";
                case ActionVerb.AnswerPhone: return "answer_phone";
                case ActionVerb.CleanDesk: return "clean_desk";
                case ActionVerb.CleanSpill: return "clean_spill";
                case ActionVerb.SpillCoffee: return "spill_coffee";
                case ActionVerb.LockWorkstation: return "lock_pc";
                case ActionVerb.UnlockWorkstation: return "unlock_pc";
                case ActionVerb.SearchContainer: return "search";
                default: return v.ToString();
            }
        }
    }
}
