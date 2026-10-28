using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Minimal IMGUI emote wheel, used only until the UI module draws its own (IFeelUi.DrawsEmoteWheel). The component
    /// is enabled only while the wheel is open, so OnGUI costs nothing the rest of the time.
    /// </summary>
    public sealed class EmoteWheelGui : MonoBehaviour
    {
        static readonly string[] Keys = { "emote.wave", "emote.point", "emote.facepalm", "emote.shrug", "emote.laugh" };
        static readonly string[] Fallback = { "El salla", "Göster", "Facepalm", "Omuz silk", "Gül" };

        string[] _labels;
        GUIStyle _normal, _selected, _dot;

        void OnGUI()
        {
            var lpc = LocalPlayerController.I;
            if (lpc == null || !lpc.EmoteWheelOpen) return;
            if (_labels == null)
            {
                _labels = new string[Keys.Length];
                for (int i = 0; i < Keys.Length; i++) _labels[i] = (i + 1) + "  " + (Loc.Has(Keys[i]) ? Loc.T(Keys[i]) : Fallback[i]);
                _normal = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
                _normal.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
                _selected = new GUIStyle(_normal) { fontStyle = FontStyle.Bold, fontSize = 19 };
                _selected.normal.textColor = new Color(1f, 0.85f, 0.25f, 1f);
                _dot = new GUIStyle(GUI.skin.box);
            }
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float r = Mathf.Min(Screen.height * 0.17f, 160f);
            int sel = lpc.EmoteWheelSelection;
            for (int i = 0; i < _labels.Length; i++)
            {
                float a = i * (360f / _labels.Length) * Mathf.Deg2Rad;
                var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r;
                bool s = i == sel;
                float w = s ? 170f : 150f, h = s ? 42f : 36f;
                GUI.Box(new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h), _labels[i], s ? _selected : _normal);
            }
            Vector2 cur = lpc.EmoteWheelCursor * (r / LocalPlayerController.EmoteWheelRadius);
            GUI.Box(new Rect(c.x + cur.x - 6f, c.y - cur.y - 6f, 12f, 12f), GUIContent.none, _dot);
        }
    }
}
