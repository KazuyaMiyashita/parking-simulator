using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkingSim
{
    /// <summary>
    /// シミュレーター全体の管理。空のシーンでも再生すると自動で生成される。
    /// 世界は起動時に一度だけ作り、やり直しは選んだスタート地点へ車を置き直すだけ。
    /// </summary>
    [DefaultExecutionOrder(100)] // 車の入力処理より後
    public class GameManager : MonoBehaviour
    {
        [Header("補助機能")]
        public bool showPath = true;
        public bool sensorSound = true;
        public bool showSensors = true;
        public bool creep;
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

        // 目的地ゲーム
        [System.NonSerialized] public MapView map;
        [System.NonSerialized] public TargetSpot destination; // マップで選んだ目的地(null=自由に練習)
        [System.NonSerialized] public int arrivals;           // 目的地に駐車できた回数
        public readonly Dictionary<TargetSpot, int> bestScores = new Dictionary<TargetSpot, int>();
        DestinationMarker marker;
        ParkingJudge.Baseline tripBase, destBase; // 目的地を決めた時点 / 目的地に近づいた時点の記録
        float tripStartDistance;

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
            car.engineChanged = OnEngineChanged;

            sensors = parts.gameObject.AddComponent<ParkingSensors>();
            sensors.Init(car);

            // 警告音の AudioSource と分けるため子オブジェクトに付ける(OnAudioFilterRead は同じオブジェクトの音源に掛かる)
            Util.Child("Car Audio", parts.transform, Vector3.zero).AddComponent<CarAudio>().Init(car);

            path = Util.Child("Path Guide", root.transform, Vector3.zero).AddComponent<PathGuide>();
            path.Init(car);

            map = new MapView(transform, world);
            marker = Util.Child("Destination Marker", root.transform, Vector3.zero).AddComponent<DestinationMarker>();
            marker.Init(car);

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
            // 目的地はそのままで、スタート地点から数え直す
            tripBase = destBase = baseline;
            tripStartDistance = car.travelled;
            rig.Snap();
            ApplyInputLock();
        }

        void OnDestroy()
        {
            map?.Release();
        }

        // ---------------------------------------------------------------- 目的地ゲーム
        public void SetDestination(TargetSpot s)
        {
            destination = s;
            marker.Show(s);
            tripBase = destBase = ParkingJudge.Baseline.Of(car);
            tripStartDistance = car.travelled;
            if (s != null && hud) hud.Toast($"目的地: {s.name}", new Color(1f, 0.7f, 0.3f), 3f);
        }

        public void OpenMap(bool open)
        {
            if (!hud || map == null) return;
            hud.mapOpen = open;
            if (open)
            {
                showResult = false;
                map.Render();
                int i = destination != null ? map.destinations.IndexOf(destination) : -1;
                if (i >= 0) map.cursor = i;
                else map.SelectNearest(car.Center);
            }
            hud.ApplyPause();
            ApplyInputLock();
        }

        public void ConfirmMap()
        {
            if (map.Selected == null) return;
            SetDestination(map.Selected);
            OpenMap(false);
        }

        /// <summary>今いる場所から 30m 以上離れた候補からランダムに選ぶ</summary>
        public void PickRandom()
        {
            var far = new List<int>();
            for (int i = 0; i < map.destinations.Count; i++)
                if ((map.destinations[i].center - car.Center).sqrMagnitude > 30f * 30f && map.destinations[i] != destination) far.Add(i);
            if (far.Count > 0) map.cursor = far[Random.Range(0, far.Count)];
        }

        void MapInput()
        {
            Vector2 dir = Pad.LeftStick + Pad.RightStick;
            if (Pad.Held(JoyButton.Up) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) dir.y += 1f;
            if (Pad.Held(JoyButton.Down) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) dir.y -= 1f;
            if (Pad.Held(JoyButton.Right) || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) dir.x += 1f;
            if (Pad.Held(JoyButton.Left) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) dir.x -= 1f;
            map.Steer(dir);
            if (Pad.Down(JoyButton.A) || Pad.Down(JoyButton.LStick) || Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                ConfirmMap();
            else if (Pad.Down(JoyButton.X) || Input.GetKeyDown(KeyCode.R)) PickRandom();
            else if (Pad.Down(JoyButton.Y) || Input.GetKeyDown(KeyCode.Delete))
            {
                if (destination != null) hud.Toast("目的地を取り消しました", Color.white);
                SetDestination(null);
            }
            else if (Pad.Down(JoyButton.B)) OpenMap(false);
        }

        /// <summary>目的地に停めてエンジンを切った(または Enter で判定した)</summary>
        void FinishTrip()
        {
            var r = ParkingJudge.Evaluate(destination, car, destBase);
            r.trip = true;
            r.tripTime = car.driveTime - tripBase.time;
            r.tripDistance = car.travelled - tripStartDistance;
            r.tripCollisions = car.collisions - tripBase.collisions;
            r.tripCurbHits = car.curbHits - tripBase.curbHits;
            r.prevBest = bestScores.TryGetValue(destination, out int best) ? best : -1;
            if (r.score > r.prevBest) bestScores[destination] = r.score;
            arrivals++;
            lastResult = r;
            showResult = true;
            destination = null;
            marker.Show(null);
        }

        void OnEngineChanged(bool on)
        {
            if (!on) Judge(true); // エンジンを止めたら近くの駐車枠で自動判定
        }

        void OnGearChanged(Gear g)
        {
            if (hud) hud.Toast($"シフト: {g}", Color.white, 1.2f);
        }

        public void Judge(bool auto)
        {
            if (destination != null && ParkingJudge.Measure(destination, car).nearTarget)
            {
                FinishTrip();
                return;
            }
            if (nearSpot == null)
            {
                if (!auto && hud) hud.Toast("近くに駐車枠がありません", Color.white);
                return;
            }
            var r = ParkingJudge.Evaluate(nearSpot, car, baseline);
            if (auto && !r.nearTarget) return;
            if (destination != null && r.nearTarget) r.notes.Insert(0, $"ここは目的地({destination.name})ではありません");
            lastResult = r;
            showResult = true;
        }

        void UpdateNearSpot()
        {
            // 目的地の近くでは目的地を優先する(位置ガイドも目的地に対して出す)
            bool nearDest = destination != null && (destination.center - car.Center).sqrMagnitude < NearRange * NearRange;
            var s = nearDest ? destination : world.NearestSpot(car.Center, NearRange);
            if (destination != null && !nearDest) destBase = ParkingJudge.Baseline.Of(car);
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
            bool mapOpen = hud && hud.mapOpen;
            // Esc / F1 / Joy-Con(L) のキャプチャーボタン: 設定メニュー(マップが開いていればマップを閉じる)
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F1) || Pad.Down(JoyButton.Capture))
            {
                if (mapOpen) OpenMap(false);
                else if (showResult && !menu) showResult = false;
                else hud.SetMenu(!menu);
            }
            // N / Joy-Con(L) の − ボタン: マップ
            else if (!menu && (Input.GetKeyDown(KeyCode.N) || Pad.Down(JoyButton.Minus))) OpenMap(!mapOpen);
            else if (mapOpen && !menu) MapInput();
            menu = hud && hud.menuOpen;
            mapOpen = hud && hud.mapOpen;
            if (!menu && !mapOpen)
            {
                if (Input.GetKeyDown(KeyCode.F5) || Input.GetKeyDown(KeyCode.Backspace)) Respawn();
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    if (showResult) showResult = false; else Judge(false);
                }
                // 判定結果はエンジンをかけるか、車が動き出したら自動で閉じる
                if (showResult && (car.engineOn || Mathf.Abs(car.speed) > 0.05f)) showResult = false;
                if (Input.GetKeyDown(KeyCode.G)) { showPath = !showPath; hud.Toast(showPath ? "予想進路線 ON" : "予想進路線 OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.K)) { rig.backMonitor = !rig.backMonitor; hud.Toast(rig.backMonitor ? "バックモニター ON" : "バックモニター OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.O)) { sensorSound = !sensorSound; hud.Toast(sensorSound ? "センサー音 ON" : "センサー音 OFF", Color.white); }
                if (Input.GetKeyDown(KeyCode.L)) hud.showGuideInfo = !hud.showGuideInfo;
                if (Input.GetKeyDown(KeyCode.H)) hud.helpOpen = !hud.helpOpen;
            }
            if (path) path.show = showPath;
            car.steerRate = steerRate;
            car.creepEnabled = creep;
            if (sensors) sensors.sound = sensorSound && !menu;
            ApplyInputLock();
        }

        public void ApplyInputLock()
        {
            bool locked = hud && (hud.menuOpen || hud.mapOpen);
            if (car) car.inputEnabled = !locked;
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
            foreach (var (name, center) in new[] { ("map_ne", new Vector3(80f, 0, 38f)), ("map_se", new Vector3(80f, 0, -38f)),
                                                    ("map_nw", new Vector3(-38f, 0, 38f)), ("map_sw", new Vector3(-38f, 0, -38f)) })
            {
                rig.fixedCenter = center;
                rig.orthoSize = 40f;
                rig.Snap();
                yield return new WaitForSecondsRealtime(0.6f);
                ScreenCapture.CaptureScreenshot(Path(name + ".png"));
                yield return new WaitForSecondsRealtime(0.4f);
            }
            rig.fixedCenter = new Vector3(37f, 0, 0);
            rig.orthoSize = 95f;
            rig.Snap();
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(Path("map_town.png"));
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
            car.SetEngine(true);
            car.TryShift(Gear.R);
            car.TryShift(Gear.P);
            car.SetEngine(false);
            rig.SetMode(ViewMode.Top2D);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path("home_judge.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            showResult = false;

            // マップと目的地
            OpenMap(true);
            map.cursor = map.destinations.FindIndex(s => s.name.StartsWith("コンビニ"));
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path("map_select.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            ConfirmMap();
            rig.SetMode(ViewMode.Top2D);
            rig.wide = true;
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path("trip_top.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            rig.wide = false;
            var dest = destination;
            car.Spawn(Kit.ParkedRearAxle(dest, true), dest.yaw);
            Physics.SyncTransforms();
            rig.SetMode(ViewMode.Chase3D);
            car.SetEngine(true);
            car.SetEngine(false);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path("trip_result.png"));
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
