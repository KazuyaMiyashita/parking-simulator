using UnityEngine;

namespace ParkingSim
{
    public enum ViewMode { Top2D, Driver3D, Chase3D }

    /// <summary>俯瞰 2D / 運転席 3D / 外部 3D の視点切替と、ミラー・バックカメラの制御</summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera cam;
        public ViewMode mode = ViewMode.Top2D;
        public float orthoSize = 9f;
        public bool wide;                 // 2D の広域表示
        public const float WideSize = 60f;
        public Vector3? fixedCenter;       // 指定すると 2D はその位置を中心に表示(スクリーンショット用)
        public float driverFov = 60f;
        public bool mirrorTiltInReverse = true;
        public bool backMonitor = true;
        public bool inputEnabled = true;

        CarController car;
        OwnCarParts parts;
        float lookYaw, lookPitch;
        float padYaw, padPitch;   // 左スティックで動かした視線(押し込みで正面に戻す)
        Vector3 topPos, chasePos;
        float mirrorTilt;
        static readonly Color Sky = new Color(0.62f, 0.75f, 0.9f);

        public bool BackMonitorVisible => backMonitor && car && car.gear == Gear.R;
        public bool MirrorsActive => mode == ViewMode.Driver3D;

        public void Init()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.backgroundColor = Sky;
            cam.clearFlags = CameraClearFlags.SolidColor;
            go.AddComponent<AudioListener>();
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
            padYaw = padPitch = 0;
            Snap();
        }

        public void Cycle() => SetMode((ViewMode)(((int)mode + 1) % 3));

        public void Snap()
        {
            if (!car) return;
            topPos = fixedCenter ?? car.Center;
            chasePos = ChaseTarget();
            UpdateCamera(0f);
        }

        void LateUpdate()
        {
            if (inputEnabled) HandleInput();
            if (Time.deltaTime > 0f) UpdateCamera(Time.deltaTime); // 一時停止中はカメラも止める
        }

        void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.Tab)) Cycle();
            // 左スティック: 視線を動かす(倒した量に比例した速さ, 離してもそのまま)。押し込みで正面へ
            Vector2 ls = Pad.LeftStick;
            float dt = Time.deltaTime;
            if (Pad.Down(JoyButton.LStick)) padYaw = padPitch = 0f;
            else if (mode != ViewMode.Top2D)
            {
                padYaw = Mathf.Clamp(padYaw + ls.x * 120f * dt, -170f, 170f);
                if (mode == ViewMode.Driver3D) padPitch = Mathf.Clamp(padPitch - ls.y * 60f * dt, -40f, 50f);
            }
            float wheel = Input.mouseScrollDelta.y;
            if (mode == ViewMode.Top2D)
            {
                if (wheel != 0) orthoSize = Mathf.Clamp(orthoSize * (1f - wheel * 0.1f), 3f, 40f);
                if (ls.y != 0) orthoSize = Mathf.Clamp(orthoSize * (1f - ls.y * 1.5f * dt), 3f, 40f);
                if (Pad.Down(JoyButton.LStick)) orthoSize = 9f;
                if (Input.GetKeyDown(KeyCode.F)) wide = !wide;
                if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) orthoSize = Mathf.Max(3f, orthoSize - 10f * Time.deltaTime);
                if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) orthoSize = Mathf.Min(40f, orthoSize + 10f * Time.deltaTime);
            }
            else if (mode == ViewMode.Driver3D)
            {
                if (wheel != 0) driverFov = Mathf.Clamp(driverFov - wheel * 3f, 35f, 90f);
            }
        }

        Vector3 ChaseTarget()
        {
            Vector3 back = -CarSpec.Forward(car.yaw + padYaw); // シフトに関係なく車の後方から(左スティックで回り込める)
            return car.Center + back * 7.5f + Vector3.up * 4.2f;
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
                    cam.orthographicSize = wide ? WideSize : orthoSize;
                    Vector3 target = fixedCenter ?? car.Center;
                    topPos = dt > 0 ? Vector3.Lerp(topPos, target, 1f - Mathf.Exp(-8f * dt)) : target;
                    cam.transform.SetPositionAndRotation(new Vector3(topPos.x, 60f, topPos.z), Quaternion.Euler(90f, 0f, 0f));
                    cam.nearClipPlane = 1f; cam.farClipPlane = 120f;
                    cam.cullingMask = ~0;
                    break;
                }
                case ViewMode.Driver3D:
                {
                    cam.orthographic = false;
                    cam.fieldOfView = driverFov;
                    cam.nearClipPlane = 0.08f; cam.farClipPlane = 400f;
                    cam.cullingMask = ~Layers.Mask(Layers.OwnCabin, Layers.Guide);

                    float targetYaw = 0f, targetPitch = 0f;
                    bool lookL = inputEnabled && Input.GetKey(KeyCode.Z);
                    bool lookR = inputEnabled && Input.GetKey(KeyCode.X);
                    bool free = inputEnabled && Input.GetMouseButton(1);
                    if (lookL && lookR) targetYaw = -160f;      // 左肩越しに後方
                    else if (lookL) targetYaw = -75f;
                    else if (lookR) targetYaw = 75f;
                    else { targetYaw = padYaw; targetPitch = padPitch; }
                    if (free)
                    {
                        lookYaw = Mathf.Clamp(lookYaw + Input.GetAxis("Mouse X") * 3f, -170f, 170f);
                        lookPitch = Mathf.Clamp(lookPitch - Input.GetAxis("Mouse Y") * 3f, -40f, 50f);
                    }
                    else
                    {
                        float k = dt > 0 ? 1f - Mathf.Exp(-10f * dt) : 1f;
                        lookYaw = Mathf.Lerp(lookYaw, targetYaw, k);
                        lookPitch = Mathf.Lerp(lookPitch, targetPitch, k);
                    }
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
                    Vector3 target = ChaseTarget();
                    chasePos = dt > 0 ? Vector3.Lerp(chasePos, target, 1f - Mathf.Exp(-3f * dt)) : target;
                    cam.transform.position = chasePos;
                    cam.transform.rotation = Quaternion.LookRotation(car.Center + Vector3.up * 0.6f - chasePos);
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
            float target = mirrorTiltInReverse && car.gear == Gear.R ? 1f : 0f;
            mirrorTilt = dt > 0 ? Mathf.MoveTowards(mirrorTilt, target, dt * 2f) : target;
            float pitch = Mathf.Lerp(4f, 16f, mirrorTilt);
            parts.AimMirror(parts.mirrorLeftGlass, parts.mirrorLeftCam, parts.mirrorLeftPos, 188f, pitch);
            parts.AimMirror(parts.mirrorRightGlass, parts.mirrorRightCam, parts.mirrorRightPos, 172f, pitch);
        }
    }
}
