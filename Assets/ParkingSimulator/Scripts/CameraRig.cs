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
        public bool follow = true;
        public float driverFov = 60f;
        public bool mirrorTiltInReverse = true;
        public bool backMonitor = true;
        public bool inputEnabled = true;

        CarController car;
        OwnCarParts parts;
        LotResult lot;
        float lookYaw, lookPitch;
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

        public void Attach(CarController c, LotResult l)
        {
            car = c; parts = c ? c.parts : null; lot = l;
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
            topPos = follow ? car.Center : lot.viewCenter;
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
            float wheel = Input.mouseScrollDelta.y;
            if (mode == ViewMode.Top2D)
            {
                if (wheel != 0) orthoSize = Mathf.Clamp(orthoSize * (1f - wheel * 0.1f), 3f, 40f);
                if (Input.GetKeyDown(KeyCode.F)) { follow = !follow; }
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
            Vector3 back = -CarSpec.Forward(car.yaw); // シフトに関係なく常に車の後方から
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
                    cam.orthographicSize = follow ? orthoSize : Mathf.Max(orthoSize, lot.viewSize);
                    Vector3 target = follow ? car.Center : lot.viewCenter;
                    topPos = dt > 0 ? Vector3.Lerp(topPos, target, 1f - Mathf.Exp(-8f * dt)) : target;
                    cam.transform.SetPositionAndRotation(new Vector3(topPos.x, 60f, topPos.z), Quaternion.Euler(90f, 0f, 0f));
                    cam.nearClipPlane = 1f; cam.farClipPlane = 100f;
                    cam.cullingMask = ~0;
                    break;
                }
                case ViewMode.Driver3D:
                {
                    cam.orthographic = false;
                    cam.fieldOfView = driverFov;
                    cam.nearClipPlane = 0.04f; cam.farClipPlane = 400f;
                    cam.cullingMask = ~Layers.Mask(Layers.OwnCabin, Layers.Guide);

                    float targetYaw = 0f, targetPitch = 0f;
                    bool lookL = inputEnabled && Input.GetKey(KeyCode.Z), lookR = inputEnabled && Input.GetKey(KeyCode.X);
                    bool free = inputEnabled && Input.GetMouseButton(1);
                    if (lookL && lookR) targetYaw = -160f;      // 左肩越しに後方
                    else if (lookL) targetYaw = -75f;
                    else if (lookR) targetYaw = 75f;
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
