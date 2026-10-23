using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Bridges rules state and the 3D office. Every slot/station/desk has a scene anchor named by its entity id
    /// (created by the ProjectBuilder from DefaultOffice). Items are pooled visuals positioned from the local
    /// player's filtered view only: anything not in the view (e.g. unknown drawer contents) is simply not shown.
    /// </summary>
    public sealed class OfficeWorld : MonoBehaviour
    {
        public static OfficeWorld I { get; private set; }

        [Serializable]
        public struct ItemPrefab { public string Variant; public ItemVisual Prefab; }

        public ItemPrefab[] ItemPrefabs;
        public ManagerVisual Manager;
        public DeskVisual[] Desks;
        public StationVisual[] Stations;
        public Light[] AlarmLights;
        public Transform AlarmRotor;

        readonly Dictionary<string, Transform> _anchors = new Dictionary<string, Transform>();
        readonly Dictionary<string, ItemVisual> _items = new Dictionary<string, ItemVisual>();
        readonly Dictionary<string, ItemVisual> _prefabByVariant = new Dictionary<string, ItemVisual>();
        readonly HashSet<string> _seen = new HashSet<string>();
        PlayerView _attractView;

        void Awake()
        {
            I = this;
            foreach (var p in ItemPrefabs) if (p.Prefab != null) _prefabByVariant[p.Variant] = p.Prefab;
            var layout = GameConfig.Layout;
            foreach (var s in layout.Slots) Register(s.Id);
            foreach (var s in layout.Stations) Register(s.Id);
            foreach (var d in layout.Desks) Register(d.Id);
        }

        void Register(string id)
        {
            var go = GameObject.Find(id);
            if (go != null) _anchors[id] = go.transform;
            else Debug.LogWarning("[World] Missing scene anchor " + id);
        }

        public Transform Anchor(string id)
        {
            Transform t;
            return _anchors.TryGetValue(id, out t) ? t : null;
        }

        // ------------------------------------------------------------------ targets (interaction focus)

        /// <summary>World position used to focus a target. Items use their visual; layout entities their layout point.</summary>
        public bool TryTargetPosition(string id, out Vector3 pos)
        {
            ItemVisual iv;
            if (_items.TryGetValue(id, out iv) && iv.gameObject.activeSelf) { pos = iv.transform.position; return true; }
            var layout = GameConfig.Layout;
            var slot = layout.GetSlot(id);
            if (slot != null) { pos = V(slot.Position); return true; }
            var st = layout.GetStation(id);
            if (st != null) { pos = V(st.Position) + Vector3.up * 0.9f; return true; }
            var desk = layout.GetDesk(id);
            if (desk != null) { pos = V(desk.Position) + Vector3.up * 0.76f; return true; }
            pos = Vector3.zero;
            return false;
        }

        public static Vector3 V(Vec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            var cs = ClientState.I;
            var view = cs != null ? cs.View : null;
            double now = cs != null ? cs.Now : Time.timeAsDouble;
            if (view == null) view = AttractView();

            _seen.Clear();
            foreach (var item in view.Items)
            {
                if (item.Location == ItemLocationKind.Archived) continue;
                var iv = GetVisual(item);
                if (iv == null) continue;
                _seen.Add(item.Id);
                if (!iv.gameObject.activeSelf) { iv.gameObject.SetActive(true); iv.Placed = false; }
                iv.ApplyState(item, view);
                PlaceItem(iv, item, view);
            }
            foreach (var kv in _items)
                if (!_seen.Contains(kv.Key) && kv.Value.gameObject.activeSelf) kv.Value.gameObject.SetActive(false);

            if (Desks != null)
                foreach (var dv in Desks)
                {
                    DeskView d = null;
                    foreach (var x in view.Desks) if (x.Index == dv.DeskIndex) d = x;
                    if (d != null) dv.Apply(d);
                    dv.WantOpen = DrawerBusy(view, EntityIds.DeskDrawer(dv.DeskIndex));
                }
            if (Stations != null)
                foreach (var sv in Stations)
                {
                    StationView s = null;
                    foreach (var x in view.Stations) if (x.Id == sv.StationId) s = x;
                    sv.Apply(view, s, now);
                }
            if (Manager != null) Manager.Apply(cs != null ? cs.View : null, now);
            UpdateAlarm(cs != null ? cs.View : null, now);
        }

        bool DrawerBusy(PlayerView view, string drawerId)
        {
            foreach (var p in view.Players)
                if (p.ActivityTargetId == drawerId && p.Activity == PublicActivity.Rummaging) return true;
            var me = view.Me != null ? view.Me.ActiveInteraction : null;
            return me != null && me.TargetId == drawerId;
        }

        void UpdateAlarm(PlayerView v, double now)
        {
            bool on = v != null && (v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection) &&
                      v.Manager.State != ManagerState.InOffice && v.Manager.State != ManagerState.Returning;
            if (AlarmLights != null) foreach (var l in AlarmLights) if (l != null) l.enabled = on;
            if (AlarmRotor != null && on) AlarmRotor.Rotate(0, 360f * Time.deltaTime, 0, Space.World);
            Sfx.Loop("alarm", on && v.Manager.State == ManagerState.Warning ? "siren" : null, Vector3.zero);
        }

        ItemVisual GetVisual(ItemView item)
        {
            ItemVisual iv;
            if (_items.TryGetValue(item.Id, out iv)) return iv;
            ItemVisual prefab;
            if (!_prefabByVariant.TryGetValue(item.Variant ?? "", out prefab)) return null;
            iv = Instantiate(prefab, transform);
            iv.ItemId = item.Id;
            iv.name = item.Id;
            _items[item.Id] = iv;
            return iv;
        }

        void PlaceItem(ItemVisual iv, ItemView item, PlayerView view)
        {
            switch (item.Location)
            {
                case ItemLocationKind.Carried:
                {
                    var a = PlayerAvatar.BySlot(item.CarrierId);
                    if (a == null || a.CarrySocket == null) { iv.gameObject.SetActive(false); return; }
                    iv.transform.localScale = Vector3.one;
                    iv.MoveTo(a.CarrySocket.position, a.CarrySocket.rotation, true);
                    return;
                }
                case ItemLocationKind.Floor:
                    iv.transform.localScale = Vector3.one;
                    iv.MoveTo(V(item.FloorPosition), Quaternion.Euler(0, item.FloorYaw, 0) * Quaternion.Euler(0, 0, item.Kind == ItemKind.Report || item.Kind == ItemKind.Clutter ? 0 : 0), false);
                    return;
                case ItemLocationKind.Slot:
                {
                    Vector3 pos; Quaternion rot; float scale;
                    SlotPose(item, view, out pos, out rot, out scale);
                    iv.transform.localScale = Vector3.one * scale;
                    iv.MoveTo(pos, rot, false);
                    return;
                }
            }
        }

        void SlotPose(ItemView item, PlayerView view, out Vector3 pos, out Quaternion rot, out float scale)
        {
            scale = 1f;
            var layout = GameConfig.Layout;
            var slot = layout.GetSlot(item.SlotId);
            Transform a = Anchor(item.SlotId);
            int i = Mathf.Max(0, item.SlotIndex);
            if (a == null || slot == null)
            {
                pos = slot != null ? V(slot.Position) : Vector3.zero;
                rot = Quaternion.identity;
                return;
            }
            Vector3 local;
            switch (slot.Kind)
            {
                case SlotKind.DeskSurface: local = new Vector3(-0.28f + 0.28f * i, 0f, 0f); break;
                case SlotKind.Drawer: local = new Vector3(-0.1f + 0.1f * i, 0f, 0f); scale = 0.55f; break;
                case SlotKind.NameplateHolder: local = Vector3.zero; break;
                case SlotKind.HidingSpot: local = new Vector3(0f, 0f, -0.15f + 0.3f * i); scale = 0.8f; break;
                case SlotKind.MugShelf: local = new Vector3(-0.18f + 0.12f * i, 0.02f, 0f); break;
                case SlotKind.PrinterTray: local = new Vector3(0f, 0.012f * i, 0f); break;
                case SlotKind.DeliveryTray:
                    local = item.Kind == ItemKind.Report
                        ? new Vector3(-0.24f, 0.02f + 0.012f * CountBefore(view, item, ItemKind.Report), 0f)
                        : new Vector3(0.25f + 0.11f * Mathf.Cos(i * 1.7f), 0.01f, 0.11f * Mathf.Sin(i * 1.7f));
                    break;
                case SlotKind.JunkBin: local = new Vector3(-0.22f + 0.15f * (i % 4), 0.08f + 0.04f * (i / 4), (i % 2 == 0 ? -0.08f : 0.08f)); break;
                case SlotKind.LostAndFound: local = new Vector3(-0.18f + 0.18f * (i % 3), 0.02f, (i < 3 ? -0.08f : 0.08f)); scale = 0.85f; break;
                case SlotKind.SprayRack: local = new Vector3(-0.15f + 0.3f * i, 0f, 0f); break;
                default: local = Vector3.zero; break;
            }
            pos = a.TransformPoint(local);
            rot = a.rotation * Quaternion.Euler(0, (i * 37) % 30 - 15 + (item.Kind == ItemKind.Mug ? 0 : 0), 0);
            if (slot.Kind == SlotKind.NameplateHolder || slot.Kind == SlotKind.MugShelf || slot.Kind == SlotKind.SprayRack) rot = a.rotation;
        }

        static int CountBefore(PlayerView view, ItemView item, ItemKind kind)
        {
            int n = 0;
            foreach (var x in view.Items)
                if (x.SlotId == item.SlotId && x.Kind == kind && x.Location == ItemLocationKind.Slot && x.SlotIndex < item.SlotIndex) n++;
            return n;
        }

        /// <summary>Office as it looks before a match (menu backdrop): items in their initial places.</summary>
        PlayerView AttractView()
        {
            if (_attractView != null) return _attractView;
            var layout = GameConfig.Layout;
            var v = new PlayerView();
            var counts = new Dictionary<string, int>();
            foreach (var def in layout.Items)
            {
                string slot = def.InitialSlotId ?? (def.Kind == ItemKind.Nameplate ? EntityIds.DeskNameplate(def.NameplateOf) : null);
                if (slot == null) continue;
                int idx;
                counts.TryGetValue(slot, out idx);
                counts[slot] = idx + 1;
                v.Items.Add(new ItemView { Id = def.Id, Kind = def.Kind, Variant = def.Variant, Location = ItemLocationKind.Slot, SlotId = slot, SlotIndex = idx, NameplateOf = def.NameplateOf });
            }
            foreach (var d in layout.Desks) v.Desks.Add(new DeskView { Index = d.Index, Id = d.Id, AssignedOwner = d.Index, DisplayedNameplateOf = d.Index, Dirty = d.Index == 2 });
            v.Manager = new ManagerView { State = ManagerState.InOffice, Position = layout.ManagerHome, Yaw = layout.ManagerHomeYaw };
            _attractView = v;
            return v;
        }
    }
}
