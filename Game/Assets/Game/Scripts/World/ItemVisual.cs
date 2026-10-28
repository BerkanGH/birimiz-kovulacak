using BirimizKovulacak.Core;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Visual for one item entity (pooled by id). Purely presentational.</summary>
    public sealed class ItemVisual : MonoBehaviour
    {
        public string Variant;
        public GameObject Coffee;            // mug fill
        public TextMeshPro[] Labels;         // nameplate faces / report label / signs
        public Renderer StripeRenderer;      // nameplate colour stripe
        public GameObject SealTape;          // shown when sealed
        public GameObject EvidenceRing;      // shown when the item is active evidence
        [HideInInspector] public string ItemId;
        [HideInInspector] public bool Placed;

        Vector3 _vel;

        public void ApplyState(ItemView item, PlayerView view)
        {
            if (Coffee != null) Coffee.SetActive(item.Kind == ItemKind.Mug && item.Fill == MugFill.Full);
            if (SealTape != null) SealTape.SetActive(item.Sealed);
            if (EvidenceRing != null) EvidenceRing.SetActive(item.IsEvidence && item.Location != ItemLocationKind.Carried);
            if (Labels != null)
            {
                string text = null;
                if (item.Kind == ItemKind.Nameplate) text = Loc.PlayerName(view, item.NameplateOf).ToUpperInvariant();
                else if (item.Kind == ItemKind.Report) text = Loc.PlayerName(view, item.ReportOwner);
                else if (item.Kind == ItemKind.Mail && item.AddressedDesk != _mailDesk) { _mailOwner = MailOwner(view, item.AddressedDesk); _mailDesk = item.AddressedDesk; }
                if (item.Kind == ItemKind.Mail && _mailOwner >= 0) text = Loc.PlayerName(view, _mailOwner);
                if (text != null)
                    foreach (var l in Labels) if (l != null && l.text != text) l.text = text;
            }
            if (StripeRenderer != null && WorldPalette.I != null)
            {
                int who = item.Kind == ItemKind.Nameplate ? item.NameplateOf : item.Kind == ItemKind.Mail ? MailOwner(view, item.AddressedDesk) : PlayerIds.None;
                var m = who >= 0 ? WorldPalette.I.PlayerColor(view, who) : null;
                if (m != null && StripeRenderer.sharedMaterial != m) StripeRenderer.sharedMaterial = m;
            }
        }

        int _mailDesk = int.MinValue, _mailOwner = PlayerIds.None;

        /// <summary>Owner of the desk a mail item is addressed to (the addressee's colour is printed on the envelope).</summary>
        static int MailOwner(PlayerView view, int desk)
        {
            if (view == null || desk < 0) return PlayerIds.None;
            for (int i = 0; i < view.Desks.Count; i++) if (view.Desks[i].Index == desk) return view.Desks[i].AssignedOwner;
            return PlayerIds.None;
        }

        public void MoveTo(Vector3 pos, Quaternion rot, bool snap)
        {
            if (snap || !Placed)
            {
                transform.SetPositionAndRotation(pos, rot);
                Placed = true;
                return;
            }
            transform.position = Vector3.SmoothDamp(transform.position, pos, ref _vel, 0.08f);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }
    }
}
