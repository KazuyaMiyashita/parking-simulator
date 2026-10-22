using UnityEngine;
using UnityEngine.InputSystem;

namespace ParkingSim
{
    /// <summary>
    /// コントローラー入力のまとめ役。Joy-Con (L)+(R) を主対象にし、Input System が認識するゲームパッド
    /// (プロコン等)も同じボタン配置(A=右, B=下, X=上, Y=左)で読む。
    /// </summary>
    public static class Pad
    {
        static uint cur, prev;
        public static Vector2 LeftStick, RightStick;
        public static JoyConHID JoyLeft, JoyRight;
        public static bool GamepadConnected;

        public static bool Connected => JoyLeft != null || JoyRight != null || GamepadConnected;

        public static bool Held(JoyButton b) => (cur & (1u << (int)b)) != 0;
        public static bool Down(JoyButton b) => (cur & ~prev & (1u << (int)b)) != 0;

        public static void Poll()
        {
            prev = cur;
            cur = 0;
            LeftStick = RightStick = Vector2.zero;
            JoyLeft = JoyRight = null;
            GamepadConnected = false;

            foreach (var d in InputSystem.devices)
            {
                if (d is JoyConHID jc && jc.Alive)
                {
                    cur |= jc.buttons;
                    if (jc.isLeft) { JoyLeft = jc; LeftStick = jc.stick; }
                    else { JoyRight = jc; RightStick = jc.stick; }
                }
            }

            var gp = Gamepad.current;
            if (gp != null && gp.added)
            {
                GamepadConnected = true;
                void Map(UnityEngine.InputSystem.Controls.ButtonControl c, JoyButton b) { if (c.isPressed) cur |= 1u << (int)b; }
                Map(gp.buttonEast, JoyButton.A); Map(gp.buttonSouth, JoyButton.B);
                Map(gp.buttonNorth, JoyButton.X); Map(gp.buttonWest, JoyButton.Y);
                Map(gp.leftShoulder, JoyButton.L); Map(gp.rightShoulder, JoyButton.R);
                Map(gp.leftTrigger, JoyButton.ZL); Map(gp.rightTrigger, JoyButton.ZR);
                Map(gp.startButton, JoyButton.Capture); Map(gp.selectButton, JoyButton.Minus);
                Map(gp.leftStickButton, JoyButton.LStick); Map(gp.rightStickButton, JoyButton.RStick);
                Map(gp.dpad.up, JoyButton.Up); Map(gp.dpad.down, JoyButton.Down);
                Map(gp.dpad.left, JoyButton.Left); Map(gp.dpad.right, JoyButton.Right);
                Vector2 ls = gp.leftStick.ReadValue(), rs = gp.rightStick.ReadValue();
                if (ls.sqrMagnitude > LeftStick.sqrMagnitude) LeftStick = ls;
                if (rs.sqrMagnitude > RightStick.sqrMagnitude) RightStick = rs;
            }
        }

        public static string Status()
        {
            string L = JoyLeft != null ? (JoyLeft.fullMode ? "<color=#7f7>●</color>" : "<color=#fc6>●</color>") : "<color=#666>○</color>";
            string R = JoyRight != null ? (JoyRight.fullMode ? "<color=#7f7>●</color>" : "<color=#fc6>●</color>") : "<color=#666>○</color>";
            return $"Joy-Con L{L} R{R}" + (GamepadConnected ? "  ゲームパッド<color=#7f7>●</color>" : "");
        }
    }

    /// <summary>他のスクリプトより先に毎フレーム Pad を更新する</summary>
    [DefaultExecutionOrder(-1000)]
    public class PadUpdater : MonoBehaviour
    {
        void Update() => Pad.Poll();
    }
}
