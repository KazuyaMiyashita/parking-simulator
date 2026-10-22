using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkingSim
{
    /// <summary>
    /// シミュレーター全体の管理。空のシーンでも再生すると自動で生成される。
    /// 世界は起動時に一度だけ作り、やり直しは選んだスタート地点へ車を置き直すだけ。
    /// </summary>
    [DefaultExecutionOrder(100)] // 車の入力処理より後(結果画面を閉じたボタンでシフトが動かないように)
    public class GameManager : MonoBehaviour
    {
        [Header("補助機能")]
        public bool showPath = true;
        public bool sensorSound = true;
        public bool showSensors = true;
        public float steerRate = 450f;

        [System.NonSerialized] public int startIndex;
        [System.NonSerialized] public CarController car;
        [System.NonSerialized] public ParkingSensors sensors;
        [System.NonSerialized] public PathGuide path;
        [System.NonSerialized] public CameraRig rig;
        [System.NonSerialized] public HUD hud;
        [System.NonSerialized] public WorldResult world;
        [System.NonSerialized] public JudgeResult lastResult;
        [System.NonSerialized] public bool showResult;

        /// <summary>近くの駐車枠(位置ガイドと判定に使う)</summary>
        [System.NonSerialized] public TargetSpot nearSpot;
        ParkingJudge.Baseline baseline;
        bool initialized;

        const float NearRange = 9f; // この距離まで近づいた区画を対象にする

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!FindAnyObjectByType<GameManager>())
                new GameObject("Parking Simulator").AddComponent<GameManager>();
        }

        void Awake() => Init();

        public void Init()
        {
            if (initialized) return;
            initialized = true;

            // シーンに既存のカメラ/ライトがあれば無効化(このシミュレーターが自前で用意する)
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (!c.transform.IsChildOf(transform)) c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && !l.transform.IsChildOf(transform)) l.gameObject.SetActive(false);

            SetupEnvironment();
            rig = new GameObject("Camera Rig").AddComponent<CameraRig>();
            rig.transform.SetParent(transform, false);
            rig.Init();
            if (!TryGetComponent(out hud)) hud = gameObject.AddComponent<HUD>();
            if (!TryGetComponent<PadUpdater>(out _)) gameObject.AddComponent<PadUpdater>();
            hud.gm = this;
            BuildWorld();

            if (Application.isPlaying)
            {
                var args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-autoshot");
                if (i >= 0 && i + 1 < args.Length) StartCoroutine(AutoShot(args[i + 1]));
            }
        }

        void SetupEnvironment()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.52f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.3f, 0.3f);
            RenderSettings.fog = false;
            QualitySettings.shadowDistance = 90f;
        }

        void BuildWorld()
        {
            var root = new GameObject("World");
            world = WorldBuilder.Build(root.transform);

            var parts = VehicleBuilder.BuildOwnCar(root.transform);
            car = parts.gameObject.AddComponent<CarController>();
            car.Init(parts);
            car.notify = (msg, col) => { if (hud) hud.Toast(msg, col); };
            car.gearChanged = OnGearChanged;

            sensors = parts.gameObject.AddComponent<ParkingSensors>();
            sensors.Init(car);

            path = Util.Child("Path Guide", root.transform, Vector3.zero).AddComponent<PathGuide>();
            path.Init(car);

            Physics.SyncTransforms();
            rig.Attach(car);
            Respawn();
        }

        /// <summary>選んだスタート地点に停車した状態で置き直す</summary>
        public void Respawn()
        {
            startIndex = Mathf.Clamp(startIndex, 0, world.starts.Count - 1);
            var sp = world.starts[startIndex];
            car.Spawn(sp.rearAxle, sp.yaw);
            car.steerRate = steerRate;
            showResult = false;
            lastResult = null;
            nearSpot = null;
            baseline = ParkingJudge.Baseline.Of(car);
            rig.Snap();
            ApplyInputLock();
        }

        void OnGearChanged(Gear g)
        {
            if (g == Gear.P) Judge(true);
            if (hud) hud.Toast($"シフト: {g}", Color.white, 1.2f);
        }

        public void Judge(bool auto)
        {
            if (nearSpot == null)
            {
                if (!auto && hud) hud.Toast("近くに駐車枠がありません", Color.white);
                return;
            }
            var r = ParkingJudge.Evaluate(nearSpot, car, baseline);
            if (auto && !r.nearTarget) return;
            lastResult = r;
            showResult = true;
        }

        void UpdateNearSpot()
        {
            var s = world.NearestSpot(car.Center, NearRange);
            // 区画から離れている間は記録の起点を更新し続ける(近づいてからの接触・切り返しだけを数える)
            if (s == null) baseline = ParkingJudge.Baseline.Of(car);
            else if (s != nearSpot && nearSpot == null) baseline = ParkingJudge.Baseline.Of(car);
            nearSpot = s;
        }

        void Update()
        {
            if (!car) return;
            UpdateNearSpot();
            bool menu = hud && hud.menuOpen;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F1) || Pad.Down(JoyButton.Plus))
            {
                if (showResult && !menu) showResult = false;
                else hud.SetMenu(!menu);
            }
            if (!menu)
            {
                if (Input.GetKeyDown(KeyCode.F5) || Input.GetKeyDown(KeyCode.Backspace) || Pad.Down(JoyButton.Capture) ||
                    (showResult && Pad.Down(JoyButton.Y))) Respawn();
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Pad.Down(JoyButton.RStick))
                {
                    if (showResult) showResult = false; else Judge(false);
                }
                else if (showResult && Pad.Down(JoyButton.A)) showResult = false;
                if (Input.GetKeyDown(KeyCode.G)) { showPath = !showPath; hud.Toast(showPath ? "予想進路線 ON" : "予想進路線 OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.K)) { rig.backMonitor = !rig.backMonitor; hud.Toast(rig.backMonitor ? "バックモニター ON" : "バックモニター OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.O)) { sensorSound = !sensorSound; hud.Toast(sensorSound ? "センサー音 ON" : "センサー音 OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.L)) hud.showGuideInfo = !hud.showGuideInfo;
                if (Input.GetKeyDown(KeyCode.H) || Pad.Down(JoyButton.Minus)) hud.helpOpen = !hud.helpOpen;
            }
            else hud.PadMenuInput();
            if (path) path.show = showPath;
            car.steerRate = steerRate;
            if (sensors) sensors.sound = sensorSound && !menu;
            ApplyInputLock();
        }

        public void ApplyInputLock()
        {
            bool locked = hud && hud.menuOpen;
            if (car) car.inputEnabled = !locked && !showResult;
            if (rig) rig.inputEnabled = !locked;
        }

        /// <summary>動作確認用: -autoshot &lt;dir&gt; で各スタート地点のスクリーンショットを保存して終了</summary>
        IEnumerator AutoShot(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            Application.runInBackground = true;
            hud.SetMenu(false);
            yield return null;
            string Path(string name) => System.IO.Path.Combine(dir, name);

            // 全体図
            rig.SetMode(ViewMode.Top2D);
            rig.fixedCenter = new Vector3(0, 0, 0);
            float size = rig.orthoSize;
            rig.orthoSize = 120f;
            rig.Snap();
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(Path("map_all.png"));
            yield return new WaitForSecondsRealtime(0.4f);
            rig.fixedCenter = new Vector3(76f, 0, 58f);
            rig.orthoSize = 52f;
            rig.Snap();
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(Path("map_school.png"));
            yield return new WaitForSecondsRealtime(0.4f);
            rig.fixedCenter = null;
            rig.orthoSize = size;

            for (int i = 0; i < world.starts.Count; i++)
            {
                startIndex = i;
                Respawn();
                foreach (var m in new[] { ViewMode.Top2D, ViewMode.Driver3D })
                {
                    rig.SetMode(m);
                    yield return new WaitForSecondsRealtime(0.5f);
                    ScreenCapture.CaptureScreenshot(Path($"start{i}_{m}.png"));
                    yield return new WaitForSecondsRealtime(0.3f);
                }
            }
            startIndex = 0;
            Respawn();
            rig.SetMode(ViewMode.Chase3D);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path("home_chase.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            car.TryShift(Gear.R);
            car.TryShift(Gear.P);
            rig.SetMode(ViewMode.Top2D);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path("home_judge.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            showResult = false;
            hud.SetMenu(true);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path("menu.png"));
            yield return new WaitForSecondsRealtime(0.4f);
            Application.Quit();
        }
    }
}
