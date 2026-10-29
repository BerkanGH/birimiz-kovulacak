using System.Text;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>Turns rules records into short Turkish descriptions (what was observed, never hidden truth).</summary>
    public static class Describe
    {
        static string P(PlayerView v, int id) { return Loc.PlayerName(v, id); }
        static string By(EvidenceView e, PlayerView v) { return e.Source == DiscoverySource.Manager ? Loc.T("ev.by_manager") : e.Source == DiscoverySource.System ? Loc.T("ev.by_system") : P(v, e.SourcePlayer); }

        // ------------------------------------------------------------------ evidence

        /// <summary>Card title, e.g. "Şüpheli eşya: Lastik ördek".</summary>
        public static string EvidenceTitle(EvidenceView e)
        {
            switch (e.Kind)
            {
                case EvidenceKind.SuspiciousItemFound: return Loc.F("evt.item", Loc.Item(e.ItemVariant));
                default: return Loc.T("evt." + e.Kind);
            }
        }

        public static string EvidenceIcon(EvidenceView e)
        {
            switch (e.Kind)
            {
                case EvidenceKind.SuspiciousItemFound: return "sab_plant";
                case EvidenceKind.HiddenReportFound: return "sab_hide_report";
                case EvidenceKind.WorkstationLockLog: return "sab_password";
                case EvidenceKind.WitnessStatement: return "notes";
                case EvidenceKind.SearchStatement: return "hr_scan";
                case EvidenceKind.ScanStatement: return "hr_scan";
                case EvidenceKind.EmailLog: return "sab_fake_email";
                case EvidenceKind.CctvFootage: return "footage";
                case EvidenceKind.CallLogStatement: return "hr_call_logs";
                case EvidenceKind.TasteStatement: return "sab_salt";
                default: return "info";
            }
        }

        public static string Evidence(EvidenceView e, PlayerView v)
        {
            string t = Loc.Time(e.Time, v);
            switch (e.Kind)
            {
                case EvidenceKind.SuspiciousItemFound:
                    return Loc.F("ev.item_found", t, Loc.Item(e.ItemVariant), Loc.Location(e.LocationId), P(v, e.AttributedTo), By(e, v));
                case EvidenceKind.HiddenReportFound:
                    return e.AttributedTo >= 0
                        ? Loc.F("ev.report_found_desk", t, Loc.Location(e.LocationId), P(v, e.AttributedTo), By(e, v))
                        : Loc.F("ev.report_found", t, Loc.Location(e.LocationId), By(e, v));
                case EvidenceKind.WorkstationLockLog:
                    return Loc.F("ev.lock_log", t, Loc.Desk(e.DeskIndex));
                case EvidenceKind.WitnessStatement:
                    return Loc.F("ev.witness", t, P(v, e.SourcePlayer), P(v, e.SubjectPlayer), Loc.Activity(e.ObservedActivity), Loc.Location(e.LocationId), Held(e.HeldBefore), Held(e.HeldAfter));
                case EvidenceKind.SearchStatement:
                    return Loc.F("ev.search", t, P(v, e.SourcePlayer), Loc.Location(e.LocationId), Contents(e.Contents, e.ItemVariant));
                case EvidenceKind.ScanStatement:
                    return Loc.F("ev.scan", t, P(v, e.SourcePlayer), string.IsNullOrEmpty(e.ItemVariant) ? Loc.T("common.evidence") : Loc.Item(e.ItemVariant), Loc.T("rel." + e.ScanReliability), Loc.T("signal." + e.ScanSignal));
                case EvidenceKind.EmailLog:
                    return Loc.F("ev.email_log", t, Loc.Location(e.LocationId), EmailSubject(e.EmailTemplate, v, e.AttributedTo), P(v, e.AttributedTo));
                case EvidenceKind.CctvFootage:
                    return e.NoRecording
                        ? Loc.F("ev.footage_none", t, Loc.Camera(e.CameraId), Loc.Time(e.WindowStart, v), Loc.Time(e.WindowEnd, v), P(v, e.SourcePlayer))
                        : Loc.F("ev.footage", t, Loc.Camera(e.CameraId), Loc.Time(e.WindowStart, v), Loc.Time(e.WindowEnd, v), DistinctPlayers(e), P(v, e.SourcePlayer));
                case EvidenceKind.CallLogStatement:
                    return Loc.F("ev.call_log", t, P(v, e.SourcePlayer), e.Calls.Count);
                case EvidenceKind.TasteStatement:
                    return Loc.F(e.Salted ? "ev.taste_salted" : "ev.taste_ok", t, P(v, e.SourcePlayer));
                default: return e.Kind.ToString();
            }
        }

        static int DistinctPlayers(EvidenceView e)
        {
            int mask = 0, n = 0;
            foreach (var s in e.Sightings)
            {
                if (s.Player < 0 || s.Player > 30) continue;
                int bit = 1 << s.Player;
                if ((mask & bit) == 0) { mask |= bit; n++; }
            }
            return n;
        }

        public static string Contents(System.Collections.Generic.List<string> contents, string legacyVariant = "")
        {
            if (contents != null && contents.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < contents.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Loc.Item(contents[i]));
                }
                return sb.ToString();
            }
            return string.IsNullOrEmpty(legacyVariant) ? Loc.T("common.nothing") : Loc.Item(legacyVariant);
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

        public static string Sighting(FootageSighting s, PlayerView v)
        {
            string act = s.Activity == PublicActivity.None ? Loc.T("footage.idle") : Loc.Activity(s.Activity);
            string carry = string.IsNullOrEmpty(s.Carrying) ? "" : Loc.F("footage.carrying", Loc.Item(s.Carrying));
            return Loc.F("footage.sighting", Loc.Time(s.Time, v), P(v, s.Player), act, carry, s.Crouched ? Loc.T("footage.crouched") : "");
        }

        public static string Call(CallRecordView c, PlayerView v)
        {
            string t = Loc.Time(c.Start, v);
            if (c.Tip) return Loc.F("call.tip", t, P(v, c.Caller), c.TipDesk >= 0 ? Loc.Desk(c.TipDesk) : Loc.T("common.unknown"));
            int dur = c.Answered && c.End > c.Start ? (int)(c.End - c.Start + 0.5) : 0;
            return c.Answered ? Loc.F("call.answered", t, P(v, c.Caller), P(v, c.Callee), dur) : Loc.F("call.missed", t, P(v, c.Caller), P(v, c.Callee));
        }

        // ------------------------------------------------------------------ observations

        public static string ObservationIcon(ObservationKind k)
        {
            switch (k)
            {
                case ObservationKind.MessyDesk: return "sab_clutter";
                case ObservationKind.NotAtDesk: case ObservationKind.AtWrongDesk: return "warning";
                case ObservationKind.MovingDuringInspection: return "warning";
                case ObservationKind.CarryingSuspicious: case ObservationKind.SuspiciousItemAtDesk: return "sab_plant";
                case ObservationKind.CaughtInAct: return "accuse";
                case ObservationKind.BehindOnWork: return "timer";
                case ObservationKind.WorkingHard: return "star";
                case ObservationKind.PersonalCall: return "call";
                case ObservationKind.InappropriateEmail: return "sab_fake_email";
                case ObservationKind.SaltyCoffee: return "sab_salt";
                case ObservationKind.TipClean: return "check";
                case ObservationKind.TipFound: return "warning";
                default: return "manager";
            }
        }

        public static string Observation(ObservationView o, PlayerView v)
        {
            string t = Loc.Time(o.Time, v);
            string who = P(v, o.Subject);
            string desk = o.DeskIndex >= 0 ? Loc.Desk(o.DeskIndex) : "";
            switch (o.Kind)
            {
                case ObservationKind.MessyDesk: return Loc.F("obs.messy", t, who, MessDetail(o.Detail));
                case ObservationKind.NotAtDesk: return Loc.F("obs.not_at_desk", t, who);
                case ObservationKind.AtWrongDesk: return Loc.F("obs.wrong_desk", t, who, desk);
                case ObservationKind.MovingDuringInspection: return Loc.F("obs.moving", t, who);
                case ObservationKind.CarryingSuspicious: return Loc.F("obs.carrying", t, who, Loc.Item(o.Detail));
                case ObservationKind.CaughtInAct: return Loc.F("obs.caught", t, who, desk);
                case ObservationKind.BehindOnWork: return Loc.F("obs.behind", t, who);
                case ObservationKind.WorkingHard: return Loc.F("obs.working", t, who);
                case ObservationKind.SuspiciousItemAtDesk: return Loc.F("obs.item", t, Loc.Item(o.Detail), who);
                case ObservationKind.PersonalCall: return Loc.F("obs.call", t, who);
                case ObservationKind.InappropriateEmail: return Loc.F("obs.email", t, who);
                case ObservationKind.SaltyCoffee: return Loc.F("obs.salty", t, who);
                case ObservationKind.TipClean: return Loc.F("obs.tip_clean", t, who, desk);
                case ObservationKind.TipFound: return Loc.F("obs.tip_found", t, who, desk);
                default: return o.Kind.ToString();
            }
        }

        static string MessDetail(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            var parts = detail.Split(',');
            var sb = new StringBuilder();
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

        // ------------------------------------------------------------------ private notes

        public static string NoteIcon(PrivateNote n)
        {
            switch (n.Kind)
            {
                case NoteKind.Witness: return "notes";
                case NoteKind.SearchResult: return "hr_scan";
                case NoteKind.ScanResult: return "hr_scan";
                case NoteKind.CallLog: return "hr_call_logs";
                case NoteKind.TasteResult: return "sab_salt";
                default: return "notes";
            }
        }

        public static string Note(PrivateNote n, PlayerView v)
        {
            string t = Loc.Time(n.Time, v);
            switch (n.Kind)
            {
                case NoteKind.Witness:
                    return Loc.F("note.witness", t, P(v, n.Subject), Loc.Activity(n.Activity), Loc.Location(n.TargetId), Held(n.HeldBefore), Held(n.HeldAfter));
                case NoteKind.SearchResult:
                    return Loc.F("note.search", t, Loc.Location(n.TargetId), Contents(n.Contents));
                case NoteKind.ScanResult:
                {
                    ScanResultView s = null;
                    if (v.Me.Informant != null) foreach (var x in v.Me.Informant.Scans) if (x.Id == n.ScanId) s = x;
                    return s != null ? Scan(s, v) : Loc.F("note.scan_generic", t);
                }
                case NoteKind.CallLog:
                    return Loc.F("note.call_log", t, n.Calls.Count);
                case NoteKind.TasteResult:
                    return Loc.F(n.Salted ? "note.taste_salted" : "note.taste_ok", t);
                default: return n.Kind.ToString();
            }
        }

        public static string Scan(ScanResultView s, PlayerView v)
        {
            string what = string.IsNullOrEmpty(s.ItemVariant) ? Loc.T("common.evidence") : Loc.Item(s.ItemVariant);
            string owner = s.ApparentOwner >= 0 ? Loc.F("scan.owner", P(v, s.ApparentOwner)) : "";
            return Loc.F("scan.result", Loc.Time(s.Time, v), what, owner, Loc.T("rel." + s.Reliability), Loc.T("signal." + s.Signal));
        }

        // ------------------------------------------------------------------ emails

        /// <summary>{0} = manager name, {1} = apparent sender.</summary>
        public static string EmailSubject(int template, PlayerView v, int fromPlayer)
        {
            if (template < 0) return Loc.T("email.unknown");
            return Loc.F("email." + template + ".subject", Loc.ManagerName(v != null ? v.ManagerProfileId : ""), P(v, fromPlayer));
        }

        public static string EmailBody(int template, PlayerView v, int fromPlayer)
        {
            if (template < 0) return "";
            return Loc.F("email." + template + ".body", Loc.ManagerName(v != null ? v.ManagerProfileId : ""), P(v, fromPlayer));
        }

        public static string DeskOwnerName(PlayerView v, int desk)
        {
            if (v == null || desk < 0) return Loc.T("common.nobody");
            foreach (var d in v.Desks) if (d.Index == desk) return d.AssignedOwner >= 0 ? P(v, d.AssignedOwner) : Loc.T("desk.unassigned");
            return Loc.Desk(desk);
        }

        // ------------------------------------------------------------------ feed

        /// <summary>Feed line for an event, or null when the event should not produce a toast.</summary>
        public static string Event(GameEvent e, PlayerView v, out Hud.ToastKind kind)
        {
            kind = Hud.ToastKind.Info;
            int me = ClientState.I != null ? ClientState.I.MySlot : PlayerIds.None;
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
                    return Loc.T("feed.evidence_generic");
                }
                case EventType.EvidenceUpdated:
                {
                    foreach (var ev in v.Evidence)
                        if (ev.Id == e.IntArg && ev.Sealed) return Loc.F("feed.sealed", ev.Id);
                    return null;
                }
                case EventType.NoteAdded: kind = Hud.ToastKind.Private; return Loc.F("feed.note", GameInput.KeyLabel(GameInput.Phone));
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
                // ---- v2
                case EventType.IncomingCall:
                    if (e.Actor == me) return null; // the call widget shows it
                    // The ringtone is a 3D sound at the callee: only people who can see the callee get a toast,
                    // otherwise an office-wide toast lets everyone pair callers and callees.
                    if (AvatarVisibility.Alpha(e.Actor) < 0.5f) return null;
                    return Loc.F("feed.ringing", P(v, e.Actor));
                case EventType.CallStarted: kind = Hud.ToastKind.Private; return Loc.F("feed.call_started", P(v, e.Actor));
                case EventType.CallEnded: kind = Hud.ToastKind.Private; return e.IntArg == 1 ? Loc.F("feed.call_ended", P(v, e.Actor)) : Loc.F("feed.call_not_answered", P(v, e.Actor));
                case EventType.EmailSent:
                {
                    kind = Hud.ToastKind.Manager;
                    foreach (var m in v.Inbox) if (m.Id == e.IntArg) return Loc.F("feed.email", P(v, m.FromPlayer), EmailSubject(m.TemplateId, v, m.FromPlayer));
                    return Loc.T("feed.email_generic");
                }
                case EventType.InternetDown: kind = Hud.ToastKind.Warning; return Loc.T("feed.internet_down");
                case EventType.InternetRestored: kind = Hud.ToastKind.Good; return e.Actor >= 0 ? Loc.F("feed.internet_restored_by", P(v, e.Actor)) : Loc.T("feed.internet_restored");
                case EventType.PowerDown: kind = Hud.ToastKind.Warning; return Loc.T("feed.power_down");
                case EventType.PowerRestored: kind = Hud.ToastKind.Good; return e.Actor >= 0 ? Loc.F("feed.power_restored_by", P(v, e.Actor)) : Loc.T("feed.power_restored");
                case EventType.PlugPulled: kind = Hud.ToastKind.Warning; return Loc.F("feed.plug_pulled", Loc.Location(e.TargetId));
                case EventType.PlugRestored: kind = Hud.ToastKind.Good; return Loc.F("feed.plug_restored", Loc.Location(e.TargetId), P(v, e.Actor));
                case EventType.ManagerTipReceived: kind = Hud.ToastKind.Manager; return Loc.T("feed.tip");
                case EventType.FavorChanged: return null; // shown as a flash on the coworker list
                case EventType.FootageShown:
                {
                    kind = Hud.ToastKind.Warning;
                    foreach (var ev in v.Evidence) if (ev.Id == e.IntArg) return Loc.F("feed.footage", P(v, ev.SourcePlayer), Loc.Camera(ev.CameraId));
                    return Loc.T("feed.footage_generic");
                }
                case EventType.SurpriseInspection: kind = Hud.ToastKind.Warning; return Loc.T("feed.surprise");
                case EventType.MailArrived: return Loc.F("feed.mail", DeskOwnerName(v, e.IntArg));
                case EventType.Emote: return null;
                case EventType.CoffeeTasted: kind = Hud.ToastKind.Private; return Loc.T(e.IntArg == 1 ? "feed.taste_salted" : "feed.taste_ok");
                default: return null;
            }
        }

        // ------------------------------------------------------------------ results

        public static string Incident(IncidentReveal i, PlayerView v)
        {
            string target = i.DeskIndex >= 0 ? Loc.Desk(i.DeskIndex) : "";
            string s = Loc.F("incident." + i.Type, P(v, i.Actor), target, P(v, i.Victim), string.IsNullOrEmpty(i.ItemId) ? "" : Loc.ItemFromId(i.ItemId));
            if (i.BlamedPlayer >= 0 && i.BlamedPlayer != i.Actor) s += " " + Loc.F("incident.blamed", P(v, i.BlamedPlayer));
            if (i.ReversedBy >= 0) s += " " + Loc.F("incident.reversed", P(v, i.ReversedBy));
            if (!i.Discovered) s += " " + Loc.T("incident.undiscovered");
            return s;
        }

        public static string Recap(RecapBeat b, PlayerView v)
        {
            string key = b.Key.StartsWith("recap.") ? b.Key : "recap." + b.Key;
            return Loc.F(key, P(v, b.Actor), P(v, b.Other), string.IsNullOrEmpty(b.ItemVariant) ? "" : Loc.Item(b.ItemVariant), b.DeskIndex >= 0 ? Loc.Desk(b.DeskIndex) : "", Loc.Time(b.Time, v));
        }

        public static string Title(string titleKey) { return Loc.T(titleKey.StartsWith("title.") ? titleKey : "title." + titleKey); }
    }
}
