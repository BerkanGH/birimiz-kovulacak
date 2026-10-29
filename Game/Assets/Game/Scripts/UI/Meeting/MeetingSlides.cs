using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Off-screen UI panel (second PanelSettings with a target texture) that renders the projector slides into a
    /// 1600×900 RenderTexture. The texture goes to the 3D projector (<see cref="IRoomPresenter.SetProjectorTexture"/>)
    /// and is also shown in screen space when the player looks at the projector, so both always match.
    /// Created once on first use and kept for the whole process (survives UI rebuilds).
    /// </summary>
    public static class ProjectorSurface
    {
        public const int Width = 1600, Height = 900;
        static GameObject _go;
        static UIDocument _doc;
        static RenderTexture _rt;
        static PanelSettings _ps;
        static VisualElement _content;

        public static RenderTexture Texture => _rt;

        /// <summary>Content container of the projector panel (1600×900, panel pixels).</summary>
        public static VisualElement Ensure()
        {
            if (_content != null && _doc != null && _content.panel != null) return _content;
            if (_rt == null)
            {
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Default)
                {
                    name = "ProjectorRT", antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear,
                };
                _rt.Create();
            }
            if (_ps == null)
            {
                _ps = ScriptableObject.CreateInstance<PanelSettings>();
                _ps.name = "ProjectorPanel";
                _ps.targetTexture = _rt;
                _ps.scaleMode = PanelScaleMode.ConstantPixelSize;
                _ps.scale = 1f;
                _ps.clearColor = true;
                _ps.colorClearValue = new Color32(0xFB, 0xF8, 0xF0, 255);
                _ps.sortingOrder = -100;
                var main = UIRoot.I != null ? UIRoot.I.GetComponent<UIDocument>() : null;
                if (main != null && main.panelSettings != null)
                {
                    _ps.themeStyleSheet = main.panelSettings.themeStyleSheet;
                    _ps.textSettings = main.panelSettings.textSettings;
                }
                // Never takes mouse input (the screen-space copy has the controls).
                _ps.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));
            }
            if (_go == null)
            {
                _go = new GameObject("ProjectorUI");
                Object.DontDestroyOnLoad(_go);
                _doc = _go.AddComponent<UIDocument>();
                _doc.panelSettings = _ps;
            }
            var root = _doc.rootVisualElement;
            root.Clear();
            root.styleSheets.Clear();
            if (UIRoot.I != null && UIRoot.I.Style != null) root.styleSheets.Add(UIRoot.I.Style);
            var meeting = MeetingUi.Style;
            if (meeting != null) root.styleSheets.Add(meeting);
            root.AddToClassList("root");
            if (UiArt.KitAvailable) root.AddToClassList("art");
            UseFont(root, BodyFont);
            root.pickingMode = PickingMode.Ignore;
            _content = Box("projector-panel").NoPick().AddTo(root);
            _content.style.width = Width;
            _content.style.height = Height;
            return _content;
        }

        /// <summary>Hide/show the panel content (an idle panel costs nothing; it only repaints when dirty).</summary>
        public static void SetActive(bool on)
        {
            if (_content != null) _content.Show(on);
        }
    }

    /// <summary>
    /// The manager's slide deck for one meeting: cover, call recordings, today's rounds, incidents (trick traces, tips,
    /// complaint leak, favourite), and the blame board. Built for the 1600×900 projector panel.
    /// </summary>
    public sealed class DossierDeck
    {
        public enum Kind : byte { Cover, Recording, Rounds, Incidents, Board }

        struct Slide { public Kind Kind; public int Arg; }

        readonly List<Slide> _slides = new List<Slide>();
        readonly List<string> _tmp = new List<string>();
        readonly VisualElement _root, _page, _dots;
        readonly Label _brand, _meta, _footer;
        TapeDeck _deck;
        int _shown = -1, _listKey = int.MinValue, _boardKey = int.MinValue;
        LineRecordingPick _deckPick;

        public int Count => _slides.Count;
        public int Current => _shown;

        public DossierDeck(VisualElement parent)
        {
            _root = Box("slide").NoPick().AddTo(parent);
            var head = Box("slide-top").NoPick().AddTo(_root);
            var brandMark = Box("slide-brand-mark").NoPick().AddTo(head);
            if (!UiArt.Apply(brandMark, "computer/boot_logo")) UiArt.Apply(brandMark, "logo/logo_mark");
            _brand = Title(Loc.T("dossier.brand"), "slide-brand").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            _meta = Memo("", "slide-meta").NoPick().AddTo(head);
            Box("slide-rule").NoPick().AddTo(_root);
            _page = Box("slide-page").NoPick().AddTo(_root);
            var foot = Box("slide-foot").NoPick().AddTo(_root);
            _footer = Memo(Loc.T("dossier.footer"), "slide-foot-text").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _dots = Box("slide-dots").NoPick().AddTo(foot);
        }

        public Kind KindAt(int i) { return i >= 0 && i < _slides.Count ? _slides[i].Kind : Kind.Cover; }

        public int BoardIndex => _slides.Count - 1;

        public int RecordingIndex(int pick)
        {
            for (int i = 0; i < _slides.Count; i++) if (_slides[i].Kind == Kind.Recording && _slides[i].Arg == pick) return i;
            return -1;
        }

        /// <summary>Recomputes the slide list for this meeting; returns true when it changed (the caller re-shows).</summary>
        public bool SetView(LineView v)
        {
            var m = v.Meeting;
            int key = v.WeekId * 1000003 + v.Day * 101 + (m != null ? m.Dossier.Count * 7 + m.Recordings.Count : -1) + (m != null && m.IsFinal ? 50000 : 0);
            if (key == _listKey) return false;
            _listKey = key;
            _slides.Clear();
            _slides.Add(new Slide { Kind = Kind.Cover });
            if (m != null) for (int i = 0; i < m.Recordings.Count; i++) _slides.Add(new Slide { Kind = Kind.Recording, Arg = m.Recordings[i].Index });
            if (v.TodayReveals.Count > 0) _slides.Add(new Slide { Kind = Kind.Rounds });
            if (HasIncidents(v)) _slides.Add(new Slide { Kind = Kind.Incidents });
            _slides.Add(new Slide { Kind = Kind.Board });
            _dots.Clear();
            for (int i = 0; i < _slides.Count; i++) Box("slide-dot").NoPick().AddTo(_dots);
            _shown = -1;
            return true;
        }

        static bool HasIncidents(LineView v)
        {
            if (v.Meeting == null) return false;
            foreach (var e in v.Meeting.Dossier)
                if (e.Kind != DossierKind.RoundOutcome && e.Kind != DossierKind.Recording && e.Kind != DossierKind.None) return true;
            return false;
        }

        /// <summary>The slide the manager shows right now (dossier timeline: cover, each recording at its PlayAt, then the rest).</summary>
        public int AutoIndex(LineView v, double now)
        {
            if (_slides.Count == 0) return 0;
            if (v.Phase != LinePhase.MeetingDossier) return BoardIndex;
            var m = v.Meeting;
            double start = MeetingText.DossierStart(v);
            double per = MeetingText.Config.Durations.DossierPerRecording;
            int recs = m != null ? m.Recordings.Count : 0;
            if (m != null)
                for (int i = 0; i < recs; i++)
                {
                    var p = m.Recordings[i];
                    if (now >= p.PlayAt - 1.0 && now < p.PlayAt + per - 1.0) return RecordingIndex(p.Index);
                }
            double afterRecs = recs > 0 ? m.Recordings[recs - 1].PlayAt + per - 1.0 : start + 4.0;
            if (now < (recs > 0 ? m.Recordings[0].PlayAt - 1.0 : afterRecs)) return 0;
            int firstRest = 1 + recs, rest = _slides.Count - firstRest;
            if (rest <= 0) return _slides.Count - 1;
            double span = System.Math.Max(3.0, v.PhaseEndTime - afterRecs);
            int k = (int)((now - afterRecs) / (span / rest));
            return firstRest + Mathf.Clamp(k, 0, rest - 1);
        }

        public string TitleOf(int i)
        {
            if (i < 0 || i >= _slides.Count) return "";
            switch (_slides[i].Kind)
            {
                case Kind.Cover: return Loc.T("dossier.slide.cover");
                case Kind.Recording: return Loc.T("dossier.slide.recording");
                case Kind.Rounds: return Loc.T("dossier.slide.rounds");
                case Kind.Incidents: return Loc.T("dossier.slide.incidents");
                default: return Loc.T("dossier.slide.board");
            }
        }

        /// <summary>Builds slide <paramref name="i"/> (no-op when already shown unless forced).</summary>
        public void Show(int i, LineView v, bool force = false)
        {
            if (_slides.Count == 0) return;
            i = Mathf.Clamp(i, 0, _slides.Count - 1);
            if (i == _shown && !force) return;
            _shown = i;
            _page.Clear();
            _deck = null;
            _deckPick = null;
            _boardKey = int.MinValue;
            _meta.text = Loc.F("dossier.meta", MeetingText.Day(v.Day), i + 1, _slides.Count);
            for (int d = 0; d < _dots.childCount; d++) _dots[d].EnableInClassList("slide-dot-on", d == i);
            var s = _slides[i];
            switch (s.Kind)
            {
                case Kind.Cover: BuildCover(v); break;
                case Kind.Recording: BuildRecording(v, s.Arg); break;
                case Kind.Rounds: BuildRounds(v); break;
                case Kind.Incidents: BuildIncidents(v); break;
                default: BuildBoard(v); break;
            }
            // slide transition: the new page slides in from the right like a clicker advance
            UiMotion.SlideIn(_page, new Vector2(60f, 0f), 0.32f);
        }

        /// <summary>New view inside the same meeting: refresh what can change (recording speakers, blame board).</summary>
        public void Refresh(LineView v)
        {
            if (_shown < 0 || _shown >= _slides.Count) return;
            var s = _slides[_shown];
            if (s.Kind == Kind.Recording && _deck != null)
            {
                var p = FindPick(v, s.Arg);
                _deckPick = p;
                _deck.Set(p, v);
            }
            else if (s.Kind == Kind.Board && BoardKey(v) != _boardKey) Show(_shown, v, true);
        }

        public void Tick(double now, float dt)
        {
            if (_deck != null) _deck.Tick(now, dt);
        }

        static LineRecordingPick FindPick(LineView v, int index)
        {
            if (v.Meeting == null) return null;
            foreach (var p in v.Meeting.Recordings) if (p.Index == index) return p;
            return null;
        }

        // ------------------------------------------------------------------ slides

        void BuildCover(LineView v)
        {
            bool final = v.Meeting != null && v.Meeting.IsFinal;
            var col = Box("cover").NoPick().AddTo(_page);
            Title(Loc.T(final ? "dossier.cover.final" : "dossier.cover.daily"), "cover-title").NoPick().AddTo(col);
            Bold(Loc.F("dossier.cover.day", MeetingText.Day(v.Day), v.Day + 1, v.DayCount), "cover-sub").NoPick().AddTo(col);
            var row = Box("cover-row").NoPick().AddTo(col);
            var mgr = Box("cover-mgr").NoPick().AddTo(row);
            ManagerPortrait(v.ManagerProfileId, final, "badge-xl").AddTo(mgr);
            var mc = Box("col").NoPick().AddTo(mgr);
            Text(Loc.T("dossier.cover.presenter"), "cover-label").NoPick().AddTo(mc);
            Title(Loc.ManagerName(v.ManagerProfileId), "cover-mgr-name").NoPick().AddTo(mc);
            Bold(Loc.ManagerTitle(v.ManagerProfileId), "cover-mgr-title").NoPick().AddTo(mc);

            var agenda = Box("cover-agenda").NoPick().AddTo(row);
            Bold(Loc.T("dossier.cover.agenda"), "cover-label").NoPick().AddTo(agenda);
            int n = 1;
            int recs = v.Meeting != null ? v.Meeting.Recordings.Count : 0;
            if (recs > 0) AgendaLine(agenda, n++, Loc.F("dossier.cover.item_recordings", recs));
            if (v.TodayReveals.Count > 0) AgendaLine(agenda, n++, Loc.T("dossier.cover.item_rounds"));
            if (HasIncidents(v)) AgendaLine(agenda, n++, Loc.T("dossier.cover.item_incidents"));
            AgendaLine(agenda, n++, Loc.T("dossier.cover.item_board"));
            AgendaLine(agenda, n, Loc.T(final ? "dossier.cover.item_firing" : "dossier.cover.item_vote"));
            if (final) Bold(Loc.T("dossier.cover.final_note"), "cover-final").NoPick().AddTo(col);
        }

        static void AgendaLine(VisualElement parent, int n, string text)
        {
            var r = Box("agenda-line").NoPick().AddTo(parent);
            Title(n + ".", "agenda-num").NoPick().AddTo(r);
            Text(text, "agenda-text").NoPick().AddTo(r);
        }

        void BuildRecording(LineView v, int pickIndex)
        {
            var p = FindPick(v, pickIndex);
            int total = v.Meeting != null ? v.Meeting.Recordings.Count : 1;
            var head = Box("slide-head").NoPick().AddTo(_page);
            Title(total > 1 ? Loc.F("dossier.rec.title_n", pickIndex + 1, total) : Loc.T("dossier.rec.title"), "slide-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            Memo(Loc.T("rec.notice"), "rec-notice").NoPick().AddTo(head);
            _deck = new TapeDeck(_page);
            _deckPick = p;
            _deck.Set(p, v);
        }

        void BuildRounds(LineView v)
        {
            Title(Loc.T("dossier.slide.rounds"), "slide-title").NoPick().AddTo(_page);
            var row = Box("rounds-row").NoPick().AddTo(_page);
            for (int i = 0; i < v.TodayReveals.Count; i++)
            {
                var r = v.TodayReveals[i];
                var card = Box("round-card").NoPick().AddTo(row);
                var h = Box("round-card-head").NoPick().AddTo(card);
                MeetingText.ApplyIcon(Box("icon", "icon-l").NoPick().AddTo(h), MeetingText.RoundIcon(r.Type));
                var hc = Box("col", "grow").NoPick().AddTo(h);
                Memo(Loc.F("dossier.round_n", r.Index + 1), "round-card-n").NoPick().AddTo(hc);
                Title(MeetingText.Round(r.Type), "round-card-name").NoPick().AddTo(hc);
                Bold(MeetingText.RoundHeadline(r), "round-card-headline").NoPick().AddTo(card);
                MeetingText.RoundDetails(r, _tmp, 3);
                foreach (var line in _tmp) Text(line, "round-card-line").NoPick().AddTo(card);
                Box("spacer").NoPick().AddTo(card);
                var chips = Box("delta-row").NoPick().AddTo(card);
                foreach (var d in r.Deltas)
                {
                    if (d.Delta == 0) continue;
                    DeltaChip(chips, d.Player, d.Delta);
                }
                if (chips.childCount == 0) Text(Loc.T("dossier.no_change"), "round-card-line", "t-muted").NoPick().AddTo(chips);
                UiMotion.PopIn(card, 0.3f, 0.12f + i * 0.12f, 0.92f);
            }
        }

        public static void DeltaChip(VisualElement parent, int player, int delta)
        {
            var c = Box("delta-chip", delta > 0 ? "delta-bad" : "delta-good").NoPick().AddTo(parent);
            Badge(player, "badge-xs").AddTo(c);
            Bold(MeetingText.Name(player), "delta-name").NoPick().AddTo(c);
            Title(MeetingText.Signed(delta), "delta-num").NoPick().AddTo(c);
        }

        void BuildIncidents(LineView v)
        {
            Title(Loc.T("dossier.slide.incidents"), "slide-title").NoPick().AddTo(_page);
            var list = Box("incident-list").NoPick().AddTo(_page);
            int n = 0;
            foreach (var e in v.Meeting.Dossier)
            {
                if (e.Kind == DossierKind.RoundOutcome || e.Kind == DossierKind.Recording) continue;
                MeetingText.Line line;
                if (!MeetingText.Describe(e, v, out line)) continue;
                var row = Box("incident", "incident-" + line.Tone.ToString().ToLowerInvariant()).NoPick().AddTo(list);
                MeetingText.ApplyIcon(Box("icon", "icon-l").NoPick().AddTo(row), line.Icon);
                var col = Box("col", "grow").NoPick().AddTo(row);
                Bold(line.Title, "incident-title").NoPick().AddTo(col);
                Text(line.Detail, "incident-detail").NoPick().AddTo(col);
                if (line.Who >= 0) Badge(line.Who, "badge-m").AddTo(row);
                else
                {
                    var unknown = Badge(-1, "badge-m").AddTo(row);
                    unknown.AddToClassList("incident-unknown");
                }
                UiMotion.SlideIn(row, new Vector2(0f, 18f), 0.26f, 0.1f + n * 0.09f);
                n++;
                if (n >= 7) break;
            }
            if (n == 0) Text(Loc.T("dossier.no_incidents"), "incident-empty").NoPick().AddTo(list);
        }

        static int BoardKey(LineView v)
        {
            int k = 17;
            foreach (var p in v.Players) k = k * 31 + p.Blame * 7 + p.Ihtars + (p.Connected ? 1 : 0) * 3;
            return k;
        }

        void BuildBoard(LineView v)
        {
            _boardKey = BoardKey(v);
            bool final = v.Meeting != null && v.Meeting.IsFinal;
            var head = Box("slide-head").NoPick().AddTo(_page);
            Title(Loc.T("dossier.slide.board"), "slide-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            var prof = MeetingText.Profile(v);
            Memo(final ? Loc.F("dossier.board.final_rule", prof.FinalVoteWeight) : Loc.F("dossier.board.daily_rule", prof.IhtarBlame), "rec-notice").NoPick().AddTo(head);

            var rows = new List<LinePlayerView>(v.Players);
            rows.Sort((a, b) => b.Blame != a.Blame ? b.Blame.CompareTo(a.Blame) : b.Ihtars != a.Ihtars ? b.Ihtars.CompareTo(a.Ihtars) : a.Id.CompareTo(b.Id));
            int max = 1;
            foreach (var p in rows) if (p.Blame > max) max = p.Blame;
            var table = Box("board").NoPick().AddTo(_page);
            int n = 0;
            foreach (var p in rows)
            {
                var row = Box("board-row").NoPick().AddTo(table);
                if (p.Id == v.Me) row.AddToClassList("board-me");
                if (!p.Connected) row.AddToClassList("offline");
                Badge(p.Id, "badge-m").AddTo(row);
                var nc = Box("board-name-col").NoPick().AddTo(row);
                Title(p.Name, "board-name").NoPick().AddTo(nc);
                if (p.Id == v.Favourite) Bold(Loc.T("dossier.board.favourite"), "board-fav").NoPick().AddTo(nc);
                var barWrap = Box("board-bar").NoPick().AddTo(row);
                var fill = Box("board-fill").NoPick().AddTo(barWrap);
                float frac = p.Blame / (float)max;
                fill.style.width = Length.Percent(Mathf.Max(2f, frac * 100f));
                Title(p.Blame.ToString(), "board-num").NoPick().AddTo(row);
                var marks = Box("board-marks").NoPick().AddTo(row);
                for (int i = 0; i < p.Ihtars; i++) IhtarMark(marks);
                float delay = 0.08f + n * 0.07f;
                UiMotion.SlideIn(row, new Vector2(-30f, 0f), 0.26f, delay);
                UiMotion.Tween(fill, 0.6f, t => fill.style.width = Length.Percent(Mathf.Max(2f, frac * 100f * t)), UiMotion.Ease.OutCubic, null, delay + 0.1f, "grow");
                n++;
            }
        }

        /// <summary>Small red "İ"/"W" warning mark (one per İhtar).</summary>
        public static VisualElement IhtarMark(VisualElement parent)
        {
            var m = Box("ihtar-mark").NoPick().AddTo(parent);
            if (!UiArt.Apply(m, "icons_v3/warning_letter")) Title(Loc.T("meeting.ihtar_mark"), "ihtar-mark-text").NoPick().AddTo(m);
            else m.AddToClassList("ihtar-mark-art");
            return m;
        }
    }
}
