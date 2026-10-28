using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EventType = BirimizKovulacak.Core.EventType;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// In-office HUD. Everything is built once and pooled; texts are rebuilt only when the view's StateVersion (or
    /// the focused prompt) changes. Per frame only timers (cached clock strings), the hold bar, mic level and
    /// speaking rings are touched, without allocations.
    /// </summary>
    public sealed class Hud : UiKit.Screen
    {
        public enum ToastKind { Info, Good, Warning, Manager, Private }
        static Hud _i;

        /// <summary>The emote wheel is open (UIRoot frees the cursor while it is).</summary>
        public static bool WheelOpen => _i != null && _i._wheel.Open;

        readonly VisualElement _office, _feed, _darkness;
        // top
        readonly Label _phase, _timer, _dayChip;
        readonly VisualElement _phaseIcon, _mgrChip, _mgrBadge;
        readonly Label _mgrState, _mgrTimer;
        // banners
        readonly VisualElement _bannerMgr, _bannerNet, _bannerPower;
        readonly Label _bannerMgrText, _bannerMgrTimer, _bannerNetTimer, _bannerPowerTimer;
        // quota
        readonly Label _quotaText;
        readonly VisualElement _quotaFill;
        readonly OrderChip _orderCoffee, _orderReport, _orderPhone, _orderDirty, _orderMail;
        // crew
        readonly VisualElement _crew;
        readonly List<CrewRow> _rows = new List<CrewRow>();
        // prompts
        readonly PromptRow[] _prompts = new PromptRow[2];
        readonly VisualElement _hold, _holdFill;
        readonly Label _holdLabel;
        // me card
        readonly VisualElement _meBadge, _favorFill, _favorRow, _myPips, _mic, _micIcon, _micFill;
        readonly Label _carry, _micKey, _micRoute;
        // call
        readonly CallWidget _call;
        // hints
        readonly Label _hintPhoneKey, _hintDropKey, _hintPoseKey, _hintEmoteKey, _inboxBadge;
        readonly VisualElement _hintPose;
        // emotes
        readonly EmoteWheel _wheel;
        // toasts
        readonly ToastSlot[] _toasts = new ToastSlot[7];
        readonly HashSet<string> _recent = new HashSet<string>();
        float _clearRecentAt;

        int _lastVersion = -1, _lastMatch = -1, _lastPhaseSerial = -1, _lastTimerSec = -1, _lastMgrSec = -1, _lastNetSec = -1, _lastPowerSec = -1;
        int _promptSig = int.MinValue, _lastMicQ = -1;
        VoiceRoute _lastRoute = (VoiceRoute)255;
        string _carryText = "";
        bool _officeVisible = true, _meetingFeed;

        public Hud(VisualElement parent, VisualElement feedLayer) : base(parent, "hud")
        {
            _i = this;
            Root.pickingMode = PickingMode.Ignore;
            _office = Box("hud").NoPick().AddTo(Root);
            _darkness = Box("dark-vignette").NoPick().AddTo(_office);
            _darkness.Show(false);

            // ---- phase pill + manager chip
            var top = Box("hud-top").NoPick().AddTo(_office);
            var pill = Box("phase-pill").NoPick().AddTo(top);
            _phaseIcon = Icon("timer", "icon-m").AddTo(pill);
            var pc = Box("phase-col").NoPick().AddTo(pill);
            _phase = Bold("", "phase-name").NoPick().AddTo(pc);
            _timer = Title("", "timer").NoPick().AddTo(pc);
            _dayChip = Bold("", "day-chip").NoPick().AddTo(pill);
            _mgrChip = Box("mgr-chip").NoPick().AddTo(top);
            _mgrBadge = ManagerPortrait("", false, "badge-s").AddTo(_mgrChip);
            _mgrState = Bold("", "mgr-state").NoPick().AddTo(_mgrChip);
            _mgrTimer = Title("", "mgr-timer").NoPick().AddTo(_mgrChip);

            // ---- banners
            var banners = Box("banners").NoPick().AddTo(_office);
            _bannerMgr = Banner(banners, "manager", "banner-red", out _bannerMgrText, out _bannerMgrTimer);
            _bannerNet = Banner(banners, "sab_internet", "banner-dark", out var netText, out _bannerNetTimer);
            netText.text = Loc.T("hud.internet_down");
            _bannerPower = Banner(banners, "sab_power", "banner-dark", out var powerText, out _bannerPowerTimer);
            powerText.text = Loc.T("hud.power_down");

            // ---- quota
            var q = Panel("panel-dark", "quota-card").NoPick().AddTo(_office);
            var qh = Box("quota-head").NoPick().AddTo(q);
            Icon("quota", "icon-s").AddTo(qh);
            _quotaText = Bold("", "quota-text").NoPick().AddTo(qh);
            Bar(0, out _quotaFill).NoPick().AddTo(q);
            var orders = Box("orders").NoPick().AddTo(q);
            _orderCoffee = new OrderChip(orders, "task_coffee");
            _orderReport = new OrderChip(orders, "task_report");
            _orderPhone = new OrderChip(orders, "task_phone");
            _orderDirty = new OrderChip(orders, "task_clean");
            _orderMail = new OrderChip(orders, "task_mail");

            // ---- crew
            _crew = Panel("panel-dark", "crew").NoPick().AddTo(_office);
            Bold(Loc.T("hud.coworkers"), "crew-title").NoPick().AddTo(_crew);

            // ---- prompts + hold bar
            var pa = Box("prompt-area").NoPick().AddTo(_office);
            for (int i = 0; i < _prompts.Length; i++) _prompts[i] = new PromptRow(pa);
            _hold = Box("hold").NoPick().AddTo(pa);
            _holdFill = Box("hold-fill").NoPick().AddTo(_hold);
            _holdLabel = Bold("", "prompt-time").NoPick().AddTo(pa);
            _hold.Show(false);
            _holdLabel.Show(false);

            // ---- me card
            var me = Panel("panel-dark", "me-card").NoPick().AddTo(_office);
            _meBadge = Badge(-1, "badge-l").AddTo(me);
            var meters = Box("me-meters").NoPick().AddTo(me);
            _favorRow = Box("meter-row").NoPick().AddTo(meters);
            Icon("favor_heart").AddTo(_favorRow);
            Text(Loc.T("hud.favor"), "meter-label").NoPick().AddTo(_favorRow);
            Bar(0, out _favorFill, "bar-bad").NoPick().AddTo(_favorRow);
            var susRow = Box("meter-row").NoPick().AddTo(meters);
            Icon("suspicion_0").AddTo(susRow);
            Text(Loc.T("hud.suspicion"), "meter-label").NoPick().AddTo(susRow);
            _myPips = Pips(0).AddTo(susRow);
            _carry = Text("", "carry-text").NoPick().AddTo(meters);
            _mic = Box("mic").NoPick().AddTo(me);
            _micIcon = Icon("mic_on").AddTo(_mic);
            var lvl = Box("mic-level").NoPick().AddTo(_mic);
            _micFill = Box("mic-level-fill").NoPick().AddTo(lvl);
            _micFill.style.height = Length.Percent(0);
            var micCol = Box("col").NoPick().AddTo(_mic);
            _micKey = KeyCap("V", "mic-key").AddTo(micCol);
            _micRoute = Text("", "mic-route").NoPick().AddTo(micCol);
            _mic.Show(false);

            // ---- call widget
            _call = new CallWidget(_office);

            // ---- hints
            var hints = Box("hints").NoPick().AddTo(_office);
            _hintEmoteKey = Hint(hints, "hint.emote", out _);
            _hintDropKey = Hint(hints, "hint.drop", out _);
            _hintPoseKey = Hint(hints, "hint.work_pose", out _hintPose);
            _hintPhoneKey = Hint(hints, "hint.phone", out var phoneHint);
            _inboxBadge = Bold("", "inbox-badge").NoPick().AddTo(phoneHint);
            _inboxBadge.Show(false);

            // ---- emote wheel
            _wheel = new EmoteWheel(Root);

            // ---- toast feed (separate layer so it stays above meeting/results panels)
            _feed = Box("feed").NoPick().AddTo(feedLayer);
            for (int i = 0; i < _toasts.Length; i++) _toasts[i] = new ToastSlot(_feed);

            if (ClientState.I != null)
            {
                ClientState.I.EventReceived += OnEvent;
                ClientState.I.ResultReceived += OnResult;
            }
            RefreshKeys();
        }

        static VisualElement Banner(VisualElement parent, string icon, string kind, out Label text, out Label timer)
        {
            var b = Box("banner", kind).NoPick().AddTo(parent);
            Icon(icon, "icon-m").AddTo(b);
            text = Title("", "banner-text").NoPick().AddTo(b);
            timer = Title("", "banner-timer").NoPick().AddTo(b);
            b.Show(false);
            return b;
        }

        static Label Hint(VisualElement parent, string key, out VisualElement row)
        {
            row = Box("hint").NoPick().AddTo(parent);
            var k = KeyCap("?").AddTo(row);
            Text(Loc.T(key), "hint-text").NoPick().AddTo(row);
            return k;
        }

        /// <summary>Re-reads key labels (call after rebinding).</summary>
        public static void RefreshKeys()
        {
            if (_i == null) return;
            _i._hintPhoneKey.text = GameInput.KeyLabel(GameInput.Phone);
            _i._hintDropKey.text = GameInput.KeyLabel(GameInput.Drop);
            _i._hintPoseKey.text = GameInput.KeyLabel(GameInput.WorkPose);
            _i._hintEmoteKey.text = UiInput.KeyLabel(UiInput.EmoteWheel);
            _i._call.RefreshKeys();
            _i._promptSig = int.MinValue;
        }

        /// <summary>Office HUD (phase, quota, prompts...) vs. only the feed (meeting / results).</summary>
        public void SetOfficeVisible(bool office)
        {
            if (office == _officeVisible) return;
            _officeVisible = office;
            _office.Show(office);
            _feed.EnableInClassList("feed-meeting", !office);
            if (!office) _wheel.Close();
        }

        protected override void OnShow() { _lastVersion = -1; _feed.Show(true); RefreshKeys(); }
        protected override void OnHide() { _feed.Show(false); _wheel.Close(); }

        // ------------------------------------------------------------------ toasts

        public static void Toast(string text, ToastKind kind = ToastKind.Info, float seconds = 5f, string icon = null)
        {
            if (_i == null || string.IsNullOrEmpty(text)) return;
            if (_i._recent.Contains(text)) return;
            _i._recent.Add(text);
            _i._clearRecentAt = Time.time + 1.5f;
            ToastSlot slot = null;
            for (int i = 0; i < _i._toasts.Length; i++) if (!_i._toasts[i].Active) { slot = _i._toasts[i]; break; }
            if (slot == null)
            {
                slot = _i._toasts[0];
                for (int i = 1; i < _i._toasts.Length; i++) if (_i._toasts[i].Until < slot.Until) slot = _i._toasts[i];
            }
            slot.Set(text, kind, icon ?? DefaultToastIcon(kind), Time.time + seconds);
            _i._feed.Insert(0, slot.Root);
        }

        static string DefaultToastIcon(ToastKind k)
        {
            switch (k)
            {
                case ToastKind.Good: return "check";
                case ToastKind.Warning: return "warning";
                case ToastKind.Manager: return "manager";
                case ToastKind.Private: return "notes";
                default: return "info";
            }
        }

        void OnEvent(GameEvent e)
        {
            var v = ClientState.I.View;
            if (v == null) return;
            ToastKind kind;
            string s = Describe.Event(e, v, out kind);
            if (s != null) Toast(s, kind, kind == ToastKind.Manager ? 7f : 5f, EventIcon(e));
            switch (e.Type)
            {
                case EventType.TaskCompleted: Sfx.Play("complete", 0.6f); break;
                case EventType.ManagerObservation: Sfx.Play("manager_hm"); CameraRig.I?.Shake(0.4f); break;
                case EventType.EvidenceCreated: Sfx.Play("evidence"); break;
                case EventType.NoteAdded: Sfx.Play("toast"); break;
                case EventType.InteractionCompleted: Sfx.Play("place", 0.6f); break;
                case EventType.AccusationFiled: Sfx.Play("evidence", 0.7f); break;
                case EventType.BallotCast: Sfx.Play("vote"); break;
                case EventType.IncomingCall: if (e.Actor == ClientState.I.MySlot) Sfx.Play("phone_ring"); break;
                case EventType.SurpriseInspection: Sfx.Play("siren", 0.8f); CameraRig.I?.Shake(0.3f); break;
                case EventType.InternetDown:
                case EventType.PowerDown: Sfx.Play("sabotage"); break;
                case EventType.EmailSent: Sfx.Play("toast"); break;
                case EventType.FavorChanged: FlashFavour(e.Actor); break;
                case EventType.CoffeeTasted: Sfx.Play(e.IntArg == 1 ? "deny" : "complete", 0.6f); break;
            }
        }

        static string EventIcon(GameEvent e)
        {
            switch (e.Type)
            {
                case EventType.TaskCompleted: return UiArt.TaskIcon((TaskType)e.IntArg);
                case EventType.OrderAdded: return UiArt.TaskIcon((TaskType)e.IntArg);
                case EventType.PhoneRinging: case EventType.PhoneMissed: return "task_phone";
                case EventType.WorkstationLocked: case EventType.WorkstationUnlocked: return "lock_screen";
                case EventType.EvidenceCreated: return "warning";
                case EventType.ManagerObservation: case EventType.ManagerWarning: case EventType.ManagerTipReceived: return "manager";
                case EventType.AccusationFiled: return "accuse";
                case EventType.BallotCast: return "ballot";
                case EventType.IncomingCall: return "ringing";
                case EventType.CallStarted: return "call";
                case EventType.CallEnded: return "hang_up";
                case EventType.EmailSent: return "email";
                case EventType.InternetDown: case EventType.InternetRestored: return "sab_internet";
                case EventType.PowerDown: case EventType.PowerRestored: return "sab_power";
                case EventType.PlugPulled: case EventType.PlugRestored: return "sab_pull_plug";
                case EventType.FootageShown: return "footage";
                case EventType.SurpriseInspection: return "manager";
                case EventType.MailArrived: return "task_mail";
                case EventType.CoffeeTasted: return "sab_salt";
                case EventType.DeskDirty: return "task_clean";
                case EventType.ReportPrinted: return "task_report";
                default: return null;
            }
        }

        void FlashFavour(int player)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Player == player)
                {
                    var r = _rows[i].Root;
                    r.AddToClassList("favour-flash");
                    r.schedule.Execute(() => r.RemoveFromClassList("favour-flash")).StartingIn(900);
                }
        }

        void OnResult(ActionResult r, ActionRequest req)
        {
            if (r.Accepted)
            {
                if (req != null && req.Verb == ActionVerb.PickUp) Sfx.Play("pickup", 0.7f);
                return;
            }
            if (r.Failure == FailureCode.Duplicate || r.Failure == FailureCode.WrongMatch) return;
            // Fallback when the gameplay code sent SendFakeEmail without a template: open the picker instead of an error.
            if (req != null && req.Verb == ActionVerb.SendFakeEmail && req.IntParam < 0)
            {
                EmailPicker.Open(req.TargetId);
                return;
            }
            Toast(Loc.Failure(r.Failure), ToastKind.Warning, 3f);
            Sfx.Play("deny");
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Called every frame by UIRoot (also while hidden so toasts expire).</summary>
        public void Tick(bool inputAllowed)
        {
            float t = Time.time;
            if (t > _clearRecentAt && _recent.Count > 0) _recent.Clear();
            for (int i = 0; i < _toasts.Length; i++) _toasts[i].Tick(t);
            if (!Visible) return;
            var cs = ClientState.I;
            var v = cs.View;
            if (v == null) return;
            double now = cs.Now;

            if (v.MatchId != _lastMatch) { _lastMatch = v.MatchId; _lastVersion = -1; _lastPhaseSerial = -1; }
            if (v.PhaseSerial != _lastPhaseSerial)
            {
                _lastPhaseSerial = v.PhaseSerial;
                if (v.Phase != MatchPhase.Briefing) Sfx.Play("phase");
                Toast(Loc.F("feed.phase", Loc.Phase(v.Phase)), ToastKind.Info, 4f, "timer");
                _lastVersion = -1;
            }
            if (v.StateVersion != _lastVersion)
            {
                _lastVersion = v.StateVersion;
                Rebuild(v, now);
            }

            _wheel.Tick(inputAllowed && v.Phase != MatchPhase.Abandoned && v.Phase != MatchPhase.None);
            if (!_officeVisible) return;
            TickTimers(v, now);
            TickPrompts(v, now);
            _call.Tick(v, now, inputAllowed);
            TickVoice(v);
        }

        public override void Tick() { Tick(true); }

        void TickTimers(PlayerView v, double now)
        {
            double remaining = v.PhaseEndTime - now;
            int sec = remaining <= 0 ? 0 : (int)System.Math.Ceiling(remaining);
            if (sec != _lastTimerSec)
            {
                _lastTimerSec = sec;
                _timer.text = ClockInt(sec);
                _timer.EnableInClassList("timer-urgent", sec <= 10);
            }
            var m = v.Manager;
            if (m.State == ManagerState.Warning)
            {
                int ms = (int)System.Math.Ceiling(System.Math.Max(0, m.ArrivesAt - now));
                if (ms != _lastMgrSec) { _lastMgrSec = ms; _mgrTimer.text = ClockInt(ms); _bannerMgrTimer.text = _mgrTimer.text; }
            }
            else if (_lastMgrSec != -1) { _lastMgrSec = -1; _mgrTimer.text = ""; _bannerMgrTimer.text = ""; }
            if (v.Office.InternetDown)
            {
                int ns = (int)System.Math.Ceiling(System.Math.Max(0, v.Office.InternetBackAt - now));
                if (ns != _lastNetSec) { _lastNetSec = ns; _bannerNetTimer.text = ClockInt(ns); }
            }
            if (v.Office.PowerDown)
            {
                int ps = (int)System.Math.Ceiling(System.Math.Max(0, v.Office.PowerBackAt - now));
                if (ps != _lastPowerSec) { _lastPowerSec = ps; _bannerPowerTimer.text = ClockInt(ps); }
            }
            if (v.Quota.PhoneRinging) _orderPhone.SetCount(ClockInt((int)System.Math.Ceiling(System.Math.Max(0, v.Quota.PhoneRingEnds - now))));
        }

        void Rebuild(PlayerView v, double now)
        {
            var cs = ClientState.I;
            int my = cs.MySlot;

            // phase + day
            _phase.text = Loc.Phase(v.Phase);
            bool week = v.Mode == GameMode.Week;
            _dayChip.Show(week);
            if (week) _dayChip.text = v.Features.CareerMultiplier > 1.01f ? Loc.F("hud.day_bonus", Loc.DayName(v.WeekDay), v.Features.CareerMultiplier.ToString("0.#")) : Loc.DayName(v.WeekDay);

            // manager chip + banner
            var m = v.Manager;
            bool inspectionPhase = v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection || m.Surprise;
            bool angry = m.State == ManagerState.Reacting || m.Surprise;
            SetManagerPortrait(_mgrBadge, v.ManagerProfileId, angry);
            _mgrState.text = m.Surprise ? Loc.T("mgr.state.surprise") : Loc.T("mgr.state." + m.State);
            _mgrChip.EnableInClassList("mgr-warning", m.State == ManagerState.Warning);
            _mgrChip.EnableInClassList("mgr-walking", !m.Surprise && (m.State == ManagerState.Walking || m.State == ManagerState.Inspecting || m.State == ManagerState.Reacting));
            _mgrChip.EnableInClassList("mgr-surprise", m.Surprise);
            bool walking = m.State == ManagerState.Warning || m.State == ManagerState.Walking || m.State == ManagerState.Inspecting || m.State == ManagerState.Reacting;
            _bannerMgr.Show(inspectionPhase && walking);
            if (m.State == ManagerState.Warning) _bannerMgrText.text = Loc.T("hud.manager_coming");
            else if (m.Surprise) _bannerMgrText.text = Loc.T("hud.surprise");
            else _bannerMgrText.text = Loc.T(v.Phase == MatchPhase.Raid ? "hud.raid" : "hud.final_inspection");

            // office status
            _bannerNet.Show(v.Office.InternetDown);
            _bannerPower.Show(v.Office.PowerDown);
            _darkness.Show(v.Office.PowerDown);
            if (!v.Office.InternetDown) _lastNetSec = -1;
            if (!v.Office.PowerDown) _lastPowerSec = -1;

            // quota + orders
            var q = v.Quota;
            _quotaText.text = Loc.F("hud.quota", q.Points.ToString("0"), q.Target.ToString("0"));
            SetFill(_quotaFill, q.Target > 0 ? q.Points / q.Target : 0);
            _quotaFill.EnableInClassList("good", q.Points >= q.Target);
            _orderCoffee.Set(q.CoffeeOrders > 0, false, Loc.F("hud.order_n", q.CoffeeOrders));
            _orderReport.Set(q.ReportOrders > 0, false, Loc.F("hud.order_n", q.ReportOrders));
            _orderPhone.Set(q.PhoneRinging, true, null);
            _orderPhone.Root.Show(q.PhoneRinging);
            _orderDirty.Set(q.DirtyDesks > 0, false, Loc.F("hud.order_n", q.DirtyDesks));
            _orderDirty.Root.Show(q.DirtyDesks > 0);
            _orderMail.Set(q.OpenMail > 0, false, Loc.F("hud.order_n", q.OpenMail));
            _orderMail.Root.Show(v.Features.Mail);

            // crew
            bool favor = GameInfo.FavorMatters(v);
            while (_rows.Count < v.Players.Count) _rows.Add(new CrewRow(_crew));
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i < v.Players.Count) _rows[i].Set(v.Players[i], v, my, favor, now);
                else _rows[i].Hide();
            }

            // me card
            var me = GameInfo.Player(v, my);
            if (me != null)
            {
                SetBadge(_meBadge, me.Id, me.Name);
                _favorRow.Show(favor);
                SetFill(_favorFill, me.Favor / GameInfo.MaxFavor(v));
                SetPips(_myPips, me.SuspicionLevel);
                string carrying = "";
                if (!string.IsNullOrEmpty(me.CarryingItemId))
                    foreach (var it in v.Items) if (it.Id == me.CarryingItemId) { carrying = ItemLabel(it, v); break; }
                string ct = string.IsNullOrEmpty(carrying) ? Loc.T("hud.hands_empty") : Loc.F("hud.carrying", carrying);
                if (ct != _carryText) { _carryText = ct; _carry.text = ct; }
                _hintPose.EnableInClassList("hint-on", me.WorkPose);
            }

            // inbox badge
            int unread = PhonePanel.UnreadCount(v);
            _inboxBadge.Show(unread > 0);
            if (unread > 0) _inboxBadge.text = Num(unread);

            _call.Rebuild(v);
        }

        void TickPrompts(PlayerView v, double now)
        {
            var me = v.Me;
            var active = me != null ? me.ActiveInteraction : null;
            bool busy = active != null && active.Verb != ActionVerb.None;
            var lp = LocalPlayerController.I;
            var options = lp != null && LocalPlayerController.OfficePhase(v.Phase) && !busy ? lp.Options : null;

            int sig = busy ? (int)active.Verb * 7919 + 1 : 17;
            if (options != null)
                for (int i = 0; i < options.Count && i < 2; i++)
                {
                    var o = options[i];
                    sig = sig * 31 + (int)o.Verb;
                    sig = sig * 31 + (o.LabelKey != null ? o.LabelKey.GetHashCode() : 0);
                    sig = sig * 31 + (o.TargetId != null ? o.TargetId.GetHashCode() : 0);
                    sig = sig * 31 + (int)o.BlockedBy + (o.IsSabotage ? 1000 : 0);
                }
            if (sig != _promptSig)
            {
                _promptSig = sig;
                string k0 = GameInput.KeyLabel(GameInput.Interact), k1 = GameInput.KeyLabel(GameInput.Secondary);
                for (int i = 0; i < _prompts.Length; i++)
                {
                    if (options != null && i < options.Count) _prompts[i].Set(options[i], i == 0 ? k0 : k1);
                    else _prompts[i].Hide();
                }
                _hold.Show(busy);
                _holdLabel.Show(busy);
                if (busy)
                {
                    _holdLabel.text = Loc.F("hud.doing", Loc.T(VerbLabelKey(active.Verb)));
                    _hold.EnableInClassList("hold-sabotage", IsSabotageVerb(active.Verb));
                }
            }
            if (busy)
            {
                double total = active.EndTime - active.StartTime;
                float f = total <= 0 ? 1f : (float)((now - active.StartTime) / total);
                _holdFill.style.width = Length.Percent(Mathf.Clamp01(f) * 100f);
            }
        }

        void TickVoice(PlayerView v)
        {
            var voice = UiServices.Voice;
            bool avail = voice != null && voice.Available;
            _mic.Show(avail);
            for (int i = 0; i < _rows.Count; i++) _rows[i].TickSpeaking(avail ? voice : null);
            if (!avail) return;
            bool muted = UiSettings.MicMuted;
            int q = muted ? -2 : Mathf.RoundToInt(Mathf.Clamp01(voice.MicLevel) * 20f);
            if (q != _lastMicQ)
            {
                _lastMicQ = q;
                _micFill.style.height = Length.Percent(q < 0 ? 0 : q * 5f);
                SetIcon(_micIcon, muted ? "mic_off" : "mic_on");
                _micKey.Show(UiSettings.PushToTalk && !muted);
                _micKey.text = voice.PttKeyLabel;
            }
            _mic.EnableInClassList("mic-live", voice.Transmitting);
            var route = voice.Route;
            if (route != _lastRoute) { _lastRoute = route; _micRoute.text = Loc.T("voice.route." + route); }
        }

        // ------------------------------------------------------------------ helpers

        public static string ItemLabel(ItemView it, PlayerView v)
        {
            string s = Loc.Item(it.Variant);
            if (it.Kind == ItemKind.Mug) s += " (" + Loc.T("mug." + it.Fill) + (it.KnownSalted ? ", " + Loc.T("mug.salted") : "") + ")";
            if (it.Kind == ItemKind.Report) s += " — " + Loc.PlayerName(v, it.ReportOwner);
            if (it.Kind == ItemKind.Nameplate) s += " — " + Loc.PlayerName(v, it.NameplateOf);
            if (it.Kind == ItemKind.Mail && it.AddressedDesk >= 0) s += " — " + Loc.F("hud.mail_to", Describe.DeskOwnerName(v, it.AddressedDesk), Loc.Desk(it.AddressedDesk));
            return s;
        }

        public static string VerbLabelKey(ActionVerb v)
        {
            switch (v)
            {
                case ActionVerb.PickUp: return "verb.take";
                case ActionVerb.Place: return "verb.place";
                case ActionVerb.BrewCoffee: return "verb.brew_coffee";
                case ActionVerb.WriteReport: return "verb.write_report";
                case ActionVerb.AnswerPhone: return "verb.answer_phone";
                case ActionVerb.CleanDesk: return "verb.clean_desk";
                case ActionVerb.CleanSpill: return "verb.clean_spill";
                case ActionVerb.SpillCoffee: return "verb.spill_coffee";
                case ActionVerb.LockWorkstation: return "verb.lock_pc";
                case ActionVerb.UnlockWorkstation: return "verb.unlock_pc";
                case ActionVerb.SearchContainer: return "verb.search";
                case ActionVerb.AuditScanItem: return "verb.audit_scan_item";
                case ActionVerb.LockScreen: return "verb.lock_screen";
                case ActionVerb.SendFakeEmail: return "verb.send_fake_email";
                case ActionVerb.PullPlug: return "verb.pull_plug";
                case ActionVerb.PlugIn: return "verb.plug_in";
                case ActionVerb.SaltCoffee: return "verb.salt_coffee";
                case ActionVerb.TasteCoffee: return "verb.taste_coffee";
                case ActionVerb.CutInternet: return "verb.cut_internet";
                case ActionVerb.RestartRouter: return "verb.restart_router";
                case ActionVerb.CutPower: return "verb.cut_power";
                case ActionVerb.ResetFuse: return "verb.reset_fuse";
                case ActionVerb.TipManager: return "verb.tip_manager";
                default: return "verb." + v;
            }
        }

        /// <summary>v1 name kept for compatibility.</summary>
        public static string VerbKey(ActionVerb v) { return VerbLabelKey(v).Substring(5); }

        static bool IsSabotageVerb(ActionVerb v)
        {
            switch (v)
            {
                case ActionVerb.SpillCoffee: case ActionVerb.LockWorkstation: case ActionVerb.SendFakeEmail: case ActionVerb.PullPlug:
                case ActionVerb.SaltCoffee: case ActionVerb.CutInternet: case ActionVerb.CutPower: return true;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ pooled parts

        sealed class OrderChip
        {
            public readonly VisualElement Root;
            readonly Label _text;
            public OrderChip(VisualElement parent, string icon)
            {
                Root = Box("order").NoPick().AddTo(parent);
                Icon(icon).AddTo(Root);
                _text = Bold("", "order-text").NoPick().AddTo(Root);
            }
            public void Set(bool active, bool alert, string text)
            {
                Root.EnableInClassList("order-off", !active);
                Root.EnableInClassList("order-alert", alert && active);
                if (text != null) _text.text = text;
            }
            public void SetCount(string text) { _text.SetText(text); }
        }

        sealed class CrewRow
        {
            public readonly VisualElement Root;
            readonly VisualElement _badge, _ring, _callFlag, _ringFlag, _crown, _favor, _pips;
            readonly Label _name, _perf, _favorText;
            public int Player = -1;
            bool _speaking;

            public CrewRow(VisualElement parent)
            {
                Root = Box("crew-row").NoPick().AddTo(parent);
                _badge = Badge(-1, "badge-s").AddTo(Root);
                _ring = Box("speak-ring").NoPick().AddTo(_badge);
                _name = Text("", "crew-name").NoPick().AddTo(Root);
                _crown = Icon("crown", "crew-flag").AddTo(Root);
                _callFlag = Icon("call", "crew-flag").AddTo(Root);
                _ringFlag = Icon("ringing", "crew-flag").AddTo(Root);
                _favor = Box("crew-favor").NoPick().AddTo(Root);
                Icon("favor_heart").AddTo(_favor);
                _favorText = Bold("", "crew-favor-text").NoPick().AddTo(_favor);
                _perf = Bold("", "crew-perf").NoPick().AddTo(Root);
                _pips = Pips(0).AddTo(Root);
            }

            public void Set(PlayerPublicView p, PlayerView v, int my, bool favor, double now)
            {
                Root.Show(true);
                if (Player != p.Id) { Player = p.Id; SetBadge(_badge, p.Id, p.Name); }
                string name = p.Id == my ? p.Name + " " + Loc.T("hud.you") : p.Name;
                if (!p.Connected) name += " " + Loc.F("hud.offline", Clock(p.DisconnectDeadline - now));
                _name.SetText(name);
                Root.EnableInClassList("offline", !p.Connected);
                Root.EnableInClassList("crew-me", p.Id == my);
                _perf.SetText(Num(Mathf.RoundToInt(p.Performance)));
                SetPips(_pips, p.SuspicionLevel);
                _callFlag.Show(p.Activity == PublicActivity.OnCall);
                _ringFlag.Show(p.Ringing);
                _crown.Show(v.Favourite == p.Id);
                _favor.Show(favor);
                if (favor) _favorText.SetText(p.Favor.ToString("0.#"));
            }

            public void Hide() { Root.Show(false); Player = -1; }

            public void TickSpeaking(IVoiceUi voice)
            {
                bool s = voice != null && Player >= 0 && voice.IsSpeaking(Player);
                if (s == _speaking) return;
                _speaking = s;
                _ring.EnableInClassList("on", s);
            }
        }

        sealed class PromptRow
        {
            readonly VisualElement _root, _icon;
            readonly Label _key, _text, _time, _reason, _tag;
            public PromptRow(VisualElement parent)
            {
                _root = Box("prompt").NoPick().AddTo(parent);
                _icon = Icon("info").AddTo(_root);
                _key = KeyCap("E").AddTo(_root);
                _text = Bold("", "prompt-text").NoPick().AddTo(_root);
                _time = Text("", "prompt-time").NoPick().AddTo(_root);
                _reason = Text("", "prompt-reason").NoPick().AddTo(_root);
                _tag = Tag(Loc.T("hud.sabotage_tag"), "tag-red").AddTo(_root);
                _root.Show(false);
            }
            public void Set(Affordance o, string key)
            {
                _root.Show(true);
                SetIcon(_icon, UiArt.VerbIcon(o.Verb, o.LabelKey));
                _key.text = key;
                _text.text = Loc.T(o.LabelKey);
                _time.Show(o.Seconds > 0);
                if (o.Seconds > 0) _time.text = Loc.F("hud.seconds", o.Seconds.ToString("0.#"));
                bool blocked = o.BlockedBy != FailureCode.None;
                _reason.Show(blocked);
                if (blocked) _reason.text = Loc.Failure(o.BlockedBy);
                _root.EnableInClassList("prompt-blocked", blocked);
                _root.EnableInClassList("prompt-sabotage", o.IsSabotage);
                _tag.Show(o.IsSabotage);
            }
            public void Hide() { _root.Show(false); }
        }

        sealed class ToastSlot
        {
            public readonly VisualElement Root;
            readonly VisualElement _icon;
            readonly Label _text;
            public float Until;
            public bool Active;
            public ToastSlot(VisualElement parent)
            {
                Root = Box("toast").NoPick().AddTo(parent);
                _icon = Icon("info").AddTo(Root);
                _text = Text("", "toast-text").NoPick().AddTo(Root);
                Root.Show(false);
            }
            public void Set(string text, ToastKind kind, string icon, float until)
            {
                _text.text = text;
                Root.EnableInClassList("toast-good", kind == ToastKind.Good);
                Root.EnableInClassList("toast-warning", kind == ToastKind.Warning);
                Root.EnableInClassList("toast-manager", kind == ToastKind.Manager);
                Root.EnableInClassList("toast-private", kind == ToastKind.Private);
                SetIcon(_icon, icon);
                Until = until;
                Active = true;
                Root.Show(true);
                Pop(Root, "toast-enter");
            }
            public void Tick(float now)
            {
                if (Active && now > Until) { Active = false; Root.Show(false); }
            }
        }

        /// <summary>Incoming / outgoing / active call with hotkeys (answer, decline / hang up).</summary>
        sealed class CallWidget
        {
            readonly VisualElement _root, _badge;
            readonly Label _title, _name, _timer;
            readonly Button _answer, _decline;
            readonly Label _answerKey, _declineKey;
            CallState _state;
            int _other = -2, _lastSec = -1;
            double _sentAt;

            public CallWidget(VisualElement parent)
            {
                _root = Box("call-widget").AddTo(parent);
                _badge = Badge(-1, "badge-m").AddTo(_root);
                var col = Box("col", "grow").AddTo(_root);
                _title = Bold("", "call-title").AddTo(col);
                _name = Title("", "call-name").AddTo(col);
                _timer = Bold("", "call-timer").AddTo(col);
                var actions = Box("call-actions").AddTo(col);
                _answer = IconBtn("call", "", Answer, "btn-primary", "btn-small").AddTo(actions);
                _answerKey = KeyCap("Y").AddTo(_answer);
                _answer.Q<Label>(className: "btn-label")?.RemoveFromHierarchy();
                Bold(Loc.T("call.answer"), "btn-label").NoPick().AddTo(_answer);
                _decline = IconBtn("hang_up", "", Decline, "btn-danger", "btn-small").AddTo(actions);
                _declineKey = KeyCap("N").AddTo(_decline);
                Bold("", "btn-label").NoPick().AddTo(_decline);
                _root.Show(false);
            }

            public void RefreshKeys()
            {
                _answerKey.text = UiInput.KeyLabel(UiInput.AnswerCall);
                _declineKey.text = UiInput.KeyLabel(UiInput.DeclineCall);
            }

            public void Rebuild(PlayerView v)
            {
                var c = v.Me != null ? v.Me.Call : null;
                CallState st = c != null ? c.State : CallState.None;
                if (st == _state && (c == null || c.Other == _other)) return;
                _state = st;
                _other = c != null ? c.Other : -1;
                _lastSec = -1;
                _root.Show(st != CallState.None);
                if (st == CallState.None) return;
                SetBadge(_badge, _other, Loc.PlayerName(v, _other));
                _name.text = Loc.PlayerName(v, _other);
                _root.EnableInClassList("call-incoming", st == CallState.RingingIn);
                _root.EnableInClassList("call-active", st == CallState.Active);
                _title.text = Loc.T("call.state." + st);
                _answer.Show(st == CallState.RingingIn);
                var declineLabel = _decline.Q<Label>(className: "btn-label");
                if (declineLabel != null) declineLabel.text = Loc.T(st == CallState.Active ? "call.hang_up" : st == CallState.RingingOut ? "call.cancel" : "call.decline");
                Pop(_root);
            }

            public void Tick(PlayerView v, double now, bool inputAllowed)
            {
                if (_state == CallState.None) return;
                var c = v.Me.Call;
                int sec = _state == CallState.Active ? (int)(now - c.Since) : (int)System.Math.Ceiling(System.Math.Max(0, c.Until - now));
                if (sec != _lastSec)
                {
                    _lastSec = sec;
                    _timer.text = _state == CallState.Active ? ClockInt(sec) + " / " + ClockInt((int)(c.Until - c.Since)) : ClockInt(sec);
                }
                if (!inputAllowed || Time.unscaledTime - _sentAt < 0.4f) return;
                if (_state == CallState.RingingIn && UiInput.AnswerCall.WasPressedThisFrame()) Answer();
                else if (UiInput.DeclineCall.WasPressedThisFrame()) Decline();
            }

            void Answer()
            {
                if (_state != CallState.RingingIn) return;
                _sentAt = Time.unscaledTime;
                ClientState.I.Send(ActionVerb.AnswerCall);
            }

            void Decline()
            {
                if (_state == CallState.None) return;
                _sentAt = Time.unscaledTime;
                ClientState.I.Send(_state == CallState.Active ? ActionVerb.HangUp : ActionVerb.DeclineCall);
            }
        }

        /// <summary>Hold the emote key: wheel with five emotes (mouse direction or 1-5), release to send.</summary>
        sealed class EmoteWheel
        {
            readonly VisualElement _root;
            readonly VisualElement[] _slots = new VisualElement[5];
            int _hot = -1;
            float _openedAt;
            public bool Open { get; private set; }

            public EmoteWheel(VisualElement parent)
            {
                _root = Box("emote-wheel").AddTo(parent);
                Box("emote-ring").NoPick().AddTo(_root);
                Bold(Loc.T("emote.title"), "emote-center").NoPick().AddTo(_root);
                for (int i = 0; i < 5; i++)
                {
                    int index = i;
                    float a = i * 72f * Mathf.Deg2Rad;
                    var s = Box("emote-slot").AddTo(_root);
                    s.style.left = 210f + 150f * Mathf.Sin(a);
                    s.style.top = 210f - 150f * Mathf.Cos(a);
                    Icon(UiArt.EmoteIcon(i)).AddTo(s);
                    Bold((i + 1) + " · " + Loc.T("emote." + (EmoteId)i), "emote-label").NoPick().AddTo(s);
                    s.RegisterCallback<PointerEnterEvent>(_ => SetHot(index));
                    s.RegisterCallback<ClickEvent>(_ => { Send(index); Close(); });
                    _slots[i] = s;
                }
                _root.Show(false);
            }

            public void Close()
            {
                if (!Open) return;
                Open = false;
                _root.Show(false);
                SetHot(-1);
            }

            void SetHot(int i)
            {
                if (i == _hot) return;
                _hot = i;
                for (int k = 0; k < _slots.Length; k++) _slots[k].EnableInClassList("emote-slot-hot", k == i);
            }

            static void Send(int i)
            {
                if (i < 0) return;
                ClientState.I.Send(ActionVerb.Emote, "", i);
                Sfx.Play("click");
            }

            public void Tick(bool allowed)
            {
                var key = UiInput.EmoteWheel;
                if (!allowed || key == null) { Close(); return; }
                if (!Open)
                {
                    if (key.WasPressedThisFrame()) { Open = true; _openedAt = Time.unscaledTime; _root.Show(true); Pop(_root); SetHot(-1); }
                    return;
                }
                var kb = Keyboard.current;
                if (kb != null)
                    for (int i = 0; i < 5; i++)
                        if (kb[Key.Digit1 + i].wasPressedThisFrame) { Send(i); Close(); return; }
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    Vector2 d = mouse.position.ReadValue() - new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f);
                    if (d.sqrMagnitude > 40f * 40f)
                    {
                        float ang = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; // 0 = up, clockwise
                        if (ang < 0) ang += 360f;
                        SetHot(Mathf.RoundToInt(ang / 72f) % 5);
                    }
                }
                if (key.WasReleasedThisFrame())
                {
                    if (_hot >= 0 && Time.unscaledTime - _openedAt > 0.12f) Send(_hot);
                    Close();
                }
            }
        }
    }
}
