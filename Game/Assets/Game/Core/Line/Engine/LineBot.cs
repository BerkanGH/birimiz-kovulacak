using System;

namespace BirimizKovulacak.Core
{
    /// <summary>A deterministic, deliberately imperfect colleague. Its only game input is its own view.</summary>
    public sealed class LineBot
    {
        private readonly int player;
        private uint random;
        private uint request;
        private int week = int.MinValue, day = -1, round = -1;
        private double roundStart = double.NaN, submitAt, callAt, mailAt, trickAt, tipAt;
        private bool wantsTrick, wantsTip, wantsMail;
        private int connectedCall = -1;
        private double hangAt;

        public LineBot(int player, int seed)
        {
            this.player = player;
            random = unchecked((uint)seed ^ ((uint)player + 1) * 0x9e3779b9u);
            if (random == 0) random = 0xa341316cu;
        }

        public LineAction Next(LineView view, LineConfig config, double now)
        {
            if (view == null || config == null || view.Me != player || view.Mine == null ||
                double.IsNaN(now) || double.IsInfinity(now) || now >= view.PhaseEndTime) return null;
            LinePlayerView self = Find(view, player);
            if (self == null || !self.Connected) return null;
            double scale = Math.Max(0.00001, config.Durations.RoundPlay / 150.0);
            if (week != view.WeekId || day != view.Day)
            {
                week = view.WeekId; day = view.Day; round = -1; roundStart = double.NaN;
                wantsTrick = Chance(0.82); wantsTip = Chance(0.30);
                trickAt = now + Between(18, 45) * scale;
                tipAt = now + Between(50, 100) * scale;
                connectedCall = -1;
            }
            bool office = view.Phase >= LinePhase.Morning && view.Phase <= LinePhase.RoundReveal;
            var mine = view.Mine;
            var phone = mine.Phone;
            if (office && phone != null && mine.PhoneDeadUntil <= now)
            {
                if (phone.Status == CallStatus.Ringing)
                    return Action(view, Chance(0.70) ? LineVerb.Answer : LineVerb.Decline);
                if (phone.Status == CallStatus.Connected)
                {
                    if (connectedCall != phone.CallId)
                    {
                        connectedCall = phone.CallId;
                        hangAt = phone.Since + Between(8, 25) * scale;
                    }
                    if (now >= hangAt) return Action(view, LineVerb.HangUp);
                }
                else connectedCall = -1;
            }
            if (view.Phase == LinePhase.MeetingVote || view.Phase == LinePhase.FinalVote)
            {
                if (!self.Voted && view.Meeting != null)
                    return Action(view, LineVerb.Vote, VoteTarget(view));
                return null;
            }
            if (view.Phase == LinePhase.WeekResults && view.Result != null && view.Result.Fired == player &&
                string.IsNullOrEmpty(view.Result.NextManagerPick))
            {
                int pick = -1, count = 0;
                for (int i = 0; i < LineManagerIds.All.Length; i++)
                    if (LineManagerIds.All[i] != view.ManagerProfileId && NextInt(++count) == 0) pick = i;
                return pick < 0 ? null : Action(view, LineVerb.PickNextManager, a: pick);
            }
            if (!office) return null;

            if (view.Phase == LinePhase.RoundPlay && view.Round != null)
            {
                var current = view.Round;
                if (round != view.RoundIndex || roundStart != view.PhaseStartTime)
                {
                    round = view.RoundIndex; roundStart = view.PhaseStartTime;
                    submitAt = view.PhaseStartTime + Between(10, 60) * scale;
                    callAt = view.PhaseStartTime + Between(1, 12) * scale;
                    mailAt = view.PhaseStartTime + Between(20, 65) * scale;
                    wantsMail = Chance(0.35);
                }
                if (mine.ComputerBlockedUntil <= now &&
                    (now >= submitAt || view.PhaseEndTime - now <= 2 * scale))
                {
                    if (current.Type == RoundType.OrtakSifre)
                    {
                        if (!current.Submitted && current.DigitPosition >= 0 && current.Digit >= 0 &&
                            current.NeighborPosition >= 0 && current.NeighborDigit >= 0 &&
                            current.CodeLength > 0 && current.CodeLength <= 6)
                        {
                            int code = 0;
                            for (int position = 0; position < current.CodeLength; position++)
                                code = code * 10 + (position == current.DigitPosition ? current.Digit :
                                    position == current.NeighborPosition ? current.NeighborDigit :
                                    position == 0 ? 1 + NextInt(9) : NextInt(10));
                            return Action(view, LineVerb.SubmitChoice, a: code);
                        }
                    }
                    else if (!current.Submitted)
                    {
                        int target;
                        switch (current.Type)
                        {
                            case RoundType.PrimHavuzu:
                                return Action(view, LineVerb.SubmitChoice, a: Chance(0.60) ? 0 : 1);
                            case RoundType.FarkliEposta:
                                target = Pick(view);
                                int guess = current.IsOdd && current.GuessOptions.Count > 0
                                    ? current.GuessOptions[NextInt(current.GuessOptions.Count)] : -1;
                                if (target >= 0) return Action(view, LineVerb.SubmitChoice, target, guess);
                                break;
                            case RoundType.KayipRapor:
                                target = current.IsCulprit && current.NoAlibi.Count > 0
                                    ? current.NoAlibi[NextInt(current.NoAlibi.Count)]
                                    : Pick(view, exclude: current.ClueInnocent);
                                if (target >= 0) return Action(view, LineVerb.SubmitChoice, target);
                                break;
                            case RoundType.IhbarKutusu:
                                target = Chance(0.65) && ValidOther(view, mine.AgendaTarget, false)
                                    ? mine.AgendaTarget : Pick(view);
                                return Action(view, LineVerb.SubmitChoice, target,
                                    config.Rounds.ComplaintReasons > 0 ? NextInt(config.Rounds.ComplaintReasons) : 0);
                            case RoundType.TerfiListesi:
                                target = Pick(view, exclude: Chance(0.80) ? mine.AgendaTarget : -1);
                                return Action(view, LineVerb.SubmitChoice, target);
                        }
                    }
                }
                if (phone != null && phone.Status == CallStatus.Idle && mine.PhoneDeadUntil <= now && now >= callAt)
                {
                    callAt = now + Between(12, 30) * scale;
                    int target = current.Type == RoundType.PrimHavuzu && current.Partners.Count > 0
                        ? current.Partners[NextInt(current.Partners.Count)] : -1;
                    if (!ValidOther(view, target, true)) target = Pick(view, connectedOnly: true);
                    // A colleague's phone state is private. Busy can only be discovered by calling.
                    if (target >= 0) return Action(view, LineVerb.Call, target);
                }
                if (wantsMail && now >= mailAt && mine.ComputerBlockedUntil <= now &&
                    mine.MailsSentToday < config.MailsPerDay && config.MailTemplates > 0)
                {
                    wantsMail = false;
                    int target = Pick(view, connectedOnly: true);
                    if (target >= 0) return Action(view, LineVerb.SendMail, target, NextInt(config.MailTemplates), mine.AgendaTarget);
                }
            }
            if (view.Features != null && view.Features.TricksUnlocked && wantsTrick && !mine.TrickUsedToday &&
                mine.TricksLeftThisWeek > 0 && mine.Trick != TrickType.None && mine.Trick != TrickType.KayitSil &&
                now >= trickAt && view.Phase != LinePhase.Morning)
            {
                int target = Pick(view, connectedOnly: true);
                if (target >= 0 && (mine.Trick != TrickType.SahteEposta || config.MailTemplates > 0))
                {
                    wantsTrick = false;
                    return Action(view, LineVerb.UseTrick, target,
                        mine.Trick == TrickType.SahteEposta ? NextInt(config.MailTemplates) : -1, mine.AgendaTarget);
                }
            }
            if (view.Features != null && view.Features.TipLineUnlocked && wantsTip && !mine.TipUsedToday &&
                mine.TipsLeftThisWeek > 0 && mine.PhoneDeadUntil <= now && now >= tipAt)
            {
                int target = Pick(view, connectedOnly: true);
                if (target >= 0) { wantsTip = false; return Action(view, LineVerb.TipOff, target); }
            }
            return null;
        }

