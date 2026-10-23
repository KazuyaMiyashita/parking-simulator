using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 操作の割り当てをここ1か所にまとめる。Joy-Con とキーボードは同じ操作ができ、違いはスティックの角度(連続値)だけ。
    ///
    /// | 操作                     | Joy-Con             | キーボード        |
    /// |--------------------------|---------------------|-------------------|
    /// | アクセル / ブレーキ      | A / B               | W / S             |
    /// | ハンドル                 | 左スティック左右    | A / D             |
    /// | ハンドル固定(切替)     | 左スティック押し込み | Space             |
    /// | シフト P→R→N→D / 逆     | R / L               | E / Q             |
    /// | 見渡す                   | 右スティック        | 矢印キー          |
    /// | 視点切替                 | X                   | V                 |
    /// | ズームイン / アウト      | ZR / ZL(同時で元に) | R / F(同時で元に) |
    /// | マップ / メニュー        | − / +               | M / Esc           |
    /// | (画面内)選ぶ・決定・戻る | 左スティック・十字 / A / B | 矢印・WASD / Enter / Backspace |
    /// </summary>
    public static class Controls
    {
        // ---------------------------------------------------------------- 運転
        public static bool Accel => Pad.Held(JoyButton.A) || Input.GetKey(KeyCode.W);
        public static bool Brake => Pad.Held(JoyButton.B) || Input.GetKey(KeyCode.S);

        /// <summary>ハンドルの入力(-1=左いっぱい ~ +1=右いっぱい)。スティックは倒した角度、キーは押している間 ±1</summary>
        public static float Steer
        {
            get
            {
                float s = Axis(Pad.LeftStick.x);
                if (s != 0f) return s;
                return (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            }
        }

        public static bool SteerHoldToggle => Pad.Down(JoyButton.LStick) || Input.GetKeyDown(KeyCode.Space);

        /// <summary>D 側へ1段(P → R → N → D)</summary>
        public static bool ShiftUpDown => Pad.Down(JoyButton.R) || Input.GetKeyDown(KeyCode.E);
        public static bool ShiftUpHeld => Pad.Held(JoyButton.R) || Input.GetKey(KeyCode.E);
        /// <summary>P 側へ1段</summary>
        public static bool ShiftDownDown => Pad.Down(JoyButton.L) || Input.GetKeyDown(KeyCode.Q);
        public static bool ShiftDownHeld => Pad.Held(JoyButton.L) || Input.GetKey(KeyCode.Q);

        /// <summary>見渡す(x=右, y=上)</summary>
        public static Vector2 Look
        {
            get
            {
                Vector2 v = Pad.RightStick;
                if (Input.GetKey(KeyCode.RightArrow)) v.x += 1f;
                if (Input.GetKey(KeyCode.LeftArrow)) v.x -= 1f;
                if (Input.GetKey(KeyCode.UpArrow)) v.y += 1f;
                if (Input.GetKey(KeyCode.DownArrow)) v.y -= 1f;
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        public static bool ViewCycle => Pad.Down(JoyButton.X) || Input.GetKeyDown(KeyCode.V);
        public static bool ZoomInHeld => Pad.Held(JoyButton.ZR) || Input.GetKey(KeyCode.R);
        public static bool ZoomOutHeld => Pad.Held(JoyButton.ZL) || Input.GetKey(KeyCode.F);
        public static bool MapToggle => Pad.Down(JoyButton.Minus) || Input.GetKeyDown(KeyCode.M);
        public static bool MenuToggle => Pad.Down(JoyButton.Plus) || Input.GetKeyDown(KeyCode.Escape);

        // ---------------------------------------------------------------- 画面内(メニュー・マップ)
        public static bool Confirm => Pad.Down(JoyButton.A) || Input.GetKeyDown(KeyCode.Return) ||
                                      Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
        public static bool Back => Pad.Down(JoyButton.B) || Input.GetKeyDown(KeyCode.Backspace);

        /// <summary>このフレームの選択の移動(-1/0/+1)。押しっぱなしは一定間隔でくり返す。y は上が +1</summary>
        public static int NavX { get; private set; }
        public static int NavY { get; private set; }

        static Vector2Int held;
        static float repeatAt;

        /// <summary>Pad.Poll の後に毎フレーム呼ぶ(一時停止中も動くよう実時間で数える)</summary>
        public static void Update()
        {
            Vector2 v = Pad.LeftStick;
            if (Pad.Held(JoyButton.Up) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) v.y += 1f;
            if (Pad.Held(JoyButton.Down) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v.y -= 1f;
            if (Pad.Held(JoyButton.Right) || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) v.x += 1f;
            if (Pad.Held(JoyButton.Left) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) v.x -= 1f;

            var dir = Vector2Int.zero;
            if (Mathf.Abs(v.y) >= 0.5f && Mathf.Abs(v.y) >= Mathf.Abs(v.x)) dir.y = v.y > 0 ? 1 : -1;
            else if (Mathf.Abs(v.x) >= 0.5f) dir.x = v.x > 0 ? 1 : -1;

            NavX = NavY = 0;
            float now = Time.unscaledTime;
            if (dir == Vector2Int.zero) { held = dir; return; }
            bool fresh = dir != held;
            if (!fresh && now < repeatAt) return;
            repeatAt = now + (fresh ? 0.4f : 0.12f);
            held = dir;
            NavX = dir.x; NavY = dir.y;
        }

        /// <summary>
        /// スティックの軸ごとの遊び(上に倒したつもりでハンドルが少し回る、などを防ぐ)。
        /// 外周の枠は丸いので、真横に倒しきったつもりでも少し斜めだと 1 に届かない。0.9 以上は倒しきりとして 1 にする
        /// </summary>
        static float Axis(float v)
        {
            const float dead = 0.2f, full = 0.9f;
            float a = Mathf.Abs(v);
            return a < dead ? 0f : Mathf.Sign(v) * Mathf.Clamp01((a - dead) / (full - dead));
        }

        // ---------------------------------------------------------------- 表示用
        /// <summary>操作説明の表(操作, Joy-Con, キーボード)</summary>
        public static readonly string[,] Table =
        {
            { "アクセル / ブレーキ", "A / B", "W / S" },
            { "ハンドル", "左スティック 左右", "A / D" },
            { "ハンドル固定(もう一度で解除)", "左スティック押し込み", "Space" },
            { "シフト P→R→N→D / D→N→R→P", "R / L", "E / Q" },
            { "見渡す", "右スティック", "矢印キー" },
            { "視点切替", "X", "V" },
            { "ズームイン / アウト(同時押しで元に戻す)", "ZR / ZL", "R / F" },
            { "マップ", "−", "M" },
            { "メニュー", "+", "Esc" },
            { "(画面内)選ぶ / 決定 / 戻る", "左スティック・十字 / A / B", "矢印 / Enter / Backspace" },
        };
    }
}
