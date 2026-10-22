using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace ParkingSim
{
    /// <summary>Joy-Con のボタン(L/R 共通のビット割り当て)</summary>
    public enum JoyButton
    {
        A, B, X, Y, R, ZR, L, ZL, Plus, Minus, LStick, RStick, Home, Capture,
        Up, Down, Left, Right, SL, SR,
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct JoyConDummyState : IInputStateTypeInfo
    {
        public static FourCC Format => new FourCC('J', 'C', 'O', 'N');
        public FourCC format => Format;

        [InputControl(name = "dummy", layout = "Button", bit = 0)]
        [FieldOffset(0)] public uint dummy;
    }

    /// <summary>Joy-Con へ送る HID 出力レポート(Bluetooth: レポートID込み 49 バイト)</summary>
    [StructLayout(LayoutKind.Explicit, Size = kSize)]
    unsafe struct JoyConOutputCommand : IInputDeviceCommandInfo
    {
        public static FourCC Type => new FourCC('H', 'I', 'D', 'O');
        public FourCC typeStatic => Type;
        const int kBase = 8; // InputDeviceCommand のヘッダサイズ
        public const int kReportSize = 49;
        public const int kSize = kBase + kReportSize;

        [FieldOffset(0)] public InputDeviceCommand baseCommand;
        [FieldOffset(kBase)] public fixed byte report[kReportSize];
    }

    /// <summary>
    /// Joy-Con (L) / (R) の HID ドライバー。
    /// 接続時にサブコマンドでフル入力モード(レポート 0x30)へ切り替え、生レポートからボタンとアナログスティックを読む。
    /// プロトコル: https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering
    /// </summary>
    [InputControlLayout(stateType = typeof(JoyConDummyState), displayName = "Joy-Con")]
    public class JoyConHID : InputDevice, IInputStateCallbackReceiver
    {
        public const int VendorId = 0x057E, ProductLeft = 0x2006, ProductRight = 0x2007;

        public bool isLeft;
        public uint buttons;              // JoyButton のビット
        public Vector2 stick;             // -1~1(デッドゾーン適用後)
        public bool fullMode;             // 0x30 レポートを受信中
        public int reportCount;
        public long lastCommandResult;
        byte lastReportId;

        Vector2 center, range = new Vector2(1300, 1300);
        int centerSamples;
        double lastReport = -10, lastFull = -10, lastCommand = -10;
        bool lightsSent;
        byte packet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
            InputSystem.RegisterLayout<JoyConHID>(matches: new InputDeviceMatcher()
                .WithInterface("HID")
                .WithCapability("vendorId", VendorId)
                .WithCapability("productId", ProductLeft));
            InputSystem.RegisterLayoutMatcher<JoyConHID>(new InputDeviceMatcher()
                .WithInterface("HID")
                .WithCapability("vendorId", VendorId)
                .WithCapability("productId", ProductRight));
        }

        public bool Alive => Time.realtimeSinceStartupAsDouble - lastReport < 1.0;

        protected override void OnAdded()
        {
            base.OnAdded();
            int pid = 0;
            try { pid = UnityEngine.InputSystem.HID.HID.HIDDeviceDescriptor.FromJson(description.capabilities).productId; }
            catch { /* 記述子が読めない場合は製品名で判断 */ }
            isLeft = pid == ProductLeft || (pid == 0 && (description.product ?? "").Contains("(L)"));
            Debug.Log($"[JoyCon] 接続: {description.product} pid=0x{pid:X4} {(isLeft ? "L" : "R")}");
        }

        // ---------------------------------------------------------------- 出力(サブコマンド)
        unsafe long SendSubcommand(byte id, params byte[] args)
        {
            var cmd = new JoyConOutputCommand { baseCommand = new InputDeviceCommand(JoyConOutputCommand.Type, JoyConOutputCommand.kSize) };
            byte[] rumbleNeutral = { 0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40 };
            cmd.report[0] = 0x01;
            cmd.report[1] = (byte)(packet++ & 0x0F);
            for (int i = 0; i < 8; i++) cmd.report[2 + i] = rumbleNeutral[i];
            cmd.report[10] = id;
            for (int i = 0; i < args.Length && 11 + i < JoyConOutputCommand.kReportSize; i++) cmd.report[11 + i] = args[i];
            lastCommandResult = ExecuteCommand(ref cmd);
            if (lastCommandResult < 0) Debug.LogWarning($"[JoyCon] サブコマンド 0x{id:X2} の送信に失敗 ({lastCommandResult})");
            return lastCommandResult;
        }

        public void OnNextUpdate()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - lastCommand < 0.25) return;
            if (now - lastFull > 1.0)
            {
                // フル入力モード(0x30)にする。届くまで繰り返す
                lastCommand = now;
                lightsSent = false;
                SendSubcommand(0x03, 0x30);
            }
            else if (!lightsSent)
            {
                // プレイヤーランプを点灯(接続できた合図)
                lastCommand = now;
                lightsSent = true;
                SendSubcommand(0x30, 0x01);
            }
        }

        // ---------------------------------------------------------------- 入力
        public unsafe void OnStateEvent(InputEventPtr eventPtr)
        {
            if (eventPtr.type != StateEvent.Type) return;
            var se = StateEvent.From(eventPtr);
            if (se->stateFormat != new FourCC('H', 'I', 'D')) return;
            byte* p = (byte*)se->state;
            uint size = se->stateSizeInBytes;
            if (size < 1) return;

            double now = Time.realtimeSinceStartupAsDouble;
            byte id = p[0];
            if (reportCount == 0 || id != lastReportId) Debug.Log($"[JoyCon] {(isLeft ? "L" : "R")} レポート 0x{id:X2} ({size} bytes)");
            lastReportId = id;
            if ((id == 0x30 || id == 0x31 || id == 0x21) && size >= 12)
            {
                lastReport = now;
                if (id != 0x21) { lastFull = now; fullMode = true; }
                reportCount++;
                ParseFull(p);
            }
            else if (id == 0x3F && size >= 4)
            {
                lastReport = now;
                fullMode = false;
                reportCount++;
                ParseSimple(p);
            }
        }

        public bool GetStateOffsetForEvent(InputControl control, InputEventPtr eventPtr, ref uint offset) => false;

        static uint Bit(JoyButton b) => 1u << (int)b;

        unsafe void ParseFull(byte* p)
        {
            byte r = p[3], s = p[4], l = p[5];
            uint b = 0;
            if (isLeft)
            {
                if ((l & 0x01) != 0) b |= Bit(JoyButton.Down);
                if ((l & 0x02) != 0) b |= Bit(JoyButton.Up);
                if ((l & 0x04) != 0) b |= Bit(JoyButton.Right);
                if ((l & 0x08) != 0) b |= Bit(JoyButton.Left);
                if ((l & 0x10) != 0) b |= Bit(JoyButton.SR);
                if ((l & 0x20) != 0) b |= Bit(JoyButton.SL);
                if ((l & 0x40) != 0) b |= Bit(JoyButton.L);
                if ((l & 0x80) != 0) b |= Bit(JoyButton.ZL);
            }
            else
            {
                if ((r & 0x01) != 0) b |= Bit(JoyButton.Y);
                if ((r & 0x02) != 0) b |= Bit(JoyButton.X);
                if ((r & 0x04) != 0) b |= Bit(JoyButton.B);
                if ((r & 0x08) != 0) b |= Bit(JoyButton.A);
                if ((r & 0x10) != 0) b |= Bit(JoyButton.SR);
                if ((r & 0x20) != 0) b |= Bit(JoyButton.SL);
                if ((r & 0x40) != 0) b |= Bit(JoyButton.R);
                if ((r & 0x80) != 0) b |= Bit(JoyButton.ZR);
            }
            if ((s & 0x01) != 0) b |= Bit(JoyButton.Minus);
            if ((s & 0x02) != 0) b |= Bit(JoyButton.Plus);
            if ((s & 0x04) != 0) b |= Bit(JoyButton.RStick);
            if ((s & 0x08) != 0) b |= Bit(JoyButton.LStick);
            if ((s & 0x10) != 0) b |= Bit(JoyButton.Home);
            if ((s & 0x20) != 0) b |= Bit(JoyButton.Capture);
            buttons = b;

            int o = isLeft ? 6 : 9;
            int x = p[o] | ((p[o + 1] & 0x0F) << 8);
            int y = (p[o + 1] >> 4) | (p[o + 2] << 4);
            UpdateStick(x, y);
        }

        void UpdateStick(int x, int y)
        {
            var raw = new Vector2(x, y);
            if (raw == Vector2.zero) return; // スティックのデータが無いレポート
            // 接続直後の数回で中立位置を記録(触らずに接続している前提)、範囲は実際の最大値に合わせて広げる
            if (centerSamples < 5) { center = centerSamples == 0 ? raw : Vector2.Lerp(center, raw, 0.5f); centerSamples++; }
            Vector2 d = raw - center;
            range.x = Mathf.Max(range.x, Mathf.Abs(d.x));
            range.y = Mathf.Max(range.y, Mathf.Abs(d.y));
            Vector2 v = new Vector2(d.x / range.x, d.y / range.y);
            const float dead = 0.15f;
            float m = v.magnitude;
            stick = m < dead ? Vector2.zero : v / m * Mathf.Clamp01((m - dead) / (1 - dead));
        }

        unsafe void ParseSimple(byte* p)
        {
            // 簡易モード(0x3F)。スティックは方向のみなので使わず、ボタンだけ読む
            byte b1 = p[1], b2 = p[2];
            uint b = 0;
            if (isLeft)
            {
                if ((b1 & 0x01) != 0) b |= Bit(JoyButton.Left);
                if ((b1 & 0x02) != 0) b |= Bit(JoyButton.Down);
                if ((b1 & 0x04) != 0) b |= Bit(JoyButton.Up);
                if ((b1 & 0x08) != 0) b |= Bit(JoyButton.Right);
                if ((b2 & 0x01) != 0) b |= Bit(JoyButton.Minus);
                if ((b2 & 0x04) != 0) b |= Bit(JoyButton.LStick);
                if ((b2 & 0x20) != 0) b |= Bit(JoyButton.Capture);
                if ((b2 & 0x40) != 0) b |= Bit(JoyButton.L);
                if ((b2 & 0x80) != 0) b |= Bit(JoyButton.ZL);
            }
            else
            {
                if ((b1 & 0x01) != 0) b |= Bit(JoyButton.A);
                if ((b1 & 0x02) != 0) b |= Bit(JoyButton.X);
                if ((b1 & 0x04) != 0) b |= Bit(JoyButton.B);
                if ((b1 & 0x08) != 0) b |= Bit(JoyButton.Y);
                if ((b2 & 0x02) != 0) b |= Bit(JoyButton.Plus);
                if ((b2 & 0x08) != 0) b |= Bit(JoyButton.RStick);
                if ((b2 & 0x10) != 0) b |= Bit(JoyButton.Home);
                if ((b2 & 0x40) != 0) b |= Bit(JoyButton.R);
                if ((b2 & 0x80) != 0) b |= Bit(JoyButton.ZR);
            }
            if ((b1 & 0x10) != 0) b |= Bit(JoyButton.SL);
            if ((b1 & 0x20) != 0) b |= Bit(JoyButton.SR);
            buttons = b;
            stick = Vector2.zero;
        }
    }
}
