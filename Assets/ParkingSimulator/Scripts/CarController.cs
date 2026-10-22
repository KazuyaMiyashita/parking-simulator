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
        public bool steerHold;     // ハンドル固定(ZL / Space で切り替え)

        [Header("設定")]
        public bool creepEnabled; // オートマのクリープ現象

        [Header("操作")]
        public bool inputEnabled = true;
        public float steerRate = 540f; // 入力の角度へハンドルを回す速さ(度/秒)

        [Header("記録")]
        public float travelled;    // 走った道のり(m)
        public bool touchingBody, touchingStop;

        public System.Action<string, Color> notify;
        public System.Action<Gear> gearChanged;
        public System.Action contact;

        bool regrab;              // ハンドル固定中にスティック(キー)を一度離した。次に倒すと固定を解いてつかみ直す
        int curbWheels;           // 縁石・歩道に乗っているタイヤの数
        readonly float[] wheelLift = new float[4]; // 段差で持ち上がったタイヤの高さ(見た目)
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
            if (inputEnabled) ReadInput(dt);
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
            // ペダル(押している長さで踏み込み量が増える)
            throttle = Controls.Accel ? Mathf.MoveTowards(Mathf.Max(throttle, 0.05f), 1f, 0.6f * dt) : Mathf.MoveTowards(throttle, 0f, 3f * dt);
            brake = Controls.Brake ? Mathf.MoveTowards(Mathf.Max(brake, 0.08f), 1f, 1.2f * dt) : Mathf.MoveTowards(brake, 0f, 4f * dt);

            ReadSteering(dt);

            int idx = System.Array.IndexOf(Order, gear);
            if (Controls.ShiftDownDown && idx > 0) TryShift(Order[idx - 1]);
            if (Controls.ShiftUpDown && idx < Order.Length - 1) TryShift(Order[idx + 1]);
        }

        /// <summary>
        /// スティックの角度(キーは押している間いっぱい)がハンドルの目標角。離すと中央へ徐々に戻る。
        /// 固定中は離しても角度を保ち、一度離してから倒し直すと固定を解いてつかみ直す
        /// </summary>
        void ReadSteering(float dt)
        {
            float maxW = CarSpec.MaxWheelDeg;
            float input = Controls.Steer;
            bool active = input != 0f;
            if (Controls.SteerHoldToggle)
            {
                steerHold = !steerHold;
                regrab = !active;
                notify?.Invoke(steerHold ? "ハンドル固定" : "ハンドル固定を解除", Color.white);
            }
            if (steerHold)
            {
                if (!active) regrab = true;
                else if (regrab) steerHold = false;
                else return;
            }
            if (active) wheelDeg = Mathf.MoveTowards(wheelDeg, input * maxW, steerRate * dt);
            else wheelDeg = Mathf.MoveTowards(wheelDeg, 0f, Mathf.Lerp(360f, 900f, Mathf.Abs(speed) / 3f) * dt);
            wheelDeg = Mathf.Clamp(wheelDeg, -maxW, maxW);
        }

        /// <summary>
        /// シフトを切り替える。ブレーキは問わず、走行中の危険な切り替えだけ断る
        /// (動いている間の P、前進中の R、後退中の D)
        /// </summary>
        public void TryShift(Gear g)
        {
            if (g == gear) return;
            const float moving = 0.4f;
            bool danger = (g == Gear.P && Mathf.Abs(speed) > moving) || (g == Gear.R && speed > moving) || (g == Gear.D && speed < -moving);
            if (danger)
            {
                notify?.Invoke("止まってからシフト操作してください", new Color(1f, 0.8f, 0.3f));
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
                float resist = 0.12f + 0.002f * speed * speed + brake * 7.5f;
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
                    notify?.Invoke("接触しました", new Color(1f, 0.3f, 0.25f));
                    contact?.Invoke();
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
        /// </summary>
        void UpdateCurb()
        {
            int n = CurbWheels(rearAxle, yaw);
            if (n > curbWheels)
            {
                if (curbWheels == 0 && Mathf.Abs(speed) > 0.02f)
                    notify?.Invoke("縁石に乗り上げました", new Color(1f, 0.7f, 0.3f));
                speed *= 0.5f;
            }
            else if (n < curbWheels) speed *= 0.85f;
            curbWheels = n;
        }

        void Move(Vector3 r, float y, float ds)
        {
            rearAxle = r; yaw = y;
            travelled += Mathf.Abs(ds);
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

        /// <summary>指定位置に停車した状態(P)で置き直す。エンジンは常にかかっている</summary>
        public void Spawn(Vector3 rear, float yawDeg)
        {
            SetPose(rear, yawDeg);
            gear = Gear.P;
            wheelDeg = 0f;
            steerHold = false;
            throttle = brake = 0f;
            touchingBody = touchingStop = false;
            UpdateVisuals();
        }
    }
}
