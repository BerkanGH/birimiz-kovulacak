using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    /// <summary>Regressions for the lead's spec-conformance review of the v4 engine (2026-11-01).</summary>
    public sealed class LineReviewFixTests
    {
        [Test]
        public void ReportReviewDecisionWaitingForAMissingReportIsNotAMissedTask()
        {
            var m = LineTest.Task(TaskKind.ReportReview);
            m.Until(LinePhase.RoundPlay);
            int q = 1;
            int p = m.View(q).Task.Facts.Single(f => f.Key == "review_figure").P;
            foreach (var v in m.Views()) if (v.Me != p) m.Submit(v.Me, 0);                        // everyone but P files a report
            foreach (var v in m.Views())
            {
                int preparer = v.Task.Facts.Single(f => f.Key == "review_figure").P;
                if (preparer != p) m.Submit(v.Me, -1, 0, fields: TaskField.B);                     // decisions that are possible
            }
            var reveal = m.Reveal();
            CollectionAssert.Contains(reveal.Missed, p, "the preparer who filed nothing missed the task");
            CollectionAssert.DoesNotContain(reveal.Missed, q, "the approver could not decide on a report that never came");
        }

        [Test]
        public void ShiftOvertimeIsPaidOnlyToTheSoleVolunteerOfAShift()
        {
            bool doubled = false, sole = false;
            for (int seed = 1; seed <= 60 && !(doubled && sole); seed++)
            {
                var m = LineTest.Task(TaskKind.ShiftCover, 4, seed: seed);
                m.Until(LinePhase.RoundPlay);
                var views = m.Views();
                var onZero = views.Where(v => v.Task.Facts.Any(f => f.Key == "available" && f.N1 == 0)).Select(v => v.Me).ToList();
                if (onZero.Count < 2) continue;
                int start = views[0].Mine.Money;
                var volunteers = doubled ? onZero.Take(1).ToList() : onZero.Take(2).ToList();
                foreach (var v in views) m.Submit(v.Me, volunteers.Contains(v.Me) ? 0 : 9);
                m.Reveal();
                foreach (int id in volunteers)
                {
                    int cost = m.View(id).Task.Facts.Single(f => f.Key == "cost").N1;
                    int expected = volunteers.Count == 1 ? start + System.Math.Max(0, m.Config.TaskRules.ShiftOvertime - cost) : start;
                    Assert.AreEqual(expected, m.View(id).Mine.Money, "seed " + seed + " volunteers " + volunteers.Count);
                }
                if (volunteers.Count == 2) doubled = true; else sole = true;
            }
            Assert.IsTrue(doubled && sole, "fixtures for both cases");
        }

        [Test]
        public void ShiftFactsDoNotAlwaysListTheCoveringShiftFirst()
        {
            bool swapped = false;
            for (int seed = 1; seed <= 300 && !swapped; seed++)
            {
                var m = LineTest.Task(TaskKind.ShiftCover, 4, seed: seed);
                m.Until(LinePhase.RoundPlay);
                var firsts = m.Views().Select(v => v.Task.Facts.First(f => f.Key == "available").N1).Distinct().Count();
                if (firsts < (4 + 1) / 2) swapped = true; // first-listed shifts alone no longer cover every shift
            }
            Assert.IsTrue(swapped);
        }

        [Test]
        public void ArchiveHrFileSubjectIsNotAlwaysTheLowestSeat()
        {
            var subjects = new HashSet<int>();
            for (int seed = 1; seed <= 30; seed++)
            {
                var m = LineTest.Task(TaskKind.ArchiveVisitor, 5, seed: seed);
                m.Until(LinePhase.RoundPlay);
                var visitor = m.Views().Single(v => v.Task.Role == 1);
                int subject = visitor.Task.Facts.Single(f => f.Key == "hr_file").P;
                Assert.AreNotEqual(visitor.Me, subject);
                subjects.Add(subject);
            }
            Assert.Greater(subjects.Count, 1);
        }

        [Test]
        public void ASubmissionMadeBeforeADisconnectStands()
        {
            var m = LineTest.Task(TaskKind.BonusPool);
            m.Until(LinePhase.RoundPlay);
            m.Submit(0, 1);                                  // take everything
            foreach (var v in m.Views()) if (v.Me != 0) m.Submit(v.Me, 0);
            m.Engine.SetConnected(0, false, m.Now);
            var reveal = m.Reveal();
            Assert.AreEqual(1, reveal.PublicFacts.Single(f => f.Key == "takes").N1);
            CollectionAssert.DoesNotContain(reveal.Missed, 0);
        }
    }
}
