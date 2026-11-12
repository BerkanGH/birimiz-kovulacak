using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 bot talk in the meeting room (docs/V5_BOT_TALK.md): a bot's chat line also comes up as a small speech bubble over
    /// its head for a few seconds (it mumbles and its talk animation runs meanwhile). During the final defence the
    /// defending bot's line is a subtitle over its standing character until its turn ends; when it is out of view the
    /// subtitle sits at the bottom of the screen instead.
    /// </summary>
    public sealed class MeetingSubtitles
    {
        const int Slots = 8;
        readonly VisualElement[] _box = new VisualElement[Slots];
        readonly Label[] _who = new Label[Slots], _text = new Label[Slots];
        readonly float[] _until = new float[Slots], _from = new float[Slots];
        readonly bool[] _defence = new bool[Slots];
        readonly string[] _full = new string[Slots];

        const float CharsPerSecond = 40f;

        public MeetingSubtitles(VisualElement parent)
        {
            for (int i = 0; i < Slots; i++)
            {
                var b = Box("mt-sub").NoPick().AddTo(parent);
                _who[i] = Bold("", "mt-sub-who").NoPick().AddTo(b);
                _text[i] = Text("", "mt-sub-text").NoPick().AddTo(b);
                b.Show(false);
                _box[i] = b;
            }
        }

        /// <summary>A bot said <paramref name="text"/> in the meeting chat. True when the bubble is on screen (its head is in
        /// view, or it is the defence subtitle, which always shows).</summary>
        public bool Say(LineClient lc, LineView v, int slot, string text)
        {
            if (slot < 0 || slot >= Slots || lc == null || v == null) return false;
            bool defence = v.Phase == LinePhase.FinalDefense && v.Final != null && v.Final.Defender == slot;
            _defence[slot] = defence;
            _full[slot] = text ?? "";
            _from[slot] = Time.unscaledTime;
            _until[slot] = Time.unscaledTime + (defence ? 14f : Mathf.Clamp(2.5f + text.Length * 0.05f, 4f, 7f));
            var p = lc.Player(slot);
            _who[slot].SetText(p != null ? p.Name : Name(slot));
            var colour = PlayerColor(lc.ColorOf(slot));
            _who[slot].style.color = defence ? Color.Lerp(colour, Color.white, 0.4f) : Color.Lerp(colour, new Color(0.12f, 0.16f, 0.27f), 0.35f);
            _box[slot].EnableInClassList("mt-sub-defence", defence);
            _box[slot].Show(true);
            UiMotion.FadeIn(_box[slot], 0.15f);
            Vector3 head;
            var room = RoomsPresenter.Instance != null ? RoomsPresenter.Instance.Meeting : null;
            Vector3? at = room != null && room.TryGetHead(slot, Vector3.zero, out head) ? head : (Vector3?)null;
            BotSpeech.Speak(slot, text, at);
            if (defence) return true;
            var cam = RoomsPresenter.Instance != null ? RoomsPresenter.Instance.PovCamera : null;
            if (cam == null || !at.HasValue) return false;
            var sp = cam.WorldToScreenPoint(at.Value);
            return sp.z > 0.2f && sp.x > Screen.width * 0.04f && sp.x < Screen.width * 0.96f && sp.y > Screen.height * 0.12f && sp.y < Screen.height * 0.98f;
        }

        public void Clear()
        {
            for (int i = 0; i < Slots; i++) { _until[i] = 0f; _box[i].Show(false); }
        }

        /// <summary>Follows the heads (call every frame while the meeting room is on screen).</summary>
        public void Tick(LineView v, VisualElement root, bool hidden)
        {
            var rooms = RoomsPresenter.Instance;
            var cam = rooms != null ? rooms.PovCamera : null;
            var room = rooms != null && rooms.InMeeting ? rooms.Meeting : null;
            var panel = root.panel;
            float now = Time.unscaledTime;
            for (int i = 0; i < Slots; i++)
            {
                if (_until[i] <= 0f) continue;
                // the defence subtitle stays for the speaker's turn
                bool over = now > _until[i] || (_defence[i] && (v == null || v.Phase != LinePhase.FinalDefense || v.Final == null || v.Final.Defender != i));
                if (over || hidden || room == null || cam == null || panel == null)
                {
                    if (over) _until[i] = 0f;
                    _box[i].Show(false);
                    continue;
                }
                _box[i].Show(true);
                int n = Mathf.Clamp(Mathf.FloorToInt((now - _from[i]) * CharsPerSecond), 0, _full[i].Length);
                _text[i].SetText(n >= _full[i].Length ? _full[i] : _full[i].Substring(0, n));
                Vector3 head;
                bool placed = false;
                if (room.TryGetHead(i, cam.transform.position, out head))
                {
                    var sp = cam.WorldToScreenPoint(head + Vector3.up * 0.18f);
                    float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
                    var size = panel.visualTree.layout.size;
                    if (sp.z > 0.2f && sp.x > sw * 0.04f && sp.x < sw * 0.96f && sp.y > sh * 0.12f && sp.y < sh * 0.98f)
                    {
                        float x = sp.x * size.x / sw, y = (sh - sp.y) * size.y / sh;
                        // keep the whole bubble on screen (it is centred over the head)
                        float half = Mathf.Max(60f, _box[i].layout.width * 0.5f);
                        x = Mathf.Clamp(x, half + 16f, size.x - half - 16f);
                        y = Mathf.Max(y, _box[i].layout.height + 150f);
                        _box[i].EnableInClassList("mt-sub-screen", false);
                        _box[i].style.left = x;
                        _box[i].style.top = y;
                        _box[i].style.bottom = StyleKeyword.Auto;
                        placed = true;
                    }
                }
                if (!placed)
                {
                    // out of view: the defence still reads as a subtitle at the bottom; other lines are in the chat toast
                    if (!_defence[i]) { _box[i].Show(false); continue; }
                    _box[i].EnableInClassList("mt-sub-screen", true);
                    _box[i].style.left = StyleKeyword.Null;
                    _box[i].style.top = StyleKeyword.Null;
                    _box[i].style.bottom = StyleKeyword.Null;
                }
            }
        }
    }
}
