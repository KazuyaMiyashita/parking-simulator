using Cinemachine;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>視点。この順に切り替わる(車外の三人称 → 運転席 → 真上から)</summary>
    public enum ViewMode { Chase3D, Driver3D, Top2D }

    /// <summary>
    /// 車外 3D / 運転席 3D / 俯瞰 2D の視点切替と、ミラー・バックカメラの制御。
    /// 3D の視点は Starter Assets(Third Person / First Person Character Controller)と同じ Cinemachine の構成:
    /// 追従先(カメラターゲット)の向きを見渡す操作で回し、車外は 3rd Person Follow(建物などに当たると寄る)、
    /// 運転席は Hard Lock To Target + Same As Follow Target で映す
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera cam;
        public ViewMode mode = ViewMode.Chase3D;
        public const float DefaultOrthoSize = 9f;
        public float orthoSize = DefaultOrthoSize;
        public Vector3? fixedCenter;       // 指定すると 2D はその位置を中心に表示(スクリーンショット用)
        public float driverFov = 60f;
        public bool inputEnabled = true;

        CarController car;
        OwnCarParts parts;
        CinemachineBrain brain;
        CinemachineVirtualCamera chaseCam, driverCam;
        Cinemachine3rdPersonFollow chaseFollow;
        Transform chaseTarget, driverTarget;

        float lookYaw, lookPitch;
        public const float DefaultChasePitch = 29f;
        public float chasePitch = DefaultChasePitch; // 外部視点の仰角(見渡す操作の上下で変更、離してもそのまま)
        public const float DefaultChaseDistance = 8.6f;
        public float chaseDistance = DefaultChaseDistance; // 外部視点の距離(ズームで変更)
        public float driverLean;                           // 運転席で見ている方向へ体を乗り出す量(m、- で退ける)
        const float LeanForward = 0.30f, LeanBack = 0.18f;
        float chaseYaw, chaseYawOffset;
        bool zoomLatched;                                  // ズームの同時押しで戻した後、両方離すまでズームしない
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

            // 視点の切り替えは即座に。更新はカメラターゲットを動かした直後にこちらから呼ぶ
            brain = go.AddComponent<CinemachineBrain>();
            brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);
            brain.m_UpdateMethod = CinemachineBrain.UpdateMethod.ManualUpdate;
            brain.m_BlendUpdateMethod = CinemachineBrain.BrainUpdateMethod.LateUpdate;

            // 車外: Third Person の PlayerFollowCamera と同じ 3rd Person Follow。
            // 腕の付け根を屋根より上に置き、建物などに当たって寄っても車体の中に入らないようにする
            chaseTarget = new GameObject("Chase Camera Target").transform;
            chaseTarget.SetParent(transform, false);
            chaseCam = VirtualCamera("Chase Follow Camera", chaseTarget, 55f, 0.1f);
            chaseFollow = chaseCam.AddCinemachineComponent<Cinemachine3rdPersonFollow>();
            chaseFollow.Damping = new Vector3(0.1f, 0.25f, 0.3f);
            chaseFollow.ShoulderOffset = new Vector3(0f, 1.1f, 0f);
            chaseFollow.VerticalArmLength = 0f;
            chaseFollow.CameraSide = 0.5f;
            chaseFollow.CameraDistance = chaseDistance;
            chaseFollow.CameraCollisionFilter = Layers.Mask(Layers.Obstacle);
            chaseFollow.IgnoreTag = "";
            chaseFollow.CameraRadius = 0.15f;
            chaseFollow.DampingIntoCollision = 0f;
            chaseFollow.DampingFromCollision = 0.5f;

            // 運転席: First Person の PlayerFollowCamera と同じく、目の位置と向きにそのまま合わせる
            driverTarget = new GameObject("Driver Camera Target").transform;
            driverTarget.SetParent(transform, false);
            driverCam = VirtualCamera("Driver Follow Camera", driverTarget, driverFov, 0.08f);
            driverCam.AddCinemachineComponent<CinemachineHardLockToTarget>();
            driverCam.AddCinemachineComponent<CinemachineSameAsFollowTarget>();
        }

        CinemachineVirtualCamera VirtualCamera(string name, Transform follow, float fov, float near)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<CinemachineVirtualCamera>();
            v.Follow = follow;
            v.m_Lens.FieldOfView = fov;
            v.m_Lens.NearClipPlane = near;
            v.m_Lens.FarClipPlane = 400f;
            v.m_Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            go.SetActive(false);
            return v;
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
            chaseYawOffset = 0f;
            chaseCam.PreviousStateIsValid = false;
            driverCam.PreviousStateIsValid = false;
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
            HandleZoom(Time.deltaTime);
            // 3D の見渡す操作は UpdateCamera で読む
            if (mode == ViewMode.Chase3D)
                chasePitch = Mathf.Clamp(chasePitch - Controls.Look.y * 40f * Time.deltaTime, 5f, 80f);
        }

        /// <summary>
        /// ZR(R)でズームイン、ZL(F)でズームアウト。同時押しで今の視点の既定に戻す。
        /// 2D は表示の広さ、車外は車との距離、運転席は見ている方向へ体を乗り出す / 退ける。
        /// 運転席だけは、離すと元の姿勢に戻る(2D と車外は離してもそのまま)
        /// </summary>
        void HandleZoom(float dt)
        {
            bool zin = Controls.ZoomInHeld, zout = Controls.ZoomOutHeld;
            if (zin && zout)
            {
                if (!zoomLatched) ResetZoom();
                zoomLatched = true;
                return;
            }
            if (!zin && !zout)
            {
                zoomLatched = false;
                driverLean = Mathf.MoveTowards(driverLean, 0f, 0.8f * dt); // 運転席の姿勢だけは離すと戻す
                return;
            }
            if (zoomLatched) return;

            float dir = zin ? 1f : -1f;
            switch (mode)
            {
                case ViewMode.Top2D:
                    orthoSize = Mathf.Clamp(orthoSize * Mathf.Exp(-dir * 1.2f * dt), 4f, 40f);
                    break;
                case ViewMode.Chase3D:
                    chaseDistance = Mathf.Clamp(chaseDistance * Mathf.Exp(-dir * 1.2f * dt), 3f, 20f);
                    break;
                case ViewMode.Driver3D:
                    driverLean = Mathf.Clamp(driverLean + dir * 0.5f * dt, -LeanBack, LeanForward);
                    break;
            }
        }

        void ResetZoom()
        {
            switch (mode)
            {
                case ViewMode.Top2D: orthoSize = DefaultOrthoSize; break;
                case ViewMode.Chase3D: chaseDistance = DefaultChaseDistance; break;
                case ViewMode.Driver3D: driverLean = 0f; break;
            }
        }

        public void UpdateCamera(float dt)
        {
            if (!car || !cam) return;
            UpdateMirrors(dt);
            chaseCam.gameObject.SetActive(mode == ViewMode.Chase3D);
            driverCam.gameObject.SetActive(mode == ViewMode.Driver3D);

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
                    return;
                }
                case ViewMode.Driver3D:
                {
                    cam.cullingMask = ~Layers.Mask(Layers.OwnCabin);
                    driverCam.m_Lens.FieldOfView = driverFov;

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
                    // ズームは見ている方向へ体を乗り出す / 退ける。車内の壁・天井・座席にめり込まない範囲に収める
                    Quaternion view = Quaternion.Euler(5f + lookPitch, lookYaw, 0f);
                    eye += view * Vector3.forward * driverLean;
                    eye = new Vector3(Mathf.Clamp(eye.x, -0.55f, 0.60f), Mathf.Clamp(eye.y, 1.00f, 1.28f), Mathf.Clamp(eye.z, -0.35f, 0.25f));
                    driverTarget.SetPositionAndRotation(car.transform.TransformPoint(eye), car.transform.rotation * view);
                    break;
                }
                case ViewMode.Chase3D:
                {
                    cam.cullingMask = ~0;
                    // 向き: 車の後方へゆっくり追従 + 見渡す操作の左右(倒している間だけ、離すと後方に戻る)
                    // 仰角: 見渡す操作の上下で変えた値をそのまま使う(慣性なし)
                    chaseYaw = dt > 0 ? Mathf.LerpAngle(chaseYaw, car.yaw, 1f - Mathf.Exp(-3f * dt)) : car.yaw;
                    float off = inputEnabled ? Controls.Look.x * 150f : 0f;
                    chaseYawOffset = dt > 0 ? Mathf.Lerp(chaseYawOffset, off, 1f - Mathf.Exp(-10f * dt)) : off;
                    chaseFollow.CameraDistance = chaseDistance;
                    chaseTarget.SetPositionAndRotation(car.transform.position + Vector3.up * 0.6f,
                        Quaternion.Euler(chasePitch, chaseYaw + chaseYawOffset, 0f));
                    break;
                }
            }
            brain.ManualUpdate();
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
