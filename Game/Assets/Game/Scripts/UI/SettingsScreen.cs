using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Settings: audio (master / sfx / voice / per-player), microphone (device, push-to-talk vs open mic, VAD
    /// threshold with a live meter, mute), controls (sensitivity, key bindings incl. UI keys), display + language.
    /// Values persist in PlayerPrefs (keys in <see cref="UiSettings"/>); voice/audio code reads them.
    /// </summary>
    public sealed class SettingsScreen : UiKit.Screen
    {
        enum Tab { Audio = 0, Mic = 1, Controls = 2, Display = 3 }
        static SettingsScreen _i;

        readonly VisualElement _body;
        readonly UiKit.TabBar _tabs;
        Tab _tab = Tab.Audio;
        VisualElement _levelFill, _vadMark;
        Label _voiceState;
        float _nextMeter;

        public SettingsScreen(VisualElement parent) : base(parent, "dim", "center")
        {
            _i = this;
            var card = Panel("panel", "settings", "col").AddTo(Root);
            var head = Box("row").AddTo(card);
            Icon("settings", "icon-l", "gap-m").AddTo(head);
            Title(Loc.T("menu.settings"), "h1", "grow").AddTo(head);
            Btn(Loc.T("common.close"), Hide, "btn-primary").AddTo(head);
            _tabs = new UiKit.TabBar(card, id => { _tab = (Tab)id; Build(); });
            _tabs.Add((int)Tab.Audio, Loc.T("settings.tab.audio"), "speaker");
            _tabs.Add((int)Tab.Mic, Loc.T("settings.tab.mic"), "mic_on");
            _tabs.Add((int)Tab.Controls, Loc.T("settings.tab.controls"), "info");
            _tabs.Add((int)Tab.Display, Loc.T("settings.tab.display"), "objective");
            var scroll = new ScrollView().Cls("settings-body").AddTo(card);
            _body = scroll.contentContainer;
        }

        public static void Open()
        {
            if (_i == null) return;
            _i.SetVisible(true);
        }

        public void Hide()
        {
            UiSettings.Save();
            VoiceSettingsNow().Save();
            SetMicTest(false);
            var vs = VoiceSystem.Instance;
            if (vs != null && vs.LoopbackTest) vs.LoopbackTest = false;
            SetVisible(false);
            Hud.RefreshKeys();
        }

        protected override void OnShow() { Build(); }

        void Build()
        {
            _body.Clear();
            _levelFill = null;
            _vadMark = null;
            _voiceState = null;
            SetMicTest(false);
            switch (_tab)
            {
                case Tab.Audio: BuildAudio(); break;
                case Tab.Mic: BuildMic(); break;
                case Tab.Controls: BuildControls(); break;
                case Tab.Display: BuildDisplay(); break;
            }
        }

        static Slider SliderRow(VisualElement parent, string labelKey, float min, float max, float value, System.Action<float> onChange, bool percent = true)
        {
            var row = Box("setting-row").AddTo(parent);
            var s = new Slider(Loc.T(labelKey), min, max) { value = value }.Cls("slider", "grow").AddTo(row);
            var val = Text(percent ? Mathf.RoundToInt(value / max * 100f) + "%" : value.ToString("0.00"), "setting-value").AddTo(row);
            s.RegisterValueChangedCallback(e =>
            {
                onChange(e.newValue);
                val.text = percent ? Mathf.RoundToInt(e.newValue / max * 100f) + "%" : e.newValue.ToString("0.00");
            });
            return s;
        }

        void BuildAudio()
        {
            Section(_body, Loc.T("settings.volumes"), "speaker");
            SliderRow(_body, "settings.volume", 0f, 1f, Sfx.Volume, v => Sfx.Volume = v);
            SliderRow(_body, "settings.sfx_volume", 0f, 1f, Sfx.EffectsVolume, v => Sfx.EffectsVolume = v);
            SliderRow(_body, "settings.ambience_volume", 0f, 1f, Sfx.AmbienceVolume, v => Sfx.AmbienceVolume = v);
            var vset = VoiceSettingsNow();
            SliderRow(_body, "settings.voice_volume", 0f, 2f, vset.MasterVolume, v => vset.MasterVolume = v);

            Section(_body, Loc.T("settings.player_volumes"), "contacts");
            var names = new List<(int id, string name)>();
            var cs = ClientState.I;
            if (cs != null && cs.View != null) foreach (var p in cs.View.Players) { if (p.Id != cs.MySlot) names.Add((p.Id, p.Name)); }
            else if (cs != null && cs.Lobby != null) foreach (var s in cs.Lobby.Slots) { if (s.Occupied && !s.IsBot && s.Slot != cs.MySlot) names.Add((s.Slot, s.Name)); }
            if (names.Count == 0) Empty(_body, Loc.T("settings.player_volumes_empty"));
            foreach (var (id, name) in names)
            {
                var row = Box("setting-row").AddTo(_body);
                Badge(id, "badge-s").AddTo(row);
                int slot = id;
                string n = name;
                var vs = VoiceSystem.Instance;
                float cur = vs != null ? vs.GetPlayerVolume(slot) : vset.GetPlayerVolume(n);
                var s = new Slider(n, 0f, 2f) { value = cur }.Cls("slider", "grow").AddTo(row);
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
            _voiceState = Bold(vs != null ? Loc.T("settings.voice_ready") : Loc.T("settings.voice_missing"), vs != null ? "t-good" : "t-accent").AddTo(_body);
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
            SliderRow(_body, "settings.vad_threshold", 0f, 1f, set.VadSensitivity, v => set.VadSensitivity = v);
            SliderRow(_body, "settings.input_gain", 0f, 4f, set.InputGain, v => set.InputGain = v);
            if (vs != null)
            {
                var loop = new Toggle(Loc.T("settings.loopback")) { value = vs.LoopbackTest }.Cls("toggle").AddTo(_body);
                loop.RegisterValueChangedCallback(e => { var sys = VoiceSystem.Instance; if (sys != null) sys.LoopbackTest = e.newValue; });
            }
            SetMicTest(true);
        }

        void BuildControls()
        {
            Section(_body, Loc.T("settings.mouse"), "settings");
            SliderRow(_body, "settings.sensitivity", 0.03f, 0.4f, PlayerPrefs.GetFloat("settings.sensitivity", 0.12f), v =>
            {
                PlayerPrefs.SetFloat("settings.sensitivity", v);
                if (CameraRig.I != null) CameraRig.I.Sensitivity = v;
            }, false);
            SliderRow(_body, "settings.camera_shake", 0f, 1f, PlayerPrefs.GetFloat(UiSettings.CameraShakeKey, 1f), v =>
            {
                PlayerPrefs.SetFloat(UiSettings.CameraShakeKey, v);
                if (CameraRig.I != null) CameraRig.I.JuiceScale = v;
            });
            Section(_body, Loc.T("settings.bindings"), "info");
            foreach (var (action, index, label) in GameInput.Rebindable) BindRow(action, index, label);
            Text(Loc.T("settings.quick_emotes"), "t-small", "t-muted").AddTo(_body);
            var row = Box("row", "mt-m").AddTo(_body);
            Btn(Loc.T("settings.reset_bindings"), () => { GameInput.ResetBindings(); Build(); }, "btn-small", "btn-ghost").AddTo(row);
        }

        void BindRow(UnityEngine.InputSystem.InputAction action, int index, string label)
        {
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
            Section(_body, Loc.T("settings.display"), "objective");
            var fs = new Toggle(Loc.T("settings.fullscreen")) { value = UnityEngine.Screen.fullScreen }.Cls("toggle").AddTo(_body);
            fs.RegisterValueChangedCallback(e => UnityEngine.Screen.fullScreen = e.newValue);
            Section(_body, Loc.T("settings.language"), "info");
            var langs = new List<string> { Loc.T("settings.lang_tr") };
            var dd = new DropdownField(Loc.T("settings.language"), langs, 0).Cls("dropdown").AddTo(_body);
            dd.SetEnabled(false);
            Text(Loc.T("settings.language_soon"), "t-small", "t-muted").AddTo(_body);
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

    /// <summary>"Nasıl Oynanır": six illustrated pages.</summary>
    public sealed class HowToPlayScreen : UiKit.Screen
    {
        static HowToPlayScreen _i;
        const int Pages = 6;
        /// <summary>Page illustration (Art/UI/howto, 1280x720) and the fallback icons used when it is missing.</summary>
        static readonly string[] PageArt = { "howto/howto_1_work", "howto/howto_2_sabotage", "howto/howto_3_manager", "howto/howto_4_calls", "howto/howto_5_meeting", "howto/howto_6_week" };
        static readonly string[][] Art =
        {
            new[] { "logo/logo_mark", "icons/quota", "icons/objective", "icons/hr_seal" },
            new[] { "cards/card_fake_email", "cards/card_salt", "icons/sab_nameplate", "icons/sab_plant" },
            new[] { "portraits/mgr_paragoz", "portraits/mgr_disiplinci", "portraits/mgr_kayirmaci", "portraits/mgr_paranoyak" },
            new[] { "phone/app_contacts", "icons/ringing", "icons/email", "icons/mic_on" },
            new[] { "icons/accuse", "icons/ballot", "icons/footage", "icons/notes" },
            new[] { "icons/week_calendar", "week/week_trophy", "icons/star", "icons/crown" },
        };
        static readonly string[][] LineIcons =
        {
            new[] { "contacts", "quota", "objective", "hr_seal", "info" },
            new[] { "sab_plant", "cards", "lock_screen", "warning" },
            new[] { "manager", "warning", "suspicion_2", "ringing", "favor_heart" },
            new[] { "call", "mic_on", "email", "emote_wave" },
            new[] { "accuse", "notes", "footage", "ballot" },
            new[] { "week_calendar", "cards", "star", "crown", "check" },
        };

        readonly VisualElement _art, _lines, _dots;
        readonly Label _title, _pageLabel;
        readonly Button _prev, _next;
        int _page;

        public HowToPlayScreen(VisualElement parent) : base(parent, "dim", "center")
        {
            _i = this;
            var card = Panel("panel-folder", "howto", "col").AddTo(Root);
            var head = Box("row").AddTo(card);
            _title = Title("", "h1", "grow").AddTo(head);
            _pageLabel = Bold("", "t-muted", "gap-m").AddTo(head);
            Btn(Loc.T("common.close"), Hide, "btn-ghost", "btn-small").AddTo(head);
            var page = Box("howto-page").AddTo(card);
            _art = Box("howto-art").AddTo(page);
            _lines = Box("howto-text").AddTo(page);
            _dots = Box("dots").AddTo(card);
            for (int i = 0; i < Pages; i++)
            {
                int p = i;
                var d = Box("dot").AddTo(_dots);
                d.RegisterCallback<ClickEvent>(_ => Show(p));
            }
            var nav = Box("row").AddTo(card);
            _prev = Btn(Loc.T("howto.prev"), () => Show(_page - 1), "btn-ghost").AddTo(nav);
            Box("spacer").AddTo(nav);
            _next = Btn(Loc.T("howto.next"), () => { if (_page < Pages - 1) Show(_page + 1); else Hide(); }, "btn-primary").AddTo(nav);
        }

        public static void Open()
        {
            if (_i == null) return;
            _i.SetVisible(true);
            _i.Show(0);
        }

        public void Hide() { SetVisible(false); }

        protected override void OnHide()
        {
            _art.Clear(); // drop references, then unload the 1280x720 pictures
            for (int i = 0; i < PageArt.Length; i++) UiArt.Release(PageArt[i]);
        }

        void Show(int page)
        {
            _page = Mathf.Clamp(page, 0, Pages - 1);
            _title.text = Loc.T("howto." + _page + ".title");
            _pageLabel.text = (_page + 1) + " / " + Pages;
            _art.Clear();
            var pic = Box("howto-pic").AddTo(_art);
            if (UiArt.Apply(pic, PageArt[_page])) { }
            else foreach (var path in Art[_page])
            {
                pic.RemoveFromHierarchy();
                var e = Box("icon", path.StartsWith("cards/") ? "icon-xxl" : path.StartsWith("logo/") ? "icon-xxl" : "icon-xl").AddTo(_art);
                if (path.StartsWith("cards/")) e.style.height = 224;
                if (!UiArt.Apply(e, path)) SetIcon(e, path.Substring(path.IndexOf('/') + 1));
            }
            _lines.Clear();
            for (int i = 0; i < 8; i++)
            {
                string key = "howto." + _page + ".line." + i;
                if (!Loc.Has(key)) break;
                var r = Box("howto-line").AddTo(_lines);
                string icon = i < LineIcons[_page].Length ? LineIcons[_page][i] : "info";
                Icon(icon).AddTo(r);
                Text(Loc.T(key), "howto-line-text").AddTo(r);
            }
            for (int i = 0; i < _dots.childCount; i++) _dots[i].EnableInClassList("dot-on", i == _page);
            _prev.SetEnabled(_page > 0);
            _next.text = _page < Pages - 1 ? Loc.T("howto.next") : Loc.T("howto.done");
            Pop(_lines);
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
