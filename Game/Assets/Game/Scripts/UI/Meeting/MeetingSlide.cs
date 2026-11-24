using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3b: what is left on screen of the old public card (step 3a). The meeting's public information is on the
    /// projector now (<see cref="MeetingProjector"/>); this keeps only what needs the mouse: the results bar at the bottom
    /// (my place, play again / back to the lobby / leave, the full reveal in the folder, projector / table) and the card of
    /// an abandoned match. The detail tabs of the results moved into the folder's Dosya tab (<see cref="MeetingReveal"/>).
    /// </summary>
    public sealed class MeetingSlide
    {
        readonly VisualElement _bar, _award;
        readonly Label _line, _awardName, _awardText;
        readonly PlaySection _buttons;
        readonly Func<string, string, float, bool> _toast;
        readonly Action _openReveal, _toggleProjector;
        bool _dirty = true, _lastProjector;

        /// <summary>UI mocks: open the folder's reveal on this tab (summary, robbed, ballots, firings, transfers, choices, incidents).</summary>
        public static string MockResultsTab;

        public MeetingSlide(VisualElement parent, Func<string, string, float, bool> toast, Action openReveal, Action toggleProjector)
        {
            _toast = toast;
            _openReveal = openReveal;
            _toggleProjector = toggleProjector;
            _bar = Box("mt-results").AddTo(parent);
            // my place, and next to it (v5.2) my award of the match (LineStory) when I got one: one row, so the bar never
            // grows over the projector's footer in the close-up
            var top = Box("mt-results-top").NoPick().AddTo(_bar);
            _line = Bold("", "mt-results-line").NoPick().AddTo(top);
            _award = Box("mt-award").NoPick().AddTo(top);
            Bold(T("results.my_award"), "mt-award-kicker").NoPick().AddTo(_award);
            _awardName = UiKit.StampText("", "mt-award-name").NoPick().AddTo(_award);
            _awardText = Text("", "mt-award-text").NoPick().AddTo(_award);
            _award.Show(false);
            _buttons = new PlaySection(_bar, "mt-results-buttons");
            _bar.Show(false);
        }

        public VisualElement Root => _bar;
        /// <summary>Results and an abandoned match need the cursor (buttons).</summary>
        public bool WantsCursor { get; private set; }

        public void MarkDirty() { _dirty = true; }
        public void ResetWeek() { _dirty = true; }

        public void Tick(LineClient lc, LineView v, double now, bool dirty, bool projector)
        {
            bool results = v.Phase == LinePhase.Results, abandoned = v.Phase == LinePhase.Abandoned;
            WantsCursor = results || abandoned;
            bool show = results || abandoned;
            // (a fade: the bar is centred with a translate that a slide would override)
            if (show != _bar.IsShown()) { _bar.Show(show); if (show) UiMotion.FadeIn(_bar, 0.3f); _dirty = true; }
            if (projector != _lastProjector) { _lastProjector = projector; _dirty = true; }
            if (!show) return;
            _bar.EnableInClassList("mt-results-abandoned", abandoned);
            _line.SetText(abandoned ? T("abandoned.text") : MyLine(lc, v));
            // my title appears with the projector's awards slide (not before the ranking has been read out)
            var story = results ? StoryText.Of(v) : null;
            var mine = story != null ? story.AwardOf(lc.MySlot) : null;
            bool showAward = mine != null && now - v.PhaseStartTime >= LineStory.AwardsAt(v.PhaseEndTime - v.PhaseStartTime) + 0.4;
            if (showAward != _award.IsShown())
            {
                _award.Show(showAward);
                if (showAward) { UiMotion.PopIn(_award, 0.3f); Sfx.Play("stamp", 0.5f); }
            }
            if (showAward)
            {
                _awardName.SetText(Loc.Upper(StoryText.AwardName(mine.Kind)));
                _awardText.SetText(StoryText.AwardText(mine));
            }
            if (dirty || _dirty)
            {
                _dirty = false;
                _buttons.Update(p => Buttons(p, v, abandoned, projector));
            }
        }

        static string MyLine(LineClient lc, LineView v)
        {
            var r = v.Results;
            if (r == null) return T("results.waiting");
            int me = lc.MySlot;
            var mine = r.Players.Find(x => x.Player == me);
            if (mine == null) return F(r.Winners.Count > 1 ? "results.winner_many" : "results.winner_one", PlayText.Names(r.Winners), 0);
            int rank = 1;
            foreach (var o in r.Players) if (!o.Fired && !mine.Fired && o.Prim > mine.Prim) rank++;
            return mine.Fired ? T("results.me_fired") : mine.Winner ? F("results.me_won", mine.Prim) : F("results.me_rank", rank, mine.Prim);
        }

        void Buttons(VisualElement p, LineView v, bool abandoned, bool projector)
        {
            var row = Wrap(p);
            if (abandoned)
            {
                Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(row);
                return;
            }
            if (HostLineController.Current != null) Btn(T("results.continue"), () => HostLineController.Current?.ContinueAfterResults(), "pl-primary", "pl-big").AddTo(row);
            Btn(T("results.again"), () => { LineClient.I?.RequestRematch(true); TelemetryHooks.RematchRequested(); _toast?.Invoke(T("results.again_sent"), null, 5f); }, "pl-big").AddTo(row);
            Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(row);
            Btn(F("meeting.results.reveal", OfficeText.Key(GameInput.Computer)), () => _openReveal?.Invoke(), "pl-big", "mt-results-reveal").AddTo(row);
            Btn(F(projector ? "meeting.results.table" : "meeting.results.projector", OfficeText.Key(GameInput.Phone)), () => _toggleProjector?.Invoke(), "pl-big").AddTo(row);
        }
    }
}
