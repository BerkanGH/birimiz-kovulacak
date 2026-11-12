using System.Collections.Generic;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3b world side of the meeting room (docs/V5_INTERACTION.md §3): the defender stands up (their chair rolls
    /// back, the spotlight follows the head; the local defender's camera rises too), everyone's attention can go to the
    /// projector (a leaked recording, the desk opening, the results), and the desk payout: after a firing the fired
    /// colleague's prim appears as banknote bundles at their place on the table, then the shares slide to the places of
    /// the colleagues who voted for them and stay there; what cannot be split burns away. Local presentation only.
    /// </summary>
    public sealed partial class MeetingRoom
    {
        [Header("Step 3b: desk payout, standing defender")]
        public GameObject BundleTemplate, BillTemplate;
        /// <summary>How far the defender's chair rolls back, how much the standing bean rises (m) and how much higher a
        /// standing local eye is.</summary>
        public float StandChairBack = 0.55f, StandLift = 0.26f, StandEyeUp = 0.28f;

        // ---- standing (one defender at a time)
        readonly float[] _stand = new float[6];
        Vector3[] _chairRest;
        Quaternion[] _chairRestRot;
        bool _attentionProjector;

        // ---- payouts
        sealed class Pile
        {
            public readonly List<Transform> Items = new List<Transform>();
            public readonly List<Vector3> Offsets = new List<Vector3>();
            public Vector3 From, To;
            public float ShowAt, Start, Duration;
            public bool Burn, Done;
        }
        readonly List<Pile> _piles = new List<Pile>();
        readonly int[] _payoutRows = new int[MaxSlots];
        bool _payoutSound;

        /// <summary>0 = seated .. 1 = standing, for the local player's seat (the camera rises with it).</summary>
        public float LocalStand { get { int s = LocalSeat; return s >= 0 && s < _stand.Length ? Ease(_stand[s]) : 0f; } }

        /// <summary>Where the projected picture is (the centre of the canvas).</summary>
        public Vector3 ProjectorPoint => Canvas != null ? Canvas.bounds.center : transform.TransformPoint(new Vector3(-0.25f, 1.94f, 3f));

        /// <summary>Beans without a fresh shared gaze (and the manager) look at the projector (a leak, the desk opening, the results).</summary>
        public void SetAttentionProjector(bool on) { _attentionProjector = on; }
        public bool AttentionProjector => _attentionProjector;

        /// <summary>Yaw/pitch (relative to the seat, right +, down +) from a seat's eye to the projected picture.</summary>
        public bool ProjectorAngles(int slot, out float yaw, out float pitch)
        {
            yaw = 0f; pitch = 0f;
            var a = SeatAnchorOfSlot(slot);
            Vector3 eye;
            if (a == null || !SeatHead(slot, out eye)) return false;
            return AnglesTo(a, eye, ProjectorPoint, out yaw, out pitch);
        }

        void InitStage()
        {
            if (BundleTemplate != null) BundleTemplate.SetActive(false);
            if (BillTemplate != null) BillTemplate.SetActive(false);
            _chairRest = new Vector3[Seats.Length];
            _chairRestRot = new Quaternion[Seats.Length];
            for (int s = 0; s < Seats.Length; s++)
            {
                var c = ChairOf(s);
                _chairRest[s] = c != null ? c.localPosition : Vector3.zero;
                _chairRestRot[s] = c != null ? c.localRotation : Quaternion.identity;
            }
        }

        Transform ChairOf(int seat)
        {
            if (seat < 0 || seat >= Seats.Length) return null;
            var rig = Seats[seat];
            if (rig.Chair != null) return rig.Chair;
            return rig.Anchor != null ? rig.Anchor.Find("Chair") : null;
        }

        /// <summary>The defender (spotlit seat) stands up: chair back, the bean on its feet (standing clips), the light higher.</summary>
        void TickStand(float dt)
        {
            if (_chairRest == null || _chairRest.Length != Seats.Length) InitStage();
            int spotSeat = _spot >= 0 && _spot < MaxSlots ? _seatOfSlot[_spot] : -1;
            for (int s = 0; s < _stand.Length && s < Seats.Length; s++)
            {
                bool up = s == spotSeat && SlotAtSeat(s) != _fired;
                float before = _stand[s];
                _stand[s] = Mathf.MoveTowards(_stand[s], up ? 1f : 0f, dt / (up ? 0.7f : 0.55f));
                if (before == _stand[s] && before == 0f) continue;
                var chair = ChairOf(s);
                if (chair != null)
                {
                    // the chair rolls back (and turns a little) ahead of the bean getting up
                    float k = Ease(Mathf.Clamp01(_stand[s] * 1.4f));
                    chair.localPosition = _chairRest[s] + Vector3.back * (StandChairBack * k);
                    chair.localRotation = Quaternion.Euler(0f, 14f * k, 0f) * _chairRestRot[s];
                }
                if (_beans[s] != null)
                {
                    _beans[s].SetStanding(up ? _stand[s] > 0.3f : _stand[s] > 0.55f);
                    var a = Seats[s].Anchor;
                    float e = Ease(_stand[s]);
                    // a small overshoot as the bean pushes itself up
                    float rise = e + (up ? Mathf.Sin(Mathf.Clamp01(_stand[s]) * Mathf.PI) * 0.12f : 0f);
                    // up against the table edge, the chair out of the way behind
                    if (a != null) _beans[s].transform.position = a.position + a.forward * (0.05f * e) + Vector3.up * (StandLift * rise);
                }
            }
        }

        /// <summary>The spotlight target: the seated head, higher while the defender stands.</summary>
        float SpotHeadHeight(int slot)
        {
            int s = slot >= 0 && slot < MaxSlots ? _seatOfSlot[slot] : -1;
            float k = s >= 0 && s < _stand.Length ? Ease(_stand[s]) : 0f;
            return 1.15f + StandLift * k;
        }

        void ResetStand()
        {
            for (int s = 0; s < _stand.Length; s++) _stand[s] = 0f;
            if (_chairRest == null || _chairRest.Length != Seats.Length) InitStage();
            for (int s = 0; s < Seats.Length; s++)
            {
                var c = ChairOf(s);
                if (c != null) { c.localPosition = _chairRest[s]; c.localRotation = _chairRestRot[s]; }
            }
        }

        // ------------------------------------------------------------------ desk payout

        /// <summary>Clears every bundle on the table (a new meeting room / week).</summary>
        public void ClearPayouts()
        {
            foreach (var p in _piles) foreach (var t in p.Items) if (t != null) Destroy(t.gameObject);
            _piles.Clear();
            for (int i = 0; i < _payoutRows.Length; i++) _payoutRows[i] = 0;
            _payoutSound = false;
        }

        /// <summary>Where bundles lie at a seat: on the glass side of the folder, a little beyond it (the ballot paper
        /// lies on the other side; an earlier payout stays, a later one lands further out).</summary>
        bool PlaceOf(int slot, int row, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var a = SeatAnchorOfSlot(slot);
            if (a == null) return false;
            // the same side rule as the builder: the paper toward the middle of the table, the glass the other way
            Vector3 toMiddle = transform.TransformPoint(new Vector3(0f, 0f, -0.15f)) - a.position;
            float middle = Vector3.Dot(a.right, toMiddle) >= 0f ? 1f : -1f;
            pos = a.position + a.forward * (0.74f + 0.02f * row) - a.right * middle * (0.25f + 0.18f * row) + Vector3.up * 0.752f;
            // the long side of a bundle along the table edge, a little askew
            rot = Quaternion.LookRotation(a.forward, Vector3.up) * Quaternion.Euler(0f, (row % 2 == 0 ? 6f : -5f) * middle, 0f);
            return true;
        }

        /// <summary>The fired colleague's prim appears at their place (<paramref name="desk"/> prim in bundles of five)
        /// <paramref name="showIn"/> seconds from now, then <paramref name="share"/> slides to each recipient's place
        /// <paramref name="slideIn"/> seconds from now; the remainder (or everything, without recipients) burns.
        /// <paramref name="skip"/>: already past (late join): the shares lie where they ended up.</summary>
        public void StartPayout(int fired, int desk, IList<int> recipients, int share, float showIn, float slideIn, float slideTime, bool skip)
        {
            if (BundleTemplate == null || desk <= 0) return;
            Vector3 fromPos; Quaternion fromRot;
            if (!PlaceOf(fired, 0, out fromPos, out fromRot)) return;
            float now = Time.time;
            int k = 0;
            if (recipients != null && share > 0)
            {
                foreach (int id in recipients)
                {
                    Vector3 to; Quaternion toRot;
                    int row = id >= 0 && id < _payoutRows.Length ? _payoutRows[id]++ : 0;
                    if (!PlaceOf(id, row, out to, out toRot)) continue;
                    var pile = new Pile { From = fromPos, To = to, ShowAt = now + showIn, Start = now + slideIn + k * 0.14f, Duration = slideTime };
                    Fill(pile, share, toRot);
                    Pose(pile, skip ? pile.To : pile.From, skip ? 1f : 0f);
                    pile.Done = skip;
                    _piles.Add(pile);
                    k++;
                }
            }
            int burned = Mathf.Max(0, desk - share * k);
            if (burned > 0 && !skip)
            {
                var pile = new Pile { From = fromPos, To = fromPos + Vector3.up * 0.16f, ShowAt = now + showIn, Start = now + slideIn + 0.3f, Duration = slideTime * 0.8f, Burn = true };
                Fill(pile, burned, fromRot);
                Pose(pile, pile.From, 0f);
                _piles.Add(pile);
            }
        }

        static readonly Vector3[] Spread = { new Vector3(-0.081f, 0f, -0.039f), new Vector3(0.081f, 0f, -0.037f), new Vector3(-0.079f, 0f, 0.04f), new Vector3(0.082f, 0f, 0.039f) };

        /// <summary>Bundles of five in up to four stacks (two by two) and the loose notes on top; at most 16 bundles show.</summary>
        void Fill(Pile pile, int prim, Quaternion rot)
        {
            int bundles = Mathf.Min(prim / 5, 16), loose = Mathf.Min(prim % 5, 4);
            int stacks = Mathf.Clamp(Mathf.CeilToInt(bundles / 3f), 1, 4);
            var tops = new float[4];
            for (int i = 0; i < bundles; i++)
            {
                int s = i % stacks;
                var t = Instantiate(BundleTemplate, transform).transform;
                t.gameObject.SetActive(true);
                t.name = "PayBundle" + i;
                t.rotation = rot * Quaternion.Euler(0f, (i * 37 % 9 - 4) * 1.2f, 0f);
                pile.Items.Add(t);
                pile.Offsets.Add(rot * (stacks == 1 ? Vector3.zero : Spread[s]) + Vector3.up * tops[s]);
                tops[s] += 0.0094f;
            }
            int last = bundles > 0 ? (bundles - 1) % stacks : 0;
            for (int i = 0; i < loose && BillTemplate != null; i++)
            {
                var t = Instantiate(BillTemplate, transform).transform;
                t.gameObject.SetActive(true);
                t.name = "PayNote" + i;
                t.rotation = rot * Quaternion.Euler(0f, 8f + i * 13f, 0f);
                pile.Items.Add(t);
                Vector3 baseOff = bundles > 0 && stacks > 1 ? rot * Spread[last] : Vector3.zero;
                pile.Offsets.Add(baseOff + rot * new Vector3(0.004f * i - 0.006f, 0f, 0.003f * i) + Vector3.up * (tops[last] + 0.0019f * i));
            }
        }

        static void Pose(Pile pile, Vector3 centre, float scale)
        {
            for (int i = 0; i < pile.Items.Count; i++)
            {
                var t = pile.Items[i];
                if (t == null) continue;
                t.position = centre + pile.Offsets[i];
                t.localScale = Vector3.one * scale;
            }
        }

        void TickPayouts(float now, float dt)
        {
            for (int i = _piles.Count - 1; i >= 0; i--)
            {
                var p = _piles[i];
                if (p.Done) continue;
                if (now < p.Start)
                {
                    // the pile pops up at the fired colleague's place (the drawer emptied onto the table)
                    if (now < p.ShowAt) { Pose(p, p.From, 0f); continue; }
                    if (!_payoutSound) { _payoutSound = true; Sfx.Play("bills", 0.6f); }
                    float u = Mathf.Clamp01((now - p.ShowAt) / 0.3f);
                    Pose(p, p.From + Vector3.up * (0.05f * (1f - u)), Mathf.Min(1f, u * 1.4f));
                    continue;
                }
                float f = Mathf.Clamp01((now - p.Start) / Mathf.Max(0.05f, p.Duration));
                if (p.Burn)
                {
                    // what cannot be split curls up and is gone
                    Pose(p, Vector3.Lerp(p.From, p.To, f), 1f - Ease(f));
                    foreach (var t in p.Items) if (t != null) t.rotation = t.rotation * Quaternion.Euler(0f, 260f * dt, 0f);
                    if (f >= 1f)
                    {
                        foreach (var t in p.Items) if (t != null) Destroy(t.gameObject);
                        _piles.RemoveAt(i);
                    }
                    continue;
                }
                // slides along the table (a little lift in the middle so stacks never clip the table)
                float e = Ease(f);
                Pose(p, Vector3.Lerp(p.From, p.To, e) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * 0.03f), 1f);
                if (f >= 1f)
                {
                    p.Done = true;
                    Sfx.Play("drawer_thunk", 0.3f);
                }
            }
        }

        /// <summary>Bundles still moving (screenshots / tests).</summary>
        public int PayoutsMoving { get { int n = 0; foreach (var p in _piles) if (!p.Done) n++; return n; } }
        public int PayoutPiles => _piles.Count;
    }
}
