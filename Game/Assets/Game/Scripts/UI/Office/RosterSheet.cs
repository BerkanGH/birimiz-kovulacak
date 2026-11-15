using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 2: the Weekend Shift roster on the office clipboard, drawn into the paper with a UI Toolkit panel
    /// (<see cref="WorldPanel"/>). Header, "Gerekli: K kişi", one numbered line per colleague. While the task runs only
    /// your own line can carry a name (yours, written in by the pencil; everyone else's choice is hidden until the
    /// reveal); at the reveal the public roster fills the lines in seat order (yours first) with a TAMAM / EKSİK stamp.
    /// Rebuilt when what it shows changes; the writing / rubbing animation only resizes or fades your name.
    /// </summary>
    public sealed class RosterSheet
    {
        public const int W = 512, H = 704;
        const float Row0 = 214f, RowStep = 62f, NameX = 92f, NameMax = 380f, NameSize = 44f;

        public struct State
        {
            /// <summary>0 = before the shift task (empty sheet), 1 = the task runs, 2 = revealed.</summary>
            public int Stage;
            public int Lines, Needed, SkipPay;
            /// <summary>The volunteers' cleaning and the skippers' suç when the roster is short (the rule note).</summary>
            public int Clean, FailBlame;
            /// <summary>Stage 1: my name is on the sheet (submitted Volunteer).</summary>
            public bool MineOn;
            public string MyName;
            public int MyColor;
            /// <summary>Stage 2: the volunteers (ids; mine first), and whether the roster is short.</summary>
            public List<int> Roster;
        }

        /// <summary>The sheet's own paper material (kept across UI rebuilds, which make a new RosterSheet).</summary>
        static Material s_paper;
        readonly WorldPanel _panel = new WorldPanel();
        VisualElement _root, _mineClip;
        Label _mine;
        string _sig;
        bool _failed;
        float _mineWidth = -1f;

        /// <summary>Creates the panel and swaps the sheet's material (once per surface).</summary>
        public bool Ensure(Renderer surface, PanelSettings like)
        {
            if (_failed || surface == null) return false;
            if (_panel.Surface == surface && _root != null) { _panel.Keep(); return true; }
            try
            {
                var orig = surface.sharedMaterial;
                if (orig != null && orig.name.StartsWith("M_RosterLive") && s_paper != null) orig = s_paper;
                else if (orig != null) s_paper = orig;
                _root = _panel.Create(surface, like, W, H, new Color32(0xFB, 0xF8, 0xF0, 255), "Roster", orig);
                _root.AddToClassList("rs-root");
                _sig = null;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RosterSheet] disabled: " + e.Message);
                _failed = true;
                Release();
                return false;
            }
        }

        public void Release()
        {
            _panel.Release();
            _root = null; _mine = null; _mineClip = null; _sig = null;
        }

        /// <summary>Sheet UVs (origin bottom-left) where your name is written: start and end of the stroke on line 1.</summary>
        public void MineStroke(string name, out Vector2 from, out Vector2 to)
        {
            float w = _mineWidth > 0f ? _mineWidth : Mathf.Min(NameMax, 6f + NameSize * 0.5f * (name ?? "").Length);
            float y = Row0 + 40f;
            from = new Vector2(NameX / W, 1f - y / H);
            to = new Vector2((NameX + w) / W, 1f - y / H);
        }

        /// <summary>Your name being written (0..1 shows that much of it) or rubbed out (alpha 1..0); 1 = settled.</summary>
        public void Animate(float write, float alpha)
        {
            if (_mine == null || _mineClip == null) return;
            float w = _mine.resolvedStyle.width;
            if (!float.IsNaN(w) && w > 1f) _mineWidth = w;
            float full = _mineWidth > 0f ? _mineWidth + 8f : NameMax;
            _mineClip.style.width = Mathf.Clamp01(write) * full;
            _mine.style.opacity = Mathf.Clamp01(alpha);
        }

        public void Update(State s)
        {
            if (_root == null) return;
            string sig = Signature(s);
            if (sig == _sig) return;
            _sig = sig;
            _root.Clear();
            _mine = null; _mineClip = null;
            Ink(PlayText.T("office.roster.sheet_title"), UiKit.StampFont, "rs-title").AddTo(_root);
            Ink(PlayText.T("office.roster.sheet_days"), UiKit.HandFont, "rs-days").AddTo(_root);
            var need = Ink(s.Stage == 0 ? PlayText.T("office.roster.sheet_later") : PlayText.F("office.roster.sheet_needed", s.Needed), UiKit.HandFont, "rs-needed").AddTo(_root);
            need.EnableInClassList("rs-needed-dim", s.Stage == 0);
            int lines = Mathf.Clamp(s.Lines, 3, 6);
            for (int i = 0; i < lines; i++)
            {
                var row = Place("rs-row", 32f, Row0 + i * RowStep, W - 64f, RowStep);
                Ink((i + 1) + ".", UiKit.HandFont, "rs-num", s.Stage > 0 && i < s.Needed ? "rs-num-need" : null).AddTo(row);
                UiKit.Box("rs-rule").NoPick().AddTo(row);
                if (s.Stage > 0 && i == s.Needed - 1 && s.Needed < lines) UiKit.Box("rs-need-line").NoPick().AddTo(row);
                if (s.Stage == 1 && i == 0)
                {
                    // my line: the pencil writes into it (the clip grows), rubbing fades it
                    _mineClip = UiKit.Box("rs-clip").NoPick().AddTo(row);
                    _mine = Ink(s.MyName, UiKit.HandFont, "rs-name").AddTo(_mineClip);
                    _mine.style.color = InkColor(s.MyColor);
                    _mineClip.style.width = s.MineOn ? NameMax : 0f;
                }
                else if (s.Stage == 2 && s.Roster != null && i < s.Roster.Count)
                {
                    var clip = UiKit.Box("rs-clip").NoPick().AddTo(row);
                    clip.style.width = NameMax;
                    var lc = LineClient.I;
                    var name = Ink(PlayText.Name(s.Roster[i]), UiKit.HandFont, "rs-name").AddTo(clip);
                    name.style.color = InkColor(lc != null ? lc.ColorOf(s.Roster[i]) : s.Roster[i]);
                }
            }
            string note = s.Stage == 1 ? PlayText.T("office.roster.sheet_hidden") : PlayText.F("office.roster.sheet_rule", s.SkipPay, s.Clean, s.FailBlame);
            var n = Place("rs-note-box", 40f, Row0 + lines * RowStep + 8f, W - 80f, 60f);
            Ink(note, UiKit.HandFont, "rs-note").AddTo(n);
            if (s.Stage == 2)
            {
                bool ok = s.Roster != null && s.Roster.Count >= s.Needed;
                var stamp = Place("rs-stamp", 262f, 150f, 222f, 66f);
                stamp.AddToClassList(ok ? "rs-stamp-ok" : "rs-stamp-bad");
                Ink(PlayText.F(ok ? "office.roster.stamp_ok" : "office.roster.stamp_short", s.Roster != null ? s.Roster.Count : 0, s.Needed), UiKit.StampFont, "rs-stamp-text").AddTo(stamp);
                stamp.style.rotate = new Rotate(new Angle(-9f, AngleUnit.Degree));
            }
            _mineWidth = -1f;
        }

        static string Signature(State s)
        {
            var sb = new StringBuilder(96);
            sb.Append(Loc.Language).Append('|').Append(s.Stage).Append('|').Append(s.Lines).Append('|').Append(s.Needed).Append('|').Append(s.SkipPay).Append('|').Append(s.Clean).Append('|').Append(s.FailBlame)
              .Append('|').Append(s.MyName).Append(s.MyColor).Append(s.MineOn ? '+' : '-');
            if (s.Roster != null) foreach (int id in s.Roster) sb.Append(',').Append(id).Append(PlayText.Name(id));
            return sb.ToString();
        }

        /// <summary>Player colours darkened to read as ink on paper.</summary>
        static Color InkColor(int colorIndex)
        {
            var c = UiKit.PlayerColor(colorIndex);
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            return Color.HSVToRGB(h, Mathf.Min(1f, sat * 1.05f + 0.1f), Mathf.Min(v, 0.62f));
        }

        VisualElement Place(string cls, float x, float y, float w, float h)
        {
            var e = UiKit.Box(cls).NoPick();
            e.style.position = Position.Absolute;
            e.style.left = x; e.style.top = y; e.style.width = w; e.style.height = h;
            _root.Add(e);
            return e;
        }

        static Label Ink(string text, Font font, params string[] cls)
        {
            var l = UiKit.Text(text, cls).NoPick();
            UiKit.UseFont(l, font);
            // names and titles stay on one line (the clip that "writes" a name only grows sideways)
            if (System.Array.IndexOf(cls, "rs-note") < 0) l.style.whiteSpace = WhiteSpace.NoWrap;
            return l;
        }
    }
}