        private LineAction Action(LineView view, LineVerb verb, int target = -1, int a = -1, int b = -1)
        {
            return new LineAction(view.WeekId, ++request, verb, target, a, b);
        }

        private int VoteTarget(LineView view)
        {
            if (Chance(0.5) && ValidVote(view, view.Mine.AgendaTarget)) return view.Mine.AgendaTarget;
            int chosen = -1, highest = int.MinValue, ties = 0;
            for (int i = 0; i < view.Players.Count; i++)
            {
                var p = view.Players[i];
                if (!ValidVote(view, p.Id)) continue;
                if (p.Blame > highest) { highest = p.Blame; chosen = p.Id; ties = 1; }
                else if (p.Blame == highest && NextInt(++ties) == 0) chosen = p.Id;
            }
            return chosen;
        }

        private bool ValidVote(LineView view, int id)
        {
            if (!ValidOther(view, id, false)) return false;
            if (view.Phase == LinePhase.FinalVote) return view.Meeting.Shortlist.Contains(id);
            return Find(view, id).Ihtars == 0;
        }

        private int Pick(LineView view, bool connectedOnly = false, int exclude = -1)
        {
            int chosen = -1, count = 0;
            for (int i = 0; i < view.Players.Count; i++)
            {
                var p = view.Players[i];
                if (p.Id != player && p.Id != exclude && (!connectedOnly || p.Connected) && NextInt(++count) == 0)
                    chosen = p.Id;
            }
            return chosen;
        }

        private bool ValidOther(LineView view, int id, bool connectedOnly)
        {
            var p = Find(view, id);
            return p != null && id != player && (!connectedOnly || p.Connected);
        }

        private static LinePlayerView Find(LineView view, int id)
        {
            for (int i = 0; i < view.Players.Count; i++) if (view.Players[i].Id == id) return view.Players[i];
            return null;
        }

        private uint NextUInt()
        {
            uint x = random; x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return random = x;
        }
        private int NextInt(int maximum) { return (int)(NextUInt() % (uint)maximum); }
        private bool Chance(double chance) { return NextUInt() / 4294967296.0 < chance; }
        private double Between(double minimum, double maximum) { return minimum + (maximum - minimum) * (NextUInt() / 4294967296.0); }
    }
}
