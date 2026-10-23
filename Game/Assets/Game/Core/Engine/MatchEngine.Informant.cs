using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private int scanSerial;

        partial void SubmitInformant(int p, ActionRequest request, ref bool handled, ref FailureCode failure)
        {
            if (handled || (request.Verb != ActionVerb.AuditScanEvidence && request.Verb != ActionVerb.AuditScanItem && request.Verb != ActionVerb.SealEvidence)) return;
            handled = true;
            InformantView tools = players[p].Private.Informant;
            if (players[p].Private.Role != Role.Informant || tools == null) { failure = FailureCode.NotAllowed; return; }
            bool sealing = request.Verb == ActionVerb.SealEvidence;
            if ((sealing ? tools.SealsLeft : tools.ScansLeft) <= 0) { failure = FailureCode.NoCharges; return; }
            EvidenceState record = null;
            ItemState item;
            if (request.Verb != ActionVerb.AuditScanItem)
            {
                record = evidence.Find(e => e.View.Id == request.IntParam);
                if (record == null) { failure = FailureCode.EvidenceIneligible; return; }
                EvidenceView e = record.View;
                bool scanKind = e.Kind == EvidenceKind.SuspiciousItemFound || e.Kind == EvidenceKind.HiddenReportFound;
                if ((!scanKind && !(sealing && e.Kind == EvidenceKind.WorkstationLockLog)) || (sealing && (e.Status != EvidenceStatus.Active || e.Sealed)))
                { failure = FailureCode.EvidenceIneligible; return; }
                if (sealing)
                {
                    record.Sealer = p; e.Sealed = true; e.RiskWeight = EvidenceWeight(e);
                    ItemState sealedItem = Item(e.ItemId);
                    if (sealedItem != null && sealedItem.View.Location == ItemLocationKind.Slot && sealedItem.View.SlotId == e.LocationId) sealedItem.View.Sealed = true;
                    tools.SealsLeft--; tools.SealedEvidenceId = e.Id;
                    Emit(EventType.EvidenceUpdated, arg: e.Id);
                    failure = FailureCode.None; return;
                }
                item = Item(e.ItemId);
            }
            else
            {
                item = Item(request.TargetId);
                if (item == null || !Visible(item, p) || (item.View.Kind != ItemKind.Suspicious && item.View.Kind != ItemKind.Report && item.View.Kind != ItemKind.Nameplate)) { failure = FailureCode.EvidenceIneligible; return; }
                if (Vec3.FlatDistance(players[p].Avatar.Position, ItemPosition(item)) > Config.Informant.ItemScanRange) { failure = FailureCode.TooFar; return; }
            }
            SlotDef slot = item != null && item.View.Location == ItemLocationKind.Slot ? Layout.GetSlot(item.View.SlotId) : null;
            int desk = record != null ? record.View.DeskIndex : slot == null ? -1 : slot.DeskIndex;
            int apparent = record != null ? record.View.AttributedTo : ApparentOwner(desk);
            int placer = record != null ? record.Placer : item == null ? PlayerIds.None : item.LastPlacer;
            double placedAt = record != null ? record.PlacedAt : item == null ? double.NegativeInfinity : item.PlacedAt;
            Reliability reliability = apparent == PlayerIds.None || placer == PlayerIds.None ? Reliability.Medium : placer == apparent ? Reliability.High : item != null && item.Handlers.Contains(apparent) ? Reliability.Medium : Reliability.Low;
            ScanSignal signal = ScanSignal.NoInconsistency;
            DeskState deskState = Desk(desk);
            if (deskState != null && deskState.NameplateChangedAt > placedAt) signal = ScanSignal.NameplateChangedAfterPlacement;
            else if (item != null && clock - item.MovedAt <= Config.Informant.MovedRecentlySeconds) signal = ScanSignal.MovedRecently;
            else if (item != null)
            {
                var handlers = new List<int>(); foreach (int handler in item.Handlers) if (!handlers.Contains(handler)) handlers.Add(handler);
                if (handlers.Count >= 2) signal = ScanSignal.HandledByMultiple;
            }
            var result = new ScanResultView { Id = ++scanSerial, Time = clock, EvidenceId = record == null ? -1 : record.View.Id, ItemId = item == null ? record.View.ItemId : item.View.Id, ItemVariant = item == null ? record.View.ItemVariant : item.View.Variant, DeskIndex = desk, ApparentOwner = apparent, Reliability = reliability, Signal = signal };
            var note = new PrivateNote { Kind = NoteKind.ScanResult, ScanId = result.Id, DeskIndex = desk, TargetId = record == null ? item.View.Id : record.View.LocationId };
            AddNote(p, note); result.NoteId = note.Id; tools.Scans.Add(result); tools.ScansLeft--;
            failure = FailureCode.None;
        }
    }
}
