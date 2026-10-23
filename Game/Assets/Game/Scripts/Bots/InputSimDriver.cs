using System.Collections;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Test driver for the HUMAN control path (-inputsim): injects real keyboard events through the Input System so
    /// LocalPlayerController's movement, focus selection, prompts and E-key requests are exercised end to end.
    /// It only steers the camera yaw (as a player would with the mouse). Script: take a mug, brew coffee, deliver it.
    /// </summary>
    public sealed class InputSimDriver : MonoBehaviour
    {
        Keyboard _kb;

        IEnumerator Start()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            // A dedicated device: the real keyboard may already be disabled because this window is not focused.
            _kb = InputSystem.AddDevice<Keyboard>("InputSimKeyboard");
            InputSystem.EnableDevice(_kb);
            while (ClientState.I.View == null || ClientState.I.View.Phase != MatchPhase.Work1) yield return null;
            yield return new WaitForSeconds(1.5f);
            MatchLog.Write("inputsim: start");

            yield return Step("take-mug", EntityIds.MugShelf, () => CarryingKind(ItemKind.Mug));
            yield return Step("brew", EntityIds.CoffeeMachine, () => CarryingFullMug(), 14f);
            float before = ClientState.I.View.Quota.Points;
            yield return Step("deliver", EntityIds.DeliveryPoint, () => !CarryingKind(ItemKind.Mug), 10f);
            MatchLog.Write("inputsim: quota " + before + " -> " + ClientState.I.View.Quota.Points);
            yield return Press(Key.R, 0.15f);
            yield return new WaitForSeconds(1f);
            var me = ClientState.I.Me;
            MatchLog.Write("inputsim: work pose " + (me != null && me.WorkPose ? "on" : "off"));
            MatchLog.Write("inputsim: done");
            Release();
        }

        bool CarryingKind(ItemKind k)
        {
            var v = ClientState.I.View; var me = ClientState.I.Me;
            if (v == null || me == null || string.IsNullOrEmpty(me.CarryingItemId)) return false;
            foreach (var it in v.Items) if (it.Id == me.CarryingItemId) return it.Kind == k;
            return false;
        }

        bool CarryingFullMug()
        {
            var v = ClientState.I.View; var me = ClientState.I.Me;
            if (v == null || me == null) return false;
            foreach (var it in v.Items) if (it.Id == me.CarryingItemId) return it.Kind == ItemKind.Mug && it.Fill == MugFill.Full;
            return false;
        }

        IEnumerator Step(string name, string targetId, System.Func<bool> done, float timeout = 20f)
        {
            float until = Time.time + timeout;
            Vector3 target;
            OfficeWorld.I.TryTargetPosition(targetId, out target);
            var avatar = PlayerAvatar.Local;
            // Walk with W while steering the camera (mouse) towards the target.
            while (Time.time < until && avatar != null)
            {
                Vector3 d = target - avatar.transform.position; d.y = 0;
                if (d.magnitude < 1.25f) break;
                CameraRig.I.Yaw = Mathf.MoveTowardsAngle(CameraRig.I.Yaw, Quaternion.LookRotation(d).eulerAngles.y, 360f * Time.deltaTime);
                Hold(Key.W);
                yield return null;
            }
            Release();
            // Face it, then press E (or retry) until the expected state appears.
            float pressAt = 0;
            while (Time.time < until && !done())
            {
                Vector3 d = target - avatar.transform.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f) CameraRig.I.Yaw = Quaternion.LookRotation(d).eulerAngles.y;
                if (Time.time > pressAt && (ClientState.I.View.Me.ActiveInteraction == null || ClientState.I.View.Me.ActiveInteraction.Verb == ActionVerb.None))
                {
                    var lpc = LocalPlayerController.I;
                    MatchLog.Write("inputsim: " + name + " focus=" + (lpc != null ? lpc.FocusedId : "?") + " options=" + (lpc != null && lpc.Options.Count > 0 ? lpc.Options[0].LabelKey + "/" + lpc.Options[0].BlockedBy : "none"));
                    yield return Press(Key.E, 0.1f);
                    pressAt = Time.time + 1.5f;
                }
                yield return null;
            }
            MatchLog.Write("inputsim: step " + name + (done() ? " OK" : " FAILED"));
        }

        void Hold(Key k) { InputSystem.QueueStateEvent(_kb, new KeyboardState(k)); }
        void Release() { InputSystem.QueueStateEvent(_kb, new KeyboardState()); }

        IEnumerator Press(Key k, float seconds)
        {
            Hold(k);
            yield return new WaitForSeconds(seconds);
            Release();
            yield return null;
        }
    }
}
