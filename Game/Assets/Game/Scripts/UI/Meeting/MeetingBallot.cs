using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3a ballot paper (docs/V5_INTERACTION.md §3): only during a vote a paper and a pencil lie in front of you.
    /// E on it (or E on a colleague, which marks their name) brings it up; you put an X next to a name (one column per
    /// ballot when a meeting has two failed tasks); "Katla ve at" folds it and drops it into the box in the middle of the
    /// table, and the votes are sent at that moment. Until time runs out you can take a fresh paper and vote again (it
    /// replaces the one in the box). The final vote has an explicit "Boş oy (çekimser)" row; the suç and crisis votes
    /// have none (an empty ballot counts against yourself, same rules as before). Others only see a paper going into the
    /// box. Near the end of the vote a filled-in paper you have not dropped yet is dropped for you. Step 3b: a small pencil
    /// draws each new X.
    /// The same layout is drawn into the paper's texture (what folds and flies) and, interactive, laid over it.
    /// </summary>
    public sealed class MeetingBallot
    {
        public const int PW = 592, PH = 840;
        const int Abstain = -2;

        readonly MeetingUi _ui;
        readonly VisualElement _overlay;
        readonly WorldPanel _tex = new WorldPanel();
        VisualElement _texRoot;
        Renderer _texSurface;
        bool _texFailed;
        string _overlaySig, _texSig;
        float _alpha;
        bool _open;
        // the current vote
        double _round = double.NaN;
        LinePhase _roundPhase;
        int[] _marks = new int[0];
        int[] _lastCast;
        bool _droppedThisRound, _autoDone;
        string _flash;
        float _flashUntil;
        // v5 step 3b: the pencil draws the newest X (the column and name just marked)
        int _penCol = -1, _penTarget = int.MinValue;
        float _penAt = -10f;

        public bool IsOpen => _open;

        public MeetingBallot(MeetingUi ui, VisualElement parent)
        {
            _ui = ui;
            _overlay = Box("mt-paper", "mt-paper-live").AddTo(parent);
            _overlay.style.position = Position.Absolute;
            _overlay.style.width = PW; _overlay.style.height = PH;
            _overlay.style.transformOrigin = new TransformOrigin(0, 0);
            _overlay.Show(false);
        }

        public void Release() { _tex.Release(); _texRoot = null; _texSurface = null; _texSig = null; }

        // ------------------------------------------------------------------ state

        /// <summary>The paper lies in front of you: a vote is running and you have a vote in it.</summary>
        public static bool Available(LineView v) { return v != null && MeetingText.CanVote(v); }

        /// <summary>My vote is in the box this round (dropped here, or the view says my ballots are filled).</summary>
        public bool Cast(LineView v)
        {
            if (!Available(v)) return false;
            if (_droppedThisRound) return true;
            foreach (int b in v.Mine.Ballots) if (b < 0) return false;
            return true;
        }

        void SyncRound(LineView v)
        {
            if (v.PhaseStartTime == _round && v.Phase == _roundPhase) return;
            _round = v.PhaseStartTime;
            _roundPhase = v.Phase;
            _marks = new int[Math.Max(0, v.Mine.Ballots.Count)];
            for (int i = 0; i < _marks.Length; i++) _marks[i] = -1;
            _lastCast = null;
            _droppedThisRound = false;
            _autoDone = false;
            _flash = null;
            _overlaySig = _texSig = null;
            if (_open) _ui.CloseAll();
        }

        public void ResetWeek() { _round = double.NaN; _lastCast = null; }

        static List<int> Options(LineClient lc, LineView v)
        {
            var list = new List<int>();
            if (v.Phase == LinePhase.FinalVote) { if (v.Final != null) list.AddRange(v.Final.Options); }
            else
            {
                var players = new List<LinePlayerView>(v.Players);
                players.Sort((a, b) => a.Id.CompareTo(b.Id));
                foreach (var p in players) if (!p.Fired) list.Add(p.Id);
            }
            list.Remove(lc.MySlot);
            return list;
        }

        bool Complete()
        {
            if (_marks.Length == 0) return false;
            foreach (int m in _marks) if (m == -1) return false;
            return true;
        }

        bool AnyMark() { foreach (int m in _marks) if (m != -1) return true; return false; }

        // ------------------------------------------------------------------ actions

        public void Open()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            var room = Room();
            if (!Available(v) || room == null || room.BallotDropping) return;
            SyncRound(v);
            _open = true;
            _alpha = 0f;
            _overlay.style.opacity = 0f;
            room.SetBallotHeld(true);
            Sfx.Play("bills", 0.45f);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _overlay.Show(false);
            var room = Room();
            if (room != null) room.SetBallotHeld(false);
        }

        /// <summary>E on a colleague during the vote: their name gets the X in the first free column, the paper comes up.</summary>
        public bool MarkFromLook(int slot)
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (!Available(v)) return false;
            SyncRound(v);
            if (!Options(lc, v).Contains(slot)) { _ui.Toast(F("meeting.paper.not_option", Name(slot)), "of-toast-warn", 3f); Sfx.Play("deny", 0.5f); return false; }
            int col = Array.IndexOf(_marks, -1);
            if (col < 0) col = 0;
            if (Array.IndexOf(_marks, slot) < 0 || v.Phase != LinePhase.MeetingVote) { _marks[col] = slot; Pen(col, slot); }
            Sfx.Play("pencil", 0.6f);
            if (!_open) Open();
            return true;
        }

        /// <summary>UI mocks: an X in a column (<see cref="AbstainMark"/> = the blank row of the final vote).</summary>
        public void MockMark(int col, int target)
        {
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (v != null && Available(v)) SyncRound(v);
            if (col >= 0 && col < _marks.Length) _marks[col] = target;
        }

        public const int AbstainMark = Abstain;

        void Mark(int col, int target)
        {
            if (col < 0 || col >= _marks.Length) return;
            _marks[col] = _marks[col] == target ? -1 : target;
            if (_marks[col] == target) Pen(col, target);
            Sfx.Play("pencil", 0.55f);
            _flash = null;
        }

        void Pen(int col, int target) { _penCol = col; _penTarget = target; _penAt = Time.unscaledTime; }

        /// <summary>The X of the box just marked, drawn by a pencil: the cross is revealed left to right in two strokes
        /// while the pencil's tip runs along them (0.35 s), then the pencil lifts away.</summary>
        static void DrawX(VisualElement box)
        {
            var clip = Box("mt-paper-xclip").NoPick().AddTo(box);
            Hand("X", "mt-paper-x", "mt-paper-x-drawn").NoPick().AddTo(clip);
            var pen = Box("mt-paper-pen").NoPick().AddTo(box);
            Box("mt-paper-pen-tip").NoPick().AddTo(pen);
            clip.style.width = 0;
            UiMotion.Tween(box, 0.38f, t =>
            {
                clip.style.width = Length.Percent(100f * t);
                // the tip zigzags down one stroke and up the other
                float x = 6f + 40f * t, y = t < 0.5f ? 6f + 80f * t : 46f - 80f * (t - 0.5f);
                pen.style.left = x; pen.style.top = y - 62f;
            }, UiMotion.Ease.Linear, () =>
            {
                clip.style.width = Length.Percent(100f);
                UiMotion.Tween(pen, 0.2f, u => { pen.style.opacity = 1f - u; pen.style.translate = new Translate(12f * u, -18f * u); }, UiMotion.Ease.OutCubic, () => pen.RemoveFromHierarchy());
            });
        }

        /// <summary>E with the paper up: fold and drop it when it is filled in.</summary>
        public void Primary()
        {
            if (Complete()) { Drop(false); return; }
            Flash(T(_marks.Length > 1 ? "meeting.paper.need_all" : "meeting.paper.need_one"));
        }

        void Flash(string text)
        {
            _flash = text;
            _flashUntil = Time.unscaledTime + 2.6f;
            Sfx.Play("deny", 0.5f);
        }

        /// <summary>Sends the marked ballots and drops the paper into the box (auto: the time is nearly up).</summary>
        void Drop(bool auto)
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            var room = Room();
            if (!Available(v) || room == null || !AnyMark()) return;
            UpdateTexture(lc, v, true);
            bool meeting = v.Phase == LinePhase.MeetingVote;
            for (int i = 0; i < _marks.Length; i++)
            {
                if (_marks[i] == -1) continue;
                int target = _marks[i] == Abstain ? LineIds.None : _marks[i];
                Send(LineVerb.Vote, target, meeting ? i : 0);
            }
            _lastCast = (int[])_marks.Clone();
            _droppedThisRound = true;
            BallotDrops.Dropped(v, lc.MySlot);
            for (int i = 0; i < _marks.Length; i++) _marks[i] = -1;
            _open = false;
            _overlay.Show(false);
            room.DropBallot();
            if (auto) _ui.Toast(T("meeting.paper.auto_dropped"), "of-toast-phase", 4f);
            RoomsPresenter.UseConsumedFrame = Time.frameCount;
        }

        // ------------------------------------------------------------------ per frame

        static MeetingRoom Room() { return RoomsPresenter.Instance != null && RoomsPresenter.Instance.InMeeting ? RoomsPresenter.Instance.Meeting : null; }

        public void Tick(LineClient lc, LineView v, double now, float dt, PanelSettings like)
        {
            var room = Room();
            bool available = Available(v);
            if (available) SyncRound(v);
            if (room != null)
            {
                room.SetBallot(available);
                if (!available && _open) _ui.CloseAll();
            }
            if (!available || room == null) { _overlay.Show(false); return; }
            // the time is nearly up: a paper you marked but did not drop goes in for you
            double left = v.PhaseEndTime - now;
            if (!_autoDone && left < 1.6 && AnyMark() && !room.BallotDropping)
            {
                _autoDone = true;
                Drop(true);
            }
            EnsureTexture(room, like);
            // the folding paper keeps its marks (inside the fold); the fresh one comes back blank
            if (!room.BallotDropping) UpdateTexture(lc, v, false);
            if (!_open) { _overlay.Show(false); return; }
            if (_flash != null && Time.unscaledTime > _flashUntil) _flash = null;
            string sig = Sig(lc, v, true);
            if (sig != _overlaySig)
            {
                _overlaySig = sig;
                BuildPaper(_overlay, lc, v, true);
            }
            Place(room, dt);
        }

        void Place(MeetingRoom room, float dt)
        {
            var panel = _overlay.panel;
            var cam = RoomsPresenter.Instance != null ? RoomsPresenter.Instance.PovCamera : null;
            Rect r;
            if (panel == null || cam == null || !room.TryGetBallotScreen(cam, out r)) { _overlay.Show(false); return; }
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            var size = panel.visualTree.layout.size;
            float kx = size.x / sw, ky = size.y / sh;
            var pr = new Rect(r.xMin * kx, (sh - r.yMax) * ky, r.width * kx, r.height * ky);
            float k = pr.height / PH;
            _overlay.style.left = pr.x + (pr.width - PW * k) * 0.5f;
            _overlay.style.top = pr.y;
            _overlay.style.scale = new Scale(new Vector3(k, k, 1f));
            bool settled = room.BallotSettled;
            _alpha = Mathf.MoveTowards(_alpha, settled ? 1f : 0f, dt * (settled ? 8f : 20f));
            _overlay.style.opacity = _alpha;
            _overlay.pickingMode = _alpha > 0.5f ? PickingMode.Position : PickingMode.Ignore;
            _overlay.Show(true);
        }

        // ------------------------------------------------------------------ the paper's texture (what folds and flies)

        void EnsureTexture(MeetingRoom room, PanelSettings like)
        {
            if (_texFailed || room.BallotSurface == null) return;
            if (_texRoot != null && _texSurface == room.BallotSurface) { _tex.Keep(); return; }
            try
            {
                _texRoot = _tex.Create(room.BallotSurface, like, PW, PH, new Color32(0xFB, 0xF8, 0xF0, 255), "BallotPaper");
                var sheet = Resources.Load<StyleSheet>("ui/styles/meeting");
                if (sheet != null) _texRoot.styleSheets.Add(sheet);
                if (UiKit.BodyFont != null) _texRoot.style.unityFontDefinition = new StyleFontDefinition(UiKit.BodyFont);
                _texSurface = room.BallotSurface;
                if (room.BallotSurfaceTop != null) room.BallotSurfaceTop.sharedMaterial = _tex.LiveMaterial;
                _texSig = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MeetingBallot] paper texture disabled: " + e.Message);
                _texFailed = true;
                Release();
            }
        }

        void UpdateTexture(LineClient lc, LineView v, bool force)
        {
            if (_texRoot == null) return;
            string sig = Sig(lc, v, false);
            if (!force && sig == _texSig) return;
            _texSig = sig;
            _texRoot.Clear();
            var page = Box("mt-paper").AddTo(_texRoot);
            BuildPaper(page, lc, v, false);
        }

        string Sig(LineClient lc, LineView v, bool live)
        {
            var sb = new StringBuilder(96);
            sb.Append(Loc.Language).Append((int)v.Phase).Append('|').Append(_round).Append('|').Append(live ? 1 : 0);
            foreach (int m in _marks) sb.Append(',').Append(m);
            foreach (int o in Options(lc, v)) sb.Append(';').Append(o).Append(lc.NameOf(o));
            if (live)
            {
                sb.Append('|').Append(_flash).Append('|').Append(Cast(v) ? 1 : 0);
                if (_lastCast != null) foreach (int m in _lastCast) sb.Append(':').Append(m);
                foreach (int b in v.Mine.Ballots) sb.Append('#').Append(b);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ layout (shared by the texture and the overlay)

        void BuildPaper(VisualElement root, LineClient lc, LineView v, bool live)
        {
            root.Clear();
            var c = lc.Config;
            int n = Math.Max(1, _marks.Length);
            bool final = v.Phase == LinePhase.FinalVote;
            UiKit.StampText(T("meeting.paper.title"), "mt-paper-title").NoPick().AddTo(root);
            string sub = v.Phase == LinePhase.MeetingVote ? F("meeting.paper.sub_meeting", v.Meeting != null ? v.Meeting.Index + 1 : 1)
                : v.Phase == LinePhase.CrisisVote ? T("meeting.paper.sub_crisis")
                : F(v.Final != null && v.Final.Revote ? "meeting.paper.sub_revote" : "meeting.paper.sub_final", v.Final != null ? v.Final.Round + 1 : 1, v.Final != null ? Math.Max(1, v.Final.FiringsTotal) : 1);
            Bold(sub, "mt-paper-sub").NoPick().AddTo(root);
            Box("mt-paper-rule").NoPick().AddTo(root);
            string how = v.Phase == LinePhase.MeetingVote ? F(n > 1 ? "meeting.paper.how_meeting_many" : "meeting.paper.how_meeting", n, c.BlamePerVote)
                : v.Phase == LinePhase.CrisisVote ? F("meeting.paper.how_crisis", c.CrisisBlame) : T("meeting.paper.how_final");
            Text(how, "mt-paper-how").NoPick().AddTo(root);
            if (n > 1)
            {
                var heads = Box("mt-paper-cols").NoPick().AddTo(root);
                for (int i = 0; i < n; i++) Bold(F("meeting.paper.col", i + 1), "mt-paper-col").NoPick().AddTo(heads);
            }
            var rows = Box("mt-paper-rows").NoPick().AddTo(root);
            foreach (int id in Options(lc, v)) Row(rows, live, Name(id), lc.ColorOf(id), id, n);
            if (final) Row(rows, live, T("meeting.paper.abstain"), -1, Abstain, 1);
            if (!live) return;
            var foot = Box("mt-paper-foot").AddTo(root);
            if (Cast(v)) Bold(F("meeting.paper.in_box", CastNames(v)), "mt-paper-cast").NoPick().AddTo(foot);
            if (_flash != null) Bold(_flash, "oc-flash").NoPick().AddTo(foot);
            var btns = Box("oc-buttons").AddTo(foot);
            var drop = Btn(F(Cast(v) ? "meeting.paper.drop_again" : "meeting.paper.drop", OfficeText.Key(GameInput.Interact)), Primary, "pl-primary", "pl-big").AddTo(btns);
            drop.SetEnabled(Complete());
            Btn(T("meeting.paper.put_down"), () => _ui.CloseAll(), "pl-big").AddTo(btns);
        }

        void Row(VisualElement parent, bool live, string name, int color, int target, int n)
        {
            var row = Box("mt-paper-row", target == Abstain ? "mt-paper-row-abstain" : null).AddTo(parent);
            if (!live) row.pickingMode = PickingMode.Ignore;
            var dot = Box("mt-paper-dot").NoPick().AddTo(row);
            if (color >= 0) dot.style.backgroundColor = PlayerColor(color);
            else dot.AddToClassList("mt-paper-dot-none");
            Hand(name, "mt-paper-name").NoPick().AddTo(row);
            for (int i = 0; i < n; i++)
            {
                int col = i;
                bool on = col < _marks.Length && _marks[col] == target;
                VisualElement box;
                if (live)
                {
                    var b = new Button(() => Mark(col, target)).Cls("mt-paper-box");
                    b.RemoveFromClassList(Button.ussClassName);
                    box = b;
                }
                else box = Box("mt-paper-box").NoPick();
                box.AddTo(row);
                if (on && live && col == _penCol && target == _penTarget && Time.unscaledTime - _penAt < 0.3f) { _penAt = -10f; DrawX(box); }
                else if (on) Hand("X", "mt-paper-x").NoPick().AddTo(box);
            }
        }

        string CastNames(LineView v)
        {
            var sb = new StringBuilder();
            if (_lastCast != null)
            {
                foreach (int m in _lastCast)
                {
                    if (m == -1) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(m == Abstain ? T("meeting.paper.abstain_short") : Name(m));
                }
            }
            else
            {
                foreach (int b in v.Mine.Ballots)
                {
                    if (b < 0) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Name(b));
                }
            }
            return sb.Length > 0 ? sb.ToString() : T("meeting.paper.abstain_short");
        }
    }
}
