using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Top-down security-camera floor plan drawn from GameConfig.Layout (rooms, desks, cameras) with pooled player
    /// dots. Positions are percentages of the plan, so moving a dot is a style write (no allocation, no layout pass
    /// of the whole tree). North (+Z) is up.
    /// </summary>
    public sealed class FloorPlan
    {
        public readonly VisualElement Root;
        readonly VisualElement _zone, _dotLayer, _overlay;
        readonly List<Dot> _dots = new List<Dot>();
        readonly Dictionary<int, VisualElement> _desks = new Dictionary<int, VisualElement>();
        readonly float _minX, _maxX, _minZ, _maxZ;

        public FloorPlan(VisualElement parent, float width, float height, bool overlay)
        {
            Root = Box("plan").AddTo(parent);
            Root.style.width = width;
            Root.style.height = height;
            var layout = GameConfig.Layout;
            _minX = layout.BoundsMin.X; _maxX = layout.BoundsMax.X;
            _minZ = layout.BoundsMin.Z; _maxZ = layout.BoundsMax.Z;
            if (_maxX - _minX < 1f) { _minX = -16; _maxX = 16; }
            if (_maxZ - _minZ < 1f) { _minZ = -10; _maxZ = 10; }

            foreach (var r in layout.Rooms)
            {
                var e = Box("plan-room").NoPick().AddTo(Root);
                PlaceRect(e, r.Min.X, r.Min.Z, r.Max.X, r.Max.Z);
                Text(Loc.Room(r.Id), "plan-room-label").NoPick().AddTo(e);
            }
            _zone = Box("plan-zone").NoPick().AddTo(Root);
            _zone.Show(false);
            foreach (var d in layout.Desks)
            {
                var e = Box("plan-desk").NoPick().AddTo(Root);
                PlaceRect(e, d.Position.X - 0.8f, d.Position.Z - 0.4f, d.Position.X + 0.8f, d.Position.Z + 0.4f);
                _desks[d.Index] = e;
            }
            foreach (var c in layout.Cameras)
            {
                var e = Box("plan-cam").NoPick().AddTo(Root);
                e.style.left = Length.Percent(PX(c.Position.X));
                e.style.top = Length.Percent(PZ(c.Position.Z));
            }
            _dotLayer = Box().NoPick().AddTo(Root);
            _dotLayer.style.position = Position.Absolute;
            _dotLayer.style.left = 0; _dotLayer.style.top = 0; _dotLayer.style.right = 0; _dotLayer.style.bottom = 0;
            if (overlay)
            {
                _overlay = Box("plan-overlay").NoPick().AddTo(Root);
                if (!UiArt.Apply(_overlay, "meeting/cctv_overlay")) _overlay.Show(false);
            }
        }

        float PX(float x) { return (x - _minX) / (_maxX - _minX) * 100f; }
        float PZ(float z) { return (_maxZ - z) / (_maxZ - _minZ) * 100f; }

        void PlaceRect(VisualElement e, float x0, float z0, float x1, float z1)
        {
            e.style.left = Length.Percent(PX(x0));
            e.style.top = Length.Percent(PZ(z1));
            e.style.width = Length.Percent((x1 - x0) / (_maxX - _minX) * 100f);
            e.style.height = Length.Percent((z1 - z0) / (_maxZ - _minZ) * 100f);
        }

        public void ShowZone(CameraDef cam)
        {
            _zone.Show(cam != null);
            if (cam != null) PlaceRect(_zone, cam.ZoneMin.X, cam.ZoneMin.Z, cam.ZoneMax.X, cam.ZoneMax.Z);
        }

        public void HighlightDesk(int desk)
        {
            foreach (var kv in _desks) kv.Value.EnableInClassList("plan-desk-hot", kv.Key == desk);
        }

        /// <summary>Pooled dot i (created on demand, construction only).</summary>
        public Dot GetDot(int i)
        {
            while (_dots.Count <= i) _dots.Add(new Dot(_dotLayer));
            return _dots[i];
        }

        public void HideDotsFrom(int count)
        {
            for (int i = count; i < _dots.Count; i++) _dots[i].Hide();
        }

        public void SetDot(Dot d, float x, float z)
        {
            d.Root.style.left = Length.Percent(PX(x));
            d.Root.style.top = Length.Percent(PZ(z));
        }

        public VisualElement AddOverlayChild(VisualElement e) { Root.Add(e); return e; }

        public sealed class Dot
        {
            public readonly VisualElement Root;
            readonly VisualElement _icon;
            readonly Label _name;
            int _player = -99;
            PublicActivity _activity = (PublicActivity)255;

            public Dot(VisualElement parent)
            {
                Root = Box("plan-dot").NoPick().AddTo(parent);
                _icon = Icon("info", "plan-dot-icon").AddTo(Root);
                _icon.Show(false);
                _name = Bold("", "plan-dot-name").NoPick().AddTo(Root);
                Root.Show(false);
            }

            public void Set(int player, string name, PublicActivity activity, bool crouched, bool actor, bool showName)
            {
                Root.Show(true);
                if (player != _player)
                {
                    _player = player;
                    Root.style.backgroundColor = PlayerColor(player);
                    _name.text = name ?? "";
                }
                _name.Show(showName);
                if (activity != _activity)
                {
                    _activity = activity;
                    string icon = UiArt.ActivityIcon(activity);
                    _icon.Show(icon != null);
                    if (icon != null) SetIcon(_icon, icon);
                }
                Root.EnableInClassList("plan-dot-crouch", crouched);
                Root.EnableInClassList("plan-dot-actor", actor);
            }

            public void SetOpacity(float a) { Root.style.opacity = a; }
            public void Hide() { Root.Show(false); }
        }
    }

    /// <summary>Static footage snapshot for a CctvFootage evidence: camera zone + every sighting (older = fainter).</summary>
    public static class FootageView
    {
        public static VisualElement Build(VisualElement parent, EvidenceView e, PlayerView v, float width, float height)
        {
            var wrap = Box("col").AddTo(parent);
            var plan = new FloorPlan(wrap, width, height, true);
            plan.ShowZone(GameConfig.Layout.GetCamera(e.CameraId));
            var hud = Box("plan-hud").NoPick();
            Box("plan-rec").NoPick().AddTo(hud);
            Bold(Loc.Camera(e.CameraId) + "  " + Loc.Time(e.WindowStart, v) + "–" + Loc.Time(e.WindowEnd, v), "plan-time").NoPick().AddTo(hud);
            plan.AddOverlayChild(hud);
            if (e.NoRecording)
            {
                var noise = Box("plan-noise").NoPick();
                Title(Loc.T("footage.no_recording"), "h2", "t-light").AddTo(noise);
                plan.AddOverlayChild(noise);
                return wrap;
            }
            bool hasPos = false;
            foreach (var s in e.Sightings) if (s.Position.X != 0f || s.Position.Z != 0f) { hasPos = true; break; }
            double span = System.Math.Max(1.0, e.WindowEnd - e.WindowStart);
            int n = 0;
            if (hasPos)
            {
                for (int i = 0; i < e.Sightings.Count; i++)
                {
                    var s = e.Sightings[i];
                    bool latest = true;
                    for (int k = i + 1; k < e.Sightings.Count; k++) if (e.Sightings[k].Player == s.Player) { latest = false; break; }
                    var d = plan.GetDot(n++);
                    d.Set(s.Player, Loc.PlayerName(v, s.Player), latest ? s.Activity : PublicActivity.None, s.Crouched, false, latest);
                    plan.SetDot(d, s.Position.X, s.Position.Z);
                    d.SetOpacity(latest ? 1f : Mathf.Lerp(0.25f, 0.7f, (float)((s.Time - e.WindowStart) / span)));
                }
            }
            // timeline: one dot per sighting along the window
            var tl = Box("timeline").AddTo(wrap);
            tl.style.width = width;
            foreach (var s in e.Sightings)
            {
                var dot = Box("timeline-dot").NoPick().AddTo(tl);
                dot.style.position = Position.Absolute;
                dot.style.left = Length.Percent((float)((s.Time - e.WindowStart) / span) * 100f);
                dot.style.top = 8 + (s.Player % 3) * 4;
                dot.style.backgroundColor = PlayerColor(s.Player);
            }
            if (e.Sightings.Count == 0) Empty(wrap, Loc.T("footage.nobody"));
            return wrap;
        }
    }

    /// <summary>"Günün klibi": replays MatchResultView.Highlight on the floor plan (~10 s, skippable, allocation-free per frame).</summary>
    public sealed class HighlightPlayer
    {
        public readonly VisualElement Root;
        readonly FloorPlan _plan;
        readonly Label _time, _caption;
        readonly Button _skip, _replay;
        HighlightView _h;
        PlayerView _v;
        readonly List<int>[] _byPlayer = new List<int>[Protocol.MaxPlayers];
        readonly int[] _cursor = new int[Protocol.MaxPlayers];
        readonly string[] _names = new string[Protocol.MaxPlayers];
        double _t;
        float _speed = 1f;
        int _lastSec = -1;
        bool _playing, _revealed;
        public bool Playing => _playing;
        string _captionText = "";

        public HighlightPlayer(VisualElement parent, float width, float height)
        {
            Root = Box("col").AddTo(parent);
            _plan = new FloorPlan(Root, width, height, true);
            var hud = Box("plan-hud").NoPick();
            Box("plan-rec").NoPick().AddTo(hud);
            _time = Bold("", "plan-time").NoPick().AddTo(hud);
            _plan.AddOverlayChild(hud);
            _caption = Bold("", "plan-caption").NoPick();
            _plan.AddOverlayChild(_caption);
            var row = Box("row", "mt-s").AddTo(Root);
            Text(Loc.T("clip.help"), "t-small", "t-muted", "grow").AddTo(row);
            _skip = Btn(Loc.T("clip.skip"), Skip, "btn-small", "btn-ghost").AddTo(row);
            _replay = Btn(Loc.T("clip.replay"), Restart, "btn-small").AddTo(row);
            for (int i = 0; i < _byPlayer.Length; i++) _byPlayer[i] = new List<int>();
        }

        public bool Load(HighlightView h, PlayerView v)
        {
            _h = h;
            _v = v;
            for (int i = 0; i < _byPlayer.Length; i++) _byPlayer[i].Clear();
            if (h == null || h.Samples.Count == 0) { Root.Show(false); return false; }
            Root.Show(true);
            for (int i = 0; i < h.Samples.Count; i++)
            {
                int p = h.Samples[i].Player;
                if (p >= 0 && p < _byPlayer.Length) _byPlayer[p].Add(i);
            }
            for (int p = 0; p < _names.Length; p++) _names[p] = Loc.PlayerName(v, p);
            var rb = new RecapBeat { Key = string.IsNullOrEmpty(h.Key) ? "recap.highlight" : h.Key, Actor = h.Actor, DeskIndex = h.DeskIndex, Time = h.Start };
            _captionText = Loc.F("clip.caption", Describe.Recap(rb, v));
            _plan.HighlightDesk(h.DeskIndex);
            Restart();
            return true;
        }

        void Restart()
        {
            if (_h == null) return;
            double len = System.Math.Max(1.0, _h.End - _h.Start);
            _speed = (float)System.Math.Max(1.0, len / 10.0);
            _t = _h.Start;
            _playing = true;
            _revealed = false;
            _caption.text = "";
            for (int i = 0; i < _cursor.Length; i++) _cursor[i] = 0;
            _skip.Show(true);
            _replay.Show(false);
            Apply();
        }

        void Skip()
        {
            if (_h == null) return;
            _t = _h.End;
            Apply();
            Finish();
        }

        void Finish()
        {
            _playing = false;
            _revealed = true;
            _caption.text = _captionText;
            _skip.Show(false);
            _replay.Show(true);
            Apply();
            Sfx.Play("evidence", 0.7f);
        }

        public void Tick(float dt)
        {
            if (_h == null || !_playing) return;
            _t += dt * _speed;
            if (_t >= _h.End) { _t = _h.End; Finish(); return; }
            Apply();
        }

        void Apply()
        {
            int sec = (int)(_t - MatchClock.MatchStart);
            if (sec != _lastSec) { _lastSec = sec; _time.text = Loc.MatchSeconds(sec); }
            int n = 0;
            var samples = _h.Samples;
            for (int p = 0; p < _byPlayer.Length; p++)
            {
                var list = _byPlayer[p];
                if (list.Count == 0) continue;
                int c = _cursor[p];
                if (c >= list.Count) c = list.Count - 1;
                while (c + 1 < list.Count && samples[list[c + 1]].Time <= _t) c++;
                while (c > 0 && samples[list[c]].Time > _t) c--;
                _cursor[p] = c;
                var a = samples[list[c]];
                float x = a.Position.X, z = a.Position.Z;
                if (c + 1 < list.Count)
                {
                    var b = samples[list[c + 1]];
                    double span = b.Time - a.Time;
                    float f = span > 1e-4 ? Mathf.Clamp01((float)((_t - a.Time) / span)) : 0f;
                    x = Mathf.Lerp(a.Position.X, b.Position.X, f);
                    z = Mathf.Lerp(a.Position.Z, b.Position.Z, f);
                }
                var d = _plan.GetDot(n++);
                d.Set(p, _names[p], a.Activity, a.Crouched, _revealed && p == _h.Actor, true);
                _plan.SetDot(d, x, z);
            }
            _plan.HideDotsFrom(n);
        }
    }
}
