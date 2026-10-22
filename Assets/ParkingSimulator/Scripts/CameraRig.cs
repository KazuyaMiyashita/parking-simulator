using UnityEngine;

namespace ParkingSim
{
    /// <summary>視点。この順に切り替わる(車外の三人称 → 運転席 → 真上から)</summary>
    public enum ViewMode { Chase3D, Driver3D, Top2D }

    /// <summary>車外 3D / 運転席 3D / 俯瞰 2D の視点切替と、ミラー・バックカメラの制御</summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera cam;
        public ViewMode mode = ViewMode.Chase3D;
        public float orthoSize = 9f;
        public Vector3? fixedCenter;       // 指定すると 2D はその位置を中心に表示(スクリーンショット用)
        public float driverFov = 60f;
        public bool inputEnabled = true;

        CarController car;
        OwnCarParts parts;
        float lookYaw, lookPitch;
        public const float DefaultChasePitch = 29f;
        public float chasePitch = DefaultChasePitch; // 外部視点の仰角(見渡す操作の上下で変更、離してもそのまま)
        const float ChaseDistance = 8.6f;
        float chaseYaw, chaseYawOffset, occlusionPitch;
        int occluderMask;
        Vector3 topPos;
        float mirrorTilt;
        static readonly Color Sky = new Color(0.62f, 0.75f, 0.9f);

        /// <summary>バックカメラ(実車と同じく R のとき。2D 視点では出さない)</summary>
        public bool BackMonitorVisible => car && car.gear == Gear.R && mode != ViewMode.Top2D;
        public bool MirrorsActive => mode == ViewMode.Driver3D;

