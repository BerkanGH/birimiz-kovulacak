using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>Turns rules records into one-sentence Turkish descriptions (what was observed, never hidden truth).</summary>
    public static class Describe
    {
        static string P(PlayerView v, int id) { return Loc.PlayerName(v, id); }

        public static string Evidence(EvidenceView e, PlayerView v)
        {
            string t = Loc.Time(e.Time, v);
            switch (e.Kind)
            {
                case EvidenceKind.SuspiciousItemFound:
                    return Loc.F("ev.item_found", t, Loc.Item(e.ItemVariant), Loc.Location(e.LocationId), P(v, e.AttributedTo), e.Source == DiscoverySource.Manager ? Loc.T("ev.by_manager") : P(v, e.SourcePlayer));
                case EvidenceKind.HiddenReportFound:
                    return e.AttributedTo >= 0
                        ? Loc.F("ev.report_found_desk", t, Loc.Location(e.LocationId), P(v, e.AttributedTo), e.Source == DiscoverySource.Manager ? Loc.T("ev.by_manager") : P(v, e.SourcePlayer))
                        : Loc.F("ev.report_found", t, Loc.Location(e.LocationId), e.Source == DiscoverySource.Manager ? Loc.T("ev.by_manager") : P(v, e.SourcePlayer));
                case EvidenceKind.WorkstationLockLog:
                    return Loc.F("ev.lock_log", t, Loc.Desk(e.DeskIndex));
                case EvidenceKind.WitnessStatement:
                    return Loc.F("ev.witness", t, P(v, e.SourcePlayer), P(v, e.SubjectPlayer), Loc.Activity(e.ObservedActivity), Loc.Location(e.LocationId), Held(e.HeldBefore), Held(e.HeldAfter));
                case EvidenceKind.SearchStatement:
                    return Loc.F("ev.search", t, P(v, e.SourcePlayer), Loc.Location(e.LocationId), string.IsNullOrEmpty(e.ItemVariant) ? Loc.T("common.nothing") : Loc.Item(e.ItemVariant));
                case EvidenceKind.ScanStatement:
                    return Loc.F("ev.scan", t, P(v, e.SourcePlayer), string.IsNullOrEmpty(e.ItemVariant) ? Loc.T("common.evidence") : Loc.Item(e.ItemVariant), Loc.T("rel." + e.ScanReliability), Loc.T("signal." + e.ScanSignal));
                default: return e.Kind.ToString();
            }
        }

        public static string Held(string variant) { return string.IsNullOrEmpty(variant) ? Loc.T("common.empty_hands") : Loc.Item(variant); }

        public static string Status(EvidenceView e)
        {
            if (e.Sealed) return Loc.T("ev.status.sealed");
            switch (e.Status)
            {
                case EvidenceStatus.Moved: return Loc.T("ev.status.moved");
                case EvidenceStatus.Disposed: return Loc.T("ev.status.disposed");
                default: return "";
            }
        }

        public static string Observation(ObservationView o, PlayerView v)
        {
            string t = Loc.Time(o.Time, v);
            string who = P(v, o.Subject);
            switch (o.Kind)
            {
                case ObservationKind.MessyDesk: return Loc.F("obs.messy", t, who, MessDetail(o.Detail));
                case ObservationKind.NotAtDesk: return Loc.F("obs.not_at_desk", t, who);
                case ObservationKind.AtWrongDesk: return Loc.F("obs.wrong_desk", t, who, o.DeskIndex >= 0 ? Loc.Desk(o.DeskIndex) : "");
                case ObservationKind.MovingDuringInspection: return Loc.F("obs.moving", t, who);
                case ObservationKind.CarryingSuspicious: return Loc.F("obs.carrying", t, who, Loc.Item(o.Detail));
                case ObservationKind.CaughtInAct: return Loc.F("obs.caught", t, who, o.DeskIndex >= 0 ? Loc.Desk(o.DeskIndex) : "");
                case ObservationKind.BehindOnWork: return Loc.F("obs.behind", t, who);
                case ObservationKind.WorkingHard: return Loc.F("obs.working", t, who);
                case ObservationKind.SuspiciousItemAtDesk: return Loc.F("obs.item", t, Loc.Item(o.Detail), who);
                default: return o.Kind.ToString();
            }
        }

        static string MessDetail(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            var parts = detail.Split(',');
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                string k = p.Trim();
                string count = "";
                int colon = k.IndexOf(':');
                if (colon > 0) { count = k.Substring(colon + 1); k = k.Substring(0, colon); }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Loc.F("mess." + k, count));
            }
            return sb.ToString();
        }

        public static string Note(PrivateNote n, PlayerView v)
        {
            string t = Loc.Time(n.Time, v);
            switch (n.Kind)
            {
                case NoteKind.Witness:
                    return Loc.F("note.witness", t, P(v, n.Subject), Loc.Activity(n.Activity), Loc.Location(n.TargetId), Held(n.HeldBefore), Held(n.HeldAfter));
                case NoteKind.SearchResult:
                {
                    string items = n.Contents.Count == 0 ? Loc.T("common.nothing") : string.Join(", ", n.Contents.ConvertAll(Loc.Item));
                    return Loc.F("note.search", t, Loc.Location(n.TargetId), items);
                }
                case NoteKind.ScanResult:
                {
                    ScanResultView s = null;
                    if (v.Me.Informant != null) foreach (var x in v.Me.Informant.Scans) if (x.Id == n.ScanId) s = x;
                    return s != null ? Scan(s, v) : Loc.F("note.scan_generic", t);
                }
                default: return n.Kind.ToString();
            }
        }

        public static string Scan(ScanResultView s, PlayerView v)
        {
            string what = string.IsNullOrEmpty(s.ItemVariant) ? Loc.T("common.evidence") : Loc.Item(s.ItemVariant);
            string owner = s.ApparentOwner >= 0 ? Loc.F("scan.owner", P(v, s.ApparentOwner)) : "";
            return Loc.F("scan.result", Loc.Time(s.Time, v), what, owner, Loc.T("rel." + s.Reliability), Loc.T("signal." + s.Signal));
        }

        /// <summary>Feed line for an event, or null when the event should not produce a toast.</summary>
        public static string Event(GameEvent e, PlayerView v, out Hud.ToastKind kind)
        {
            kind = Hud.ToastKind.Info;
            switch (e.Type)
            {
                case EventType.TaskCompleted:
                    kind = Hud.ToastKind.Good;
                    return Loc.F("feed.task", P(v, e.Actor), Loc.Task((TaskType)e.IntArg));
                case EventType.QuotaReached: kind = Hud.ToastKind.Good; return Loc.T("feed.quota_reached");
                case EventType.PhoneRinging: return Loc.T("feed.phone");
                case EventType.PhoneMissed: kind = Hud.ToastKind.Warning; return Loc.T("feed.phone_missed");
                case EventType.ReportPrinted: return Loc.F("feed.report_printed", P(v, e.IntArg));
                case EventType.WorkstationLocked: kind = Hud.ToastKind.Warning; return Loc.F("feed.locked", Loc.Location(e.TargetId));
                case EventType.WorkstationUnlocked: return Loc.F("feed.unlocked", Loc.Location(e.TargetId));
                case EventType.EvidenceCreated:
                {
                    kind = Hud.ToastKind.Warning;
                    foreach (var ev in v.Evidence) if (ev.Id == e.IntArg) return Loc.T("feed.evidence") + " " + Evidence(ev, v);
                    return Loc.T("feed.evidence");
                }
                case EventType.EvidenceUpdated:
                {
                    foreach (var ev in v.Evidence)
                        if (ev.Id == e.IntArg && ev.Sealed) return Loc.F("feed.sealed", ev.Id);
                    return null;
                }
                case EventType.NoteAdded: kind = Hud.ToastKind.Private; return Loc.T("feed.note");
                case EventType.ManagerWarning: kind = Hud.ToastKind.Warning; return Loc.T("feed.manager_warning");
                case EventType.ManagerObservation:
                {
                    kind = Hud.ToastKind.Manager;
                    foreach (var o in v.Observations) if (o.Id == e.IntArg) return Loc.T("feed.manager") + " " + Observation(o, v);
                    return null;
                }
                case EventType.AccusationFiled: kind = Hud.ToastKind.Warning; return Loc.F("feed.accused", P(v, e.Actor), P(v, e.IntArg));
                case EventType.BallotCast: return Loc.F("feed.voted", P(v, e.Actor));
                case EventType.PlayerDisconnected: kind = Hud.ToastKind.Warning; return Loc.F("feed.disconnected", P(v, e.Actor));
                case EventType.PlayerReconnected: kind = Hud.ToastKind.Good; return Loc.F("feed.reconnected", P(v, e.Actor));
                case EventType.DeskDirty: return Loc.F("feed.dirty", Loc.Location(e.TargetId));
                case EventType.OrderAdded: return Loc.F("feed.order", Loc.T("order." + (TaskType)e.IntArg));
                case EventType.ItemDropped: return null;
                case EventType.RematchVote: return Loc.F("feed.rematch", P(v, e.Actor));
                case EventType.InteractionCompleted: return null;
                case EventType.InteractionCancelled: kind = Hud.ToastKind.Warning; return Loc.T("feed.cancelled");
                case EventType.ActionRejected: kind = Hud.ToastKind.Warning; return Loc.Failure((FailureCode)e.IntArg);
                default: return null;
            }
        }

        public static string Incident(IncidentReveal i, PlayerView v)
        {
            string target = i.DeskIndex >= 0 ? Loc.Desk(i.DeskIndex) : "";
            string s = Loc.F("incident." + i.Type, P(v, i.Actor), target, P(v, i.Victim), string.IsNullOrEmpty(i.ItemId) ? "" : ItemFromId(i.ItemId));
            if (i.BlamedPlayer >= 0 && i.BlamedPlayer != i.Actor) s += " " + Loc.F("incident.blamed", P(v, i.BlamedPlayer));
            if (i.ReversedBy >= 0) s += " " + Loc.F("incident.reversed", P(v, i.ReversedBy));
            return s;
        }

        static string ItemFromId(string id) { return Loc.ItemFromId(id); }

        public static string Recap(RecapBeat b, PlayerView v)
        {
            return Loc.F("recap." + b.Key.Replace("recap.", ""), P(v, b.Actor), P(v, b.Other), string.IsNullOrEmpty(b.ItemVariant) ? "" : Loc.Item(b.ItemVariant), b.DeskIndex >= 0 ? Loc.Desk(b.DeskIndex) : "", Loc.Time(b.Time, v));
        }
    }
}
