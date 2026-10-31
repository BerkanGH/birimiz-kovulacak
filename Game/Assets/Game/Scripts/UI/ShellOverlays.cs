using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// First-launch flow after the language picker: analytics consent prompt (default = No), then the 3-card visual intro.
    /// <see cref="Next"/> derives the step from persisted state, so it survives the UI rebuild that a language switch causes.
    /// </summary>
    public static class ShellFlow
    {
        public const string IntroKey = "intro.v4.done";

        /// <summary>False for bots, automation and UI mocks (no interactive first-launch dialogs).</summary>
        public static bool Interactive
        {
            get
            {
                var a = LaunchArgs.Current;
                return !(a.Bot || a.AutoHost || LineMockDriver.Active || !string.IsNullOrEmpty(a.AutoJoinAddress));
            }
        }

        public static bool NeedsConsent => Interactive && TelemetryHooks.Privacy != null && TelemetryHooks.Privacy.NeedsConsentPrompt;
        public static bool NeedsIntro => Interactive && !PlayerPrefs.HasKey(IntroKey);

        /// <summary>Opens the next pending first-launch step (consent, then intro); no-op when none is pending.</summary>
        public static void Next()
        {
            if (NeedsConsent) ConsentScreen.Open(Next);
            else if (NeedsIntro) IntroScreen.Open(true);
        }
    }

    // ====================================================================== privacy text + controls

    /// <summary>Plain-language analytics explanation and the consent radio group (shared by the prompt and Settings → Privacy).</summary>
    public static class PrivacyUi
    {
        /// <summary>Consent levels: 1 denied, 2 local logs, 3 local + cloud.</summary>
        public static VisualElement ConsentChoices(VisualElement parent, int current, Action<int> onPick)
        {
            var box = Box("privacy-choices").AddTo(parent);
            var priv = TelemetryHooks.Privacy;
            Choice(box, 1, current, "consent.opt.no", "consent.opt.no_sub", onPick);
            Choice(box, 2, current, "consent.opt.local", "consent.opt.local_sub", onPick);
            if (priv != null && priv.CloudAvailable) Choice(box, 3, current, "consent.opt.cloud", "consent.opt.cloud_sub", onPick);
            return box;
        }

        static void Choice(VisualElement box, int level, int current, string titleKey, string subKey, Action<int> onPick)
        {
            var b = Btn("", () => onPick(level), "privacy-choice", level == current ? "btn-selected" : "btn-secondary").AddTo(box);
            Title(Loc.T(titleKey), "privacy-choice-title").NoPick().AddTo(b);
            Text(Loc.T(subKey), "privacy-choice-sub").NoPick().AddTo(b);
        }

        public static void Explainer(VisualElement parent, bool brief)
        {
            Text(Loc.T(brief ? "consent.what_brief" : "privacy.what"), "privacy-text").AddTo(parent);
            var never = Box("privacy-never").AddTo(parent);
            Bold(Loc.T("privacy.never_title"), "privacy-never-title").NoPick().AddTo(never);
            Text(Loc.T("privacy.never"), "privacy-text").NoPick().AddTo(never);
            if (!brief)
            {
                Text(Loc.T("privacy.recording_note"), "privacy-text").AddTo(parent);
                Text(Loc.T("privacy.host_note"), "privacy-text").AddTo(parent);
            }
        }

        /// <summary>Settings → Privacy tab body. <paramref name="rebuild"/> redraws it after a change.</summary>
        public static void BuildTab(VisualElement body, Action rebuild)
        {
            var priv = TelemetryHooks.Privacy;
            if (priv == null) { Empty(body, Loc.T("privacy.unavailable")); return; }
            Section(body, Loc.T("privacy.title"), "info");
            Explainer(body, false);

            Section(body, Loc.T("privacy.consent_title"), "check");
            Bold(Loc.T(priv.Consent >= 2 ? (priv.Consent == 3 ? "privacy.state.cloud" : "privacy.state.local") : priv.Consent == 1 ? "privacy.state.no" : "privacy.state.unknown"), "privacy-state").AddTo(body);
            ConsentChoices(body, priv.Consent, lvl => { priv.SetConsent(lvl); rebuild(); });
            if (priv.Consent >= 2) Btn(Loc.T("privacy.withdraw"), () => { priv.SetConsent(1); rebuild(); }, "btn-small", "btn-ghost", "mt-s").AddTo(body);
            Text(Loc.T("privacy.withdraw_note"), "t-small", "t-muted").AddTo(body);

            Section(body, Loc.T("privacy.data_title"), "notes");
            var msg = Text("", "privacy-result");
            var row = Box("privacy-actions").AddTo(body);
            Btn(Loc.T("privacy.delete"), () => ConfirmDialog.Show(Loc.T("privacy.delete"), Loc.T("privacy.delete_confirm"), Loc.T("privacy.delete"), () =>
            {
                bool ok = priv.DeleteLocalData();
                msg.text = Loc.T(ok ? "privacy.deleted" : "privacy.delete_failed");
                msg.EnableInClassList("t-bad", !ok);
            }, true), "btn-small", "btn-secondary").AddTo(row);
            Btn(Loc.T("privacy.reset_id"), () => ConfirmDialog.Show(Loc.T("privacy.reset_id"), Loc.T("privacy.reset_id_confirm"), Loc.T("privacy.reset_id"), () =>
            {
                priv.ResetAnonymousId();
                msg.text = Loc.T("privacy.id_reset");
                msg.RemoveFromClassList("t-bad");
            }, false), "btn-small", "btn-secondary").AddTo(row);
            Btn(Loc.T("privacy.open_folder"), () =>
            {
                priv.OpenLogFolder();
                msg.text = Loc.F("privacy.folder_is", priv.LogDirectory ?? "-");
                msg.RemoveFromClassList("t-bad");
            }, "btn-small", "btn-secondary").AddTo(row);
            Btn(Loc.T("privacy.export"), () =>
            {
                string path = priv.ExportLogs();
                msg.text = path != null ? Loc.F("privacy.exported", path) : Loc.T("privacy.export_failed");
                msg.EnableInClassList("t-bad", path == null);
            }, "btn-small", "btn-secondary").AddTo(row);
            msg.AddTo(body);
            Text(Loc.T("privacy.export_note"), "t-small", "t-muted").AddTo(body);
        }
    }

    // ====================================================================== consent prompt

    /// <summary>First-launch analytics consent: the same text as Settings → Privacy, short; "No" is preselected; no dark patterns.</summary>
    public sealed class ConsentScreen : UiKit.Screen
    {
        static ConsentScreen _i;
        readonly VisualElement _card, _choices;
        int _choice = 1; // default: No
        Action _done;

        public ConsentScreen(VisualElement parent) : base(parent, "dim", "center", "shell-layer")
        {
            _i = this;
            _card = Panel("panel", "consent-card", "col").AddTo(Root);
            MeetingText.ApplyIcon(Box("icon", "icon-xl").NoPick().AddTo(_card), "icons/info");
            Title(Loc.T("consent.title"), "h1").AddTo(_card);
            PrivacyUi.Explainer(_card, true);
            Text(Loc.T("consent.question"), "privacy-question").AddTo(_card);
            _choices = Box("col").AddTo(_card);
            var foot = Box("row", "consent-foot").AddTo(_card);
            Text(Loc.T("consent.later"), "t-small", "t-muted", "grow").AddTo(foot);
            Btn(Loc.T("consent.continue"), Confirm, "btn-primary", "btn-big").AddTo(foot);
        }

        public static void Open(Action done = null)
        {
            if (_i == null) { done?.Invoke(); return; }
            _i._done = done;
            _i._choice = 1;
            _i.DrawChoices();
            _i.SetVisible(true);
            _i.Root.BringToFront();
            UiMotion.PopIn(_i._card, 0.3f, 0f, 0.92f);
        }

        public static void Close() { if (_i != null) _i.SetVisible(false); }

        void DrawChoices()
        {
            _choices.Clear();
            PrivacyUi.ConsentChoices(_choices, _choice, lvl => { _choice = lvl; DrawChoices(); });
        }

        void Confirm()
        {
            var p = TelemetryHooks.Privacy;
            if (p != null) p.SetConsent(_choice);
            SetVisible(false);
            var d = _done; _done = null;
            d?.Invoke();
        }
    }

    // ====================================================================== intro cards

    /// <summary>The 3-card visual intro (no wall of text): company target + cooperation, secret objective + private calls/texts, meeting + evidence + final vote.</summary>
    public sealed class IntroScreen : UiKit.Screen
    {
        public const int Cards = 3;
        static IntroScreen _i;
        static readonly string[] Art = { "onboarding_v4/intro_1_company", "onboarding_v4/intro_2_secret", "onboarding_v4/intro_3_meeting" };
        static readonly string[] FallbackArt = { "onboarding/ob_3_rounds", "onboarding/ob_2_phone", "onboarding/ob_4_blame" };

        readonly VisualElement _card, _pic, _dots;
        readonly Label _kicker, _title, _body;
        readonly Button _back, _next, _skip;
        int _page, _seen = 1;
        bool _first;

        public IntroScreen(VisualElement parent) : base(parent, "dim", "center", "shell-layer")
        {
            _i = this;
            _card = Panel("panel", "intro-card", "col").AddTo(Root);
            var head = Box("row").AddTo(_card);
            _kicker = Bold("", "howto-kicker", "grow").AddTo(head);
            _skip = Btn(Loc.T("intro.skip"), () => Finish(true), "btn-ghost", "btn-small").AddTo(head);
            _pic = Box("intro-pic").AddTo(_card);
            _title = Title("", "intro-title").AddTo(_card);
            _body = Text("", "intro-body").AddTo(_card);
            _dots = Box("dots").AddTo(_card);
            for (int i = 0; i < Cards; i++) Box("dot").AddTo(_dots);
            var nav = Box("row").AddTo(_card);
            _back = Btn(Loc.T("howto.prev"), () => Go(_page - 1), "btn-ghost").AddTo(nav);
            Box("spacer").AddTo(nav);
            _next = Btn(Loc.T("howto.next"), () => { if (_page < Cards - 1) Go(_page + 1); else Finish(false); }, "btn-primary", "btn-big").AddTo(nav);
        }

        /// <summary><paramref name="first"/> = the first-launch run (reports completion and remembers it); reopening from the menu does neither.</summary>
        public static void Open(bool first = false)
        {
            if (_i == null) return;
            _i._first = first;
            _i._seen = 1;
            _i.SetVisible(true);
            _i.Root.BringToFront();
            _i.Go(0);
            UiMotion.PopIn(_i._card, 0.3f, 0f, 0.94f);
        }

        /// <summary>Mocks: open on a card.</summary>
        public static void OpenAt(int page)
        {
            if (_i == null) return;
            if (!_i.Visible) Open(false);
            _i.Go(page);
        }

        public static void Close() { if (_i != null) _i.SetVisible(false); }
        public static bool IsOpen => _i != null && _i.Visible;
        public void Skip() { Finish(true); }
        public static void SkipOpen() { if (_i != null && _i.Visible) _i.Finish(true); }

        protected override void OnHide()
        {
            _pic.style.backgroundImage = StyleKeyword.Null;
            for (int i = 0; i < Art.Length; i++) { UiArt.Release(Art[i]); UiArt.Release(FallbackArt[i]); }
        }

        void Finish(bool skipped)
        {
            if (_first)
            {
                PlayerPrefs.SetInt(ShellFlow.IntroKey, 1);
                PlayerPrefs.Save();
                TelemetryHooks.OnboardingIntroCompleted(_seen, skipped);
            }
            _first = false;
            SetVisible(false);
        }

        void Go(int page)
        {
            int old = _page;
            _page = Mathf.Clamp(page, 0, Cards - 1);
            if (_page + 1 > _seen) _seen = _page + 1;
            _kicker.text = Loc.F("intro.kicker", _page + 1, Cards);
            _title.text = Loc.T("intro." + _page + ".title");
            _body.text = Loc.T("intro." + _page + ".text");
            _pic.Clear();
            _pic.style.backgroundImage = StyleKeyword.Null;
            bool art = UiArt.Apply(_pic, Art[_page]) || UiArt.Apply(_pic, FallbackArt[_page]);
            _pic.EnableInClassList("howto-pic-fallback", !art);
            for (int i = 0; i < _dots.childCount; i++) _dots[i].EnableInClassList("dot-on", i == _page);
            _back.SetEnabled(_page > 0);
            _next.text = _page < Cards - 1 ? Loc.T("howto.next") : Loc.T("intro.go");
            _skip.Show(_page < Cards - 1);
            float dir = _page >= old ? 1f : -1f;
            UiMotion.SlideIn(_pic, new Vector2(40f * dir, 0f), 0.3f);
        }

        public override void Tick()
        {
            if (!Visible) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rightArrowKey.wasPressedThisFrame && _page < Cards - 1) Go(_page + 1);
            if (kb.leftArrowKey.wasPressedThisFrame) Go(_page - 1);
        }
    }

    // ====================================================================== moment marker

    /// <summary>
    /// Private playtest bookmark: F8 or the small flag opens a category picker; the category (never text) goes to
    /// <see cref="TelemetryHooks.MomentMarked"/>. Client-side limits: one per 20 s, 15 per match. Nobody else sees it.
    /// </summary>
    public sealed class MomentMarker : Modal
    {
        public const float MinGap = 20f;
        public const int MaxPerMatch = 15;
        public static readonly string[] Categories = { "funny", "betrayal", "accusation", "surprise", "reveal", "other" };
        static MomentMarker _i;
        static float _lastAt = -999f;
        static int _count, _countWeek = int.MinValue;

        readonly VisualElement _flagWrap;
        readonly Label _toast;
        readonly Button _flag;
        float _toastUntil;

        public MomentMarker(VisualElement parent) : base(parent)
        {
            _i = this;
            Panel.style.minWidth = 640;
            Title(Loc.T("moment.title"), "h2").AddTo(Panel);
            Text(Loc.T("moment.note"), "confirm-text").AddTo(Panel);
            var grid = Box("moment-grid").AddTo(Panel);
            foreach (var c in Categories)
            {
                string cat = c;
                Btn(Loc.T("moment.cat." + cat), () => Mark(cat), "btn-secondary", "moment-cat").AddTo(grid);
            }
            var actions = Box("modal-actions").AddTo(Panel);
            Btn(Loc.T("common.cancel"), Close, "btn-ghost").AddTo(actions);

            _flagWrap = Box("moment-flag-wrap").AddTo(parent);
            _toast = Bold("", "moment-toast").NoPick().AddTo(_flagWrap);
            _toast.Show(false);
            _flag = IconBtn("flag", "", () => Toggle(), "btn-small", "btn-secondary", "moment-flag").AddTo(_flagWrap);
            _flagWrap.Show(false);
        }

        public static bool Available
        {
            get
            {
                var p = TelemetryHooks.Privacy;
                var c = LineClient.I;
                return p != null && p.MomentMarkerAvailable && c != null && c.View != null;
            }
        }

        public static void Toggle()
        {
            if (_i == null || !Available) return;
            if (_i.Visible) _i.Close(); else _i.OpenModal();
        }

        /// <summary>Rate-limit check; returns null when allowed, else the loc key of the reason.</summary>
        public static string Limit(float now, int week)
        {
            if (week != _countWeek) { _countWeek = week; _count = 0; }
            if (_count >= MaxPerMatch) return "moment.limit";
            if (now - _lastAt < MinGap) return "moment.wait";
            return null;
        }

        void Mark(string category)
        {
            var c = LineClient.I;
            string why = Limit(Time.unscaledTime, c != null && c.View != null ? c.View.WeekId : 0);
            Close();
            if (why != null) { Toast(Loc.F(why, Mathf.Max(1, Mathf.CeilToInt(MinGap - (Time.unscaledTime - _lastAt))), MaxPerMatch)); return; }
            _lastAt = Time.unscaledTime;
            _count++;
            TelemetryHooks.MomentMarked(category);
            Sfx.Play("click", 0.6f);
            Toast(Loc.T("moment.marked"));
        }

        void Toast(string s)
        {
            _toast.text = s;
            _toast.Show(true);
            _toastUntil = Time.unscaledTime + 2.5f;
            UiMotion.PopIn(_toast, 0.2f, 0f, 0.8f);
        }

        public override void Tick()
        {
            bool avail = Available;
            _flagWrap.Show(avail);
            if (!avail) { if (Visible) Close(); return; }
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) Toggle();
            if (_toast.IsShown() && Time.unscaledTime > _toastUntil) _toast.Show(false);
        }
    }
}
