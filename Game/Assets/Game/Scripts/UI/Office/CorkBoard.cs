using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 suç tahtası on the office cork board: a small UI Toolkit panel rendered into a texture that replaces the
    /// board surface's decal (same art underneath). Each colleague gets one pinned note (seat order) with their name
    /// in their colour, suç, ADAY / MÜHÜRLÜ / KOVULDU stamps, an on-the-phone mark and, while a task is open, whether
    /// they have chosen. The centre note is the board's title. Rebuilt only when what it shows changes.
    /// </summary>
    public sealed class CorkBoard
    {
        const int W = 1024, H = 768;
        /// <summary>Notes of the corkboard_notes art (1024 x 768): x, y, width, height; <see cref="Tilt"/> in degrees.</summary>
        static readonly Vector4[] Notes =
        {
            new Vector4(114, 92, 228, 192), new Vector4(400, 76, 218, 182), new Vector4(110, 310, 212, 156),
            new Vector4(674, 312, 236, 158), new Vector4(114, 500, 228, 160), new Vector4(400, 518, 210, 148),
        };
        static readonly float[] Tilt = { -2f, -3f, -3f, 2f, -1.5f, 2f };
        static readonly Vector4 Center = new Vector4(366, 292, 258, 192);

        /// <summary>The board's own decal material (kept across UI rebuilds, which make a new CorkBoard).</summary>
        static Material s_decal;
        Renderer _surface;
        Material _orig, _mat;
        RenderTexture _rt;
        GameObject _go;
        PanelSettings _ps;
        VisualElement _root;
        string _sig;
        bool _failed;

        public RenderTexture Texture => _rt;

        /// <summary>Creates the panel and swaps the board's material (once per surface).</summary>
        public bool Ensure(Renderer surface, PanelSettings like)
        {
            if (_failed || surface == null) return false;
            if (_surface == surface && _root != null)
            {
                if (surface.sharedMaterial != _mat) surface.sharedMaterial = _mat; // an older board restored the decal
                return true;
            }
            try
            {
                Release();
                _surface = surface;
                _orig = surface.sharedMaterial;
                if (_orig != null && _orig.name.StartsWith("M_RmCorkBoardLive") && s_decal != null) _orig = s_decal;
                else if (_orig != null) s_decal = _orig;
                _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "CorkBoardRT", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                };
                _rt.Create();
                _ps = ScriptableObject.CreateInstance<PanelSettings>();
                _ps.name = "CorkBoardPanel";
                _ps.targetTexture = _rt;
                _ps.scaleMode = PanelScaleMode.ConstantPixelSize;
                _ps.scale = 1f;
                _ps.clearColor = true;
                _ps.colorClearValue = new Color32(0xC9, 0xA4, 0x78, 255);
                _ps.sortingOrder = -50;
                if (like != null) _ps.themeStyleSheet = like.themeStyleSheet;
                _ps.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN)); // never takes the mouse
                _go = new GameObject("CorkBoardPanel");
                _go.SetActive(false);
                Object.DontDestroyOnLoad(_go);
                var doc = _go.AddComponent<UIDocument>();
                doc.panelSettings = _ps;
                _go.SetActive(true);
                _root = doc.rootVisualElement;
                _root.pickingMode = PickingMode.Ignore;
                var sheet = Resources.Load<StyleSheet>("ui/styles/office");
                if (sheet != null) _root.styleSheets.Add(sheet);
                _root.AddToClassList("cb-root");
                _root.style.width = W; _root.style.height = H;
                var art = _orig != null ? (_orig.HasProperty("_BaseMap") ? _orig.GetTexture("_BaseMap") : _orig.mainTexture) as Texture2D : null;
                if (art != null) _root.style.backgroundImage = new StyleBackground(art);
                _mat = new Material(_orig) { name = "M_RmCorkBoardLive" };
                if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _rt);
                _mat.mainTexture = _rt;
                surface.sharedMaterial = _mat;
                _sig = null;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CorkBoard] disabled: " + e.Message);
                _failed = true;
                Release();
                return false;
            }
        }

        public void Release()
        {
            if (_surface != null && _orig != null && _surface.sharedMaterial == _mat) _surface.sharedMaterial = _orig;
            if (_go != null) Object.Destroy(_go);
            if (_ps != null) Object.Destroy(_ps);
            if (_mat != null) Object.Destroy(_mat);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            _go = null; _ps = null; _mat = null; _rt = null; _root = null; _surface = null; _sig = null;
        }

        /// <summary>Rebuilds the notes when the public board state changed.</summary>
        public void Update(LineClient lc, LineView v)
        {
            if (_root == null || v == null) return;
            string sig = Signature(lc, v);
            if (sig == _sig) return;
            _sig = sig;
            _root.Clear();
            var players = new List<LinePlayerView>(v.Players);
            players.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < players.Count && i < Notes.Length; i++) Note(Notes[i], Tilt[i], players[i], lc, v);
            Title(v);
        }

        static string Signature(LineClient lc, LineView v)
        {
            var sb = new StringBuilder(128);
            sb.Append(Loc.Language).Append('|').Append(lc.MySlot).Append('|').Append(v.WeekId).Append('|').Append((int)v.Phase).Append('|').Append(v.TaskNumber);
            bool task = v.Phase == LinePhase.TaskPlay;
            foreach (var p in v.Players)
            {
                sb.Append('|').Append(p.Id).Append(p.Name).Append(p.ColorIndex).Append(',').Append(p.Blame).Append(p.Candidate ? 'C' : '-').Append(p.Fired ? 'F' : '-')
                  .Append(p.Connected ? 'o' : 'x').Append(p.IsBot ? 'b' : '-').Append(task && p.Submitted ? 's' : '-').Append(Partner(v, p.Id));
            }
            return sb.ToString();
        }

        /// <summary>Who the player talks to on the public line board (-1 none, -2 ringing).</summary>
        static int Partner(LineView v, int id)
        {
            foreach (var c in v.LineBoard)
            {
                if (c.Caller != id && c.Callee != id) continue;
                if (c.Status != CallStatus.Connected) return -2;
                return c.Caller == id ? c.Callee : c.Caller;
            }
            return -1;
        }

        static VisualElement Place(VisualElement parent, Vector4 r, float tilt, params string[] cls)
        {
            var e = UiKit.Box(cls).NoPick();
            e.style.position = Position.Absolute;
            e.style.left = r.x; e.style.top = r.y; e.style.width = r.z; e.style.height = r.w;
            e.style.rotate = new Rotate(new Angle(tilt, AngleUnit.Degree));
            parent.Add(e);
            return e;
        }

        static Label Ink(string text, Font font, params string[] cls)
        {
            var l = UiKit.Text(text, cls).NoPick();
            UiKit.UseFont(l, font);
            return l;
        }

        void Note(Vector4 r, float tilt, LinePlayerView p, LineClient lc, LineView v)
        {
            // the middle and bottom notes of the art are shorter: smaller ink there
            var note = Place(_root, r, tilt, "cb-note", r.w < 170f ? "cb-note-small" : null);
            bool me = p.Id == lc.MySlot;
            var head = UiKit.Box("cb-head").NoPick().AddTo(note);
            Ink(p.Name, UiKit.HandFont, "cb-name").AddTo(head);
            if (me) Ink(PlayText.T("office.board.me"), UiKit.HandFont, "cb-me").AddTo(note);
            var marker = UiKit.Box("cb-marker").NoPick().AddTo(note);
            marker.style.backgroundColor = UiKit.PlayerColor(p.ColorIndex);
            var blame = UiKit.Box("cb-blame-row").NoPick().AddTo(note);
            Ink(PlayText.T("office.board.blame"), UiKit.HandFont, "cb-blame-label").AddTo(blame);
            Ink(p.Blame.ToString(), UiKit.HandFont, "cb-blame", p.Blame > 0 ? "cb-blame-on" : null).AddTo(blame);
            var tags = UiKit.Box("cb-tags").NoPick().AddTo(note);
            if (p.Candidate && !p.Fired) Ink(PlayText.T("players.candidate"), UiKit.StampFont, "cb-stamp", "cb-stamp-cand").AddTo(tags);
            if (p.Candidate && !p.Fired && v.Phase < LinePhase.Results) Ink(PlayText.T("players.sealed"), UiKit.StampFont, "cb-stamp", "cb-stamp-sealed").AddTo(tags);
            int partner = Partner(v, p.Id);
            string status = null, cls = null;
            if (!p.Connected) { status = PlayText.T("office.board.offline"); cls = "cb-status-off"; }
            else if (partner >= 0) { status = PlayText.F("office.board.on_phone", PlayText.Name(partner)); cls = "cb-status-phone"; }
            else if (partner == -2) { status = PlayText.T("office.board.ringing"); cls = "cb-status-phone"; }
            else if (v.Phase == LinePhase.TaskPlay) { status = PlayText.T(p.Submitted ? "office.board.chosen" : "office.board.choosing"); cls = p.Submitted ? "cb-status-done" : "cb-status-wait"; }
            if (status != null) Ink(status, UiKit.HandFont, "cb-status", cls).AddTo(note);
            if (p.Fired)
            {
                var stamp = Ink(PlayText.T("players.fired"), UiKit.StampFont, "cb-fired").AddTo(note);
                stamp.style.rotate = new Rotate(new Angle(-14f, AngleUnit.Degree));
            }
        }

        void Title(LineView v)
        {
            var c = Place(_root, Center, 1f, "cb-note", "cb-center");
            Ink(PlayText.T("office.board.title"), UiKit.StampFont, "cb-title").AddTo(c);
            Ink(PlayText.T("office.board.legend"), UiKit.HandFont, "cb-legend").AddTo(c);
        }
    }
}