        public void Init()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.backgroundColor = Sky;
            cam.clearFlags = CameraClearFlags.SolidColor;
            go.AddComponent<AudioListener>();
            occluderMask = Layers.Mask(Layers.Obstacle);
        }

        public void Attach(CarController c)
        {
            car = c; parts = c ? c.parts : null;
            Snap();
        }

        public void SetMode(ViewMode m)
        {
            mode = m;
            lookYaw = lookPitch = 0;
            Snap();
        }

        public void Cycle() => SetMode((ViewMode)(((int)mode + 1) % 3));

        public void Snap()
        {
            if (!car) return;
            topPos = fixedCenter ?? car.Center;
            chaseYaw = car.yaw;
            chaseYawOffset = occlusionPitch = 0f;
            UpdateCamera(0f);
        }

        void LateUpdate()
        {
            if (inputEnabled) HandleInput();
            if (Time.deltaTime > 0f) UpdateCamera(Time.deltaTime); // 一時停止中はカメラも止める
        }

        void HandleInput()
        {
            if (Controls.ViewCycle) Cycle();
            // 2D: 見渡す操作の上下でズーム。3D の視点は UpdateCamera で読む
            if (mode == ViewMode.Top2D)
                orthoSize = Mathf.Clamp(orthoSize * (1f - Controls.Look.y * 1.5f * Time.deltaTime), 4f, 40f);
            else if (mode == ViewMode.Chase3D)
                chasePitch = Mathf.Clamp(chasePitch - Controls.Look.y * 40f * Time.deltaTime, 5f, 80f);
        }

        static Vector3 Orbit(Vector3 look, float yawDeg, float pitchDeg)
        {
            Vector3 back = -CarSpec.Forward(yawDeg);
            float a = pitchDeg * Mathf.Deg2Rad;
            return look + back * (ChaseDistance * Mathf.Cos(a)) + Vector3.up * (ChaseDistance * Mathf.Sin(a));
        }

        /// <summary>車とカメラの間に建物や他車などがあれば、見通せるまで仰角を上げた値を返す</summary>
        float ClearPitch(Vector3 look, float yawDeg, float pitchDeg)
        {
            for (float p = pitchDeg; p < 85f; p += 3f)
            {
                Vector3 d = Orbit(look, yawDeg, p) - look;
                if (!Physics.SphereCast(look, 0.25f, d.normalized, out _, d.magnitude, occluderMask, QueryTriggerInteraction.Ignore))
                    return p;
            }
            return 85f;
        }

        public void UpdateCamera(float dt)
        {
            if (!car || !cam) return;
            UpdateMirrors(dt);

            switch (mode)
            {
                case ViewMode.Top2D:
                {
                    cam.orthographic = true;
                    cam.orthographicSize = orthoSize;
                    Vector3 target = fixedCenter ?? car.Center;
                    topPos = dt > 0 ? Vector3.Lerp(topPos, target, 1f - Mathf.Exp(-8f * dt)) : target;
                    float up = fixedCenter.HasValue ? 0f : car.yaw; // 車の向きが常に画面の上
                    cam.transform.SetPositionAndRotation(new Vector3(topPos.x, 60f, topPos.z), Quaternion.Euler(90f, up, 0f));
                    cam.nearClipPlane = 1f; cam.farClipPlane = 120f;
                    cam.cullingMask = ~0;
                    break;
                }
                case ViewMode.Driver3D:
                {
                    cam.orthographic = false;
                    cam.fieldOfView = driverFov;
                    cam.nearClipPlane = 0.08f; cam.farClipPlane = 400f;
                    cam.cullingMask = ~Layers.Mask(Layers.OwnCabin);

                    // 見渡す操作を倒している間だけその方向を見る(離すと正面に戻る)
                    Vector2 look = inputEnabled ? Controls.Look : Vector2.zero;
                    float targetYaw = look.x * 160f;
                    float targetPitch = -look.y * (look.y > 0 ? 40f : 50f);
                    float k = dt > 0 ? 1f - Mathf.Exp(-10f * dt) : 1f;
                    lookYaw = Mathf.Lerp(lookYaw, targetYaw, k);
                    lookPitch = Mathf.Lerp(lookPitch, targetPitch, k);
                    // 後ろを振り向くときは頭を中央寄り・前寄りに移動
                    float turn = Mathf.Clamp01((Mathf.Abs(lookYaw) - 60f) / 100f);
                    Vector3 eye = parts.eyeLocal + (lookYaw < 0
                        ? new Vector3(-0.22f, 0.03f, 0.10f) * turn
                        : new Vector3(0.05f, 0.03f, 0.08f) * turn);
                    cam.transform.position = car.transform.TransformPoint(eye);
                    cam.transform.rotation = car.transform.rotation * Quaternion.Euler(5f + lookPitch, lookYaw, 0f);
                    break;
                }
                case ViewMode.Chase3D:
                {
                    cam.orthographic = false;
                    cam.fieldOfView = 55f;
                    cam.nearClipPlane = 0.1f; cam.farClipPlane = 400f;
                    cam.cullingMask = ~0;
                    // 向き: 車の後方へゆっくり追従 + 見渡す操作の左右(倒している間だけ、離すと後方に戻る)
                    // 仰角: 見渡す操作の上下で変えた値をそのまま使う(慣性なし)。隠れるときだけ持ち上げる
                    chaseYaw = dt > 0 ? Mathf.LerpAngle(chaseYaw, car.yaw, 1f - Mathf.Exp(-3f * dt)) : car.yaw;
                    float off = inputEnabled ? Controls.Look.x * 150f : 0f;
                    chaseYawOffset = dt > 0 ? Mathf.Lerp(chaseYawOffset, off, 1f - Mathf.Exp(-10f * dt)) : off;
                    float yawNow = chaseYaw + chaseYawOffset;
                    Vector3 look = car.transform.position + Vector3.up * 0.6f;
                    float need = ClearPitch(look, yawNow, chasePitch) - chasePitch;
                    occlusionPitch = dt > 0 ? Mathf.MoveTowards(occlusionPitch, need, (need > occlusionPitch ? 240f : 60f) * dt) : need;
                    Vector3 pos = Orbit(look, yawNow, chasePitch + occlusionPitch);
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                    break;
                }
            }
        }

        void UpdateMirrors(float dt)
        {
            if (!parts) return;
            bool mirrors = MirrorsActive;
            parts.mirrorLeftCam.enabled = mirrors;
            parts.mirrorRightCam.enabled = mirrors;
            parts.mirrorRearCam.enabled = mirrors;
            parts.backCam.enabled = BackMonitorVisible;

            // リバース連動ドアミラー: R のとき鏡面を下向きに
            float target = car.gear == Gear.R ? 1f : 0f;
            mirrorTilt = dt > 0 ? Mathf.MoveTowards(mirrorTilt, target, dt * 2f) : target;
            float pitch = Mathf.Lerp(4f, 16f, mirrorTilt);
            parts.AimMirror(parts.mirrorLeftGlass, parts.mirrorLeftCam, parts.mirrorLeftPos, 188f, pitch);
            parts.AimMirror(parts.mirrorRightGlass, parts.mirrorRightCam, parts.mirrorRightPos, 172f, pitch);
        }
    }
}
