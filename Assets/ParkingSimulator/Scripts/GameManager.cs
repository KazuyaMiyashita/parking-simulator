using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkingSim
{
    /// <summary>運転中の画面の上に重ねて開く画面。開いている間はゲームの時間を止める</summary>
    public enum Overlay { None, Menu, CarSelect, Help, Map }

    /// <summary>駐車枠に収まって P に入れたときの表示内容</summary>
    public class Arrival
    {
        public Place place;        // null なら目的地ではない枠(縦列駐車帯など)
        public bool first;         // その場所に初めて停めた
        public bool unlocked;      // これで遠くのエリアに行けるようになった
        public bool cleared;       // これで全部の場所を回った
        public int parkings;
        public float distance;
    }

    /// <summary>
    /// ゲーム全体の管理。空のシーンでも再生すると自動で生成される。
    /// 自宅のカーポートに P で停まった状態から始まり、マップで目的地を決めて(決めなくてもよい)運転する。
    /// 駐車枠に収まって P に入れると駐車回数と総走行距離を記録する。点数は付けない。
    /// </summary>
    [DefaultExecutionOrder(100)] // 車の入力処理より後
    public class GameManager : MonoBehaviour
    {
        [System.NonSerialized] public SaveData save;
        [System.NonSerialized] public CarController car;
        [System.NonSerialized] public ParkingSensors sensors;
        [System.NonSerialized] public PathGuide path;
        [System.NonSerialized] public ClearanceProbe clearance;
        [System.NonSerialized] public CameraRig rig;
        [System.NonSerialized] public HUD hud;
        [System.NonSerialized] public WorldResult world;
        [System.NonSerialized] public MapView map;
        [System.NonSerialized] public Overlay overlay;
        [System.NonSerialized] public Arrival arrival;

        // 目的地と経路(カーナビ)
        [System.NonSerialized] public Place destination;
        [System.NonSerialized] public readonly List<Vector3> route = new List<Vector3>();
        [System.NonSerialized] public float routeLength;
        /// <summary>マップに並べる順(近くの目的地 → 遠くの目的地 → 自宅)</summary>
        [System.NonSerialized] public readonly List<Place> mapPlaces = new List<Place>();
        [System.NonSerialized] public readonly NavGuide guide = new NavGuide();
        Navigator nav;
        DestinationMarker marker;
        CarAudio carAudio;
        float routeTimer;
        bool guideStarted;
        readonly List<Vector3> routeTmp = new List<Vector3>();
        /// <summary>目的地の敷地の入口に着いて、案内を終えた(あとは空いている枠に停めるだけ)</summary>
        [System.NonSerialized] public bool guideEnded;

        // メニュー
        [System.NonSerialized] public readonly List<MenuItem> menu = new List<MenuItem>();
        [System.NonSerialized] public int menuCursor, carCursor;
        [System.NonSerialized] public bool resetArmed;
        public const int CarSlots = 3;

        float lastTravelled, travelledAtPark;
        bool initialized;

        /// <summary>同じ場所でエンジンを切り直しても数えないよう、前回の駐車からこれだけ走ったときだけ数える</summary>
        const float MinMoveBetweenParks = 3f;
        static readonly string[] PlaceOrder = { "conveni", "super", "shop1", "friend", "shop2", "shop3", "home" };

        public class MenuItem
        {
            public string label;
            public System.Func<string> value;
            public System.Action confirm;
            public System.Action<int> adjust; // 左右
        }

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
            save = SaveData.Load();

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
            BuildMenu();

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
            foreach (var id in PlaceOrder)
            {
                var p = world.PlaceById(id);
                if (p != null) mapPlaces.Add(p);
            }

            var parts = VehicleBuilder.BuildOwnCar(root.transform);
            car = parts.gameObject.AddComponent<CarController>();
            car.Init(parts);
            car.notify = (msg, col) => { if (hud) hud.Toast(msg, col); };
            car.gearChanged = OnGearChanged;

            sensors = parts.gameObject.AddComponent<ParkingSensors>();
            sensors.Init(car);
            clearance = parts.gameObject.AddComponent<ClearanceProbe>();
            clearance.Init(car);

            // 警告音の AudioSource と分けるため子オブジェクトに付ける(OnAudioFilterRead は同じオブジェクトの音源に掛かる)
            carAudio = Util.Child("Car Audio", parts.transform, Vector3.zero).AddComponent<CarAudio>();
            carAudio.Init(car);

            path = Util.Child("Path Guide", root.transform, Vector3.zero).AddComponent<PathGuide>();
            path.Init(car);

            map = new MapView(transform);
            nav = new Navigator(world);
            marker = Util.Child("Destination Marker", root.transform, Vector3.zero).AddComponent<DestinationMarker>();
            marker.Init();
            UpdateGates();

            Physics.SyncTransforms();
            rig.Attach(car);
            GoHome();
        }

        /// <summary>自宅のカーポートに P で停まった状態に戻す</summary>
        public void GoHome()
        {
            car.Spawn(world.homeRearAxle, world.homeYaw);
            arrival = null;
            lastTravelled = travelledAtPark = car.travelled;
            rig.Snap();
        }

        void OnDestroy()
        {
            map?.Release();
            Speech.Stop();
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void OnApplicationQuit() => save?.Save();

        // ---------------------------------------------------------------- 進み具合
        /// <summary>近くの目的地(自宅以外)を全部回ると、遠くのエリアに行ける</summary>
        public bool FarUnlocked
        {
            get
            {
                foreach (var p in world.places)
                    if (!p.home && !p.far && !save.Visited(p)) return false;
                return true;
            }
        }

        public bool Locked(Place p) => p.far && !FarUnlocked;

        bool AllVisited
        {
            get
            {
                bool anyFar = false;
                foreach (var p in world.places)
                {
                    anyFar |= p.far;
                    if (!p.home && !save.Visited(p)) return false;
                }
                return anyFar;
            }
        }

        void UpdateGates()
        {
            bool open = FarUnlocked;
            foreach (var g in world.gates) if (g) g.SetActive(!open);
        }

        void OnGearChanged(Gear g)
        {
            if (hud) hud.Toast($"シフト: {g}", Color.white, 1.2f);
            if (g == Gear.P) CheckParking();
            else arrival = null;
        }

        /// <summary>P に入れたとき、駐車枠に収まっていれば記録して到着を表示する</summary>
        void CheckParking()
        {
            var fit = ParkingCheck.Find(world, car, out var spot);
            if (fit == ParkingCheck.Fit.Partial)
            {
                hud.Toast("車体が枠からはみ出しています", new Color(1f, 0.8f, 0.3f), 4f);
                return;
            }
            if (fit != ParkingCheck.Fit.Inside || car.travelled - travelledAtPark < MinMoveBetweenParks) return;

            travelledAtPark = car.travelled;
            bool wasUnlocked = FarUnlocked;
            var a = new Arrival { place = spot.place };
            save.parkings++;
            if (a.place != null && !save.Visited(a.place))
            {
                a.first = true;
                save.visited.Add(a.place.id);
            }
            if (a.place != null && a.place == destination) SetDestination(null);
            a.unlocked = !wasUnlocked && FarUnlocked;
            if (a.unlocked) UpdateGates();
            if (!save.cleared && AllVisited) save.cleared = a.cleared = true;
            a.parkings = save.parkings;
            a.distance = save.distance;
            arrival = a;
            save.Save();
        }

        // ---------------------------------------------------------------- 目的地
        public void SetDestination(Place p)
        {
            destination = p;
            marker.Show(p);
            route.Clear();
            routeLength = 0f;
            routeTimer = 0f;
            guide.Reset();
            guideStarted = p != null;
            guideEnded = false;
            if (p == null) Speech.Stop();
            else UpdateRoute();
        }

        void UpdateRoute()
        {
            string say = StepGuide();
            if (say != null && overlay == Overlay.None) Speech.Say(say, save.volume);
        }

        /// <summary>経路を引き直し、話すべき案内があれば返す</summary>
        string StepGuide()
        {
            if (destination == null || guideEnded) return null;
            RouteToDestination();
            string say;
            if (routeLength < ArriveDist)
            {
                // 敷地に入ったら案内を終える(駐車場の中までは案内しない)
                guideEnded = true;
                route.Clear();
                say = "目的地に到着しました。案内を終了します。";
            }
            else
            {
                say = guide.Update(route, routeLength, NearDestination, guideStarted);
                guideStarted = false;
            }
            return say;
        }

        /// <summary>入口がいくつかあるときは、道のりが短い方へ案内する</summary>
        void RouteToDestination()
        {
            routeLength = float.MaxValue;
            foreach (var e in destination.entrances)
            {
                float len = nav.Route(car.Center, e, routeTmp);
                if (routeTmp.Count == 0 || len >= routeLength) continue;
                routeLength = len;
                route.Clear();
                route.AddRange(routeTmp);
            }
            if (routeLength == float.MaxValue) { routeLength = 0f; route.Clear(); }
        }

        const float ArriveDist = 4f, NearDist = 40f;

        /// <summary>目的地の入口の近くまで来た(経路の残りで見る)</summary>
        public bool NearDestination => destination != null && !guideEnded && route.Count > 1 && routeLength < NearDist;

        // ---------------------------------------------------------------- 画面
        public void SetOverlay(Overlay o)
        {
            if (o != Overlay.Menu) resetArmed = false;
            if (o == Overlay.Map && overlay != Overlay.Map)
            {
                map.Render();
                int i = destination != null ? mapPlaces.IndexOf(destination) : -1;
                if (i < 0) i = mapPlaces.FindIndex(p => !save.Visited(p) && !Locked(p));
                map.cursor = Mathf.Max(0, i);
            }
            if (o == Overlay.CarSelect) carCursor = save.carIndex;
            if (overlay == Overlay.Menu && o == Overlay.None) save.Save();
            overlay = o;
            bool paused = o != Overlay.None;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            carAudio.paused = paused;
            if (paused) Speech.Stop();
            car.inputEnabled = rig.inputEnabled = !paused;
        }

        void BuildMenu()
        {
            string OnOff(bool v) => v ? "ON" : "OFF";
            void Add(string label, System.Func<string> value, System.Action confirm, System.Action<int> adjust = null) =>
                menu.Add(new MenuItem { label = label, value = value, confirm = confirm, adjust = adjust });

            Add("運転に戻る", null, () => SetOverlay(Overlay.None));
            Add("車の選択", () => CarNames[save.carIndex], () => SetOverlay(Overlay.CarSelect));
            Add("クリープ現象", () => OnOff(save.creep), () => save.creep = !save.creep, _ => save.creep = !save.creep);
            Add("センサー音", () => OnOff(save.sensorSound), () => save.sensorSound = !save.sensorSound, _ => save.sensorSound = !save.sensorSound);
            Add("他の車などとの距離(3D)", () => OnOff(save.clearance3D), () => save.clearance3D = !save.clearance3D, _ => save.clearance3D = !save.clearance3D);
            Add("音量", () => $"{Mathf.RoundToInt(save.volume * 100)}%",
                () => save.volume = save.volume >= 0.99f ? 0f : Mathf.Min(1f, save.volume + 0.1f),
                d => save.volume = Mathf.Clamp01(Mathf.Round((save.volume + d * 0.1f) * 10f) / 10f));
            Add("操作説明", null, () => SetOverlay(Overlay.Help));
            Add("自宅に戻る", null, () => { GoHome(); SetOverlay(Overlay.None); });
            Add("記録をリセット", () => resetArmed ? "もう一度決定するとリセットします" : "",
                () =>
                {
                    if (!resetArmed) { resetArmed = true; return; }
                    resetArmed = false;
                    save.ResetRecords();
                    save.Save();
                    UpdateGates();
                    hud.Toast("記録をリセットしました", Color.white);
                });
            Add("ゲームを終了", null, () => { save.Save(); Application.Quit(); });
        }

        /// <summary>車の選択の枠(2台目以降は中身なし)</summary>
        public static readonly string[] CarNames = { "インプレッサ SPORT", "(空き)", "(空き)" };

        void Update()
        {
            if (!car) return;
            save.distance += car.travelled - lastTravelled;
            lastTravelled = car.travelled;

            switch (overlay)
            {
                case Overlay.None:
                    if (Controls.MenuToggle) { menuCursor = 0; SetOverlay(Overlay.Menu); }
                    else if (Controls.MapToggle)
                    {
                        if (Mathf.Abs(car.speed) < 0.05f) SetOverlay(Overlay.Map);
                        else hud.Toast("マップは止まっているときに開けます", new Color(1f, 0.8f, 0.3f));
                    }
                    else if (arrival != null && (Mathf.Abs(car.speed) > 0.05f || Controls.Confirm)) arrival = null;
                    break;
                case Overlay.Menu:
                    if (Controls.MenuToggle || Controls.Back) SetOverlay(Overlay.None);
                    else MenuInput();
                    break;
                case Overlay.CarSelect:
                    if (Controls.MenuToggle || Controls.Back) SetOverlay(Overlay.Menu);
                    else CarSelectInput();
                    break;
                case Overlay.Help:
                    if (Controls.MenuToggle || Controls.Back || Controls.Confirm) SetOverlay(Overlay.Menu);
                    break;
                case Overlay.Map:
                    if (Controls.MapToggle || Controls.MenuToggle || Controls.Back) SetOverlay(Overlay.None);
                    else MapInput();
                    break;
            }

            car.creepEnabled = save.creep;
            sensors.sound = save.sensorSound && overlay == Overlay.None;
            clearance.show = save.clearance3D;
            AudioListener.volume = save.volume;

            if (destination != null && overlay == Overlay.None)
            {
                routeTimer -= Time.deltaTime;
                if (routeTimer <= 0f) { routeTimer = 0.5f; UpdateRoute(); }
            }
        }

        void MenuInput()
        {
            if (Controls.NavY != 0)
            {
                menuCursor = (menuCursor - Controls.NavY + menu.Count) % menu.Count;
                resetArmed = false;
            }
            var item = menu[menuCursor];
            if (Controls.NavX != 0) item.adjust?.Invoke(Controls.NavX);
            if (Controls.Confirm) item.confirm?.Invoke();
        }

        void CarSelectInput()
        {
            if (Controls.NavX != 0) carCursor = Mathf.Clamp(carCursor + Controls.NavX, 0, CarSlots - 1);
            if (!Controls.Confirm) return;
            if (carCursor == 0)
            {
                save.carIndex = 0;
                SetOverlay(Overlay.Menu);
            }
            else hud.Toast("この枠はまだ選べません", new Color(1f, 0.8f, 0.3f));
        }

        void MapInput()
        {
            int rows = mapPlaces.Count + 1; // 最後の行は「目的地を設定しない」
            if (Controls.NavY != 0)
            {
                // 行けない場所は飛ばす
                for (int k = 0; k < rows; k++)
                {
                    map.cursor = (map.cursor - Controls.NavY + rows) % rows;
                    if (map.cursor >= mapPlaces.Count || !Locked(mapPlaces[map.cursor])) break;
                }
            }
            if (!Controls.Confirm) return;
            var p = map.cursor < mapPlaces.Count ? mapPlaces[map.cursor] : null;
            SetDestination(p);
            hud.Toast(p != null ? $"目的地: {p.name}" : "目的地を設定せずに走ります", new Color(1f, 0.7f, 0.3f), 3f);
            SetOverlay(Overlay.None);
        }

        /// <summary>動作確認用: -autoshot &lt;dir&gt; で主な画面のスクリーンショットを保存して終了(記録は保存しない)</summary>
        IEnumerator AutoShot(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            Application.runInBackground = true;
            save = new SaveData { readOnly = true, volume = 0f };
            yield return null;
            IEnumerator Shot(string name)
            {
                yield return new WaitForSecondsRealtime(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
                yield return new WaitForSecondsRealtime(0.3f);
            }

            // 全体図
            rig.SetMode(ViewMode.Top2D);
            rig.fixedCenter = new Vector3(37f, 0, 0);
            float size = rig.orthoSize;
            rig.orthoSize = 95f;
            rig.Snap();
            yield return Shot("town");
            rig.fixedCenter = new Vector3(-30f, 0, -30f);
            rig.orthoSize = 30f;
            rig.Snap();
            yield return Shot("home_shop1");
            rig.fixedCenter = null;
            rig.orthoSize = size;

            // 自宅(3つの視点)
            foreach (var m in new[] { ViewMode.Chase3D, ViewMode.Driver3D, ViewMode.Top2D })
            {
                rig.SetMode(m);
                yield return Shot("home_" + m);
            }
            rig.SetMode(ViewMode.Chase3D);

            // マップでコンビニを選ぶ
            SetOverlay(Overlay.Map);
            map.cursor = mapPlaces.FindIndex(p => p.id == "conveni");
            SetDestination(mapPlaces[map.cursor]);
            yield return Shot("map");
            SetOverlay(Overlay.None);
            yield return Shot("trip_chase");

            // コンビニの空いている枠にバックで停めて P に入れる
            var conv = world.PlaceById("conveni");
            var spot = conv.spots.Find(s => s.destination);
            car.Spawn(Kit.ParkedRearAxle(spot, true), spot.yaw);
            car.travelled += 100f;
            Physics.SyncTransforms();
            rig.Snap();
            car.TryShift(Gear.D);
            car.TryShift(Gear.P);
            yield return Shot("arrival");
            arrival = null;

            // バックカメラと距離表示
            save.clearance3D = true;
            car.TryShift(Gear.R);
            yield return Shot("reverse_chase");
            rig.SetMode(ViewMode.Driver3D);
            yield return Shot("reverse_driver");
            rig.SetMode(ViewMode.Chase3D);
            save.clearance3D = false;

            // カーナビ: 自宅からスーパーまでの経路に沿って車を動かし、案内の文を記録する
            GoHome();
            SetDestination(world.PlaceById("super"));
            var path = new List<Vector3>(route);
            float walked = 0f, shotAt = 60f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                float len = Vector3.Distance(a, b);
                for (float t = 0f; t < len; t += 3f)
                {
                    Vector3 p = Vector3.Lerp(a, b, t / len);
                    float yaw = Kit.YawOf(b - a);
                    car.SetPose(p - CarSpec.Forward(yaw) * CarSpec.RearAxleToCenter, yaw);
                    string say = StepGuide();
                    var nx = guide.Next;
                    if (say != null)
                        Debug.Log($"[Nav] {walked:0}m 地点 ({p.x:0},{p.z:0}) 次={(nx.HasValue ? NavGuide.TurnWord(nx.Value.turn) + $" {nx.Value.along:0}m" : "なし")} 残り={routeLength:0}m: {say}");
                    walked += 3f;
                    if (walked >= shotAt && shotAt > 0f)
                    {
                        shotAt = -1f;
                        Physics.SyncTransforms();
                        rig.Snap();
                        yield return Shot("nav");
                    }
                }
            }

            menuCursor = 0;
            SetOverlay(Overlay.Menu);
            yield return Shot("menu");
            SetOverlay(Overlay.CarSelect);
            yield return Shot("car_select");
            SetOverlay(Overlay.Help);
            yield return Shot("help");
            SetOverlay(Overlay.None);
            Application.Quit();
        }
    }
}
