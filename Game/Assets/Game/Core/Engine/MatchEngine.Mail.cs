using System;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private int mailSerial;
        private double nextMail = double.PositiveInfinity;

        private void InitializeMail()
        {
            if (!features.Mail) return;
            for (int n = 0; n < Math.Max(0, Config.Tasks.InitialMail); n++) if (!SpawnMail()) break;
        }

        private int OpenMail()
        {
            int count = 0;
            foreach (ItemState item in items)
                if (item.View.Kind == ItemKind.Mail && (item.View.Location == ItemLocationKind.Carried ||
                    (item.View.Location == ItemLocationKind.Slot && item.View.SlotId == EntityIds.Mailroom))) count++;
            return count;
        }

        private bool SpawnMail()
        {
            SlotDef room = Layout.GetSlot(EntityIds.Mailroom);
            if (room == null || OpenMail() >= Math.Max(0, Config.Tasks.MaxOpenMail)) return false;
            int index = FreeReservedIndex(room.Id, PlayerIds.None);
            if (index >= room.Capacity) return false;
            int desk = players[playerIds[random.Index(playerIds.Count)]].Public.DeskIndex;
            string id = EntityIds.Mail(++mailSerial);
            items.Add(new ItemState { View = new ItemView { Id = id, Kind = ItemKind.Mail, Variant = ItemVariants.Mail,
                Location = ItemLocationKind.Slot, SlotId = room.Id, SlotIndex = index, AddressedDesk = desk },
                PlacementSlot = room.Id, PlacedAt = clock });
            Emit(EventType.MailArrived, item: id, arg: desk);
            return true;
        }

        private void MailPhaseChanged()
        {
            nextMail = features.Mail && (Phase == MatchPhase.Work1 || Phase == MatchPhase.Work2)
                ? After(Config.Tasks.MailInterval) : double.PositiveInfinity;
        }

        private void NextMailTime(ref double next)
        {
            next = Math.Min(next, nextMail);
            foreach (int p in playerIds)
                if (players[p].Public.Emote >= 0) next = Math.Min(next, Math.Max(clock, players[p].Public.EmoteUntil));
        }

        private void AdvanceMail()
        {
            if (clock >= nextMail)
            {
                SpawnMail();
                nextMail = After(Config.Tasks.MailInterval);
            }
            foreach (int p in playerIds)
                if (players[p].Public.Emote >= 0 && clock >= players[p].Public.EmoteUntil)
                { players[p].Public.Emote = -1; players[p].Public.EmoteUntil = 0; Changed(); }
        }

        private void DeliverMail(int p, ItemState item, SlotDef slot)
        {
            if (item.View.Kind != ItemKind.Mail || slot.Kind != SlotKind.DeskSurface || item.View.AddressedDesk != slot.DeskIndex) return;
            item.View.Location = ItemLocationKind.Archived;
            AwardTask(p, TaskType.Mail);
        }

        private FailureCode SubmitEmote(int p, int emote)
        {
            if (emote < 0 || emote > (int)EmoteId.Laugh) return FailureCode.InvalidTarget;
            if (players[p].Public.Emote >= 0 && clock < players[p].Public.EmoteUntil) return FailureCode.Cooldown;
            players[p].Public.Emote = emote; players[p].Public.EmoteUntil = clock + 2.5;
            Emit(EventType.Emote, p, arg: emote);
            return FailureCode.None;
        }
    }
}
