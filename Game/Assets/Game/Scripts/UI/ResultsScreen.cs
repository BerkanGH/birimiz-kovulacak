using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using EventType = BirimizKovulacak.Core.EventType;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// End of day: termination letter with a stamp, full reveal (roles, objectives, cards, incidents with true actors,
    /// calls), "Günün klibi" replay, week board + career, "Ayın Elemanı" on Friday, rematch / next-day vote.
    /// </summary>
    public sealed class ResultsScreen : UiKit.Screen
    {
        enum Tab { People = 0, Clip = 1, Truth = 2, Calls = 3, Week = 4 }

        readonly VisualElement _letter, _letterBody, _stamp, _letterActions, _content, _rematchBar, _eotm, _eotmBody, _clipHost;
        readonly Label _timer, _headline, _subline, _votes, _hash;
        readonly Button _rematch, _force, _toLobby, _leave, _save;
        readonly UiKit.TabBar _tabs;
        readonly ScrollView _scroll;
        readonly HighlightPlayer _clip;
        Tab _tab = Tab.People;
        int _builtMatch = -1, _builtState = -1, _lastSec = -1, _eotmShownFor = -1, _rematchKey = -1;
        bool _hasClip, _saving;

        public ResultsScreen(VisualElement parent) : base(parent, "dim-soft")
        {
            var wrap = Box("results-wrap").AddTo(Root);

            // ---- letter column
            var letterCol = Box("letter-col", "col").AddTo(wrap);
            _letter = Box("letter").AddTo(letterCol);
            if (!UiArt.Apply(_letter, "meeting/termination_letter")) _letter.AddToClassList("letter-fallback");
            _letterBody = Box("col", "grow").AddTo(_letter);
            _stamp = Box("stamp").NoPick().AddTo(_letter);
            _letterActions = Box("letter-actions").AddTo(letterCol);
            _save = IconBtn("star", Loc.T("results.save_letter"), SaveLetter, "btn-small").AddTo(_letterActions);

            // ---- reveal column
            var reveal = Panel("panel", "reveal", "col").AddTo(wrap);
            var head = Box("reveal-head").AddTo(reveal);
            var hc = Box("col", "grow").AddTo(head);
            _headline = Title("", "h2").AddTo(hc);
            _subline = Bold("", "t-small").AddTo(hc);
            _timer = Bold("", "t-muted").AddTo(head);
            _tabs = new UiKit.TabBar(reveal, id => { _tab = (Tab)id; _builtState = -1; _scroll.scrollOffset = Vector2.zero; });
            _tabs.Add((int)Tab.People, Loc.T("results.tab.people"), "contacts");
            _tabs.Add((int)Tab.Clip, Loc.T("results.tab.clip"), "footage");
            _tabs.Add((int)Tab.Truth, Loc.T("results.tab.truth"), "warning");
            _tabs.Add((int)Tab.Calls, Loc.T("results.tab.calls"), "call");
            _tabs.Add((int)Tab.Week, Loc.T("results.tab.week"), "week_calendar");
            _scroll = new ScrollView().Cls("reveal-body").AddTo(reveal);
            _content = _scroll.contentContainer;
            _clipHost = Box("col");
            _clip = new HighlightPlayer(_clipHost, 920, 575);

            _rematchBar = Box("rematch-bar").AddTo(reveal);
            _rematch = Btn("", () => ClientState.I.Send(ActionVerb.RematchVote), "btn-primary", "btn-big").AddTo(_rematchBar);
            _votes = Bold("", "t-muted", "grow").AddTo(_rematchBar);
            _force = Btn(Loc.T("results.force_rematch"), () => ConnectionManager.I.HostController?.StartRematch(), "btn-small").AddTo(_rematchBar);
            _toLobby = Btn(Loc.T("pause.return_lobby"), () => ConnectionManager.I.HostController?.ReturnToLobby(), "btn-small", "btn-ghost").AddTo(_rematchBar);
            _leave = Btn(Loc.T("pause.leave"), () => ConnectionManager.I.Leave(), "btn-small", "btn-danger").AddTo(_rematchBar);
            _hash = Text("", "t-tiny", "t-muted").AddTo(reveal);

            // ---- Ayın Elemanı overlay
            _eotm = Box("screen", "eotm").AddTo(Root);
            var ec = Panel("panel", "eotm-card").AddTo(_eotm);
            var st = Box("eotm-stamp").AddTo(ec);
            if (!UiArt.Apply(st, "stamps/stamp_ayin_elemani")) { st.AddToClassList("stamp-fallback"); StampText(Loc.T("results.eotm_stamp")).AddTo(st); }
            _eotmBody = Box("col", "center").AddTo(ec);
            Btn(Loc.T("results.eotm_close"), () => _eotm.Show(false), "btn-primary").AddTo(ec);
            _eotm.Show(false);

            if (ClientState.I != null) ClientState.I.EventReceived += OnEvent;
        }

        void OnEvent(GameEvent e)
        {
            if (Visible && e.Type == EventType.RematchVote) _rematchKey = -1;
        }

        protected override void OnShow() { _builtMatch = -1; _builtState = -1; _rematchKey = -1; }

        public override void Tick()
        {
            if (!Visible) return;
            var cs = ClientState.I;
            var v = cs.View;
            if (v == null) return;
            int sec = (int)System.Math.Ceiling(System.Math.Max(0, v.PhaseEndTime - cs.Now));
            if (sec != _lastSec)
            {
                _lastSec = sec;
                _timer.text = v.Phase == MatchPhase.Results ? Loc.F("results.next_in", ClockInt(sec)) : "";
            }
            if (_tab == Tab.Clip && _hasClip) _clip.Tick(Time.unscaledDeltaTime);

            int state = (v.Result != null ? 1 : 0) + (v.Phase == MatchPhase.Abandoned ? 2 : 0) + (int)_tab * 10;
            if (_builtMatch != v.MatchId && v.Result != null)
            {
                // New result: open on the replay when there is one, else on the people reveal.
                bool clip = v.Result.Highlight != null && v.Result.Highlight.Samples.Count > 0;
                _tab = clip ? Tab.Clip : Tab.People;
                _tabs.Select((int)_tab, false);
                state = 1 + (int)_tab * 10;
            }
            if (_builtMatch != v.MatchId || _builtState != state)
            {
                bool fresh = _builtMatch != v.MatchId || (_builtState % 10) != (state % 10);
                _builtMatch = v.MatchId;
                _builtState = state;
                if (v.Phase == MatchPhase.Abandoned) BuildAbandoned(v);
                else if (v.Result == null) BuildWaiting();
                else Build(v, fresh);
            }
            UpdateRematch(v);
        }

        // ------------------------------------------------------------------ build

        void BuildWaiting()
        {
            _letterBody.Clear();
            _content.Clear();
            Title(Loc.T("results.waiting"), "h2").AddTo(_letterBody);
            _stamp.Show(false);
            _headline.text = Loc.T("results.title");
            _subline.text = "";
        }

        void BuildAbandoned(PlayerView v)
        {
            _letterBody.Clear();
            _content.Clear();
            _stamp.Show(false);
            Title(Loc.T("results.abandoned"), "h1", "t-bad").AddTo(_letterBody);
            Text(Loc.T("abandon." + v.AbandonReason), "letter-body").AddTo(_letterBody);
            Text(Loc.T("results.abandoned_note"), "letter-body", "t-muted").AddTo(_letterBody);
            _headline.text = Loc.T("results.abandoned");
            _subline.text = "";
            _tabs.Root.Show(false);
        }

        void Build(PlayerView v, bool fresh)
        {
            var r = v.Result;
            _tabs.Root.Show(true);
            _hasClip = r.Highlight != null && r.Highlight.Samples.Count > 0;
            _tabs.SetTabVisible((int)Tab.Clip, _hasClip);
            _tabs.SetTabVisible((int)Tab.Week, r.Mode == GameMode.Week);
            _tabs.SetTabVisible((int)Tab.Calls, r.Calls.Count > 0);
            if (fresh)
            {
                BuildLetter(v, r);
                if (_hasClip) _clip.Load(r.Highlight, v);
                if (r.WeekFinished && _eotmShownFor != v.MatchId) ShowEmployeeOfMonth(v, r);
            }

            _headline.text = r.CompanySucceeded ? Loc.F("results.company_ok", r.QuotaPoints.ToString("0"), r.QuotaTarget.ToString("0")) : Loc.F("results.company_fail", r.QuotaPoints.ToString("0"), r.QuotaTarget.ToString("0"));
            _headline.EnableInClassList("t-good", r.CompanySucceeded);
            _headline.EnableInClassList("t-bad", !r.CompanySucceeded);
            string sub = Loc.F("results.informant_was", Loc.PlayerName(v, r.InformantPlayer)) + " " + Loc.T(r.InformantWon ? "results.informant_won" : "results.informant_lost");
            if (r.Favourites.Count > 0) sub += "  ·  " + Loc.F("results.favourites", Names(v, r.Favourites));
            _subline.text = sub;
            _hash.text = Loc.F("results.hash", r.ResultHash);

            _content.Clear();
            switch (_tab)
            {
                case Tab.People: BuildPeople(v, r); break;
                case Tab.Clip: _clipHost.AddTo(_content); break;
                case Tab.Truth: BuildTruth(v, r); break;
                case Tab.Calls: BuildCalls(v, r); break;
                case Tab.Week: BuildWeek(v, r); break;
            }
        }

        static string Names(PlayerView v, List<int> ids)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ids.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(Loc.PlayerName(v, ids[i])); }
            return sb.ToString();
        }

        void BuildLetter(PlayerView v, MatchResultView r)
        {
            _letterBody.Clear();
            int fired = r.FiredPlayer;
            Bold(Loc.T("letter.dept"), "letter-head").AddTo(_letterBody);
            Memo(Loc.F("letter.ref", v.MatchId, r.Mode == GameMode.Week ? Loc.DayName(r.WeekDay) : Loc.T("letter.today")), "letter-ref").AddTo(_letterBody);
            MemoBold(Loc.T(fired >= 0 ? "letter.title" : "letter.title_none"), "t-mono", "h3").AddTo(_letterBody);
            if (fired >= 0)
            {
                var who = Box("row").AddTo(_letterBody);
                Badge(fired, "badge-l").AddTo(who);
                var dear = Title(Loc.F("letter.dear", Loc.PlayerName(v, fired)), "letter-name").AddTo(who);
                dear.style.marginLeft = 12;
                Memo(Loc.T("letter.body.0"), "letter-body").AddTo(_letterBody);
                string reason = Reason(v, r);
                if (reason != null) Memo(Loc.F("letter.reason", reason), "letter-body").AddTo(_letterBody);
                foreach (var c in r.Verdict.Candidates)
                    if (c.Player == fired && c.Votes > 0) Memo(Loc.F("letter.votes", c.Votes, (c.VoteShare * 100).ToString("0")), "letter-body").AddTo(_letterBody);
                Memo(Loc.T("letter.closing." + (Mathf.Abs(r.MatchSeed) % 3)), "letter-body").AddTo(_letterBody);
                if (!r.CompanySucceeded) Memo(Loc.T("letter.company_failed"), "letter-body", "t-bad").AddTo(_letterBody);
                if (fired == ClientState.I.MySlot) Hand(Loc.T("letter.its_you"), "t-bad").AddTo(_letterBody);
            }
            else Memo(Loc.T("letter.nobody"), "letter-body").AddTo(_letterBody);
            Box("spacer").AddTo(_letterBody);
            Hand(Loc.ManagerName(r.ManagerProfileId), "letter-sign").AddTo(_letterBody);
            Memo(Loc.ManagerTitle(r.ManagerProfileId), "t-small", "t-muted").AddTo(_letterBody);

            // stamp
            _stamp.Clear();
            _stamp.Show(true);
            string stampId = fired >= 0 ? "stamps/stamp_kovuldu" : "stamps/stamp_kurtuldu";
            bool art = UiArt.Apply(_stamp, stampId);
            _stamp.EnableInClassList("stamp-fallback", !art);
            if (!art) StampText(Loc.T(fired >= 0 ? "results.stamp_fired" : "results.stamp_saved")).AddTo(_stamp);
            _stamp.AddToClassList("stamp-start");
            _stamp.schedule.Execute(() =>
            {
                _stamp.RemoveFromClassList("stamp-start");
                Sfx.Play("fired");
                CameraRig.I?.Shake(0.25f);
            }).StartingIn(700);
        }

        static string Reason(PlayerView v, MatchResultView r)
        {
            var vd = r.Verdict;
            foreach (int id in vd.KeyEvidence)
                foreach (var e in v.Evidence) if (e.Id == id) return Describe.EvidenceTitle(e);
            foreach (int id in vd.KeyObservations)
                foreach (var o in v.Observations) if (o.Id == id) return Loc.T("letter.obs." + o.Kind);
            return null;
        }

        void BuildPeople(PlayerView v, MatchResultView r)
        {
            var players = new List<PlayerOutcome>(r.Players);
            players.Sort((a, b) => a.MisconductRank.CompareTo(b.MisconductRank));
            float maxMis = 1f;
            foreach (var p in players) if (p.TrueMisconduct > maxMis) maxMis = p.TrueMisconduct;
            bool week = r.Mode == GameMode.Week;
            foreach (var p in players)
            {
                var row = Box("outcome", p.Won ? "outcome-win" : "outcome-lose").AddTo(_content);
                Badge(p.Player, "badge-l").AddTo(row);
                var col = Box("col", "grow").AddTo(row);
                var nameRow = Box("row").AddTo(col);
                Title(p.Name, "outcome-name").AddTo(nameRow);
                Tag(Loc.T(p.Role == Role.Informant ? "role.informant" : "role.employee"), p.Role == Role.Informant ? "tag-teal" : "tag-manila").AddTo(nameRow);
                if (p.Fired) Tag(Loc.T("results.fired_tag"), "tag-red").AddTo(nameRow);
                if (r.Favourites.Contains(p.Player)) Tag(Loc.T("results.favourite_tag"), "tag-sticky").AddTo(nameRow);
                string obj = p.Role == Role.Informant ? Loc.ObjectiveName(ObjectiveType.InformantGoal) : Loc.ObjectiveName(p.ObjectiveType) + (p.ObjectiveTarget >= 0 ? " (" + Loc.PlayerName(v, p.ObjectiveTarget) + ")" : "");
                Text(obj + " — " + Loc.T(p.ObjectiveCompleted ? "results.obj_done" : "results.obj_not_done") + " · " + Loc.T(p.ExplanationKey), "outcome-line").AddTo(col);
                Text(Loc.F("results.stats", p.FinalPerformance.ToString("0.#"), p.TasksCompleted, p.SabotageCount, p.TrueMisconduct.ToString("0.#"))
                     + (p.FalseAccusationPenalty > 0 ? " · " + Loc.F("results.false_accusation", p.FalseAccusationPenalty.ToString("0.#")) : "")
                     + (p.Favor > 0 ? " · " + Loc.F("results.favor", p.Favor.ToString("0.#")) : ""), "outcome-line", "t-muted").AddTo(col);
                var mis = Box("mis-bar").AddTo(col);
                mis.style.width = Length.Percent(Mathf.Clamp01(p.TrueMisconduct / maxMis) * 70f + 1f);
                if (p.Cards.Count > 0)
                {
                    var cards = Box("outcome-cards").AddTo(col);
                    Text(Loc.T("results.cards"), "t-tiny", "t-muted").AddTo(cards);
                    foreach (var c in p.Cards) Tag(Loc.CardName(c), "tag-navy").AddTo(cards);
                }
                if (p.Titles.Count > 0)
                {
                    var titles = Box("outcome-cards").AddTo(col);
                    foreach (var t in p.Titles) Tag(Describe.Title(t), "tag-sticky").AddTo(titles);
                }
                var right = Box("col", "center").AddTo(row);
                if (p.Fired)
                {
                    var s = Box("outcome-stamp").AddTo(right);
                    if (!UiArt.Apply(s, "stamps/stamp_kovuldu")) Bold(Loc.T("results.fired_tag"), "t-bad").AddTo(right);
                }
                else Bold(Loc.T(p.Won ? "results.win" : "results.lose"), p.Won ? "t-good" : "t-bad", "h3").AddTo(right);
                if (week) Title((p.CareerDelta >= 0 ? "+" : "") + p.CareerDelta, "career-delta", p.CareerDelta >= 0 ? "t-good" : "t-bad").AddTo(right);
            }
        }

        void BuildTruth(PlayerView v, MatchResultView r)
        {
            Section(_content, Loc.T("results.truth"), "warning");
            if (r.Incidents.Count == 0) Empty(_content, Loc.T("results.no_incidents"));
            foreach (var i in r.Incidents)
            {
                var row = Box("list-row").AddTo(_content);
                Icon(UiArt.SabotageIcon(i.Type), "icon-m").AddTo(row);
                Badge(i.Actor, "badge-xs").AddTo(row);
                Text(Loc.Time(i.Time, v) + "  " + Describe.Incident(i, v), "list-row-text").AddTo(row);
            }
            Section(_content, Loc.T("results.recap"), "notes");
            foreach (var b in r.Recap) Text("• " + Describe.Recap(b, v), "recap-line").AddTo(_content);
        }

        void BuildCalls(PlayerView v, MatchResultView r)
        {
            Section(_content, Loc.T("results.calls"), "call");
            if (r.Calls.Count == 0) Empty(_content, Loc.T("results.no_calls"));
            foreach (var c in r.Calls)
            {
                var row = Box("list-row").AddTo(_content);
                Icon(c.Tip ? "ringing" : c.Answered ? "call" : "hang_up", "icon-s").AddTo(row);
                Badge(c.Caller, "badge-xs").AddTo(row);
                Text(Describe.Call(c, v), "list-row-text").AddTo(row);
            }
        }

        void BuildWeek(PlayerView v, MatchResultView r)
        {
            var rows = new List<WeekBoard.Standing>();
            foreach (var p in r.Players) rows.Add(new WeekBoard.Standing { Player = p.Player, Name = p.Name, Points = p.CareerTotal, Delta = p.CareerDelta });
            WeekBoard.Build(_content, r.WeekDay, r.WeekFinished, rows, true);
        }

        void ShowEmployeeOfMonth(PlayerView v, MatchResultView r)
        {
            _eotmShownFor = v.MatchId;
            _eotmBody.Clear();
            Title(Loc.T("results.eotm_title"), "h1").AddTo(_eotmBody);
            var names = Box("eotm-names").AddTo(_eotmBody);
            foreach (int id in r.EmployeeOfTheMonth)
            {
                var p = Box("eotm-person").AddTo(names);
                Badge(id, "badge-xxl").AddTo(p);
                Title(Loc.PlayerName(v, id), "h2").AddTo(p);
                foreach (var o in r.Players) if (o.Player == id) Bold(Loc.F("results.eotm_points", o.CareerTotal), "t-accent").AddTo(p);
            }
            if (r.EmployeeOfTheMonth.Count == 0) Empty(_eotmBody, Loc.T("results.eotm_none"));
            Text(Loc.T("results.eotm_note"), "t-muted").AddTo(_eotmBody);
            _eotm.Show(true);
            Pop(_eotm, "screen-enter");
            Sfx.Play("cheer");
        }

        void UpdateRematch(PlayerView v)
        {
            int votes = 0;
            foreach (var l in v.Log) if (l.Kind == EventType.RematchVote) votes++;
            int key = votes * 4 + (v.Me.RematchVoted ? 1 : 0) + (v.Phase == MatchPhase.Results ? 2 : 0) + v.MatchId * 1000;
            if (key == _rematchKey) return;
            _rematchKey = key;
            bool host = ConnectionManager.I != null && ConnectionManager.I.HostController != null;
            bool results = v.Phase == MatchPhase.Results && v.Result != null;
            var r = v.Result;
            string label;
            if (r != null && r.Mode == GameMode.Week) label = r.WeekFinished ? Loc.T("results.new_week") : Loc.F("results.next_day", Loc.DayName(Mathf.Min(r.WeekDay + 1, 4)));
            else label = Loc.T("results.rematch");
            _rematch.text = v.Me.RematchVoted ? Loc.T("results.rematch_voted") : label;
            _rematch.SetEnabled(results && !v.Me.RematchVoted);
            _rematch.Show(results);
            int connected = 0;
            foreach (var p in v.Players) if (p.Connected) connected++;
            _votes.text = results ? Loc.F("results.rematch_votes", votes, connected) : "";
            _force.Show(host && results);
            _toLobby.Show(host);
            _leave.Show(!host && v.Phase == MatchPhase.Abandoned);
            _save.Show(v.Result != null && !_saving);
        }

        // ------------------------------------------------------------------ save letter

        void SaveLetter()
        {
            if (_saving || UIRoot.I == null) return;
            _saving = true;
            _letterActions.visible = false;
            var v = ClientState.I.View;
            string stem = "kovuldu_" + (v != null && v.Result != null ? Safe(Loc.PlayerName(v, v.Result.FiredPlayer)) : "mektup");
            UIRoot.I.StartCoroutine(ScreenshotSaver.Capture(_letter, stem, path =>
            {
                _saving = false;
                _letterActions.visible = true;
                if (path != null) Hud.Toast(Loc.F("results.saved", path), Hud.ToastKind.Good, 6f, "check");
                else Hud.Toast(Loc.T("results.save_failed"), Hud.ToastKind.Warning, 4f);
            }));
        }

        static string Safe(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }

    /// <summary>Crops a UI element out of the final frame and writes a PNG to Pictures/BirimizKovulacak.</summary>
    public static class ScreenshotSaver
    {
        public static string Directory
        {
            get
            {
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(pics)) pics = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? Application.persistentDataPath, "Pictures");
                return Path.Combine(pics, "BirimizKovulacak");
            }
        }

        public static IEnumerator Capture(VisualElement target, string fileStem, Action<string> done)
        {
            yield return null; // let hidden buttons disappear
            yield return new WaitForEndOfFrame();
            Texture2D shot = null, crop = null;
            string path = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                Rect wb = target.worldBound;
                float panelWidth = target.panel != null ? target.panel.visualTree.worldBound.width : UnityEngine.Screen.width;
                float scale = panelWidth > 0 ? shot.width / panelWidth : 1f;
                int x = Mathf.Clamp(Mathf.RoundToInt(wb.x * scale), 0, shot.width - 1);
                int w = Mathf.Clamp(Mathf.RoundToInt(wb.width * scale), 1, shot.width - x);
                int h = Mathf.Clamp(Mathf.RoundToInt(wb.height * scale), 1, shot.height);
                int y = Mathf.Clamp(shot.height - Mathf.RoundToInt(wb.yMax * scale), 0, shot.height - h);
                crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                crop.SetPixels(shot.GetPixels(x, y, w, h));
                crop.Apply();
                byte[] png = crop.EncodeToPNG();
                string dir = Directory;
                System.IO.Directory.CreateDirectory(dir);
                path = Path.Combine(dir, fileStem + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                File.WriteAllBytes(path, png);
                MatchLog.Write("saved letter " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] Could not save screenshot: " + e.Message);
                path = null;
            }
            finally
            {
                if (shot != null) UnityEngine.Object.Destroy(shot);
                if (crop != null) UnityEngine.Object.Destroy(crop);
            }
            done?.Invoke(path);
        }
    }

    /// <summary>Five-day week board + career standings (lobby and results).</summary>
    public static class WeekBoard
    {
        public struct Standing { public int Player; public string Name; public int Points; public int Delta; }

        public static void Build(VisualElement parent, int day, bool finished, List<Standing> standings, bool showDelta)
        {
            var board = Box("week-board").AddTo(parent);
            Section(board, Loc.T("week.title"), "week_calendar");
            var cols = Box("week-cols").AddTo(board);
            var cfg = GameInfo.Config;
            int days = cfg != null && cfg.WeekDays.Count > 0 ? cfg.WeekDays.Count : 5;
            for (int i = 0; i < days; i++)
            {
                var c = Box("week-col").AddTo(cols);
                if (i == day && !finished) c.AddToClassList("week-col-today");
                if (i < day || finished) c.AddToClassList("week-col-done");
                Title(Loc.DayShort(i), "week-day").AddTo(c);
                string profile = cfg != null && i < cfg.WeekDays.Count ? cfg.WeekDays[i].ProfileId : "";
                if (!string.IsNullOrEmpty(profile)) ManagerPortrait(profile, false, "badge-s").AddTo(c);
                Text(string.IsNullOrEmpty(profile) ? Loc.T("week.random_manager") : Loc.ManagerName(profile), "week-mgr").AddTo(c);
                if (cfg != null && i < cfg.WeekDays.Count && cfg.WeekDays[i].CareerMultiplier > 1.01f) Tag(Loc.F("week.multiplier", cfg.WeekDays[i].CareerMultiplier.ToString("0.#")), "tag-red").AddTo(c);
                Text(Loc.T("week.unlock." + i), "week-mgr").AddTo(c);
            }
            if (standings == null || standings.Count == 0) return;
            standings.Sort((a, b) => b.Points.CompareTo(a.Points));
            Section(board, Loc.T("week.standings"), "crown");
            int top = standings[0].Points;
            foreach (var s in standings)
            {
                var row = Box("week-table-row").AddTo(board);
                if (s.Points == top && top > 0) row.AddToClassList("week-leader");
                Badge(s.Player, "badge-s").AddTo(row);
                Bold(s.Name, "grow").AddTo(row);
                if (showDelta) Bold((s.Delta >= 0 ? "+" : "") + s.Delta, s.Delta >= 0 ? "t-good" : "t-bad").AddTo(row);
                Title(Loc.F("week.points", s.Points), "week-pts").AddTo(row);
            }
        }
    }
}
