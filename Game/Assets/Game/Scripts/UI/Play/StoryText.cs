using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5.2 texts of the prim declarations ("prim beyanı") and of the results' story (<see cref="LineStory"/>: awards and
    /// headlines), from typed data ("v5.declare.*", "v5.award.*", "v5.headline.*"). Names stay in "Label: Name" positions
    /// or lead a sentence (no Turkish suffixes on names). The story of a result is built once per result (every client
    /// builds the same one from the same full disclosure).
    /// </summary>
    public static class StoryText
    {
        static string T(string key) { return PlayText.T(key); }
        static string F(string key, params object[] a) { return PlayText.F(key, a); }
        static string Name(int id) { return PlayText.Name(id); }

        static LineStory _story;
        static ulong _hash;
        static int _week = int.MinValue;
        static string _match;

        /// <summary>The story of the results in this view (null before Results).</summary>
        public static LineStory Of(LineView v)
        {
            var r = v != null ? v.Results : null;
            if (r == null) return null;
            if (_story == null || _hash != r.ResultHash || _week != r.WeekId || _match != r.MatchId)
            {
                var lc = LineClient.I;
                _story = LineStory.Build(r, lc != null ? lc.Config : LineConfig.CreateDefault());
                _hash = r.ResultHash;
                _week = r.WeekId;
                _match = r.MatchId;
            }
            return _story;
        }

        // ------------------------------------------------------------------ declarations

        /// <summary>The declared amount as text: "7 prim" or "beyan yok".</summary>
        public static string Amount(int declared) { return declared >= 0 ? F("declare.amount", declared) : T("declare.none"); }

        /// <summary>About one colleague: "Beyanı: 7 prim" / "Beyan yok".</summary>
        public static string Claim(int declared) { return declared >= 0 ? F("declare.claim", declared) : T("declare.claim_none"); }

        /// <summary>"Sarı: 7 prim" / "Sarı: beyan yok".</summary>
        public static string Line(LineDeclarationView d) { return F("declare.line", Name(d.Player), Amount(d.Amount)); }

        /// <summary>A candidate's public declaration, or null before the reveal / when they are not a candidate.</summary>
        public static LineDeclarationView Of(LineView v, int player)
        {
            if (v == null || v.Final == null || !v.Final.DeclarationsRevealed) return null;
            foreach (var d in v.Final.Declarations) if (d.Player == player) return d;
            return null;
        }

        /// <summary>The stamp of a desk opening: true = "DOĞRU" (the declaration was their prim when the declarations were
        /// revealed), false = "YALAN".</summary>
        public static bool Truthful(LineFiringView f) { return LineStory.Truthful(f); }

        /// <summary>"Beyanlar açılırken: N prim": only when the desk holds something else than it did at the reveal
        /// (prim arrived afterwards), else null.</summary>
        public static string Then(LineFiringView f)
        {
            return f != null && f.Declared >= 0 && f.DeclaredActual >= 0 && f.DeclaredActual != f.Desk ? F("declare.then", f.DeclaredActual) : null;
        }

        public static string Stamp(LineFiringView f) { return T(Truthful(f) ? "declare.stamp.true" : "declare.stamp.lie"); }

        // ------------------------------------------------------------------ awards

        public static string AwardName(AwardKind k) { return T("award." + LineAwards.Id(k) + ".name"); }

        /// <summary>One sentence about why (the owner is named next to it, never inside it).</summary>
        public static string AwardText(LineAward a)
        {
            string id = "award." + LineAwards.Id(a.Kind);
            switch (a.Kind)
            {
                case AwardKind.WrongMan: return F(id + ".text", a.N1);
                case AwardKind.Liar: return F(id + ".text", a.N1, a.N2, a.N3);
                case AwardKind.Sellout: return F(id + ".text", Name(a.Other), a.N1, T("award.sellout.how." + System.Math.Max(0, System.Math.Min(3, a.N2))));
                case AwardKind.Loyal: return F(id + ".text", Name(a.Other), a.N1);
                case AwardKind.Robber:
                    if (a.Other < 0) return F(id + ".text_none", a.N1);
                    return a.N2 >= 2 ? F(id + ".text_many", Name(a.Other), a.N1, a.N2 - 1) : F(id + ".text", Name(a.Other), a.N1);
                case AwardKind.Scapegoat: return F(id + ".text", a.N1, a.N2);
                default: return F(id + ".text", a.N1);
            }
        }

        // ------------------------------------------------------------------ headlines

        public static string Headline(LineHeadline h, LineWeekResult r)
        {
            switch (h.Kind)
            {
                case HeadlineKind.DealBroken:
                    return F("headline.deal." + System.Math.Max(0, System.Math.Min(3, h.N2)), Name(h.Player), Name(h.Other), h.N1);
                case HeadlineKind.BonusBetrayal:
                    return h.Other2 >= 0 ? F("headline.bonus_two", Name(h.Player), Name(h.Other), Name(h.Other2), h.N1) : F("headline.bonus", Name(h.Player), Name(h.Other), h.N1);
                case HeadlineKind.Firing:
                    if (h.N2 >= 0)
                    {
                        // (judged against the prim at the reveal; a true claim on a desk that grew afterwards says so)
                        int real = h.N4 >= 0 ? h.N4 : h.N1;
                        return F(h.N2 != real ? "headline.firing_lie" : real != h.N1 ? "headline.firing_true_later" : "headline.firing_true", Name(h.Player), h.N1, h.N2);
                    }
                    // (no declaration; without any declaration in the match the feature was off: say nothing about it)
                    return F(r != null && r.Declarations != null && r.Declarations.Count > 0 ? "headline.firing_none" : "headline.firing", Name(h.Player), h.N1);
                case HeadlineKind.Crisis:
                    return h.Player >= 0 ? F("headline.crisis", Name(h.Player), h.N1, h.N3) : F("headline.crisis_tie", h.N2, h.N1);
                case HeadlineKind.BigTransfer:
                    return F("headline.transfer", Name(h.Player), Name(h.Other), h.N1);
                default:
                    return "";
            }
        }

        public static List<string> Headlines(LineView v)
        {
            var list = new List<string>();
            var story = Of(v);
            if (story == null) return list;
            foreach (var h in story.Headlines) list.Add(Headline(h, v.Results));
            return list;
        }
    }
}
