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
                _go.SetActive(false);
                Object.DontDestroyOnLoad(_go);
                _doc = _go.AddComponent<UIDocument>();
                _doc.panelSettings = _ps;
                _go.SetActive(true);
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
    /// The manager's slide deck for one meeting (v4): cover, company productivity and task outcomes, the office log
    /// (observable incident effects, missed inputs, remedial reports), anonymous complaints, call recordings, presented
    /// records, and the blame board. Never names a culprit. Built for the 1600x900 projector panel.
    /// </summary>
    public sealed class DossierDeck
    {
        public enum Kind : byte { Cover, Company, Log, Complaints, Recording, Records, Board }

        struct Slide { public Kind Kind; public int Arg; }

        readonly List<Slide> _slides = new List<Slide>();
        readonly VisualElement _root, _page, _dots;
        readonly Label _brand, _meta, _footer;
        TapeDeck _deck;
        int _shown = -1, _listKey = int.MinValue, _pageKey = int.MinValue;
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

        int IndexOf(Kind k)
        {
            for (int i = 0; i < _slides.Count; i++) if (_slides[i].Kind == k) return i;
            return -1;
        }

        public int BoardIndex => Mathf.Max(0, IndexOf(Kind.Board));
        public int RecordsIndex => IndexOf(Kind.Records);
        /// <summary>The slide shown after the dossier: the blame record.</summary>
        public int RestIndex => BoardIndex;

        public int RecordingIndex(int pick)
        {
            for (int i = 0; i < _slides.Count; i++) if (_slides[i].Kind == Kind.Recording && _slides[i].Arg == pick) return i;
            return -1;
        }

        static bool RecordingsEarly(LineView v)
        {
            var m = v.Meeting;
            if (m == null || m.Recordings.Count == 0) return false;
            return m.Recordings[0].PlayAt - MeetingText.DossierStart(v) < 8.0;
        }

        static bool HasLog(LineView v)
        {
            if (v.Meeting == null) return false;
            foreach (var e in v.Meeting.Dossier)
                if (e.Kind == DossierKind.IncidentEffect || e.Kind == DossierKind.MissedTask || e.Kind == DossierKind.Remedial) return true;
            return false;
        }

        /// <summary>Recomputes the slide list for this meeting; returns true when it changed (the caller re-shows).</summary>
        public bool SetView(LineView v)
        {
            var m = v.Meeting;
            int key = v.WeekId * 1000003 + v.Day * 101 + (m != null ? m.Dossier.Count * 7 + m.Recordings.Count * 3 + m.Complaints.Count * 1009 + m.Presentations.Count * 4001 : -1)
                + (m != null && m.IsFinal ? 50000 : 0) + (v.Company.Closed ? 70001 : 0);
            if (key == _listKey) return false;
            _listKey = key;
            _slides.Clear();
            bool early = RecordingsEarly(v);
            _slides.Add(new Slide { Kind = Kind.Cover });
            _slides.Add(new Slide { Kind = Kind.Company });
            if (early) AddRecordings(m);
            if (HasLog(v)) _slides.Add(new Slide { Kind = Kind.Log });
            if (m != null && m.Complaints.Count > 0) _slides.Add(new Slide { Kind = Kind.Complaints });
            if (!early) AddRecordings(m);
            if (m != null && m.Presentations.Count > 0) _slides.Add(new Slide { Kind = Kind.Records });
            _slides.Add(new Slide { Kind = Kind.Board });
            _dots.Clear();
            for (int i = 0; i < _slides.Count; i++) Box("slide-dot").NoPick().AddTo(_dots);
            _shown = -1;
            return true;
        }

        void AddRecordings(LineMeetingView m)
        {
            if (m == null) return;
            for (int i = 0; i < m.Recordings.Count; i++) _slides.Add(new Slide { Kind = Kind.Recording, Arg = m.Recordings[i].Index });
        }

        /// <summary>
        /// The slide the manager shows right now: every tape exactly while it plays (from its PlayAt), the other slides
        /// share the remaining dossier time in list order. Outside the dossier: the blame record.
        /// </summary>
        public int AutoIndex(LineView v, double now)
        {
            if (_slides.Count == 0) return 0;
            if (v.Phase != LinePhase.MeetingDossier) return RestIndex;
            var m = v.Meeting;
            double start = MeetingText.DossierStart(v), end = v.PhaseEndTime;
            double per = MeetingText.Config.Durations.DossierPerRecording;
            int recs = m != null ? m.Recordings.Count : 0;
            double recTime = 0, recBefore = 0;
            for (int i = 0; i < recs; i++)
            {
                double a = m.Recordings[i].PlayAt - 1.0, b = a + per;
                if (now >= a && now < b)
                {
                    int ri = RecordingIndex(m.Recordings[i].Index);
                    if (ri >= 0) return ri;
                }
                recTime += per;
                if (now >= b) recBefore += per; else if (now > a) recBefore += now - a;
            }
            // the timed slides (not tapes, not presented records) in order share the free time
            int others = 0;
            for (int i = 0; i < _slides.Count; i++) if (Timed(_slides[i].Kind)) others++;
            if (others <= 0) return 0;
            double free = System.Math.Max(1.0, (end - start) - recTime);
            double elapsed = System.Math.Max(0.0, (now - start) - recBefore);
            bool early = RecordingsEarly(v);
            double coverShare = early && recs > 0 ? System.Math.Max(1.0, m.Recordings[0].PlayAt - 1.0 - start) : free / others;
            int k;
            if (elapsed < coverShare) k = 0;
            else k = 1 + (int)((elapsed - coverShare) / System.Math.Max(1.0, (free - coverShare) / System.Math.Max(1, others - 1)));
            k = Mathf.Clamp(k, 0, others - 1);
            for (int i = 0, n = 0; i < _slides.Count; i++)
            {
                if (!Timed(_slides[i].Kind)) continue;
                if (n == k) return i;
                n++;
            }
            return RestIndex;
        }

        static bool Timed(Kind k) { return k != Kind.Recording && k != Kind.Records; }

        public string TitleOf(int i)
        {
            if (i < 0 || i >= _slides.Count) return "";
            switch (_slides[i].Kind)
            {
                case Kind.Cover: return Loc.T("dossier.slide.cover");
                case Kind.Company: return Loc.T("dossier.slide.company");
                case Kind.Log: return Loc.T("dossier.slide.log");
                case Kind.Complaints: return Loc.T("dossier.slide.complaints");
                case Kind.Recording: return Loc.T("dossier.slide.recording");
                case Kind.Records: return Loc.T("dossier.slide.records");
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
            _pageKey = PageKey(v);
            _meta.text = Loc.F("dossier.meta", MeetingText.Day(v.Day), i + 1, _slides.Count);
            for (int d = 0; d < _dots.childCount; d++) _dots[d].EnableInClassList("slide-dot-on", d == i);
            var s = _slides[i];
            switch (s.Kind)
            {
                case Kind.Cover: BuildCover(v); break;
                case Kind.Company: BuildCompany(v); break;
                case Kind.Log: BuildLog(v); break;
                case Kind.Complaints: BuildComplaints(v); break;
                case Kind.Recording: BuildRecording(v, s.Arg); break;
                case Kind.Records: BuildRecords(v); break;
                default: BuildBoard(v); break;
            }
            UiMotion.SlideIn(_page, new Vector2(60f, 0f), 0.32f);
        }

        /// <summary>Hash of what the current slide displays (rebuilt when it changes).</summary>
        int PageKey(LineView v)
        {
            if (_shown < 0 || _shown >= _slides.Count) return 0;
            var m = v.Meeting;
            int k = 17;
            switch (_slides[_shown].Kind)
            {
                case Kind.Board:
                    foreach (var p in v.Players) k = k * 31 + p.Blame * 7 + p.Warnings + (p.Connected ? 1 : 0) * 3;
                    break;
                case Kind.Company:
                    k = k * 31 + v.Company.ProvisionalPermille + v.Company.FinalPermille * 7 + v.Company.TasksResolved + (v.Company.Closed ? 99 : 0);
                    break;
                case Kind.Records:
                    k = k * 31 + (m != null ? m.Presentations.Count : 0);
                    if (m != null) foreach (var p in m.Presentations) k = k * 31 + p.ExposedIncident;
                    break;
            }
            return k;
        }

        /// <summary>New view inside the same meeting: refresh what can change (recording speakers, board, presentations).</summary>
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
            else if (PageKey(v) != _pageKey) Show(_shown, v, true);
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
            bool closed = v.Company.Closed || (v.Meeting != null && v.Meeting.CompanyClosed);
            var col = Box("cover").NoPick().AddTo(_page);
            Title(Loc.T(closed && final ? "dossier.cover.postmortem" : final ? "dossier.cover.final" : "dossier.cover.daily"), "cover-title").NoPick().AddTo(col);
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
            var m = v.Meeting;
            AgendaLine(agenda, n++, Loc.T("dossier.cover.item_company"));
            if (HasLog(v)) AgendaLine(agenda, n++, Loc.T("dossier.cover.item_log"));
            if (m != null && m.Complaints.Count > 0) AgendaLine(agenda, n++, Loc.F("dossier.cover.item_complaints", m.Complaints.Count));
            int recs = m != null ? m.Recordings.Count : 0;
            if (recs > 0) AgendaLine(agenda, n++, Loc.F("dossier.cover.item_recordings", recs));
            AgendaLine(agenda, n++, Loc.T("dossier.cover.item_board"));
            AgendaLine(agenda, n, Loc.T(closed && final ? "dossier.cover.item_closed" : final ? "dossier.cover.item_firing" : "dossier.cover.item_vote"));
            if (v.Features.PresentationOpen) Bold(Loc.T("dossier.cover.present_note"), "cover-final").NoPick().AddTo(col);
        }

        static void AgendaLine(VisualElement parent, int n, string text)
        {
            var r = Box("agenda-line").NoPick().AddTo(parent);
            Title(n + ".", "agenda-num").NoPick().AddTo(r);
            Text(text, "agenda-text").NoPick().AddTo(r);
        }

        /// <summary>Productivity gauge: fill = percent, marker = company target. Used on the projector and in results.</summary>
        public static VisualElement Gauge(VisualElement parent, int permille, int targetPercent, bool closed, params string[] classes)
        {
            var wrap = Box("gauge-wrap").Cls(classes).NoPick().AddTo(parent);
            var bar = Box("gauge").NoPick().AddTo(wrap);
            var fill = Box("gauge-fill").NoPick().AddTo(bar);
            bool ok = permille >= targetPercent * 10;
            fill.AddToClassList(permille < 0 ? "gauge-measuring" : ok && !closed ? "gauge-ok" : "gauge-low");
            fill.style.width = Length.Percent(Mathf.Clamp(permille / 10f, 0f, 100f));
            var mark = Box("gauge-target").NoPick().AddTo(bar);
            mark.style.left = Length.Percent(Mathf.Clamp(targetPercent, 0, 100));
            var lbl = Box("gauge-target-label").NoPick().AddTo(wrap);
            lbl.style.left = Length.Percent(Mathf.Clamp(targetPercent, 0, 100));
            Bold(Loc.F("company.target_mark", targetPercent), "gauge-target-text").NoPick().AddTo(lbl);
            return wrap;
        }

        void BuildCompany(LineView v)
        {
            var c = v.Company;
            bool final = v.Meeting != null && v.Meeting.IsFinal;
            int perm = final && c.FinalPermille >= 0 ? c.FinalPermille : c.ProvisionalPermille;
            int target = c.TargetPercent > 0 ? c.TargetPercent : MeetingText.Config.CompanyTargetPercent;
            bool closed = c.Closed || (v.Meeting != null && v.Meeting.CompanyClosed);
            var head = Box("sl-head").NoPick().AddTo(_page);
            Title(Loc.T("dossier.slide.company"), "sl-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            Memo(Loc.T(final && c.FinalPermille >= 0 ? "company.final" : "company.provisional"), "rec-notice").NoPick().AddTo(head);

            var top = Box("company-top").NoPick().AddTo(_page);
            Title(MeetingText.Percent(perm), "company-big").NoPick().AddTo(top);
            var side = Box("col", "grow").NoPick().AddTo(top);
            Bold(Loc.F("company.vs_target", target), "company-target").NoPick().AddTo(side);
            string verdict = perm < 0 ? Loc.T("company.measuring_long")
                : closed ? Loc.T("company.closed_line")
                : perm >= target * 10 ? Loc.T("company.on_track") : Loc.T("company.below");
            Text(verdict, "company-verdict", closed || (perm >= 0 && perm < target * 10) ? "company-bad" : "company-good").NoPick().AddTo(side);
            if (c.LastChange != CompanyChange.None && c.LastTask != TaskKind.None)
                Text(Loc.F(c.LastChange == CompanyChange.Remedial ? "company.last_remedial" : "company.last_task", MeetingText.Task(c.LastTask), MeetingText.Signed(c.LastDelta)), "company-last").NoPick().AddTo(side);
            Gauge(_page, perm, target, closed, "company-gauge");

            // task outcomes of this block
            var row = Box("rounds-row").NoPick().AddTo(_page);
            int i = 0;
            foreach (var e in v.Meeting.Dossier)
            {
                if (e.Kind != DossierKind.TaskOutcome) continue;
                var card = Box("round-card").NoPick().AddTo(row);
                var h = Box("round-card-head").NoPick().AddTo(card);
                MeetingText.ApplyIcon(Box("icon", "icon-l").NoPick().AddTo(h), MeetingText.TaskIcon(e.Task));
                var hc = Box("col", "grow").NoPick().AddTo(h);
                Memo(Loc.F("dossier.task_n", e.TaskNumber + 1), "round-card-n").NoPick().AddTo(hc);
                Title(MeetingText.Task(e.Task), "round-card-name").NoPick().AddTo(hc);
                int max = e.N2 > 0 ? e.N2 : 100;
                int pct = Mathf.Clamp(Mathf.RoundToInt(100f * Mathf.Max(0, e.N1) / max), 0, 100);
                Title(Loc.F("dossier.task_output", Mathf.Max(0, e.N1), max), "round-card-headline", pct >= 65 ? "task-good" : "task-bad").NoPick().AddTo(card);
                VisualElement fill;
                Bar(pct / 100f, out fill, pct >= 65 ? "bar-good" : "bar-bad").NoPick().AddTo(card);
                UiMotion.PopIn(card, 0.3f, 0.12f + i * 0.12f, 0.92f);
                i++;
            }
            if (i == 0) Text(Loc.T("dossier.no_tasks"), "incident-empty").NoPick().AddTo(row);
            // favourite (Kayirmaci) and the "nothing observed" line
            foreach (var e in v.Meeting.Dossier)
            {
                if (e.Kind != DossierKind.Favourite) continue;
                var f = Box("incident", "incident-good").NoPick().AddTo(_page);
                MeetingText.ApplyIcon(Box("icon", "icon-l").NoPick().AddTo(f), "icons/favor_heart");
                var col = Box("col", "grow").NoPick().AddTo(f);
                Bold(Loc.T("dossier.favourite"), "incident-title").NoPick().AddTo(col);
                Text(Loc.F("dossier.favourite_detail", MeetingText.Name(e.Player)), "incident-detail").NoPick().AddTo(col);
                Badge(e.Player, "badge-m").AddTo(f);
                break;
            }
            if (!HasLog(v)) Text(Loc.T("dossier.no_incidents"), "incident-empty").NoPick().AddTo(_page);
        }

        void BuildLog(LineView v)
        {
            var head = Box("sl-head").NoPick().AddTo(_page);
            Title(Loc.T("dossier.slide.log"), "sl-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            Memo(Loc.T("effect.header_note"), "rec-notice").NoPick().AddTo(head);
            var list = Box("incident-list").NoPick().AddTo(_page);
            int n = 0;
            foreach (var e in v.Meeting.Dossier)
            {
                MeetingText.Line line;
                if (e.Kind == DossierKind.IncidentEffect) line = MeetingText.Effect(e, v);
                else if (e.Kind == DossierKind.MissedTask)
                {
                    line = new MeetingText.Line
                    {
                        Icon = "icons/warning", Who = e.Player, Tone = MeetingText.Tone.Neutral,
                        Title = Loc.F("dossier.missed", MeetingText.Name(e.Player)),
                        Detail = Loc.F("dossier.missed_detail", e.TaskNumber + 1),
                    };
                }
                else if (e.Kind == DossierKind.Remedial)
                {
                    line = new MeetingText.Line
                    {
                        Icon = "icons_v3/warning_letter", Who = e.Player, Tone = MeetingText.Tone.Good,
                        Title = Loc.F("dossier.remedial", MeetingText.Name(e.Player), MeetingText.Name(e.Victim)),
                        Detail = Loc.F("dossier.remedial_detail", e.TaskNumber + 1, Mathf.Max(0, e.N1)),
                    };
                }
                else continue;
                var row = Box("incident", "incident-" + line.Tone.ToString().ToLowerInvariant()).NoPick().AddTo(list);
                MeetingText.ApplyIcon(Box("icon", "icon-l").NoPick().AddTo(row), line.Icon);
                var col = Box("col", "grow").NoPick().AddTo(row);
                Bold(line.Title, "incident-title").NoPick().AddTo(col);
                Text(line.Detail, "incident-detail").NoPick().AddTo(col);
                if (line.Who >= 0) Badge(line.Who, "badge-m").AddTo(row);
                else Box("icon", "icon-m", "incident-all").NoPick().AddTo(row);
                UiMotion.SlideIn(row, new Vector2(0f, 18f), 0.26f, 0.1f + n * 0.09f);
                n++;
                if (n >= 7) break;
            }
            if (n == 0) Text(Loc.T("dossier.no_incidents"), "incident-empty").NoPick().AddTo(list);
        }

        /// <summary>Preset sentence, or the free text (plain label), of one anonymous complaint.</summary>
        public static string ComplaintText(LineComplaintView c)
        {
            if (c.Preset >= 0)
            {
                string key = "complaint.preset." + c.Preset;
                return Loc.Has(key) ? Loc.T(key) : Loc.F("complaint.preset_n", c.Preset + 1);
            }
            return c.Text ?? "";
        }

        void BuildComplaints(LineView v)
        {
            var head = Box("sl-head").NoPick().AddTo(_page);
            Title(Loc.T("dossier.slide.complaints"), "sl-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            Tag(Loc.T("complaint.label"), "tag-red", "complaint-label").AddTo(head);
            var list = Box("incident-list").NoPick().AddTo(_page);
            int n = 0;
            foreach (var c in v.Meeting.Complaints)
            {
                var row = Box("incident", "complaint-row").NoPick().AddTo(list);
                MeetingText.ApplyIcon(Box("icon", "icon-l").NoPick().AddTo(row), "icons_v3/complaint");
                var col = Box("col", "grow").NoPick().AddTo(row);
                Bold(Loc.F("complaint.about", MeetingText.Name(c.Target)), "incident-title").NoPick().AddTo(col);
                Hand("“" + ComplaintText(c) + "”", "complaint-text").NoPick().AddTo(col);
                Badge(c.Target, "badge-m").AddTo(row);
                UiMotion.SlideIn(row, new Vector2(0f, 18f), 0.26f, 0.1f + n * 0.09f);
                n++;
                if (n >= 6) break;
            }
            if (v.Meeting.Complaints.Count > n) Text(Loc.F("complaint.more", v.Meeting.Complaints.Count - n), "incident-empty").NoPick().AddTo(list);
            Text(Loc.T("complaint.not_evidence"), "complaint-foot").NoPick().AddTo(_page);
        }

        void BuildRecording(LineView v, int pickIndex)
        {
            var p = FindPick(v, pickIndex);
            int total = v.Meeting != null ? v.Meeting.Recordings.Count : 1;
            var head = Box("sl-head").NoPick().AddTo(_page);
            Title(total > 1 ? Loc.F("dossier.rec.title_n", pickIndex + 1, total) : Loc.T("dossier.rec.title"), "sl-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            Memo(Loc.T("rec.notice"), "rec-notice").NoPick().AddTo(head);
            _deck = new TapeDeck(_page);
            _deckPick = p;
            _deck.Set(p, v);
        }

        void BuildRecords(LineView v)
        {
            var head = Box("sl-head").NoPick().AddTo(_page);
            Title(Loc.T("dossier.slide.records"), "sl-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            Memo(Loc.T("present.header_note"), "rec-notice").NoPick().AddTo(head);
            var grid = Box("pc-grid").NoPick().AddTo(_page);
            var list = v.Meeting.Presentations;
            int start = Mathf.Max(0, list.Count - 4);
            for (int i = start; i < list.Count; i++)
            {
                var card = RecordCards.Presentation(grid, list[i], v);
                UiMotion.PopIn(card, 0.3f, 0.1f + (i - start) * 0.1f, 0.9f);
            }
        }

        void BuildBoard(LineView v)
        {
            bool final = v.Meeting != null && v.Meeting.IsFinal;
            var head = Box("sl-head").NoPick().AddTo(_page);
            Title(Loc.T("dossier.slide.board"), "sl-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            var prof = MeetingText.Profile(v);
            Memo(final ? Loc.T("dossier.board.final_rule") : Loc.F("dossier.board.daily_rule", prof.WarningBlame, MeetingText.Config.WarningMinVotes), "rec-notice").NoPick().AddTo(head);

            var rows = new List<LinePlayerView>(v.Players);
            rows.Sort((a, b) => b.Blame != a.Blame ? b.Blame.CompareTo(a.Blame) : b.Warnings != a.Warnings ? b.Warnings.CompareTo(a.Warnings) : a.Id.CompareTo(b.Id));
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
                if (n == 0 && p.Blame > 0) fill.AddToClassList("board-fill-top");
                float frac = p.Blame / (float)max;
                fill.style.width = Length.Percent(Mathf.Max(2f, frac * 100f));
                Title(p.Blame.ToString(), "board-num").NoPick().AddTo(row);
                var marks = Box("board-marks").NoPick().AddTo(row);
                for (int i = 0; i < p.Warnings; i++) WarningMark(marks);
                float delay = 0.08f + n * 0.07f;
                UiMotion.SlideIn(row, new Vector2(-30f, 0f), 0.26f, delay);
                UiMotion.Tween(fill, 0.6f, t => fill.style.width = Length.Percent(Mathf.Max(2f, frac * 100f * t)), UiMotion.Ease.OutCubic, null, delay + 0.1f, "grow");
                n++;
            }
        }

        /// <summary>Small red warning-letter mark (one per warning).</summary>
        public static VisualElement WarningMark(VisualElement parent)
        {
            var m = Box("ihtar-mark").NoPick().AddTo(parent);
            if (!UiArt.Apply(m, "icons_v3/warning_letter")) Title(Loc.T("meeting.warning_mark"), "ihtar-mark-text").NoPick().AddTo(m);
            else m.AddToClassList("ihtar-mark-art");
            return m;
        }
    }
}
