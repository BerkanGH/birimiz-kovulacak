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
    /// v5 step 3b: the projector at the head of the meeting table is the room's public display (docs/V5_INTERACTION.md §3).
    /// A 1280 x 720 UI Toolkit panel rendered into the screen's texture (<see cref="MeetingRoom.SetProjector"/>), in the
    /// default slide's template (green bands, cream body). What it shows follows the phase:
    /// <list type="bullet">
    /// <item>discussion: the dossier as slides (the agenda, then one slide per entry: failed tasks, whistles,
    /// investigations and their results, leaks; in the final the candidates and the crisis result with the board shift);
    /// the slides page every few seconds, a new entry slides in and stays a little longer;</item>
    /// <item>a leaked recording: the cassette deck (<see cref="TapeDeck"/>) with the live waveform, "Sarı ↔ Yeşil" and the time;</item>
    /// <item>a vote: what is voted, the papers in the box (who dropped one, never the content), the time;</item>
    /// <item>verdicts: the suç handed out; the crisis result;</item>
    /// <item>the defence: who speaks (their face, a voice meter), the time left, the order;</item>
    /// <item>the desk opening (<see cref="DeskOpening"/>): the fired colleague's drawer opens, the bundles are counted up
    /// to "23 PRİM", then who shares it;</item>
    /// <item>the results: the winner(s) and the ranking by prim, row by row.</item>
    /// </list>
    /// Everything is public information; the panel never takes the mouse (Tab or E on the screen zooms in).
    /// </summary>
    public sealed class MeetingProjector
    {
        public const int PanelW = 1280, PanelH = 720;
        public enum Show { None, Dossier, Vote, Verdict, Crisis, Defense, Desk, Results, Tape, Abandoned }

        readonly WorldPanel _panel = new WorldPanel();
        VisualElement _root, _content, _dots, _tapeHost;
        Label _kicker, _clock, _foot;
        TapeDeck _tape;
        MeetingRoom _room;
        bool _failed;
        string _sig;
        Show _show;
        LineRecordingPick _pick;
        readonly List<Action<double, float>> _anims = new List<Action<double, float>>();
        // dossier deck
        double _deckRound = double.NaN;
        LinePhase _deckPhase;
        int _deckIndex, _deckEntries;
        double _deckNext;
        bool _deckNew;
        // one-shots of the current slide
        bool _stamped, _cheered;

        /// <summary>UI mocks: stop paging the dossier (and show this slide).</summary>
        public static int MockDeckIndex = -1;

        public Show Showing => _show;
        /// <summary>Something the room watches together is on the screen (a leak, the defence, the desk opening, the results).</summary>
        public bool Live => _show == Show.Tape || _show == Show.Desk || _show == Show.Results || _show == Show.Defense;
        public VisualElement Root => _root;

        // ------------------------------------------------------------------ panel

        /// <summary>Creates the panel once and puts its texture on the projector screen.</summary>
        public bool Ensure(MeetingRoom room, PanelSettings like)
        {
            if (_failed || room == null) return false;
            if (_root != null && _room == room) return true;
            try
            {
                Release();
                _root = _panel.CreateOffscreen(like, PanelW, PanelH, new Color32(0xF4, 0xEF, 0xE3, 255), "Projector");
                // seen at a steep angle from the far seats: keep the text sharp
                if (_panel.Texture != null) _panel.Texture.anisoLevel = 16;
                foreach (var n in new[] { "ui/styles/play", "ui/styles/projector" })
                {
                    var sheet = Resources.Load<StyleSheet>(n);
                    if (sheet != null) _root.styleSheets.Add(sheet);
                }
                if (UiKit.BodyFont != null) _root.style.unityFontDefinition = new StyleFontDefinition(UiKit.BodyFont);
                _root.AddToClassList("pj-root");
                var head = Box("pj-head").NoPick().AddTo(_root);
                _kicker = Title("", "pj-kicker").NoPick().AddTo(head);
                _clock = Title("", "pj-clock").NoPick().AddTo(head);
                var body = Box("pj-body").NoPick().AddTo(_root);
                _content = Box("pj-content").NoPick().AddTo(body);
                _tapeHost = Box("pj-tape").NoPick().AddTo(_root);
                _tape = new TapeDeck(_tapeHost);
                _tape.UseMutualArrow();
                _tapeHost.Show(false);
                var foot = Box("pj-foot").NoPick().AddTo(_root);
                _foot = Bold("", "pj-foot-text").NoPick().AddTo(foot);
                Box("pj-grow").NoPick().AddTo(foot);
                _dots = Box("pj-dots").NoPick().AddTo(foot);
                _room = room;
                room.SetProjector(_panel.Texture);
                _sig = null;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MeetingProjector] disabled: " + e.Message);
                _failed = true;
                Release();
                return false;
            }
        }

        /// <summary>Gives the screen its default slide back and frees the texture.</summary>
        public void Release()
        {
            if (_room != null) _room.SetProjector(null);
            _room = null;
            _panel.Release();
            _root = null; _tape = null; _sig = null; _show = Show.None;
            _anims.Clear();
            _deckRound = double.NaN;
        }

        public void MarkDirty() { _sig = null; }

        // ------------------------------------------------------------------ per frame

        public void Tick(LineClient lc, LineView v, double now, float dt)
        {
            if (_root == null || v == null) return;
            var show = Pick(v, now, out _pick);
            _clock.SetText(v.Phase == LinePhase.Abandoned ? "" : UiKit.Clock(Math.Max(0, v.PhaseEndTime - now)));
            _kicker.SetText(Loc.Upper(Kicker(show, lc, v)));
            bool tape = show == Show.Tape;
            if (_tapeHost.IsShown() != tape) { _tapeHost.Show(tape); if (tape) UiMotion.FadeIn(_tapeHost, 0.3f); }
            _content.Show(!tape);
            if (tape)
            {
                _tape.Set(_pick, v);
                _tape.Tick(now, dt);
            }
            int deckCount = 0;
            if (show == Show.Dossier) deckCount = AdvanceDeck(v, now);
            string sig = Signature(show, lc, v);
            if (sig != _sig)
            {
                bool slide = _sig != null && show == _show;
                _sig = sig;
                _show = show;
                _anims.Clear();
                _content.Clear();
                try { Build(show, lc, v, now); }
                catch (Exception e) { Debug.LogException(e); _content.Clear(); Text("UI: " + e.GetType().Name, "pj-small").AddTo(_content); }
                if (slide) UiMotion.SlideIn(_content, new Vector2(70f, 0f), 0.32f);
                else UiMotion.FadeIn(_content, 0.25f);
                Dots(show == Show.Dossier ? deckCount : 0);
                _foot.SetText(F("projector.foot", OfficeText.Key(GameInput.Phone), OfficeText.Key(GameInput.Computer)));
            }
            for (int i = 0; i < _anims.Count; i++) _anims[i](now, dt);
        }

        static Show Pick(LineView v, double now, out LineRecordingPick pick)
        {
            pick = null;
            if (v.Phase == LinePhase.Abandoned) return Show.Abandoned;
            if (v.Phase == LinePhase.Results) return Show.Results;
            if (v.Phase == LinePhase.FinalVerdict) return Show.Desk;
            if (v.Meeting != null)
                foreach (var p in v.Meeting.Recordings)
                    if (now >= p.PlayAt - 4 && now < p.PlayAt + Math.Max(1f, p.Seconds) + 3) { pick = p; return Show.Tape; }
            switch (v.Phase)
            {
                case LinePhase.FinalDefense: return Show.Defense;
                case LinePhase.MeetingVote:
                case LinePhase.CrisisVote:
                case LinePhase.FinalVote: return Show.Vote;
                case LinePhase.MeetingVerdict: return Show.Verdict;
                case LinePhase.CrisisVerdict: return Show.Crisis;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion:
                case LinePhase.FinalDiscussion: return Show.Dossier;
                default: return Show.None;
            }
        }

        static string Kicker(Show show, LineClient lc, LineView v)
        {
            int meeting = v.Meeting != null ? v.Meeting.Index + 1 : 1;
            var fv = v.Final;
            switch (show)
            {
                case Show.Tape: return T("projector.kicker.tape");
                case Show.Dossier: return v.Phase == LinePhase.FinalDiscussion ? T("projector.kicker.final") : F("projector.kicker.dossier", meeting);
                case Show.Vote:
                    if (v.Phase == LinePhase.MeetingVote) return F("projector.kicker.vote_meeting", meeting);
                    if (v.Phase == LinePhase.CrisisVote) return T("projector.kicker.vote_crisis");
                    return F(fv != null && fv.Revote ? "projector.kicker.vote_revote" : "projector.kicker.vote_final", fv != null ? fv.Round + 1 : 1, fv != null ? Math.Max(1, fv.FiringsTotal) : 1);
                case Show.Verdict: return F("projector.kicker.verdict", meeting);
                case Show.Crisis: return T("projector.kicker.crisis");
                case Show.Defense: return T("projector.kicker.defense");
                case Show.Desk: return F("projector.kicker.desk", fv != null ? fv.Firings.Count : 1, fv != null ? Math.Max(1, fv.FiringsTotal) : 1);
                case Show.Results: return T("projector.kicker.results");
                case Show.Abandoned: return T("projector.kicker.abandoned");
                default: return PlayText.PhaseTitle(v);
            }
        }

        void Dots(int n)
        {
            _dots.Clear();
            for (int i = 0; i < n && n > 1; i++) Box("pj-dot", i == _deckIndex ? "pj-dot-on" : null).NoPick().AddTo(_dots);
        }

        string Signature(Show show, LineClient lc, LineView v)
        {
            var sb = new StringBuilder(128);
            sb.Append((int)show).Append('|').Append(Loc.Language).Append('|').Append(lc.MySlot).Append('|').Append((int)v.Phase).Append('|').Append(v.PhaseStartTime);
            foreach (var p in v.Players) sb.Append(';').Append(p.Id).Append(p.Name).Append(p.ColorIndex).Append(p.Blame).Append(p.Candidate ? 'C' : '-').Append(p.Fired ? 'F' : '-').Append(p.Connected ? 'o' : 'x');
            var mt = v.Meeting;
            var fv = v.Final;
            switch (show)
            {
                case Show.Dossier:
                    sb.Append("|d").Append(_deckIndex);
                    if (mt != null)
                    {
                        sb.Append(':').Append(mt.Dossier.Count).Append(':').Append(mt.VoteableCount);
                        foreach (var r in mt.Recordings) sb.Append(',').Append(r.Index).Append(r.Played ? 'p' : '-').Append(r.Empty ? 'e' : '-');
                    }
                    if (fv != null) foreach (var g in fv.Crisis) sb.Append(";k").Append(g.Player).Append('=').Append(g.Amount);
                    break;
                case Show.Vote:
                    if (fv != null) { sb.Append("|f").Append(fv.Round).Append(fv.Revote).Append(fv.Firings.Count); foreach (int o in fv.Options) sb.Append(',').Append(o); }
                    if (mt != null) sb.Append("|m").Append(mt.VoteableCount).Append(mt.FailedTasks.Count);
                    break;
                case Show.Verdict:
                    if (mt != null) foreach (var g in mt.BlameGained) sb.Append(";g").Append(g.Player).Append('=').Append(g.Amount);
                    break;
                case Show.Crisis:
                    if (fv != null) { foreach (var g in fv.Crisis) sb.Append(";k").Append(g.Player).Append('=').Append(g.Amount); foreach (int c in fv.DefenseOrder) sb.Append(',').Append(c); }
                    break;
                case Show.Defense:
                    if (fv != null) { sb.Append("|").Append(fv.Defender).Append('@').Append(fv.DefenderEndTime); foreach (int c in fv.DefenseOrder) sb.Append(',').Append(c); }
                    break;
                case Show.Desk:
                    if (fv != null) sb.Append("|n").Append(fv.Firings.Count);
                    break;
                case Show.Results:
                    sb.Append(v.Results != null ? "|r" : "|-");
                    break;
            }
            return sb.ToString();
        }

        void Build(Show show, LineClient lc, LineView v, double now)
        {
            switch (show)
            {
                case Show.Dossier: BuildDeck(lc, v, now); break;
                case Show.Vote: BuildVote(lc, v, now); break;
                case Show.Verdict: BuildVerdict(lc, v); break;
                case Show.Crisis: BuildCrisis(lc, v, true); break;
                case Show.Defense: BuildDefense(lc, v, now); break;
                case Show.Desk: BuildDesk(lc, v, now); break;
                case Show.Results: BuildResults(lc, v, now); break;
                case Show.Abandoned:
                    Title(T("projector.abandoned"), "pj-title").NoPick().AddTo(_content);
                    Text(T("abandoned.text"), "pj-line").NoPick().AddTo(_content);
                    break;
            }
        }

        // ------------------------------------------------------------------ helpers

        static Label L(VisualElement p, string text, params string[] cls) { return Text(text, cls).NoPick().AddTo(p); }
        static Label B(VisualElement p, string text, params string[] cls) { return Bold(text, cls).NoPick().AddTo(p); }
        static Label H(VisualElement p, string text, params string[] cls) { return Title(text, cls).NoPick().AddTo(p); }
        static VisualElement Bx(VisualElement p, params string[] cls) { return Box(cls).NoPick().AddTo(p); }
        static Label Stamp(VisualElement p, string text, params string[] cls) { var l = UiKit.StampText(text, "pj-stamp").NoPick(); foreach (var c in cls) if (c != null) l.AddToClassList(c); return l.AddTo(p); }

        /// <summary>A colleague's face: their portrait in a ring of their colour (a coloured disc with the initial without art).</summary>
        static VisualElement Face(VisualElement p, LineClient lc, int id, string size = null)
        {
            var f = Box("pj-face", size).NoPick().AddTo(p);
            int color = lc.ColorOf(id);
            var c = PlayerColor(color);
            f.style.borderTopColor = f.style.borderBottomColor = f.style.borderLeftColor = f.style.borderRightColor = c;
            if (!UiArt.Apply(f, "portraits/player_" + color))
            {
                f.style.backgroundColor = c;
                string n = Name(id);
                B(f, string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpper(Loc.Culture), "pj-face-initial");
            }
            return f;
        }

        static VisualElement Person(VisualElement p, LineClient lc, int id, string size, string caption = null)
        {
            var col = Bx(p, "pj-person");
            Face(col, lc, id, size);
            var name = B(col, Name(id), "pj-person-name");
            name.style.color = Color.Lerp(PlayerColor(lc.ColorOf(id)), UiKit.Navy, 0.55f);
            if (!string.IsNullOrEmpty(caption)) L(col, caption, "pj-small");
            return col;
        }

        static VisualElement Chip(VisualElement p, LineClient lc, int id, string text, params string[] cls)
        {
            var chip = Bx(p, "pj-chip");
            foreach (var c in cls) if (c != null) chip.AddToClassList(c);
            Face(chip, lc, id, "pj-face-xs");
            B(chip, text, "pj-chip-text");
            return chip;
        }

        static float Speech(int slot)
        {
            if (LineWorldDriver.MockSpeech != null) return LineWorldDriver.MockSpeech(slot);
            var vs = VoiceSystem.Instance;
            return vs != null ? vs.GetSpeechLevel(slot) : 0f;
        }

        // ------------------------------------------------------------------ dossier deck

        enum Deck { Agenda, Candidates, Crisis, Entry, Leaks }

        static List<KeyValuePair<Deck, int>> DeckSlides(LineView v)
        {
            var list = new List<KeyValuePair<Deck, int>>();
            var mt = v.Meeting;
            if (v.Phase == LinePhase.FinalDiscussion)
            {
                list.Add(new KeyValuePair<Deck, int>(Deck.Candidates, 0));
                if (v.Final != null && v.Final.Crisis.Count > 0) list.Add(new KeyValuePair<Deck, int>(Deck.Crisis, 0));
            }
            else list.Add(new KeyValuePair<Deck, int>(Deck.Agenda, 0));
            if (mt != null)
            {
                for (int i = 0; i < mt.Dossier.Count; i++) list.Add(new KeyValuePair<Deck, int>(Deck.Entry, i));
                if (mt.Recordings.Count > 0) list.Add(new KeyValuePair<Deck, int>(Deck.Leaks, 0));
            }
            return list;
        }

        /// <summary>Pages the dossier: every 6.5 s the next slide; a new entry jumps in and stays 9 s. Returns the slide count.</summary>
        int AdvanceDeck(LineView v, double now)
        {
            var deck = DeckSlides(v);
            int entries = v.Meeting != null ? v.Meeting.Dossier.Count : 0;
            if (v.PhaseStartTime != _deckRound || v.Phase != _deckPhase)
            {
                // a new phase: the agenda first (MeetingDossier → MeetingDiscussion keeps paging where it was)
                bool cont = _deckPhase == LinePhase.MeetingDossier && v.Phase == LinePhase.MeetingDiscussion;
                _deckRound = v.PhaseStartTime;
                _deckPhase = v.Phase;
                if (!cont) { _deckIndex = 0; _deckNext = now + 7.0; }
                _deckEntries = entries;
            }
            if (entries > _deckEntries)
            {
                _deckEntries = entries;
                int at = deck.FindIndex(s => s.Key == Deck.Entry && s.Value == entries - 1);
                if (at >= 0) { _deckIndex = at; _deckNext = now + 9.0; _deckNew = true; }
            }
            else if (now >= _deckNext && deck.Count > 1)
            {
                _deckIndex = (_deckIndex + 1) % deck.Count;
                _deckNext = now + 6.5;
            }
            if (MockDeckIndex >= 0) { _deckIndex = MockDeckIndex; _deckNext = now + 1000.0; }
            _deckIndex = Mathf.Clamp(_deckIndex, 0, Mathf.Max(0, deck.Count - 1));
            return deck.Count;
        }

        void BuildDeck(LineClient lc, LineView v, double now)
        {
            var deck = DeckSlides(v);
            if (deck.Count == 0) return;
            var s = deck[Mathf.Clamp(_deckIndex, 0, deck.Count - 1)];
            switch (s.Key)
            {
                case Deck.Agenda: BuildAgenda(lc, v); break;
                case Deck.Candidates: BuildCandidates(lc, v); break;
                case Deck.Crisis: BuildCrisis(lc, v, false); break;
                case Deck.Entry: BuildEntry(lc, v, v.Meeting.Dossier[s.Value]); break;
                case Deck.Leaks: BuildLeaks(lc, v, now); break;
            }
        }

        void BuildAgenda(LineClient lc, LineView v)
        {
            var mt = v.Meeting;
            var c = lc.Config;
            if (mt == null || mt.FailedTasks.Count == 0)
            {
                H(_content, T("projector.agenda.clean_title"), "pj-title", "pj-good");
                L(_content, T("projector.agenda.clean"), "pj-line");
                if (mt != null && mt.Roster.Count > 0) L(_content, F("meeting.roster", PlayText.Names(mt.Roster)), "pj-small");
                return;
            }
            H(_content, T("projector.agenda.failed"), "pj-title");
            Bx(_content, "pj-rule");
            foreach (var t in mt.FailedTasks)
            {
                var row = Bx(_content, "pj-task");
                H(row, "✕", "pj-task-x");
                B(row, PlayText.Task(t), "pj-task-name");
            }
            string vote = mt.VoteableCount > 0 ? F("projector.agenda.ballots", mt.VoteableCount, c.BlamePerVote)
                : mt.VoteableCount == 0 ? T("meeting.no_vote") : T("focus.meeting_vote_next");
            L(_content, vote, "pj-line", "pj-warn");
            if (mt.Roster.Count > 0) L(_content, F("meeting.roster", PlayText.Names(mt.Roster)), "pj-small");
        }

        void BuildCandidates(LineClient lc, LineView v)
        {
            var fv = v.Final;
            H(_content, fv != null ? F("projector.candidates", fv.FiringsTotal) : T("hint.FinalDiscussion"), "pj-title");
            if (fv == null) return;
            var row = Bx(_content, "pj-row");
            row.style.marginTop = 12;
            foreach (int id in fv.DefenseOrder)
            {
                var pl = lc.Player(id);
                var col = Person(row, lc, id, "pj-face-l", F("players.blame", pl != null ? pl.Blame : 0));
                col.style.marginRight = 44;
                Stamp(col, T("players.sealed"), "pj-stamp-navy", "pj-stamp-small");
            }
            L(_content, T("projector.candidates_sealed"), "pj-small");
        }

        /// <summary>The crisis result: who got its suç, and how the board moved (suç before → after; the candidates).</summary>
        void BuildCrisis(LineClient lc, LineView v, bool verdict)
        {
            var fv = v.Final;
            H(_content, T("projector.crisis.title"), "pj-title");
            if (fv == null) return;
            var top = Bx(_content, "pj-wrap");
            if (fv.Crisis.Count == 0) L(top, T("crisis.none"), "pj-line", "pj-dim");
            foreach (var g in fv.Crisis) Chip(top, lc, g.Player, Name(g.Player) + "  " + F("meeting.blame_plus", g.Amount), "pj-chip-bad");
            B(_content, T("projector.crisis.shift"), "pj-small");
            var gained = new Dictionary<int, int>();
            foreach (var g in fv.Crisis) gained[g.Player] = g.Amount;
            var players = new List<LinePlayerView>(v.Players);
            players.Sort((a, b) => b.Blame != a.Blame ? b.Blame.CompareTo(a.Blame) : a.Id.CompareTo(b.Id));
            var list = Bx(_content, "pj-col");
            int shown = 0;
            foreach (var p in players)
            {
                if (shown++ >= 6) break;
                int plus;
                gained.TryGetValue(p.Id, out plus);
                var r = Bx(list, "pj-shift");
                Face(r, lc, p.Id, "pj-face-xs");
                var n = B(r, p.Name, "pj-shift-name");
                n.style.color = Color.Lerp(PlayerColor(p.ColorIndex), UiKit.Navy, 0.55f);
                if (plus > 0)
                {
                    B(r, (p.Blame - plus).ToString(), "pj-shift-from");
                    B(r, "→", "pj-shift-arrow");
                }
                else { B(r, "", "pj-shift-from"); B(r, "", "pj-shift-arrow"); }
                B(r, p.Blame.ToString(), "pj-shift-to", plus > 0 ? null : "pj-dim");
                if (p.Candidate)
                {
                    Stamp(r, T("players.candidate"), "pj-stamp-orange", "pj-stamp-small");
                    if (v.Phase < LinePhase.Results) Stamp(r, T("players.sealed"), "pj-stamp-navy", "pj-stamp-small").style.marginLeft = 10;
                }
            }
        }

        static string KindStampClass(DossierKind k)
        {
            switch (k)
            {
                case DossierKind.Investigation: return "pj-stamp-navy";
                case DossierKind.Leak: return "pj-stamp-orange";
                case DossierKind.ShiftRoster: return "pj-stamp-green";
                default: return null;
            }
        }

        void BuildEntry(LineClient lc, LineView v, LineDossierEntry e)
        {
            var box = Bx(_content, "pj-entry");
            var stamp = Stamp(box, T("projector.kind." + e.Kind), KindStampClass(e.Kind));
            // a new entry stamps itself in with a thud; paging through the deck stays quiet
            bool fresh = _deckNew;
            _deckNew = false;
            UiMotion.Stamp(stamp, -5f, 0.22f, 0.15f, fresh ? (Action)(() => Sfx.Play("stamp", 0.45f)) : null);
            string text = PlayText.Dossier(e);
            if (e.Kind == DossierKind.Investigation) text = F(e.N1 == 1 ? "projector.investigation_guilty" : "projector.investigation_innocent", Name(e.Player), Name(e.Other));
            B(box, text, "pj-entry-text", e.Kind == DossierKind.Investigation && e.N1 == 1 || e.Kind == DossierKind.Whistle || e.Kind == DossierKind.CashCaught ? "pj-bad" : null);
            var row = Bx(box, "pj-row");
            var who = new List<int>();
            switch (e.Kind)
            {
                case DossierKind.Whistle:
                case DossierKind.Investigation: who.Add(e.Player); who.Add(e.Other); break;
                case DossierKind.CashCaught: who.Add(e.Player); break;
                case DossierKind.Leak: who.Add(e.Player); who.Add(e.N1); who.Add(e.N2); break;
            }
            for (int i = 0; i < who.Count; i++)
            {
                if (who[i] < 0) continue;
                string role = RoleOf(e, i);
                Person(row, lc, who[i], "pj-face", role);
            }
            if (e.Kind == DossierKind.ShiftRoster && v.Meeting != null && v.Meeting.Roster.Count > 0)
                foreach (int id in v.Meeting.Roster) Person(row, lc, id, "pj-face-s");
        }

        static string RoleOf(LineDossierEntry e, int i)
        {
            switch (e.Kind)
            {
                case DossierKind.Whistle: return T(i == 0 ? "projector.role.whistler" : "projector.role.thief");
                case DossierKind.Investigation: return T(i == 0 ? "projector.role.investigator" : (e.N1 == 1 ? "projector.role.guilty" : "projector.role.innocent"));
                case DossierKind.CashCaught: return T("projector.role.caught");
                case DossierKind.Leak:
                    if (i == 0) return T("projector.role.leaker");
                    return i == 1 ? Loc.T("rec.caller") : Loc.T("rec.callee");
                default: return null;
            }
        }

        void BuildLeaks(LineClient lc, LineView v, double now)
        {
            var mt = v.Meeting;
            H(_content, T("projector.leaks"), "pj-title");
            Bx(_content, "pj-rule");
            foreach (var r in mt.Recordings)
            {
                var pick = r;
                var row = Bx(_content, "pj-row");
                row.style.marginBottom = 10;
                Face(row, lc, r.Caller, "pj-face-s");
                Face(row, lc, r.Callee, "pj-face-s").style.marginLeft = -18;
                var line = L(row, "", "pj-line", "pj-grow");
                line.style.marginLeft = 18;
                string head = F("projector.leak_line", Name(r.Caller), Name(r.Callee), Name(r.Leaker)) + " · ";
                _anims.Add((t, dt) => line.SetText(head + PlayBlocks.LeakState(pick, t)));
            }
        }

        // ------------------------------------------------------------------ votes and verdicts

        void BuildVote(LineClient lc, LineView v, double now)
        {
            var c = lc.Config;
            var fv = v.Final;
            string title = v.Phase == LinePhase.MeetingVote ? T("projector.vote.meeting")
                : v.Phase == LinePhase.CrisisVote ? F("projector.vote.crisis", c.CrisisBlame)
                : fv != null && fv.Revote ? T("projector.vote.revote") : T("projector.vote.final");
            H(_content, title, "pj-title");
            if (v.Phase == LinePhase.MeetingVote && v.Meeting != null && v.Meeting.FailedTasks.Count > 0)
                L(_content, F("meeting.failed", PlayBlocks.JoinTasks(v.Meeting.FailedTasks)) + " · " + F("projector.agenda.ballots", Math.Max(1, v.Meeting.VoteableCount), c.BlamePerVote), "pj-small");
            if (v.Phase == LinePhase.FinalVote && fv != null && fv.Options.Count > 0)
            {
                var row = Bx(_content, "pj-row");
                foreach (int id in fv.Options) Chip(row, lc, id, Name(id));
                L(_content, T("projector.vote.blank_ok"), "pj-small");
            }
            else L(_content, T("projector.vote.secret"), "pj-small");
            // the papers: one per colleague who can vote; a dropped one turns over (never what is on it)
            var voters = Bx(_content, "pj-voters");
            var cells = new List<KeyValuePair<int, VisualElement>>();
            foreach (var p in v.Players)
            {
                if (p.Fired) continue;
                var cell = Bx(voters, "pj-voter");
                var paper = Bx(cell, "pj-paper");
                H(paper, "✓", "pj-paper-mark");
                var n = B(cell, p.Name, "pj-voter-name");
                n.style.color = Color.Lerp(PlayerColor(p.ColorIndex), UiKit.Navy, 0.5f);
                cells.Add(new KeyValuePair<int, VisualElement>(p.Id, cell));
            }
            var count = H(_content, "", "pj-count");
            _anims.Add((t, dt) =>
            {
                var cur = LineClient.I != null ? LineClient.I.View : null;
                if (cur == null) return;
                foreach (var kv in cells)
                {
                    var pl = lc.Player(kv.Key);
                    bool inBox = BallotDrops.InBox(cur, pl);
                    if (kv.Value.ClassListContains("pj-voter-in") != inBox)
                    {
                        kv.Value.EnableInClassList("pj-voter-in", inBox);
                        if (inBox) UiMotion.Pulse(kv.Value, 0.12f, 0.3f);
                    }
                }
                int inBoxN, all;
                BallotDrops.Count(cur, out inBoxN, out all);
                count.SetText(F("meeting.slide.progress", inBoxN, all));
            });
            // 5-6 players: the first desk was opened before this vote
            if (v.Phase == LinePhase.FinalVote && fv != null && fv.Firings.Count > 0)
            {
                var f = fv.Firings[fv.Firings.Count - 1];
                var row = Bx(_content, "pj-row");
                row.style.marginTop = 6;
                Face(row, lc, f.Player, "pj-face-xs");
                var l = L(row, F("projector.vote.previous", fv.Firings.Count, Name(f.Player), f.Desk, f.Recipients.Count > 0 ? PlayText.Names(f.Recipients) : T("common.none")), "pj-small");
                l.style.marginLeft = 12;
                l.style.marginTop = 0;
            }
        }

        void BuildVerdict(LineClient lc, LineView v)
        {
            H(_content, T("projector.verdict"), "pj-title");
            var mt = v.Meeting;
            if (mt == null || !mt.BlameGained.Exists(g => g.Amount > 0)) { L(_content, T("focus.no_blame"), "pj-line", "pj-dim"); return; }
            var row = Bx(_content, "pj-row");
            row.style.marginTop = 14;
            foreach (var g in mt.BlameGained)
            {
                if (g.Amount <= 0) continue;
                var col = Person(row, lc, g.Player, "pj-face-l");
                col.style.marginRight = 40;
                var plus = H(col, F("meeting.blame_plus", g.Amount), "pj-big", "pj-bad");
                UiMotion.PopIn(plus, 0.3f, 0.2f);
            }
            L(_content, T("projector.verdict_board"), "pj-small");
        }

        // ------------------------------------------------------------------ defence

        void BuildDefense(LineClient lc, LineView v, double now)
        {
            var fv = v.Final;
            if (fv == null || fv.Defender < 0)
            {
                H(_content, T("projector.defense.over"), "pj-title");
                return;
            }
            int id = fv.Defender;
            double end = fv.DefenderEndTime;
            var row = Bx(_content, "pj-row");
            var spot = Bx(row, "pj-spot");
            Face(spot, lc, id, "pj-face-xl");
            var mid = Bx(row, "pj-col", "pj-grow");
            Stamp(mid, T("projector.defense.speaking"), "pj-stamp-orange");
            var name = H(mid, Name(id), "pj-title");
            name.style.fontSize = 78;
            name.style.color = Color.Lerp(PlayerColor(lc.ColorOf(id)), UiKit.Navy, 0.55f);
            var timerRow = Bx(mid, "pj-row");
            var timer = H(timerRow, "", "pj-timer");
            var bars = Bx(timerRow, "pj-bars");
            var bar = new VisualElement[5];
            for (int i = 0; i < bar.Length; i++) bar[i] = Bx(bars, "pj-bar");
            if (id == lc.MySlot) L(mid, T("focus.your_turn"), "pj-line", "pj-bad");
            // the order (done / now / waiting)
            var order = Bx(row, "pj-order");
            int at = fv.DefenseOrder.IndexOf(id);
            for (int i = 0; i < fv.DefenseOrder.Count; i++)
            {
                int who = fv.DefenseOrder[i];
                var r = Bx(order, "pj-order-row", who == id ? "pj-order-now" : null);
                Face(r, lc, who, "pj-face-s");
                var col = Bx(r, "pj-col");
                B(col, Name(who), "pj-order-name");
                L(col, T(who == id ? "projector.defense.now" : at >= 0 && i < at ? "projector.defense.done" : "projector.defense.wait"), "pj-order-state").style.marginLeft = 14;
            }
            float phase = UnityEngine.Random.value * 6f;
            _anims.Add((t, dt) =>
            {
                double left = Math.Max(0, end - t);
                timer.SetText(UiKit.Clock(left));
                timer.EnableInClassList("pj-timer-low", left < 5);
                float level = Mathf.Clamp01(Speech(id));
                for (int i = 0; i < bar.Length; i++)
                {
                    float h = 10f + 40f * Mathf.Clamp01(level * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * (7f + i * 1.7f) + phase + i))));
                    bar[i].style.height = h;
                }
                spot.style.scale = new Scale(Vector3.one * (1f + 0.04f * level));
            });
        }

        // ------------------------------------------------------------------ desk opening

        void BuildDesk(LineClient lc, LineView v, double now)
        {
            var fv = v.Final;
            var f = DeskOpening.Current(v);
            if (f == null) { H(_content, T("hint.FinalVerdict"), "pj-title"); return; }
            int me = lc.MySlot;
            var row = Bx(_content, "pj-row");
            row.style.alignItems = Align.FlexStart;
            // left: who, the stamp, how it was decided
            var left = Bx(row, "pj-col");
            left.style.width = 330;
            left.style.alignItems = Align.Center;
            Face(left, lc, f.Player, "pj-face-l");
            var name = H(left, Name(f.Player), "pj-title");
            name.style.fontSize = 56;
            name.style.color = Color.Lerp(PlayerColor(lc.ColorOf(f.Player)), UiKit.Navy, 0.55f);
            var fired = Stamp(left, T("players.fired"));
            fired.style.alignSelf = Align.Center;
            var decision = L(left, F("fire.decision." + f.Decision, f.Votes), "pj-small");
            decision.style.unityTextAlign = TextAnchor.MiddleCenter;
            // right: the drawer (seen from above) slides out from under the desk; the bundles are counted
            var right = Bx(row, "pj-col", "pj-grow");
            right.style.marginLeft = 30;
            var desk = Bx(right, "pj-desk");
            var drawer = Bx(desk, "pj-drawer");
            var tray = Bx(drawer, "pj-tray");
            var front = Bx(drawer, "pj-drawer-front");
            Bx(front, "pj-handle");
            Bx(desk, "pj-desktop");
            int bundles = f.Desk / 5, loose = f.Desk % 5, shownBundles = Math.Min(bundles, 13);
            var items = new List<VisualElement>();
            for (int i = 0; i < shownBundles; i++) { var b = Bx(tray, "pj-bundle"); Bx(b, "pj-band"); items.Add(b); }
            if (bundles > shownBundles) items.Add(B(tray, "+" + (bundles - shownBundles) * 5, "pj-more"));
            for (int i = 0; i < loose; i++) items.Add(Bx(tray, "pj-note"));
            foreach (var it in items) it.style.opacity = 0f;
            var stamp = Stamp(desk, F("projector.desk.count", f.Desk), "pj-desk-stamp");
            var counter = H(right, "", "pj-counter");
            // after the count: who shares it
            var shares = Bx(right, "pj-col");
            if (f.Recipients.Count > 0)
            {
                L(shares, F("fire.voters", f.Share), "pj-small");
                var chips = Bx(shares, "pj-wrap");
                foreach (int id in f.Recipients) Chip(chips, lc, id, F("fire.chip", Name(id), f.Share), id == me ? "pj-chip-me" : "pj-chip-good");
                if (f.Removed > 0) L(shares, F("fire.burned", f.Removed), "pj-small", "pj-dim");
            }
            else L(shares, F("fire.nobody", f.Desk), "pj-line", "pj-dim");
            if (me == f.Player) L(shares, T("fire.you_fired"), "pj-small", "pj-bad");
            else if (f.Recipients.Contains(me)) L(shares, F("fire.you_got", f.Share), "pj-small", "pj-good");
            shares.style.opacity = 0f;
            float t0 = DeskOpening.Time(v, now);
            _stamped = t0 >= DeskOpening.StampAt;
            bool chipsIn = t0 >= DeskOpening.ChipsAt, drawerSound = t0 >= DeskOpening.DrawerOpen * 0.3f;
            // built after the moment (a late join, a language switch): show it at rest, no replay
            if (_stamped) stamp.style.opacity = 1f;
            if (chipsIn) shares.style.opacity = 1f;
            int lastShown = -1;
            if (DeskOpening.Count(t0) >= 1f) { foreach (var it in items) it.style.opacity = 1f; lastShown = items.Count - 1; }
            _anims.Add((t, dt) =>
            {
                var cur = LineClient.I != null ? LineClient.I.View : v;
                float u = DeskOpening.Time(cur != null && cur.Phase == LinePhase.FinalVerdict ? cur : v, t);
                // the drawer slides out from under the desk top
                float open = Mathf.Clamp01(u / DeskOpening.DrawerOpen);
                float eo = 1f - (1f - open) * (1f - open) * (1f - open);
                drawer.style.translate = new Translate(0, Mathf.Lerp(-162f, 0f, eo));
                if (!drawerSound && u > DeskOpening.DrawerOpen * 0.3f) { drawerSound = true; Sfx.Play("drawer_slide", 0.5f); }
                float c = DeskOpening.Count(u);
                int shown = Mathf.Clamp(Mathf.CeilToInt(c * items.Count - 0.001f), 0, items.Count);
                for (int i = lastShown + 1; i < shown; i++) { items[i].style.opacity = 1f; UiMotion.PopIn(items[i], 0.16f, 0f, 0.6f); if (i % 2 == 0) Sfx.Play("bills", 0.25f); }
                if (shown - 1 > lastShown) lastShown = shown - 1;
                int n = Mathf.RoundToInt(c * f.Desk);
                counter.SetText(u < DeskOpening.CountStart ? T("projector.desk.opening") : F("projector.desk.count", n));
                counter.EnableInClassList("pj-counter-done", c >= 1f);
                if (!_stamped && u >= DeskOpening.StampAt)
                {
                    _stamped = true;
                    UiMotion.Stamp(stamp, -9f, 0.24f, 0f, () => { Sfx.Play("stamp", 0.7f); Sfx.Play("fired", 0.45f); });
                }
                if (!chipsIn && u >= DeskOpening.ChipsAt) { chipsIn = true; shares.style.opacity = 1f; UiMotion.FadeIn(shares, 0.35f); }
            });
        }

        // ------------------------------------------------------------------ results

        void BuildResults(LineClient lc, LineView v, double now)
        {
            var r = v.Results;
            if (r == null) { H(_content, T("results.waiting"), "pj-title"); return; }
            int me = lc.MySlot;
            var players = PlayBlocks.SortedResults(r);
            int best = players.Count > 0 && players[0].Winner ? players[0].Prim : 0;
            var row = Bx(_content, "pj-row");
            row.style.alignItems = Align.FlexStart;
            // the winner(s)
            var win = Bx(row, "pj-winner");
            var faces = Bx(win, "pj-row");
            int shownWinners = 0;
            foreach (int id in r.Winners) { if (shownWinners++ >= 3) break; Face(faces, lc, id, r.Winners.Count > 1 ? "pj-face-l" : "pj-face-xl").style.marginRight = r.Winners.Count > 1 ? 10 : 0; }
            var badge = Bx(win, "pj-winner-stamp");
            if (!UiArt.ApplyStamp(badge, "stamps_v3/stamp_haftanin_elemani", "stamps_v3/stamp_employee_week_en"))
                Stamp(badge, T("results.winner"), "pj-stamp-green");
            H(win, PlayText.Names(r.Winners), "pj-winner-name");
            B(win, F("projector.results.prim", best), "pj-winner-prim");
            win.style.opacity = (float)(now - v.PhaseStartTime) >= 0.4f + players.Count * 0.55f + 0.2f ? 1f : 0f;
            // the ranking by prim, revealed from the bottom up
            var list = Bx(row, "pj-col", "pj-grow");
            B(list, T("projector.results.ranking"), "pj-small").style.marginTop = 0;
            var rows = new List<VisualElement>();
            var prims = new List<KeyValuePair<Label, int>>();
            int rank = 0, prev = int.MinValue;
            for (int i = 0; i < players.Count; i++)
            {
                var pr = players[i];
                int sortPrim = pr.Fired ? -1 : pr.Prim;
                if (sortPrim != prev) { rank = i + 1; prev = sortPrim; }
                var line = Bx(list, "pj-rank", pr.Winner ? "pj-rank-win" : null);
                B(line, pr.Fired ? "–" : "#" + rank, "pj-rank-n");
                Face(line, lc, pr.Player, "pj-face-xs");
                var n = B(line, Name(pr.Player) + (pr.Player == me ? " (" + T("players.me") + ")" : ""), "pj-rank-name");
                n.style.color = Color.Lerp(PlayerColor(lc.ColorOf(pr.Player)), UiKit.Navy, 0.55f);
                var tagCol = Bx(line, "pj-rank-tagcol");
                if (pr.Fired) Stamp(tagCol, T("players.fired"), "pj-stamp-small", "pj-rank-tag");
                var prim = H(line, "", "pj-rank-prim");
                B(line, F("players.blame", pr.Blame), "pj-rank-blame");
                rows.Add(line);
                prims.Add(new KeyValuePair<Label, int>(prim, pr.Prim));
            }
            const float Step = 0.55f, Start = 0.4f;
            int count = rows.Count;
            float winAt = Start + count * Step + 0.2f;
            float t0 = (float)(now - v.PhaseStartTime);
            _cheered = t0 >= winAt;
            if (_cheered) badge.style.opacity = 1f;
            var revealed = new bool[count];
            _anims.Add((t, dt) =>
            {
                float u = (float)(t - v.PhaseStartTime);
                for (int i = 0; i < count; i++)
                {
                    // last place first
                    int idx = count - 1 - i;
                    float at = Start + i * Step;
                    float k = Mathf.Clamp01((u - at) / 0.35f);
                    rows[idx].style.opacity = k;
                    rows[idx].style.translate = new Translate(60f * (1f - k), 0);
                    float cnt = Mathf.Clamp01((u - at) / 0.5f);
                    prims[idx].Key.SetText(F("projector.results.prim", Mathf.RoundToInt(prims[idx].Value * cnt)));
                    if (k > 0f && !revealed[idx]) { revealed[idx] = true; if (u - at < 0.3f) Sfx.Play("click", 0.4f); }
                }
                if (!_cheered) win.style.opacity = Mathf.Clamp01((u - winAt) / 0.4f);
                if (!_cheered && u >= winAt)
                {
                    _cheered = true;
                    win.style.opacity = 1f;
                    UiMotion.PopIn(win, 0.35f, 0f, 0.7f);
                    UiMotion.Stamp(badge, -6f, 0.26f, 0.2f, () => Sfx.Play("stamp", 0.6f));
                    Sfx.Play("cheer", 0.55f);
                }
            });
        }
    }
}
