using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkingSim
{
    /// <summary>
    /// シミュレーター全体の管理。空のシーンでも再生すると自動で生成される。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public ScenarioConfig config;

        [Header("補助機能")]
        public bool showPath = true;
        public bool sensorSound = true;
        public bool showSensors = true;
        public float steerRate = 450f;

        [System.NonSerialized] public CarController car;
        [System.NonSerialized] public ParkingSensors sensors;
        [System.NonSerialized] public PathGuide path;
        [System.NonSerialized] public CameraRig rig;
        [System.NonSerialized] public HUD hud;
        [System.NonSerialized] public LotResult lot;
        [System.NonSerialized] public JudgeResult lastResult;
        [System.NonSerialized] public bool showResult;

        GameObject world;
        bool initialized;

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
            hud.gm = this;
            if (config == null || string.IsNullOrEmpty(config.name)) config = ScenarioConfig.Preset(0);
            var cmd = System.Environment.GetCommandLineArgs();
            int pi = System.Array.IndexOf(cmd, "-preset");
            if (pi >= 0 && pi + 1 < cmd.Length && int.TryParse(cmd[pi + 1], out int preset)) config = ScenarioConfig.Preset(preset);
            Build();

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
            QualitySettings.shadowDistance = 80f;
        }

        /// <summary>現在の設定でコースを作り直す</summary>
        public void Build()
        {
            bool mouseSteer = car && car.mouseSteer;
            if (world)
            {
                world.SetActive(false); // 当たり判定から即座に外す
                Util.Kill(world);
            }
            world = new GameObject("World");
            lot = ParkingLotBuilder.Build(config, world.transform);

            var parts = VehicleBuilder.BuildOwnCar(world.transform);
            car = parts.gameObject.AddComponent<CarController>();
            car.Init(parts);
            car.SetPose(lot.startRearAxle, lot.startYaw);
            car.mouseSteer = mouseSteer;
            car.steerRate = steerRate;
            car.notify = (msg, col) => { if (hud) hud.Toast(msg, col); };
            car.gearChanged = OnGearChanged;

            sensors = parts.gameObject.AddComponent<ParkingSensors>();
            sensors.Init(car);
            sensors.sound = sensorSound;

            path = Util.Child("Path Guide", world.transform, Vector3.zero).AddComponent<PathGuide>();
            path.Init(car);
            path.show = showPath;

            Physics.SyncTransforms();
            rig.Attach(car, lot);
            showResult = false;
            lastResult = null;
            ApplyInputLock();
        }

        void OnGearChanged(Gear g)
        {
            if (g == Gear.P) Judge(true);
            if (hud) hud.Toast($"シフト: {g}", Color.white, 1.2f);
        }

        public void Judge(bool auto)
        {
            var r = ParkingJudge.Evaluate(lot.target, car);
            if (auto && !r.nearTarget) return;
            lastResult = r;
            showResult = true;
        }

        void Update()
        {
            if (!car) return;
            bool menu = hud && hud.menuOpen;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F1))
            {
                if (showResult && !menu) showResult = false;
                else hud.SetMenu(!menu);
            }
            if (!menu)
            {
                if (Input.GetKeyDown(KeyCode.F5) || Input.GetKeyDown(KeyCode.Backspace)) Build();
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    if (showResult) showResult = false; else Judge(false);
                }
                if (Input.GetKeyDown(KeyCode.G)) { showPath = !showPath; hud.Toast(showPath ? "予想進路線 ON" : "予想進路線 OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.K)) { rig.backMonitor = !rig.backMonitor; hud.Toast(rig.backMonitor ? "バックモニター ON" : "バックモニター OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.O)) { sensorSound = !sensorSound; hud.Toast(sensorSound ? "センサー音 ON" : "センサー音 OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.L)) hud.showGuideInfo = !hud.showGuideInfo;
                if (Input.GetKeyDown(KeyCode.H)) hud.helpOpen = !hud.helpOpen;
            }
            if (path) path.show = showPath;
            car.steerRate = steerRate;
            if (sensors) sensors.sound = sensorSound && !menu;
            ApplyInputLock();
        }

        public void ApplyInputLock()
        {
            bool locked = hud && hud.menuOpen;
            if (car) car.inputEnabled = !locked;
            if (rig) rig.inputEnabled = !locked;
        }

        /// <summary>動作確認用: -autoshot &lt;dir&gt; で各視点のスクリーンショットを保存して終了</summary>
        IEnumerator AutoShot(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            Application.runInBackground = true;
            hud.SetMenu(false);
            yield return null;
            rig.follow = false;
            rig.SetMode(ViewMode.Top2D);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "start_overview.png"));
            yield return new WaitForSecondsRealtime(0.4f);
            rig.follow = true;
            var spot = lot.target;
            var poses = new[]
            {
                (spot.ToWorld(0.1f, 0.6f), spot.yaw + 8f, Gear.R, 120f),
                (spot.ToWorld(-0.6f, 3.2f), spot.yaw + 35f, Gear.R, -200f),
            };
            int n = 0;
            foreach (var (pos, yaw, gear, wheel) in poses)
            {
                car.SetPose(CarSpec.RearFromCenter(pos, yaw), yaw);
                car.gear = gear;
                car.wheelDeg = wheel;
                foreach (ViewMode m in System.Enum.GetValues(typeof(ViewMode)))
                {
                    rig.SetMode(m);
                    yield return new WaitForSecondsRealtime(0.4f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot{n++}_{m}.png"));
                    yield return new WaitForSecondsRealtime(0.4f);
                }
            }
            car.gear = Gear.P;
            Judge(false);
            rig.SetMode(ViewMode.Top2D);
            yield return new WaitForSecondsRealtime(0.4f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot{n++}_result.png"));
            yield return new WaitForSecondsRealtime(0.4f);
            hud.SetMenu(true);
            yield return new WaitForSecondsRealtime(0.4f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot{n}_menu.png"));
            yield return new WaitForSecondsRealtime(0.4f);
            Application.Quit();
        }
    }
}
