using UnityEngine;

namespace ParkingSim
{
    public enum Gear { P, R, N, D }

    /// <summary>
    /// 低速域の駐車操作向け運動学モデル(後輪中心の二輪モデル)。
    /// FF・CVT のクリープ、ペダル、ステアリング、障害物/輪止めとの接触を扱う。
    /// </summary>
    public class CarController : MonoBehaviour
    {
        public OwnCarParts parts;

        [Header("状態")]
        public Vector3 rearAxle;
        public float yaw;
        public float speed;        // m/s(+前進)
        public float wheelDeg;     // ハンドル角(+右)
        public Gear gear = Gear.P;
        public float throttle, brake;
        public bool parkingBrake;
        public bool engineOn;

        [Header("設定")]
        public bool creepEnabled; // オートマのクリープ現象

        [Header("操作")]
        public bool inputEnabled = true;
        public bool mouseSteer;
        public float steerRate = 450f; // ハンドルを回す速さ(度/秒)

        [Header("記録")]
        public int collisions;
        public int curbHits;       // 縁石にタイヤが当たった回数(脱輪)
        public int directionChanges;
        public float travelled;
        public float driveTime;
        public bool touchingBody, touchingStop;

        public System.Action<string, Color> notify;
        public System.Action<Gear> gearChanged;
        public System.Action<bool> engineChanged;
        public System.Action<bool> parkingBrakeChanged;

        int lastMoveSign;
        float segmentLength;
        bool centering;
        bool padSteering;         // 最後にハンドルを回したのが右スティック(離すと中立へ戻す)
        int curbWheels;           // 縁石・歩道に乗っているタイヤの数
        readonly float[] wheelLift = new float[4]; // 段差で持ち上がったタイヤの高さ(見た目)
        float engineHold;         // エンジン始動/停止ボタンを長押ししている時間
        bool engineHoldArmed;
        const float EngineHoldTime = 0.5f;
        float spinDeg;
        int obstacleMask, stopMask, curbMask;
        static readonly Gear[] Order = { Gear.P, Gear.R, Gear.N, Gear.D };

        public float SteerDeg => wheelDeg / CarSpec.SteeringRatio;
        public Vector3 Center => CarSpec.CenterFromRear(rearAxle, yaw);
        public float SpeedKmh => speed * 3.6f;

        public void Init(OwnCarParts p)
        {
            parts = p;
            obstacleMask = Layers.Mask(Layers.Obstacle);
            stopMask = Layers.Mask(Layers.WheelStop);
            curbMask = Layers.Mask(Layers.Curb);
        }

        public void SetPose(Vector3 rear, float yawDeg)
        {
            rearAxle = new Vector3(rear.x, 0, rear.z);
            yaw = yawDeg;
            speed = 0;
            curbWheels = CurbWheels(rearAxle, yaw);
            UpdateLift(0f);
            ApplyTransform();
            UpdateVisuals();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (inputEnabled)
            {
                ReadInput(dt);
                driveTime += dt;
            }
            else
            {
                throttle = Mathf.MoveTowards(throttle, 0, 3f * dt);
            }

            Simulate(dt);
        }

