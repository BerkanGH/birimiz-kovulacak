using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3a live suç tahtası on the meeting room whiteboards (docs/V5_INTERACTION.md §3): a UI Toolkit panel
    /// rendered into a texture shown on both boards (the wall one and the one on wheels). Marker style: one row per
    /// colleague in seat order (colour dot, name, tally marks and the number, ADAY / MÜHÜRLÜ / KOVULDU, "oy verdi" during a
    /// vote, "+N" after a verdict); on the right what this meeting is about (failed tasks, the vote, the candidates and
    /// the defence order). Same public data as the office cork board; rebuilt only when it changes.
    /// </summary>
    public sealed class MeetingBoard
    {
        public const int W = 1024, H = 512;
        static readonly Color Navy = new Color32(0x1F, 0x2A, 0x44, 255), Red = new Color32(0xC8, 0x34, 0x2C, 255),
            Green = new Color32(0x24, 0x78, 0x4A, 255), Orange = new Color32(0xD6, 0x6E, 0x14, 255), Blue = new Color32(0x2F, 0x5F, 0xB4, 255);

        static Material s_wallDecal;
        readonly WorldPanel _panel = new WorldPanel();
        readonly List<Renderer> _mirrors = new List<Renderer>();
        readonly List<Material> _mirrorOrig = new List<Material>();
        VisualElement _root;
        string _sig;
        bool _failed;

        /// <summary>Creates the panel on the first board and shows the same texture on the others.</summary>
        public bool Ensure(Renderer[] boards, PanelSettings like)
        {
            if (_failed || boards == null || boards.Length == 0 || boards[0] == null) return false;
            if (_panel.Surface == boards[0] && _root != null)
            {
                _panel.Keep();
                foreach (var m in _mirrors) if (m != null && m.sharedMaterial != _panel.LiveMaterial) m.sharedMaterial = _panel.LiveMaterial;
                return true;
            }
            try
            {
                Release();
                var orig = boards[0].sharedMaterial;
                if (orig != null && orig.name.StartsWith("M_MeetingBoardLive") && s_wallDecal != null) orig = s_wallDecal;
                else if (orig != null) s_wallDecal = orig;
                _root = _panel.Create(boards[0], like, W, H, new Color32(0xFA, 0xFA, 0xF6, 255), "MeetingBoard", orig);
                _root.AddToClassList("wb-root");
                var sheet = Resources.Load<StyleSheet>("ui/styles/meeting");
                if (sheet != null) _root.styleSheets.Add(sheet);
                // a matte marker board: no glare washing the ink out under the pendant lights
                var live = _panel.LiveMaterial;
                if (live != null)
                {
                    if (live.HasProperty("_Smoothness")) live.SetFloat("_Smoothness", 0.1f);
                    if (live.HasProperty("_BaseColor")) live.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.95f, 1f));
                    live.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    live.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    if (live.HasProperty("_SpecularHighlights")) live.SetFloat("_SpecularHighlights", 0f);
                    if (live.HasProperty("_EnvironmentReflections")) live.SetFloat("_EnvironmentReflections", 0f);
                }
                for (int i = 1; i < boards.Length; i++)
                {
                    if (boards[i] == null) continue;
                    var keep = boards[i].sharedMaterial;
                    if (keep != null && keep.name.StartsWith("M_MeetingBoardLive")) keep = s_wallDecal;
                    _mirrors.Add(boards[i]);
                    _mirrorOrig.Add(keep);
                    boards[i].sharedMaterial = _panel.LiveMaterial;
                }
                _sig = null;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MeetingBoard] disabled: " + e.Message);
                _failed = true;
                Release();
                return false;
            }
        }

        public void Release()
        {
            for (int i = 0; i < _mirrors.Count; i++)
                if (_mirrors[i] != null && _mirrorOrig[i] != null && _mirrors[i].sharedMaterial == _panel.LiveMaterial) _mirrors[i].sharedMaterial = _mirrorOrig[i];
            _mirrors.Clear();
            _mirrorOrig.Clear();
            _panel.Release();
            _root = null; _sig = null;
        }

        public void Update(LineClient lc, LineView v)
        {
            if (_root == null || v == null) return;
            string sig = Signature(lc, v);
            if (sig == _sig) return;
            _sig = sig;
            _root.Clear();
            Build(lc, v);
        }

        static string Signature(LineClient lc, LineView v)
        {
            var sb = new StringBuilder(160);
            sb.Append(Loc.Language).Append('|').Append(lc.MySlot).Append('|').Append(v.WeekId).Append('|').Append((int)v.Phase).Append('|').Append(v.Day);
            foreach (var p in v.Players)
                sb.Append('|').Append(p.Id).Append(p.Name).Append(p.ColorIndex).Append(',').Append(p.Blame).Append(p.Candidate ? 'C' : '-').Append(p.Fired ? 'F' : '-')
                  .Append(p.Connected ? 'o' : 'x').Append(v.Features.VoteOpen && BallotDrops.InBox(v, p) ? 'v' : '-');
            var mt = v.Meeting;
            if (mt != null)
            {
                sb.Append("|m").Append(mt.Index).Append(':').Append(mt.VoteableCount);
                foreach (var t in mt.FailedTasks) sb.Append(',').Append((int)t);
                foreach (var g in mt.BlameGained) sb.Append(";g").Append(g.Player).Append('=').Append(g.Amount);
            }
            var fv = v.Final;
            if (fv != null)
            {
                sb.Append("|f").Append(fv.Round).Append(fv.Revote ? 'r' : '-').Append(fv.Defender);
                foreach (var c in fv.DefenseOrder) sb.Append(',').Append(c);
                foreach (var g in fv.Crisis) sb.Append(";k").Append(g.Player).Append('=').Append(g.Amount);
                foreach (var d in fv.Declarations) sb.Append(";d").Append(d.Player).Append('=').Append(d.Amount);
            }
            return sb.ToString();
        }

        static Label Ink(string text, Font font, params string[] cls)
        {
            var l = UiKit.Text(text, cls).NoPick();
            UiKit.UseFont(l, font);
            return l;
        }

        static VisualElement At(VisualElement parent, float x, float y, float w, float h, params string[] cls)
        {
            var e = UiKit.Box(cls).NoPick();
            e.style.position = Position.Absolute;
            e.style.left = x; e.style.top = y;
            if (w > 0) e.style.width = w;
            if (h > 0) e.style.height = h;
            parent.Add(e);
            return e;
        }

        void Build(LineClient lc, LineView v)
        {
            var hand = UiKit.HandFont;
            var title = At(_root, 34, 14, 560, 60, "wb-head");
            Ink(PlayText.T("meeting.board.title"), hand, "wb-title").AddTo(title);
            At(_root, 34, 70, 580, 4, "wb-underline");
            var players = new List<LinePlayerView>(v.Players);
            players.Sort((a, b) => a.Id.CompareTo(b.Id));
            var gained = new Dictionary<int, int>();
            if (v.Phase == LinePhase.MeetingVerdict && v.Meeting != null) foreach (var g in v.Meeting.BlameGained) if (g.Amount > 0) gained[g.Player] = g.Amount;
            if (v.Phase == LinePhase.CrisisVerdict && v.Final != null) foreach (var g in v.Final.Crisis) if (g.Amount > 0) gained[g.Player] = g.Amount;
            float rowH = players.Count > 5 ? 68f : 74f;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var row = At(_root, 30, 84 + i * rowH, 600, rowH, "wb-row");
                var dot = UiKit.Box("wb-dot").NoPick().AddTo(row);
                dot.style.backgroundColor = UiKit.PlayerColor(p.ColorIndex);
                var name = Ink(p.Name, hand, "wb-name", p.Fired ? "wb-struck" : null).AddTo(row);
                name.style.color = Color.Lerp(UiKit.PlayerColor(p.ColorIndex), Navy, 0.55f);
                if (p.Id == lc.MySlot) Ink(PlayText.T("meeting.board.me"), hand, "wb-me").AddTo(row);
                Tally(row, p.Blame);
                Ink(p.Blame.ToString(), hand, "wb-num", p.Blame > 0 ? "wb-num-on" : null).AddTo(row);
                int plus;
                if (gained.TryGetValue(p.Id, out plus)) Ink("+" + plus, hand, "wb-plus").AddTo(row);
                if (p.Fired) Ink(PlayText.T("players.fired"), UiKit.StampFont, "wb-stamp", "wb-stamp-fired").AddTo(row);
                else if (p.Candidate)
                {
                    Ink(PlayText.T("players.candidate"), UiKit.StampFont, "wb-stamp", "wb-stamp-cand").AddTo(row);
                    if (v.Phase < LinePhase.Results) Ink(PlayText.T("players.sealed"), UiKit.StampFont, "wb-stamp", "wb-stamp-sealed").AddTo(row);
                }
                // v5 step 3b: "pusula attı" for every paper in the box (a blank one in the final too)
                if (v.Features.VoteOpen && BallotDrops.InBox(v, p)) Ink(PlayText.T("meeting.board.voted"), hand, "wb-voted").AddTo(row);
            }
            Side(lc, v, hand);
        }

        /// <summary>Tally marks: groups of four strokes crossed by a fifth (up to 20, then the number says it).</summary>
        static void Tally(VisualElement row, int n)
        {
            var box = UiKit.Box("wb-tally").NoPick().AddTo(row);
            int shown = Mathf.Min(n, 20);
            for (int g = 0; g * 5 < shown; g++)
            {
                var grp = UiKit.Box("wb-group").NoPick().AddTo(box);
                int k = Mathf.Min(5, shown - g * 5);
                for (int i = 0; i < Mathf.Min(4, k); i++)
                {
                    var bar = UiKit.Box("wb-bar").NoPick().AddTo(grp);
                    bar.style.rotate = new Rotate(new Angle(((g * 7 + i * 3) % 5 - 2) * 1.5f, AngleUnit.Degree));
                }
                if (k == 5) UiKit.Box("wb-slash").NoPick().AddTo(grp);
            }
        }

        /// <summary>Right side: what this meeting is about.</summary>
        void Side(LineClient lc, LineView v, Font hand)
        {
            var col = At(_root, 664, 18, 340, 480, "wb-side");
            var mt = v.Meeting;
            var fv = v.Final;
            bool final = fv != null && (v.Phase >= LinePhase.CrisisVerdict);
            if (final && fv.DefenseOrder.Count > 0)
            {
                Ink(PlayText.F("meeting.board.candidates", fv.FiringsTotal), hand, "wb-side-head").AddTo(col);
                foreach (int id in fv.DefenseOrder)
                {
                    var pl = lc.Player(id);
                    // v5.2: once the prim declarations are revealed each candidate's is written next to the name
                    var declared = StoryText.Of(v, id);
                    string who = declared == null ? PlayText.Name(id)
                        : declared.Amount >= 0 ? PlayText.F("meeting.board.declared", PlayText.Name(id), declared.Amount) : PlayText.F("meeting.board.declared_none", PlayText.Name(id));
                    string line = (id == fv.Defender ? "> " : "- ") + who;
                    var l = Ink(line, hand, "wb-side-line", pl != null && pl.Fired ? "wb-struck" : id == fv.Defender ? "wb-side-hot" : null).AddTo(col);
                    if (pl != null) l.style.color = Color.Lerp(UiKit.PlayerColor(pl.ColorIndex), Navy, 0.7f);
                }
                if (v.Phase == LinePhase.FinalVote) Ink(PlayText.T(fv.Revote ? "meeting.board.revote" : "meeting.board.final_vote"), hand, "wb-side-note").AddTo(col);
                return;
            }
            if (v.Phase == LinePhase.CrisisVote)
            {
                Ink(PlayText.T("meeting.board.crisis"), hand, "wb-side-head").AddTo(col);
                Ink(PlayText.F("meeting.board.crisis_note", lc.Config.CrisisBlame), hand, "wb-side-note").AddTo(col);
                return;
            }
            if (mt == null) return;
            Ink(PlayText.F("meeting.board.meeting", mt.Index + 1), hand, "wb-side-head").AddTo(col);
            if (mt.FailedTasks.Count == 0) { Ink(PlayText.T("meeting.board.clean"), hand, "wb-side-line", "wb-side-good").AddTo(col); return; }
            Ink(PlayText.T("meeting.board.failed"), hand, "wb-side-sub").AddTo(col);
            foreach (var t in mt.FailedTasks) Ink("- " + PlayText.Task(t), hand, "wb-side-line", "wb-side-bad").AddTo(col);
            if (mt.VoteableCount > 0) Ink(PlayText.F("meeting.board.ballots", mt.VoteableCount), hand, "wb-side-note").AddTo(col);
            else if (mt.VoteableCount == 0) Ink(PlayText.NoVote(mt, lc.Config), hand, "wb-side-note").AddTo(col);
        }
    }
}
