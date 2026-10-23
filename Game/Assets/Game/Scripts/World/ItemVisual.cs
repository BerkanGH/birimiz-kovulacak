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
                if (text != null)
                    foreach (var l in Labels) if (l != null && l.text != text) l.text = text;
            }
            if (StripeRenderer != null && item.Kind == ItemKind.Nameplate)
            {
                var mats = WorldPalette.I != null ? WorldPalette.I.PlayerColors : null;
                if (mats != null && item.NameplateOf >= 0 && item.NameplateOf < mats.Length) StripeRenderer.sharedMaterial = mats[item.NameplateOf];
            }
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
