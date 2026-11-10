using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Settings: audio (master / sfx / ambience / voice / per-player), microphone (device, push-to-talk vs open mic,
    /// VAD threshold with a live meter, mute), controls (first-person look: sensitivity, invert Y, field of view; the
    /// v4 key bindings), display (fullscreen is remembered) + language (switches live: the whole UI is rebuilt by
    /// <see cref="UIRoot"/>), and the Privacy tab (analytics consent and local data controls, when telemetry is installed).
    /// Values persist in PlayerPrefs (keys in <see cref="UiSettings"/> and <see cref="Rooms"/>).
    /// </summary>
    public sealed class SettingsScreen : UiKit.Screen
    {
        public enum Tab { Audio = 0, Mic = 1, Controls = 2, Display = 3, Privacy = 4 }
        static SettingsScreen _i;

        /// <summary>First-person look defaults (the world reads the same PlayerPrefs keys).</summary>
        public const float DefaultSensitivity = 1f, DefaultFov = 65f;

        readonly VisualElement _card, _body;
        readonly UiKit.TabBar _tabs;
        Tab _tab = Tab.Audio;
        VisualElement _levelFill;
        float _nextMeter;
        string _musicBucket;

        public SettingsScreen(VisualElement parent) : base(parent, "dim", "center")
        {
            _i = this;
            _card = Panel("panel", "settings", "col").AddTo(Root);
            var head = Box("row").AddTo(_card);
            Icon("settings", "icon-l", "gap-m").AddTo(head);
            Title(Loc.T("menu.settings"), "h1", "grow").AddTo(head);
            Btn(Loc.T("common.close"), Hide, "btn-primary").AddTo(head);
            _tabs = new UiKit.TabBar(_card, id => { _tab = (Tab)id; Build(); });
            _tabs.Add((int)Tab.Audio, Loc.T("settings.tab.audio"), "speaker");
            _tabs.Add((int)Tab.Mic, Loc.T("settings.tab.mic"), "mic_on");
            _tabs.Add((int)Tab.Controls, Loc.T("settings.tab.controls"), "info");
            _tabs.Add((int)Tab.Display, Loc.T("settings.tab.display"), "objective");
            _tabs.Add((int)Tab.Privacy, Loc.T("settings.tab.privacy"), "lock_screen");
            var scroll = new ScrollView().Cls("settings-body").AddTo(_card);
            _body = scroll.contentContainer;
        }

        public static void Open() { Open(-1); }

        /// <summary>Opens on a tab (-1 = the last one).</summary>
        public static void Open(int tab)
        {
            if (_i == null) return;
            bool wasOpen = _i.Visible;
            if (tab == (int)Tab.Privacy && TelemetryHooks.Privacy == null) tab = (int)Tab.Display;
            if (tab >= 0) { _i._tab = (Tab)tab; _i._tabs.Select(tab, false); }
            _i.SetVisible(true);
            if (wasOpen) _i.Build();
        }

        public static Tab CurrentTab => _i != null ? _i._tab : Tab.Audio;

        public static void Close() { if (_i != null && _i.Visible) _i.Hide(); }

        public void Hide()
        {
            UiSettings.Save();
            VoiceSettingsNow().Save();
            SetMicTest(false);
            var vs = VoiceSystem.Instance;
            if (vs != null && vs.LoopbackTest) vs.LoopbackTest = false;
            PlayerPrefs.Save();
            SetVisible(false);
        }

        protected override void OnShow()
        {
            _tabs.SetTabVisible((int)Tab.Privacy, TelemetryHooks.Privacy != null);
            if (_tab == Tab.Privacy && TelemetryHooks.Privacy == null) { _tab = Tab.Audio; _tabs.Select((int)Tab.Audio, false); }
            Build();
            UiMotion.PopIn(_card, 0.26f, 0f, 0.94f);
        }

        void Build()
        {
            _body.Clear();
            _levelFill = null;
            SetMicTest(false);
            switch (_tab)
            {
                case Tab.Audio: BuildAudio(); break;
                case Tab.Mic: BuildMic(); break;
                case Tab.Controls: BuildControls(); break;
                case Tab.Display: BuildDisplay(); break;
                case Tab.Privacy: PrivacyUi.BuildTab(_body, Build); break;
            }
            UiMotion.SlideIn(_body, new Vector2(0f, 14f), 0.2f);
        }

        static Slider SliderRow(VisualElement parent, string labelKey, float min, float max, float value, System.Action<float> onChange, System.Func<float, string> format)
        {
            var row = Box("setting-row").AddTo(parent);
            var s = new Slider(Loc.T(labelKey), min, max) { value = value }.Cls("slider", "grow").AddTo(row);
            var val = Text(format(value), "setting-value").AddTo(row);
            s.RegisterValueChangedCallback(e =>
            {
                onChange(e.newValue);
                val.text = format(e.newValue);
            });
            return s;
        }

        static System.Func<float, string> Percent(float max) { return v => Mathf.RoundToInt(v / max * 100f) + "%"; }

        void BuildAudio()
        {
            Section(_body, Loc.T("settings.volumes"), "speaker");
            SliderRow(_body, "settings.volume", 0f, 1f, Sfx.Volume, v => Sfx.Volume = v, Percent(1f));
            SliderRow(_body, "settings.sfx_volume", 0f, 1f, Sfx.EffectsVolume, v => Sfx.EffectsVolume = v, Percent(1f));
            SliderRow(_body, "settings.ambience_volume", 0f, 1f, Sfx.AmbienceVolume, v => Sfx.AmbienceVolume = v, Percent(1f));
            SliderRow(_body, "settings.music_volume", 0f, 1f, MusicSettings.Volume, v =>
            {
                MusicSettings.Volume = v;
                string bucket = (Mathf.RoundToInt(v * 4f) * 25).ToString(); // coarse bucket, never the raw value
                if (bucket != _musicBucket) { _musicBucket = bucket; TelemetryHooks.SettingChanged("music_volume", bucket); }
            }, Percent(1f));
            var musicMute = new Toggle(Loc.T("settings.music_mute")) { value = MusicSettings.Muted }.Cls("toggle").AddTo(_body);
            musicMute.RegisterValueChangedCallback(e =>
            {
                MusicSettings.Muted = e.newValue;
                TelemetryHooks.SettingChanged("music_mute", e.newValue ? "on" : "off");
            });
            var vset = VoiceSettingsNow();
            SliderRow(_body, "settings.voice_volume", 0f, 2f, vset.MasterVolume, v => vset.MasterVolume = v, Percent(1f));

            Section(_body, Loc.T("settings.player_volumes"), "contacts");
            var names = new List<(int id, string name)>();
            var lc = LineClient.I;
            if (lc != null && lc.View != null) { foreach (var p in lc.View.Players) if (p.Id != lc.MySlot && !p.IsBot) names.Add((p.Id, p.Name)); }
            else if (lc != null && lc.Lobby != null) { foreach (var s in lc.Lobby.Slots) if (s.Occupied && !s.IsBot && s.Slot != lc.MySlot) names.Add((s.Slot, s.Name)); }
            if (names.Count == 0) Empty(_body, Loc.T("settings.player_volumes_empty"));
            foreach (var (id, name) in names)
            {
                var row = Box("setting-row").AddTo(_body);
                Badge(id, "badge-s").AddTo(row);
                int slot = id;
                string n = name;
                var vs = VoiceSystem.Instance;
                float cur = vs != null ? vs.GetPlayerVolume(slot) : vset.GetPlayerVolume(n);
                var s = new Slider(n, 0f, 2f) { value = cur }.Cls("slider", "grow").PlainLabel().AddTo(row); // n is a player name: no markup
                var val = Text(Mathf.RoundToInt(cur * 100f) + "%", "setting-value").AddTo(row);
                s.RegisterValueChangedCallback(e =>
                {
                    var sys = VoiceSystem.Instance;
                    if (sys != null) sys.SetPlayerVolume(slot, e.newValue); else vset.SetPlayerVolume(n, e.newValue);
                    val.text = Mathf.RoundToInt(e.newValue * 100f) + "%";
                });
            }
        }

        /// <summary>The live voice settings (VoiceSystem) or a copy loaded from PlayerPrefs (no voice / -novoice).</summary>
        VoiceSettings _offlineVoice;
        VoiceSettings VoiceSettingsNow()
        {
            var vs = VoiceSystem.Instance;
            if (vs != null && vs.Settings != null) return vs.Settings;
            return _offlineVoice ?? (_offlineVoice = VoiceSettings.Load());
        }

        void SetMicTest(bool on)
        {
            var vs = VoiceSystem.Instance;
            if (vs != null && vs.MicTest != on) vs.MicTest = on;
        }

        void BuildMic()
        {
            var vs = VoiceSystem.Instance;
            var set = VoiceSettingsNow();
            Bold(vs != null ? Loc.T("settings.voice_ready") : Loc.T("settings.voice_missing"), vs != null ? "t-good" : "t-accent").AddTo(_body);
            if (vs != null && !string.IsNullOrEmpty(vs.MicrophoneError)) Text(Loc.TOr("voice.error." + vs.MicrophoneError, "voice.error.generic"), "t-small", "t-bad").AddTo(_body);

            Section(_body, Loc.T("settings.mic_device"), "mic_on");
            var devices = new List<string> { Loc.T("settings.mic_default") };
            try { foreach (var d in VoiceSystem.InputDevices) devices.Add(d); }
            catch (System.Exception) { }
            int idx = Mathf.Max(0, devices.IndexOf(set.InputDevice));
            var dd = new DropdownField(Loc.T("settings.mic_device"), devices, idx).Cls("dropdown").AddTo(_body);
            dd.RegisterValueChangedCallback(e => set.InputDevice = dd.index <= 0 ? "" : e.newValue);

            Section(_body, Loc.T("settings.mic_mode"), "speaker");
            var modeRow = Box("row").AddTo(_body);
            bool ptt = set.InputMode == VoiceInputMode.PushToTalk;
            Btn(Loc.F("settings.ptt", UiServices.Voice.PttKeyLabel), () => { set.InputMode = VoiceInputMode.PushToTalk; Build(); }, "btn-small", ptt ? "btn-selected" : "btn-secondary").AddTo(modeRow);
            Btn(Loc.T("settings.open_mic"), () => { set.InputMode = VoiceInputMode.OpenMic; Build(); }, "btn-small", !ptt ? "btn-selected" : "btn-secondary").AddTo(modeRow);
            var mute = new Toggle(Loc.T("settings.mic_muted")) { value = set.SelfMute }.Cls("toggle").AddTo(_body);
            mute.RegisterValueChangedCallback(e => set.SelfMute = e.newValue);
            var deafen = new Toggle(Loc.T("settings.deafen")) { value = set.Deafen }.Cls("toggle").AddTo(_body);
            deafen.RegisterValueChangedCallback(e => set.Deafen = e.newValue);

            Section(_body, Loc.T("settings.vad"), "mic_on");
            Text(Loc.T(ptt ? "settings.vad_help_ptt" : "settings.vad_help"), "t-small", "t-muted").AddTo(_body);
            var meter = Box("mic-test").AddTo(_body);
            Text(Loc.T("settings.mic_level"), "setting-label").AddTo(meter);
            Bar(0, out _levelFill, "bar-teal").AddTo(meter);
            SliderRow(_body, "settings.vad_threshold", 0f, 1f, set.VadSensitivity, v => set.VadSensitivity = v, Percent(1f));
            SliderRow(_body, "settings.input_gain", 0f, 4f, set.InputGain, v => set.InputGain = v, Percent(1f));
            if (vs != null)
            {
                var loop = new Toggle(Loc.T("settings.loopback")) { value = vs.LoopbackTest }.Cls("toggle").AddTo(_body);
                loop.RegisterValueChangedCallback(e => { var sys = VoiceSystem.Instance; if (sys != null) sys.LoopbackTest = e.newValue; });
            }
            SetMicTest(true);
        }

        void BuildControls()
        {
            // first-person look (the world reads these keys every time it applies the camera settings)
            Section(_body, Loc.T("settings.look"), "settings");
            SliderRow(_body, "settings.look_sensitivity", 0.2f, 3f, PlayerPrefs.GetFloat(Rooms.PrefSensitivity, DefaultSensitivity), v =>
UiSettings.SetFloat(Rooms.PrefSensitivity, v), v => v.ToString("0.00") + "×");
            var inv = new Toggle(Loc.T("settings.invert_y")) { value = PlayerPrefs.GetInt(Rooms.PrefInvertY, 0) == 1 }.Cls("toggle").AddTo(_body);
            inv.RegisterValueChangedCallback(e => UiSettings.SetInt(Rooms.PrefInvertY, e.newValue ? 1 : 0));
            SliderRow(_body, "settings.fov", 50f, 90f, PlayerPrefs.GetFloat(Rooms.PrefFov, DefaultFov), v => UiSettings.SetFloat(Rooms.PrefFov, Mathf.Round(v)), v => Mathf.RoundToInt(v) + "°");
            var resetRow = Box("row").AddTo(_body);
            Btn(Loc.T("settings.look_reset"), () =>
            {
                UiSettings.SetFloat(Rooms.PrefSensitivity, DefaultSensitivity);
                UiSettings.SetInt(Rooms.PrefInvertY, 0);
                UiSettings.SetFloat(Rooms.PrefFov, DefaultFov);
                Build();
            }, "btn-small", "btn-ghost").AddTo(resetRow);

            Section(_body, Loc.T("settings.bindings"), "info");
            BindRow(GameInput.Phone, 0, "input.phone");
            BindRow(GameInput.PushToTalk, 0, "input.push_to_talk");
            BindRow(GameInput.AnswerCall, 0, "input.answer_call");
            BindRow(GameInput.HangUp, 0, "input.hang_up");
            var fixedRow = Box("bind-row").AddTo(_body);
            Text(Loc.T("settings.bind_fixed"), "bind-label").AddTo(fixedRow);
            KeyCap(Loc.T("ctl.mouse_key")).AddTo(fixedRow);
            KeyCap(Loc.T("ctl.click_key")).AddTo(fixedRow);
            KeyCap("Esc").AddTo(fixedRow);
            var row = Box("row", "mt-m").AddTo(_body);
            Btn(Loc.T("settings.reset_bindings"), () => { GameInput.ResetBindings(); Build(); }, "btn-small", "btn-ghost").AddTo(row);
        }

        void BindRow(UnityEngine.InputSystem.InputAction action, int index, string label)
        {
            if (action == null) return;
            var r = Box("bind-row").AddTo(_body);
            Text(Loc.T(label), "bind-label").AddTo(r);
            Button b = null;
            b = Btn(GameInput.DisplayName(action, index), () =>
            {
                b.text = Loc.T("settings.press_key");
                GameInput.StartRebind(action, index, Build);
            }, "btn-small").AddTo(r);
            b.style.minWidth = 160;
        }

        void BuildDisplay()
        {
            Section(_body, Loc.T("settings.language"), "info");
            var langs = Box("lang-cards").AddTo(_body);
            LangCard(langs, "tr", "Türkçe", "Birimiz Kovulacak");
            LangCard(langs, "en", "English", "Someone Gets Fired");
            Text(Loc.T("settings.language_note"), "t-small", "t-muted").AddTo(_body);

            Section(_body, Loc.T("settings.display"), "objective");
            var fs = new Toggle(Loc.T("settings.fullscreen")) { value = DisplaySettings.Fullscreen }.Cls("toggle").AddTo(_body);
            fs.RegisterValueChangedCallback(e =>
            {
                DisplaySettings.Fullscreen = e.newValue; // remembered across launches
                TelemetryHooks.SettingChanged("fullscreen", e.newValue ? "on" : "off");
            });
            Text(Loc.T("settings.fullscreen_note"), "t-small", "t-muted").AddTo(_body);
        }

        static void LangCard(VisualElement parent, string lang, string name, string sub)
        {
            bool on = Loc.Language == lang;
            var b = Btn("", () => { if (Loc.Language != lang) { TelemetryHooks.SettingChanged("language", lang); Loc.SetLanguage(lang); } }, on ? "btn-selected" : "btn-secondary", "lang-card").AddTo(parent);
            Title(name, "lang-card-name").NoPick().AddTo(b);
            Text(sub, "lang-card-sub").NoPick().AddTo(b);
            if (on) Tag(Loc.T("settings.language_current"), "tag-green", "lang-card-tag").AddTo(b);
        }

        public override void Tick()
        {
            if (!Visible || _levelFill == null || Time.unscaledTime < _nextMeter) return;
            _nextMeter = Time.unscaledTime + 0.05f;
            var voice = UiServices.Voice;
            float lvl = voice.Available ? Mathf.Clamp01(voice.MicLevel) : 0f;
            _levelFill.style.width = Length.Percent(lvl * 100f);
            _levelFill.EnableInClassList("good", voice.Transmitting);
        }
    }

    /// <summary>
    /// "Nasıl Oynanır" (v5): eleven illustrated pages, a few short lines each — goal, match flow, the three tasks,
    /// blame votes, investigation, phone/recorder/leaks, transfers and promises, crisis and candidates (sealed desks),
    /// final and the desk split. Numbers come from the host's LineConfig during a match, else from the local config
    /// file (GameConfig.Line), so the pages never drift from the rules. The play screen opens the page for its phase.
    /// </summary>
    public sealed class HowToPlayScreen : UiKit.Screen
    {
        static HowToPlayScreen _i;
        public const int PageGoal = 0, PageFlow = 1, PageBonus = 2, PageCash = 3, PageShift = 4, PageBlame = 5, PageInvestigation = 6,
            PageRecorder = 7, PageTransfers = 8, PageCrisis = 9, PageFinal = 10;
        public const int Pages = 11;
        /// <summary>Illustration per page; null = the icon tile below.</summary>
        static readonly string[] PageArt =
        {
            "onboarding/ob_1_rooms", "onboarding_v4/intro_3_meeting", "onboarding/ob_3_rounds", null, null, "onboarding/ob_4_blame", null,
            "onboarding/ob_6_recording", "onboarding/ob_2_phone", null, "onboarding/ob_7_friday",
        };
        /// <summary>Fallback when an illustration is missing: one big icon on a paper tile.</summary>
        static readonly string[] PageIcon =
        {
            "icons_v3/meeting", "icons_v3/agenda_app", "icons_v3/share", "icons_v3/take_all", "icons_v3/sticky_todo", "icons_v3/blame",
            "icons_v3/agenda_target", "icons_v3/recording", "icons_v3/manager_line", "icons_v3/warning_letter", "icons_v3/defense",
        };

        /// <summary>The rules in force: the host's config in a match, else the local one.</summary>
        static LineConfig Config
        {
            get
            {
                var lc = LineClient.I;
                return lc != null && lc.HostConfig != null ? lc.HostConfig : GameConfig.Line;
            }
        }

        /// <summary>Placeholder values of page <paramref name="page"/>'s text (keep in step with tr/en.json howto.N.text).</summary>
        static object[] Args(int page)
        {
            var c = Config;
            switch (page)
            {
                case PageGoal: return new object[] { c.StartPrim };
                case PageBonus: return new object[] { c.BonusPairShare, c.BonusPairTaker, c.BonusPairSharer, c.BonusPairBothTake };
                case PageCash: return new object[] { c.CashTake, c.CashCaughtBlame, c.CashWhistleBlame, c.CashWhistleReward };
                case PageShift: return new object[] { c.ShiftSkipPay };
                case PageBlame: return new object[] { c.BlamePerVote };
                case PageInvestigation: return new object[] { c.InvestigationCost, c.InvestigationGuiltyBlame, c.InvestigationRefund, c.InvestigationFalseBlame };
                case PageRecorder: return new object[] { c.RecorderUses, Mathf.RoundToInt(c.LeakSeconds) };
                case PageTransfers: return new object[] { c.TransferMax };
                case PageCrisis: return new object[] { c.CrisisBlame, c.CandidatesFor(4), c.CandidatesFor(6) };
                case PageFinal: return new object[] { c.FiringsFor(4), c.FiringsFor(6) };
                default: return new object[0];
            }
        }

        readonly VisualElement _card, _pic, _text, _dots;
        readonly Label _title, _body, _pageLabel, _kicker;
        readonly Button _prev, _next;
        int _page;

        public HowToPlayScreen(VisualElement parent) : base(parent, "dim", "center")
        {
            _i = this;
            _card = Panel("panel", "howto", "col").AddTo(Root);
            var head = Box("row").AddTo(_card);
            Title(Loc.T("howto.title"), "h2", "grow").AddTo(head);
            _pageLabel = Bold("", "t-muted", "gap-m").AddTo(head);
            Btn(Loc.T("common.close"), Hide, "btn-ghost", "btn-small").AddTo(head);
            var page = Box("howto-page").AddTo(_card);
            _pic = Box("howto-pic").AddTo(page);
            _text = Box("howto-text").AddTo(page);
            _kicker = Bold("", "howto-kicker").AddTo(_text);
            _title = Title("", "howto-title").AddTo(_text);
            _body = Text("", "howto-body").AddTo(_text);
            _dots = Box("dots").AddTo(_card);
            for (int i = 0; i < Pages; i++)
            {
                int p = i;
                var d = Box("dot").AddTo(_dots);
                d.RegisterCallback<ClickEvent>(_ => Show(p));
            }
            var nav = Box("row").AddTo(_card);
            _prev = Btn(Loc.T("howto.prev"), () => Show(_page - 1), "btn-ghost").AddTo(nav);
            Box("spacer").AddTo(nav);
            _next = Btn(Loc.T("howto.next"), () => { if (_page < Pages - 1) Show(_page + 1); else Hide(); }, "btn-primary").AddTo(nav);
        }

        public static void Open()
        {
            if (_i == null) return;
            _i.SetVisible(true);
            _i.Show(0);
            UiMotion.PopIn(_i._card, 0.28f, 0f, 0.94f);
        }

        /// <summary>Mocks: open on a page.</summary>
        public static void OpenAt(int page)
        {
            if (_i == null) return;
            _i.SetVisible(true);
            _i.Show(page);
        }

        public void Hide() { SetVisible(false); }

        public static void Close() { if (_i != null) _i.SetVisible(false); }

        protected override void OnHide()
        {
            _pic.style.backgroundImage = StyleKeyword.Null; // drop references, then unload the 1280x720 pictures
            _pic.Clear();
            for (int i = 0; i < PageArt.Length; i++) if (PageArt[i] != null) UiArt.Release(PageArt[i]);
        }

        void Show(int page)
        {
            int old = _page;
            _page = Mathf.Clamp(page, 0, Pages - 1);
            _kicker.text = Loc.F("howto.kicker", _page + 1, Pages);
            _title.text = Loc.T("howto." + _page + ".title");
            _body.text = Loc.F("howto." + _page + ".text", Args(_page));
            _pageLabel.text = (_page + 1) + " / " + Pages;
            _pic.Clear();
            _pic.style.backgroundImage = StyleKeyword.Null;
            bool art = PageArt[_page] != null && UiArt.Apply(_pic, PageArt[_page]);
            _pic.EnableInClassList("howto-pic-fallback", !art);
            if (!art)
            {
                var icon = Box("icon", "icon-xxl").AddTo(_pic);
                UiArt.ApplyIcon(icon, PageIcon[_page]);
            }
            for (int i = 0; i < _dots.childCount; i++) _dots[i].EnableInClassList("dot-on", i == _page);
            _prev.SetEnabled(_page > 0);
            _next.text = _page < Pages - 1 ? Loc.T("howto.next") : Loc.T("howto.done");
            float dir = _page >= old ? 1f : -1f;
            UiMotion.SlideIn(_pic, new Vector2(40f * dir, 0f), 0.3f);
            UiMotion.SlideIn(_text, new Vector2(24f * dir, 0f), 0.3f, 0.06f);
        }

        public override void Tick()
        {
            if (!Visible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.rightArrowKey.wasPressedThisFrame) Show(_page + 1);
            if (kb.leftArrowKey.wasPressedThisFrame) Show(_page - 1);
        }
    }
}