        /// <summary>入力を読まずに dt 秒ぶん車両を進める(10ms 刻み)</summary>
        public void Simulate(float dt)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.01f));
            float h = dt / n;
            for (int i = 0; i < n; i++) Step(h);
            UpdateLift(dt);
            ApplyTransform();
            UpdateVisuals();
        }

        // ---------------------------------------------------------------- 入力
        void ReadInput(float dt)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            bool acc = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool brk = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool hard = Input.GetKey(KeyCode.Space);
            // Joy-Con(L) / ゲームパッドの十字: ▲ アクセル, ▼ ブレーキ(押している間だけ)
            acc |= Pad.Held(JoyButton.Up);
            brk |= Pad.Held(JoyButton.Down);

            // 右スティック: 左右でハンドルを回す
            float padSteer = PadAxis(Pad.RightStick.x);

            // ペダル(キーを押している長さで踏み込み量が増える)
            throttle = acc ? Mathf.MoveTowards(Mathf.Max(throttle, 0.05f), 1f, 0.6f * dt) : Mathf.MoveTowards(throttle, 0f, 3f * dt);
            if (hard) brake = 1f;
            else if (brk) brake = Mathf.MoveTowards(Mathf.Max(brake, 0.08f), 1f, 1.2f * dt);
            else brake = Mathf.MoveTowards(brake, 0f, 4f * dt);

            ReadEngineSwitch(dt);

            // ステアリング
            float maxW = CarSpec.MaxWheelDeg;
            if (Input.GetKeyDown(KeyCode.C)) centering = true;
            if (Input.GetKeyDown(KeyCode.M))
            {
                mouseSteer = !mouseSteer;
                notify?.Invoke(mouseSteer ? "マウスハンドル ON(マウスの左右位置=ハンドル角)" : "マウスハンドル OFF", Color.white);
            }
            if (left || right || padSteer != 0f) centering = false;
            if (padSteer != 0f) padSteering = true;
            else if (left || right || mouseSteer || centering) padSteering = false;
            if (mouseSteer)
            {
                float mx = Mathf.Clamp(Input.mousePosition.x / Mathf.Max(1, Screen.width), 0f, 1f) * 2f - 1f;
                float target = Mathf.Clamp(mx * maxW * 1.15f, -maxW, maxW);
                wheelDeg = Mathf.MoveTowards(wheelDeg, target, 1400f * dt);
            }
            else if (left || right)
            {
                float rate = steerRate * (shift ? 2f : 1f);
                wheelDeg += ((right ? 1f : 0f) - (left ? 1f : 0f)) * rate * dt;
            }
            else if (padSteer != 0f)
            {
                // スティックを倒した量に比例した速さでハンドルを回す
                wheelDeg += padSteer * steerRate * (shift ? 2f : 1.5f) * dt;
            }
            else if (padSteering && !Pad.Held(JoyButton.R))
            {
                // スティックを離すと手を離したハンドルのように中立へ戻る(走っているほど速く)。R を押している間は保持
                float rate = Mathf.Lerp(360f, 900f, Mathf.Abs(speed) / 3f);
                wheelDeg = Mathf.MoveTowards(wheelDeg, 0f, rate * dt);
            }
            else if (centering)
            {
                wheelDeg = Mathf.MoveTowards(wheelDeg, 0f, 720f * dt);
                if (Mathf.Abs(wheelDeg) < 0.01f) centering = false;
            }
            wheelDeg = Mathf.Clamp(wheelDeg, -maxW, maxW);

            // シフト(エンジン停止中は TryShift が受け付けない)
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) TryShift(Gear.P);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) TryShift(Gear.R);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) TryShift(Gear.N);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) TryShift(Gear.D);
            // Q / ZL: P 側へ1段, E / L: D 側へ1段(P → R → N → D)。
            // エンジン停止中の L は始動の長押しに使うのでシフトとしては読まない
            int idx = System.Array.IndexOf(Order, gear);
            if ((Input.GetKeyDown(KeyCode.Q) || engineOn && Pad.Down(JoyButton.ZL)) && idx > 0) TryShift(Order[idx - 1]);
            if ((Input.GetKeyDown(KeyCode.E) || engineOn && Pad.Down(JoyButton.L)) && idx < Order.Length - 1) TryShift(Order[idx + 1]);

            // 電動パーキングブレーキ(アクセルで自動解除)
            if (Input.GetKeyDown(KeyCode.B))
            {
                parkingBrake = !parkingBrake;
                notify?.Invoke(parkingBrake ? "パーキングブレーキ ON" : "パーキングブレーキ OFF", Color.white);
                parkingBrakeChanged?.Invoke(parkingBrake);
            }
            if (parkingBrake && throttle > 0.1f && (gear == Gear.D || gear == Gear.R))
            {
                parkingBrake = false;
                notify?.Invoke("パーキングブレーキ 自動解除", Color.white);
                parkingBrakeChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// ブレーキを踏みながら長押しでエンジンの始動/停止。
        /// 始動: Joy-Con L(停止中), 停止: Joy-Con ZL(P のとき)。キーボードは I で両方。
        /// 押し始めた時点で条件を満たしているときだけ数える(R から ZL で P に入れた勢いで止まらないように)
        /// </summary>
        void ReadEngineSwitch(float dt)
        {
            JoyButton btn = engineOn ? JoyButton.ZL : JoyButton.L;
            bool down = Pad.Down(btn) || Input.GetKeyDown(KeyCode.I);
            bool held = Pad.Held(btn) || Input.GetKey(KeyCode.I);
            if (down) { engineHoldArmed = !engineOn || gear == Gear.P; engineHold = 0f; }
            if (!held) { engineHoldArmed = false; engineHold = 0f; return; }
            if (!engineHoldArmed) return;
            if (brake < 0.05f || (engineOn && gear != Gear.P)) { engineHold = 0f; return; }
            engineHold += dt;
            if (engineHold < EngineHoldTime) return;
            engineHoldArmed = false;
            engineHold = 0f;
            SetEngine(!engineOn);
        }

        public void SetEngine(bool on)
        {
            if (on == engineOn) return;
            engineOn = on;
            gear = Gear.P;
            if (!on) throttle = 0f;
            notify?.Invoke(on ? "エンジン始動(シフト P)" : "エンジン停止", Color.white);
            engineChanged?.Invoke(on);
        }

        /// <summary>スティックの軸ごとの遊び(上に倒したつもりでハンドルが少し回る、などを防ぐ)</summary>
        static float PadAxis(float v)
        {
            const float dead = 0.25f;
            float a = Mathf.Abs(v);
            return a < dead ? 0f : Mathf.Sign(v) * (a - dead) / (1f - dead);
        }

        public void TryShift(Gear g)
        {
            if (g == gear) return;
            if (!engineOn)
            {
                notify?.Invoke("エンジンを始動してください", new Color(1f, 0.8f, 0.3f));
                return;
            }
            if (Mathf.Abs(speed) > 0.4f && g != Gear.N)
            {
                notify?.Invoke("停止してからシフト操作してください", new Color(1f, 0.8f, 0.3f));
                return;
            }
            gear = g;
            gearChanged?.Invoke(g);
        }

        // ---------------------------------------------------------------- 物理
        void Step(float h)
        {
            if (gear == Gear.P)
            {
                speed = Mathf.MoveTowards(speed, 0f, 20f * h);
            }
            else
            {
                float dir = gear == Gear.D ? 1f : gear == Gear.R ? -1f : 0f;
                float drive = 0f;
                if (dir != 0f)
                {
                    float vd = speed * dir; // シフト方向の速度
                    // CVT のクリープ: 前進 約6km/h, 後退 約5km/h に向かって弱く加速
                    float creepTarget = dir > 0 ? 1.7f : 1.4f;
                    float creep = !creepEnabled ? 0f : vd < 0 ? 1.4f : 1.1f * Mathf.Clamp01(1f - vd / creepTarget);
                    float vmax = dir > 0 ? 16.7f : 5.0f;
                    float thr = throttle * 2.6f * Mathf.Clamp01(1f - vd / vmax);
                    drive = dir * (creep + thr);
                }
                speed += drive * h;
                float resist = 0.12f + 0.002f * speed * speed + brake * 7.5f + (parkingBrake ? 6f : 0f);
                if (Mathf.Abs(speed) <= resist * h) speed = 0f;
                else speed -= Mathf.Sign(speed) * resist * h;
            }

            float ds = speed * h;
            if (Mathf.Abs(ds) < 1e-7f) return;

            Vector3 r2 = rearAxle; float y2 = yaw;
            CarSpec.Advance(ref r2, ref y2, ds, SteerDeg);
            int hit = Blocked(r2, y2);
            if (hit == 0)
            {
                Move(r2, y2, ds);
                touchingBody = touchingStop = false;
                UpdateCurb();
                return;
            }

            // 接触する直前まで進める(二分探索)
            float lo = 0f, hi = 1f;
            for (int k = 0; k < 7; k++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector3 r3 = rearAxle; float y3 = yaw;
                CarSpec.Advance(ref r3, ref y3, ds * mid, SteerDeg);
                if (Blocked(r3, y3) != 0) hi = mid; else lo = mid;
            }
            if (lo > 0f)
            {
                Vector3 r4 = rearAxle; float y4 = yaw;
                CarSpec.Advance(ref r4, ref y4, ds * lo, SteerDeg);
                Move(r4, y4, ds * lo);
                UpdateCurb();
            }

            if (hit == 1)
            {
                if (!touchingBody && Mathf.Abs(speed) > 0.02f)
                {
                    collisions++;
                    notify?.Invoke($"接触しました!(速度 {Mathf.Abs(SpeedKmh):0.0} km/h)", new Color(1f, 0.3f, 0.25f));
                }
                touchingBody = true;
            }
            else
            {
                if (!touchingStop) notify?.Invoke("タイヤが輪止めに当たりました", new Color(0.8f, 0.9f, 1f));
                touchingStop = true;
            }
            speed = 0f;
        }

        /// <summary>
        /// 縁石・歩道は止めずに乗り上げさせる。タイヤが段差に当たるたびに速度が落ち、降りるときも少し落ちる。
        /// 縁石に乗った時点で脱輪として1回数える(乗っている間は数え直さない)
        /// </summary>
        void UpdateCurb()
        {
            int n = CurbWheels(rearAxle, yaw);
            if (n > curbWheels)
            {
                if (curbWheels == 0 && Mathf.Abs(speed) > 0.02f)
                {
                    curbHits++;
                    notify?.Invoke("縁石に乗り上げました(脱輪)", new Color(1f, 0.7f, 0.3f));
                }
                speed *= 0.5f;
            }
            else if (n < curbWheels) speed *= 0.85f;
            curbWheels = n;
        }

        void Move(Vector3 r, float y, float ds)
        {
            rearAxle = r; yaw = y;
            travelled += Mathf.Abs(ds);
            // 切り返し回数: 逆方向へ 5cm 以上動いたら1回
            int s = ds > 0 ? 1 : -1;
            if (s == lastMoveSign) segmentLength = 0f;
            else
            {
                segmentLength += Mathf.Abs(ds);
                if (segmentLength > 0.05f)
                {
                    if (lastMoveSign != 0) directionChanges++;
                    lastMoveSign = s;
                    segmentLength = 0f;
                }
            }
            spinDeg += ds / CarSpec.TireRadius * Mathf.Rad2Deg;
        }

        /// <summary>0=自由, 1=ボディ/ミラーが障害物に接触, 2=タイヤが輪止めに接触(縁石は乗り上げるので含めない)</summary>
        public int Blocked(Vector3 rear, float yawDeg)
        {
            Vector3 c = CarSpec.CenterFromRear(rear, yawDeg);
            Quaternion q = Quaternion.Euler(0, yawDeg, 0);
            float hh = (CarSpec.Height - CarSpec.BodyBottom) * 0.5f;
            if (Physics.CheckBox(c + Vector3.up * (CarSpec.BodyBottom + hh),
                    new Vector3(CarSpec.HalfWidth, hh, CarSpec.HalfLength), q, obstacleMask, QueryTriggerInteraction.Ignore))
                return 1;
            if (Physics.CheckBox(c + q * new Vector3(0, 1.02f, 0.53f),
                    new Vector3(CarSpec.MirrorHalfWidth, 0.07f, 0.08f), q, obstacleMask, QueryTriggerInteraction.Ignore))
                return 1;
            for (int i = 0; i < 4; i++)
                if (Physics.CheckBox(WheelPos(i, c, q), TireHalf, q, stopMask, QueryTriggerInteraction.Ignore)) return 2;
            return 0;
        }

        static readonly Vector3 TireHalf = new Vector3(CarSpec.TireWidth * 0.5f, 0.05f, 0.24f);

        /// <summary>タイヤ接地部の中心(0=左前 1=右前 2=左後 3=右後)</summary>
        static Vector3 WheelPos(int i, Vector3 center, Quaternion q)
        {
            float x = (i % 2 == 0 ? -0.5f : 0.5f) * (i < 2 ? CarSpec.TrackFront : CarSpec.TrackRear);
            float z = i < 2 ? CarSpec.FrontAxleZ : CarSpec.RearAxleZ;
            return center + q * new Vector3(x, 0.06f, z);
        }

        int CurbWheels(Vector3 rear, float yawDeg)
        {
            Vector3 c = CarSpec.CenterFromRear(rear, yawDeg);
            Quaternion q = Quaternion.Euler(0, yawDeg, 0);
            int n = 0;
            for (int i = 0; i < 4; i++)
                if (Physics.CheckBox(WheelPos(i, c, q), TireHalf, q, curbMask, QueryTriggerInteraction.Ignore)) n++;
            return n;
        }

        /// <summary>各タイヤの下にある縁石・歩道の上面の高さへ、見た目の車高を寄せる</summary>
        void UpdateLift(float dt)
        {
            Vector3 c = Center;
            Quaternion q = Quaternion.Euler(0, yaw, 0);
            float rate = dt > 0f ? Mathf.Max(0.4f, Mathf.Abs(speed) * 1.5f) * dt : float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vector3 w = WheelPos(i, c, q);
                float target = Physics.Raycast(new Vector3(w.x, 1f, w.z), Vector3.down, out var hit, 1.2f, curbMask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(0f, hit.point.y) : 0f;
                wheelLift[i] = Mathf.MoveTowards(wheelLift[i], target, rate);
            }
        }

        // ---------------------------------------------------------------- 見た目
        void ApplyTransform()
        {
            // 段差に乗ったタイヤの高さから車体の傾き(ピッチ・ロール)と車高を決める
            float front = (wheelLift[0] + wheelLift[1]) * 0.5f, rear = (wheelLift[2] + wheelLift[3]) * 0.5f;
            float leftH = (wheelLift[0] + wheelLift[2]) * 0.5f, rightH = (wheelLift[1] + wheelLift[3]) * 0.5f;
            float pitch = -Mathf.Atan2(front - rear, CarSpec.Wheelbase) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(rightH - leftH, CarSpec.TrackFront) * Mathf.Rad2Deg;
            float lift = (front + rear) * 0.5f;
            transform.SetPositionAndRotation(Center + Vector3.up * lift, Quaternion.Euler(pitch, yaw, roll));
        }

        void UpdateVisuals()
        {
            if (!parts) return;
            CarSpec.Ackermann(SteerDeg, out float l, out float r);
            for (int i = 0; i < 4; i++)
            {
                if (!parts.wheelSteer[i]) continue;
                float st = i == 0 ? l : i == 1 ? r : 0f;
                parts.wheelSteer[i].localRotation = Quaternion.Euler(0, st, 0);
                parts.wheelSpin[i].localRotation = Quaternion.Euler(spinDeg, 0, 0);
            }
            if (parts.steeringWheel) parts.steeringWheel.localRotation = Quaternion.Euler(0, 0, -wheelDeg);
            bool braking = brake > 0.05f;
            foreach (var b in parts.brakeLights) if (b) b.sharedMaterial = braking ? parts.brakeOn : parts.brakeOff;
            foreach (var b in parts.reverseLights) if (b) b.sharedMaterial = gear == Gear.R ? parts.reverseOn : parts.reverseOff;
        }

        /// <summary>指定位置に停車した状態(エンジン停止・P・パーキングブレーキ ON)で置き直す</summary>
        public void Spawn(Vector3 rear, float yawDeg)
        {
            SetPose(rear, yawDeg);
            gear = Gear.P;
            parkingBrake = true;
            engineOn = false;
            engineHold = 0f; engineHoldArmed = false;
            wheelDeg = 0f;
            throttle = brake = 0f;
            centering = padSteering = false;
            ResetStats();
            UpdateVisuals();
        }

        public void ResetStats()
        {
            curbHits = 0;
            collisions = 0; directionChanges = 0; travelled = 0; driveTime = 0;
            lastMoveSign = 0; segmentLength = 0;
            touchingBody = touchingStop = false;
        }
    }
}
