using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Builders for authenticated records and evidence presentations (docs/GAMEPLAY_REDESIGN.md §9). Used on the
    /// projector slide (non-interactive) and in the side panel. A record is a game fact ("record", never "footage").
    /// </summary>
    public static class RecordCards
    {
        /// <summary>
        /// A presented record: source, sentence, time, "authenticated record", presenter. Newly proven incident: an
        /// EXPOSED stamp naming the actor from the record; a repeat says it changes nothing.
        /// </summary>
        public static VisualElement Presentation(VisualElement parent, LinePresentationView p, LineView v, bool pick = false)
        {
            var r = p.Record ?? new LineRecordView();
            var card = Box("pc-card").AddTo(parent);
            if (!pick) card.pickingMode = PickingMode.Ignore;
            bool exposed = p.ExposedIncident >= 0;
            card.EnableInClassList("pc-exposed", exposed);
            card.EnableInClassList("pc-repeat", p.Repeat);

            var head = Box("pc-head").NoPick().AddTo(card);
            MeetingText.ApplyIcon(Box("icon", "icon-m").NoPick().AddTo(head), MeetingText.SourceIcon(r.Source));
            var hc = Box("col", "grow").NoPick().AddTo(head);
            MemoBold(MeetingText.SourceName(r.Source), "pc-source").NoPick().AddTo(hc);
            Memo(Loc.F("present.time", MeetingText.OfficeClock(r.Time, v)), "pc-time").NoPick().AddTo(hc);
            Tag(Loc.T("present.authenticated"), "tag-green").AddTo(head);

            Bold(MeetingText.RecordSentence(r), "pc-body").NoPick().AddTo(card);

            var foot = Box("pc-foot").NoPick().AddTo(card);
            Badge(p.Presenter, "badge-xs").AddTo(foot);
            Text(Loc.F("present.by", MeetingText.Name(p.Presenter)), "pc-by").NoPick().AddTo(foot);

            if (exposed)
            {
                var st = Box("pc-stamp").NoPick().AddTo(card);
                StampText(Loc.F("present.exposed", MeetingText.Incident(p.ExposedKind)), "pc-stamp-text").NoPick().AddTo(st);
                if (r.Actor >= 0) Bold(Loc.F("present.exposed_actor", MeetingText.Name(r.Actor)), "pc-actor").NoPick().AddTo(st);
            }
            else if (p.Repeat) Text(Loc.T("present.repeat"), "pc-note", "pc-note-repeat").NoPick().AddTo(card);
            else Text(Loc.T("present.neutral"), "pc-note").NoPick().AddTo(card);
            return card;
        }

        /// <summary>Compact row of one of my records in the picker (source, sentence, time); click = present.</summary>
        public static VisualElement PickRow(VisualElement parent, LineRecordView r, LineView v, System.Action onPresent, bool enabled)
        {
            var row = Box("rp-row").AddTo(parent);
            row.EnableInClassList("rp-row-done", r.Presented);
            MeetingText.ApplyIcon(Box("icon", "icon-s").NoPick().AddTo(row), MeetingText.SourceIcon(r.Source));
            var col = Box("col", "grow").NoPick().AddTo(row);
            Text(MeetingText.RecordSentence(r), "rp-text").NoPick().AddTo(col);
            Memo(MeetingText.SourceName(r.Source) + " · " + MeetingText.OfficeClock(r.Time, v), "rp-meta").NoPick().AddTo(col);
            if (r.Presented) Tag(Loc.T("present.already"), "tag-grey").AddTo(row);
            var b = Btn(Loc.T("present.button"), onPresent, "btn-small", r.Presented ? "btn-ghost" : "btn-primary").AddTo(row);
            b.SetEnabled(enabled);
            return row;
        }
    }
}
