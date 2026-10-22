using UnityEngine;

namespace ParkingSim
{
    public enum CarKind { Random, Kei, Compact, Sedan, Minivan, SUV, LargeMinivan }

    /// <summary>自車の部品参照。RenderTexture の後始末も担当する。</summary>
    public class OwnCarParts : MonoBehaviour
    {
        public Transform[] wheelSteer = new Transform[4]; // 0=左前 1=右前 2=左後 3=右後
        public Transform[] wheelSpin = new Transform[4];
        public Transform steeringWheel;
        public Renderer[] brakeLights;
        public Renderer[] reverseLights;
        public Material brakeOn, brakeOff, reverseOn, reverseOff;

        public Vector3 eyeLocal = new Vector3(0.38f, 1.17f, -0.15f); // 右ハンドル運転席の目の位置

        public Camera mirrorLeftCam, mirrorRightCam, mirrorRearCam, backCam;
        public RenderTexture rtLeft, rtRight, rtRear, rtBack;
        public Transform mirrorLeftGlass, mirrorRightGlass, mirrorRearGlass;
        public Vector3 mirrorLeftPos = new Vector3(-0.985f, 1.02f, 0.50f);
        public Vector3 mirrorRightPos = new Vector3(0.985f, 1.02f, 0.50f);
        public Vector3 mirrorRearPos = new Vector3(0.0f, 1.30f, 0.34f);

        void OnDestroy()
        {
            foreach (var rt in new[] { rtLeft, rtRight, rtRear, rtBack })
                if (rt) { rt.Release(); Util.Kill(rt); }
        }

        /// <summary>
        /// 鏡の向きを物理的に計算: 目→鏡の入射方向と、見たい方向(反射方向)から法線を決める。
        /// ミラーのカメラは鏡の位置から反射方向を写し、表示は左右反転する。
        /// </summary>
        public void AimMirror(Transform glass, Camera cam, Vector3 glassPos, float yawDeg, float pitchDeg)
        {
            Vector3 view = Quaternion.Euler(pitchDeg, yawDeg, 0f) * Vector3.forward;
            Vector3 inc = (glassPos - eyeLocal).normalized;
            Vector3 n = (view - inc).normalized; // 鏡面の法線(運転者側を向く)
            if (glass) glass.localRotation = Quaternion.LookRotation(-n, Vector3.up);
            if (cam)
            {
                cam.transform.localPosition = glassPos + view * 0.02f;
                cam.transform.localRotation = Quaternion.LookRotation(view, Vector3.up);
            }
        }
    }

    public static class VehicleBuilder
    {
        // ---------------------------------------------------------------- 自車
        public static OwnCarParts BuildOwnCar(Transform parent)
        {
            var root = new GameObject("Impreza SPORT (Own Car)") { layer = Layers.OwnBody };
            root.transform.SetParent(parent, false);
            var p = root.AddComponent<OwnCarParts>();

            Material body = Util.Mat(CarSpec.BodyColor, 0.85f, 0.15f);
            Material trim = Util.Mat(new Color(0.04f, 0.04f, 0.045f), 0.3f);
            Material glass = Util.Mat(new Color(0.07f, 0.09f, 0.11f), 0.95f);
            Material lamp = Util.Mat(new Color(0.82f, 0.86f, 0.9f), 0.95f, 0.3f);
            Material grille = Util.Mat(new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.2f);
            Material tire = Util.Mat(new Color(0.05f, 0.05f, 0.05f), 0.1f);
            Material rim = Util.Mat(new Color(0.6f, 0.62f, 0.65f), 0.7f, 0.8f);
            p.brakeOff = Util.Mat(new Color(0.45f, 0.03f, 0.03f), 0.9f);
            p.brakeOn = Util.Mat(new Color(1f, 0.12f, 0.1f), 0.9f);
            p.reverseOff = Util.Mat(new Color(0.55f, 0.56f, 0.58f), 0.9f);
            p.reverseOn = Util.Mat(new Color(1f, 1f, 0.95f), 0.9f);

            Transform t = root.transform;
            float hw = CarSpec.HalfWidth, hl = CarSpec.HalfLength;
            float zF = CarSpec.FrontAxleZ, zR = CarSpec.RearAxleZ;
            float arch = CarSpec.TireRadius + 0.08f;

            // --- 下部ボディ(ホイールアーチ部分を空ける)
            Util.BoxMinMax("Sill Center", t, new Vector3(-0.58f, 0.22f, -hl + 0.15f), new Vector3(0.58f, 0.62f, hl - 0.15f), body);
            Util.BoxMinMax("Side Front", t, new Vector3(-hw, 0.30f, zF + arch), new Vector3(hw, 0.62f, hl - 0.04f), body);
            Util.BoxMinMax("Side Middle", t, new Vector3(-hw, 0.30f, zR + arch), new Vector3(hw, 0.62f, zF - arch), body);
            Util.BoxMinMax("Side Rear", t, new Vector3(-hw, 0.30f, -hl + 0.04f), new Vector3(hw, 0.62f, zR - arch), body);
            Util.BoxMinMax("Arch Top F", t, new Vector3(-hw, 0.55f, zF - arch), new Vector3(hw, 0.66f, zF + arch), body);
            Util.BoxMinMax("Arch Top R", t, new Vector3(-hw, 0.55f, zR - arch), new Vector3(hw, 0.66f, zR + arch), body);
            Util.BoxMinMax("Bumper F", t, new Vector3(-hw + 0.05f, 0.24f, hl - 0.25f), new Vector3(hw - 0.05f, 0.60f, hl), body);
            Util.BoxMinMax("Bumper R", t, new Vector3(-hw + 0.05f, 0.27f, -hl), new Vector3(hw - 0.05f, 0.62f, -hl + 0.25f), body);
            Util.BoxMinMax("Under Trim F", t, new Vector3(-hw + 0.1f, 0.20f, hl - 0.30f), new Vector3(hw - 0.1f, 0.30f, hl - 0.02f), trim);
            Util.BoxMinMax("Under Trim R", t, new Vector3(-hw + 0.1f, 0.24f, -hl + 0.02f), new Vector3(hw - 0.1f, 0.34f, -hl + 0.30f), trim);
            Util.BoxMinMax("Skirt L", t, new Vector3(-hw + 0.01f, 0.24f, zR + arch), new Vector3(-hw + 0.06f, 0.32f, zF - arch), trim);
            Util.BoxMinMax("Skirt R", t, new Vector3(hw - 0.06f, 0.24f, zR + arch), new Vector3(hw - 0.01f, 0.32f, zF - arch), trim);

            // --- ボンネット・フェンダー・ドア・リア
            Util.Box("Hood", t, new Vector3(0f, 0.75f, 1.47f), new Vector3(1.66f, 0.22f, 1.44f), body, Quaternion.Euler(4f, 0, 0));
            Util.BoxMinMax("Fender L", t, new Vector3(-hw + 0.005f, 0.62f, 0.78f), new Vector3(-0.80f, 0.84f, hl - 0.12f), body);
            Util.BoxMinMax("Fender R", t, new Vector3(0.80f, 0.62f, 0.78f), new Vector3(hw - 0.005f, 0.84f, hl - 0.12f), body);
            Util.BoxMinMax("Door L", t, new Vector3(-hw, 0.30f, -1.30f), new Vector3(-hw + 0.08f, 0.98f, 0.80f), body);
            Util.BoxMinMax("Door R", t, new Vector3(hw - 0.08f, 0.30f, -1.30f), new Vector3(hw, 0.98f, 0.80f), body);
            Util.BoxMinMax("Rear Quarter", t, new Vector3(-hw + 0.02f, 0.62f, -hl + 0.08f), new Vector3(hw - 0.02f, 1.00f, -1.30f), body);
            Util.BoxMinMax("Cowl", t, new Vector3(-0.80f, 0.62f, 0.62f), new Vector3(0.80f, 0.90f, 0.84f), trim);

            // --- 灯火類・グリル
            Util.BoxMinMax("Grille", t, new Vector3(-0.42f, 0.44f, hl - 0.03f), new Vector3(0.42f, 0.60f, hl + 0.005f), grille);
            Util.BoxMinMax("Lower Grille", t, new Vector3(-0.55f, 0.27f, hl - 0.03f), new Vector3(0.55f, 0.38f, hl + 0.005f), grille);
            Util.Box("Headlight L", t, new Vector3(-0.60f, 0.70f, hl - 0.13f), new Vector3(0.42f, 0.10f, 0.22f), lamp, Quaternion.Euler(10, 12, 0));
            Util.Box("Headlight R", t, new Vector3(0.60f, 0.70f, hl - 0.13f), new Vector3(0.42f, 0.10f, 0.22f), lamp, Quaternion.Euler(10, -12, 0));
            var tlL = Util.Box("Taillight L", t, new Vector3(-0.66f, 0.88f, -hl + 0.10f), new Vector3(0.40f, 0.13f, 0.10f), p.brakeOff, Quaternion.Euler(0, -10, 0));
            var tlR = Util.Box("Taillight R", t, new Vector3(0.66f, 0.88f, -hl + 0.10f), new Vector3(0.40f, 0.13f, 0.10f), p.brakeOff, Quaternion.Euler(0, 10, 0));
            var hb = Util.BoxMinMax("High Mount Stop", t, new Vector3(-0.25f, 1.43f, -1.86f), new Vector3(0.25f, 1.46f, -1.80f), p.brakeOff);
            p.brakeLights = new[] { tlL.GetComponent<Renderer>(), tlR.GetComponent<Renderer>(), hb.GetComponent<Renderer>() };
            var rvL = Util.BoxMinMax("Reverse L", t, new Vector3(-0.50f, 0.46f, -hl - 0.005f), new Vector3(-0.38f, 0.52f, -hl + 0.02f), p.reverseOff);
            var rvR = Util.BoxMinMax("Reverse R", t, new Vector3(0.38f, 0.46f, -hl - 0.005f), new Vector3(0.50f, 0.52f, -hl + 0.02f), p.reverseOff);
            p.reverseLights = new[] { rvL.GetComponent<Renderer>(), rvR.GetComponent<Renderer>() };
            Util.BoxMinMax("Plate F", t, new Vector3(-0.17f, 0.30f, hl + 0.002f), new Vector3(0.17f, 0.47f, hl + 0.012f), Util.Mat(new Color(0.9f, 0.9f, 0.85f)));
            Util.BoxMinMax("Plate R", t, new Vector3(-0.17f, 0.66f, -hl - 0.012f), new Vector3(0.17f, 0.83f, -hl - 0.002f), Util.Mat(new Color(0.9f, 0.9f, 0.85f)));

            // --- キャビン外装(運転席視点では非表示)
            var cabin = Util.Child("Cabin", t, Vector3.zero);
            cabin.layer = Layers.OwnCabin;
            Transform c = cabin.transform;
            Util.BoxMinMax("Greenhouse", c, new Vector3(-0.74f, 0.98f, -1.84f), new Vector3(0.74f, 1.40f, 0.14f), glass);
            Util.Segment("Windshield", c, new Vector3(0f, 0.93f, 0.84f), new Vector3(0f, 1.43f, 0.10f), 1.42f, 0.03f, glass);
            Util.Segment("Rear Glass", c, new Vector3(0f, 1.43f, -1.80f), new Vector3(0f, 1.00f, -2.10f), 1.30f, 0.03f, glass);
            Util.BoxMinMax("Roof", c, new Vector3(-0.70f, 1.40f, -1.82f), new Vector3(0.70f, CarSpec.Height, 0.12f), body);
            Util.Segment("A Pillar L", c, new Vector3(-0.73f, 0.96f, 0.80f), new Vector3(-0.69f, 1.43f, 0.12f), 0.07f, 0.07f, body);
            Util.Segment("A Pillar R", c, new Vector3(0.73f, 0.96f, 0.80f), new Vector3(0.69f, 1.43f, 0.12f), 0.07f, 0.07f, body);
            Util.BoxMinMax("Spoiler", c, new Vector3(-0.62f, 1.42f, -1.92f), new Vector3(0.62f, 1.47f, -1.78f), body);
            // 運転席シートは視点の真後ろにあるので運転席カメラからは非表示
            BuildSeat(c, new Vector3(0.38f, 0f, -0.45f));

            BuildInterior(p, t);
            BuildMirrors(p, t, body);

            // --- タイヤ
            float rr = CarSpec.TireRadius;
            Vector3[] wp =
            {
                new Vector3(-CarSpec.TrackFront * 0.5f, rr, zF), new Vector3(CarSpec.TrackFront * 0.5f, rr, zF),
                new Vector3(-CarSpec.TrackRear * 0.5f, rr, zR), new Vector3(CarSpec.TrackRear * 0.5f, rr, zR),
            };
            for (int i = 0; i < 4; i++)
            {
                var steer = Util.Child(i < 2 ? "Wheel F" : "Wheel R", t, wp[i]);
                var spin = Util.Child("Spin", steer.transform, Vector3.zero);
                var rotZ = Quaternion.Euler(0, 0, 90);
                float side = wp[i].x < 0 ? -1 : 1;
                Util.Cylinder("Tire", spin.transform, Vector3.zero, rr, CarSpec.TireWidth, tire, rotZ);
                Util.Cylinder("Rim", spin.transform, new Vector3(side * 0.012f, 0, 0), rr * 0.66f, CarSpec.TireWidth * 0.9f, rim, rotZ);
                Util.Box("Spoke", spin.transform, new Vector3(side * 0.11f, 0, 0), new Vector3(0.01f, rr * 1.25f, 0.06f), trim);
                Util.Box("Spoke2", spin.transform, new Vector3(side * 0.11f, 0, 0), new Vector3(0.01f, 0.06f, rr * 1.25f), trim);
                p.wheelSteer[i] = steer.transform;
                p.wheelSpin[i] = spin.transform;
            }

            BuildCameras(p);
            return p;
        }

        static void BuildSeat(Transform parent, Vector3 basePos)
        {
            Material fabric = Util.Mat(new Color(0.11f, 0.11f, 0.12f), 0.1f);
            var s = Util.Child("Seat", parent, basePos).transform;
            Util.BoxMinMax("Cushion", s, new Vector3(-0.25f, 0.55f, 0.05f), new Vector3(0.25f, 0.68f, 0.55f), fabric);
            Util.Box("Back", s, new Vector3(0f, 0.98f, 0.0f), new Vector3(0.50f, 0.62f, 0.12f), fabric, Quaternion.Euler(-12f, 0, 0));
            Util.Box("Headrest", s, new Vector3(0f, 1.36f, -0.08f), new Vector3(0.28f, 0.18f, 0.10f), fabric, Quaternion.Euler(-12f, 0, 0));
        }

        static void BuildInterior(OwnCarParts p, Transform t)
        {
            var interior = Util.Child("Interior", t, Vector3.zero);
            interior.layer = Layers.OwnInterior;
            Transform it = interior.transform;
            Material dash = Util.Mat(new Color(0.10f, 0.10f, 0.11f), 0.25f);
            Material dashTop = Util.Mat(new Color(0.07f, 0.07f, 0.075f), 0.15f);
            Material pillar = Util.Mat(new Color(0.22f, 0.22f, 0.23f), 0.1f);
            Material liner = Util.Mat(new Color(0.55f, 0.55f, 0.54f), 0.05f);
            Material trimIn = Util.Mat(new Color(0.16f, 0.16f, 0.17f), 0.2f);
            Material carpet = Util.Mat(new Color(0.08f, 0.08f, 0.08f), 0.0f);
            Material metal = Util.Mat(new Color(0.55f, 0.56f, 0.6f), 0.7f, 0.8f);
            Material orange = Util.Mat(new Color(1f, 0.5f, 0.05f), 0.4f);

            Util.BoxMinMax("Dashboard", it, new Vector3(-0.80f, 0.62f, 0.30f), new Vector3(0.80f, 0.94f, 0.82f), dash);
            Util.BoxMinMax("Dash Top", it, new Vector3(-0.80f, 0.94f, 0.36f), new Vector3(0.80f, 0.965f, 0.84f), dashTop);
            Util.BoxMinMax("Meter Hood", it, new Vector3(0.20f, 0.94f, 0.40f), new Vector3(0.56f, 1.00f, 0.54f), dashTop);
            Util.BoxMinMax("Center Display", it, new Vector3(-0.12f, 0.80f, 0.27f), new Vector3(0.12f, 0.95f, 0.31f), Util.Mat(new Color(0.02f, 0.03f, 0.05f), 0.95f));
            Util.BoxMinMax("Console", it, new Vector3(-0.10f, 0.62f, -0.75f), new Vector3(0.10f, 0.78f, 0.30f), trimIn);
            Util.BoxMinMax("Floor", it, new Vector3(-0.80f, 0.62f, -1.60f), new Vector3(0.80f, 0.635f, 0.30f), carpet);
            Util.BoxMinMax("Door Trim L", it, new Vector3(-0.81f, 0.62f, -1.28f), new Vector3(-0.79f, 0.98f, 0.78f), trimIn);
            Util.BoxMinMax("Door Trim R", it, new Vector3(0.79f, 0.62f, -1.28f), new Vector3(0.81f, 0.98f, 0.78f), trimIn);
            Util.BoxMinMax("Headliner", it, new Vector3(-0.72f, 1.385f, -1.80f), new Vector3(0.72f, 1.405f, 0.12f), liner);
            Util.BoxMinMax("Roof Rail L", it, new Vector3(-0.74f, 1.31f, -1.80f), new Vector3(-0.68f, 1.40f, 0.12f), pillar);
            Util.BoxMinMax("Roof Rail R", it, new Vector3(0.68f, 1.31f, -1.80f), new Vector3(0.74f, 1.40f, 0.12f), pillar);
            Util.BoxMinMax("Header", it, new Vector3(-0.72f, 1.33f, 0.06f), new Vector3(0.72f, 1.40f, 0.14f), pillar);
            Util.Segment("A Pillar In L", it, new Vector3(-0.72f, 0.96f, 0.80f), new Vector3(-0.68f, 1.40f, 0.12f), 0.07f, 0.06f, pillar);
            Util.Segment("A Pillar In R", it, new Vector3(0.72f, 0.96f, 0.80f), new Vector3(0.68f, 1.40f, 0.12f), 0.07f, 0.06f, pillar);
            Util.BoxMinMax("B Pillar L", it, new Vector3(-0.78f, 0.98f, -0.52f), new Vector3(-0.72f, 1.40f, -0.40f), pillar);
            Util.BoxMinMax("B Pillar R", it, new Vector3(0.72f, 0.98f, -0.52f), new Vector3(0.78f, 1.40f, -0.40f), pillar);
            Util.BoxMinMax("C Pillar L", it, new Vector3(-0.78f, 0.98f, -1.84f), new Vector3(-0.70f, 1.40f, -1.36f), pillar);
            Util.BoxMinMax("C Pillar R", it, new Vector3(0.70f, 0.98f, -1.84f), new Vector3(0.78f, 1.40f, -1.36f), pillar);
            Util.BoxMinMax("Rear Header", it, new Vector3(-0.72f, 1.34f, -1.86f), new Vector3(0.72f, 1.40f, -1.76f), pillar);
            Util.BoxMinMax("Hatch Trim", it, new Vector3(-0.72f, 0.80f, -2.08f), new Vector3(0.72f, 1.00f, -1.95f), trimIn);
            BuildSeat(it, new Vector3(-0.38f, 0f, -0.45f));
            // 後席
            var rs = Util.Child("Rear Seat", it, new Vector3(0, 0, -1.30f)).transform;
            Util.BoxMinMax("Cushion", rs, new Vector3(-0.68f, 0.62f, -0.05f), new Vector3(0.68f, 0.74f, 0.45f), carpet);
            Util.Box("Back", rs, new Vector3(0f, 0.98f, -0.12f), new Vector3(1.36f, 0.56f, 0.12f), carpet, Quaternion.Euler(-14f, 0, 0));

            // --- ステアリング(運転者から見て時計回りが右切り)
            Vector3 swPos = new Vector3(p.eyeLocal.x, 0.89f, 0.30f);
            Util.Segment("Column", it, swPos + new Vector3(0, -0.04f, 0.04f), new Vector3(p.eyeLocal.x, 0.80f, 0.45f), 0.06f, 0.06f, dash);
            var pivot = Util.Child("Steering Pivot", it, swPos, Quaternion.Euler(24f, 0, 0));
            var wheel = Util.Child("Steering Wheel", pivot.transform, Vector3.zero);
            Transform sw = wheel.transform;
            const int seg = 24;
            const float radius = 0.18f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 v0 = new Vector3(Mathf.Sin(a0), Mathf.Cos(a0), 0) * radius;
                Vector3 v1 = new Vector3(Mathf.Sin(a1), Mathf.Cos(a1), 0) * radius;
                bool top = i == 0 || i == seg - 1;
                Util.Segment("Rim", sw, v0, v1, 0.03f, 0.03f, top ? orange : dash);
            }
            Util.Box("Hub", sw, new Vector3(0, -0.01f, 0.01f), new Vector3(0.13f, 0.10f, 0.05f), dash);
            Util.Segment("Spoke L", sw, new Vector3(-0.05f, 0, 0), new Vector3(-radius, -0.02f, 0), 0.04f, 0.015f, trimIn);
            Util.Segment("Spoke R", sw, new Vector3(0.05f, 0, 0), new Vector3(radius, -0.02f, 0), 0.04f, 0.015f, trimIn);
            Util.Segment("Spoke B", sw, new Vector3(0, -0.05f, 0), new Vector3(0, -radius, 0), 0.05f, 0.015f, trimIn);
            Util.Box("Emblem", sw, new Vector3(0, 0.0f, -0.017f), new Vector3(0.05f, 0.025f, 0.005f), metal);
            p.steeringWheel = sw;
        }

        static void BuildMirrors(OwnCarParts p, Transform t, Material body)
        {
            var mirrors = Util.Child("Mirrors", t, Vector3.zero);
            mirrors.layer = Layers.OwnInterior; // ミラー用カメラに自分自身が映り込まないように
            Transform m = mirrors.transform;
            Material dark = Util.Mat(new Color(0.08f, 0.08f, 0.08f), 0.3f);

            // ドアミラーのハウジングとステー
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 g = s < 0 ? p.mirrorLeftPos : p.mirrorRightPos;
                Util.Box("Mirror Housing", m, g + new Vector3(0, 0, 0.045f), new Vector3(0.23f, 0.15f, 0.08f), body);
                Util.Segment("Mirror Stay", m, g + new Vector3(-s * 0.08f, -0.04f, 0.03f), new Vector3(s * 0.83f, 0.97f, 0.62f), 0.04f, 0.05f, dark);
            }
            Util.Segment("Room Mirror Stay", m, p.mirrorRearPos + new Vector3(0, 0.02f, 0.02f), new Vector3(0, 1.40f, 0.30f), 0.02f, 0.02f, dark);
            Util.Box("Room Mirror Frame", m, p.mirrorRearPos + new Vector3(0, 0, 0.012f), new Vector3(0.28f, 0.085f, 0.02f), dark);

            p.rtLeft = NewRT(384, 230, "MirrorL");
            p.rtRight = NewRT(384, 230, "MirrorR");
            p.rtRear = NewRT(512, 148, "MirrorRear");
            p.rtBack = NewRT(640, 400, "BackCamera");

            p.mirrorLeftGlass = MirrorGlass(m, "Mirror Glass L", p.mirrorLeftPos, new Vector2(0.20f, 0.12f), p.rtLeft);
            p.mirrorRightGlass = MirrorGlass(m, "Mirror Glass R", p.mirrorRightPos, new Vector2(0.20f, 0.12f), p.rtRight);
            p.mirrorRearGlass = MirrorGlass(m, "Room Mirror Glass", p.mirrorRearPos, new Vector2(0.26f, 0.072f), p.rtRear);
        }

        static RenderTexture NewRT(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 16) { name = name, antiAliasing = 2 };
            rt.Create();
            return rt;
        }

        static Transform MirrorGlass(Transform parent, string name, Vector3 pos, Vector2 size, RenderTexture rt)
        {
            var mat = Util.NewMat(Color.white, 0.0f);
            mat.mainTexture = rt;
            mat.mainTextureScale = new Vector2(-1f, 1f); // 鏡像
            mat.mainTextureOffset = new Vector2(1f, 0f);
            return Util.Quad(name, parent, pos, size, mat, Quaternion.identity).transform;
        }

        static void BuildCameras(OwnCarParts p)
        {
            Transform t = p.transform;
            int own = Layers.OwnCarAll | Layers.Mask(Layers.Guide);

            p.mirrorLeftCam = MirrorCam(t, "Mirror Cam L", p.rtLeft, 17f, ~(Layers.Mask(Layers.OwnInterior, Layers.Guide)));
            p.mirrorRightCam = MirrorCam(t, "Mirror Cam R", p.rtRight, 17f, ~(Layers.Mask(Layers.OwnInterior, Layers.Guide)));
            p.mirrorRearCam = MirrorCam(t, "Mirror Cam Rear", p.rtRear, 9f, ~own);
            p.mirrorRearCam.transform.localPosition = new Vector3(0f, 1.30f, -1.85f);
            p.mirrorRearCam.transform.localRotation = Quaternion.Euler(3f, 180f, 0f);

            // バックカメラ(リアゲート上部, 下向き)
            p.backCam = MirrorCam(t, "Back Camera", p.rtBack, 72f, ~own);
            p.backCam.transform.localPosition = new Vector3(0.0f, 0.96f, -CarSpec.HalfLength + 0.05f);
            p.backCam.transform.localRotation = Quaternion.Euler(38f, 180f, 0f);
            p.backCam.nearClipPlane = 0.05f;

            p.AimMirror(p.mirrorLeftGlass, p.mirrorLeftCam, p.mirrorLeftPos, 188f, 4f);
            p.AimMirror(p.mirrorRightGlass, p.mirrorRightCam, p.mirrorRightPos, 172f, 4f);
            p.AimMirror(p.mirrorRearGlass, null, p.mirrorRearPos, 180f, 2f);
        }

        static Camera MirrorCam(Transform parent, string name, RenderTexture rt, float fov, int mask)
        {
            var go = Util.Child(name, parent, Vector3.zero);
            var cam = go.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 250f;
            cam.cullingMask = mask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.75f, 0.9f);
            cam.enabled = false;
            return cam;
        }

        // ---------------------------------------------------------------- 他車
        public struct DummySpec
        {
            public float length, width, height, belt, cabinFront, cabinRear;
            public string label;
        }

        public static DummySpec Spec(CarKind k)
        {
            switch (k)
            {
                case CarKind.Kei: return new DummySpec { label = "軽", length = 3.395f, width = 1.475f, height = 1.75f, belt = 0.95f, cabinFront = 0.70f, cabinRear = 0.12f };
                case CarKind.Compact: return new DummySpec { label = "コンパクト", length = 3.995f, width = 1.695f, height = 1.50f, belt = 0.90f, cabinFront = 1.05f, cabinRear = 0.20f };
                case CarKind.Sedan: return new DummySpec { label = "セダン", length = 4.69f, width = 1.80f, height = 1.44f, belt = 0.88f, cabinFront = 1.45f, cabinRear = 0.95f };
                case CarKind.Minivan: return new DummySpec { label = "ミニバン", length = 4.695f, width = 1.695f, height = 1.85f, belt = 1.00f, cabinFront = 0.80f, cabinRear = 0.10f };
                case CarKind.SUV: return new DummySpec { label = "SUV", length = 4.625f, width = 1.855f, height = 1.69f, belt = 1.00f, cabinFront = 1.15f, cabinRear = 0.18f };
                default: return new DummySpec { label = "大型ミニバン", length = 4.995f, width = 1.85f, height = 1.935f, belt = 1.05f, cabinFront = 0.85f, cabinRear = 0.10f };
            }
        }

        static readonly Color[] Palette =
        {
            new Color(0.05f, 0.05f, 0.06f), new Color(0.62f, 0.63f, 0.65f), new Color(0.92f, 0.92f, 0.92f),
            new Color(0.55f, 0.06f, 0.07f), new Color(0.10f, 0.20f, 0.45f), new Color(0.32f, 0.33f, 0.35f),
            new Color(0.70f, 0.64f, 0.52f), new Color(0.12f, 0.28f, 0.20f), new Color(0.35f, 0.55f, 0.75f),
        };

        public static CarKind PickKind(CarKind k, System.Random rng)
        {
            if (k != CarKind.Random) return k;
            double r = rng.NextDouble();
            if (r < 0.25) return CarKind.Kei;
            if (r < 0.45) return CarKind.Compact;
            if (r < 0.60) return CarKind.Sedan;
            if (r < 0.80) return CarKind.Minivan;
            if (r < 0.93) return CarKind.SUV;
            return CarKind.LargeMinivan;
        }

        /// <summary>駐車中の他車(原点=車体中心の地面, +Z=前)</summary>
        public static GameObject BuildDummy(Transform parent, CarKind kind, System.Random rng, out DummySpec s)
        {
            s = Spec(kind);
            var root = new GameObject("Parked " + s.label) { layer = Layers.Obstacle };
            root.transform.SetParent(parent, false);
            Transform t = root.transform;
            Color col = Palette[rng.Next(Palette.Length)];
            Material body = Util.Mat(col, 0.75f, 0.2f);
            Material glass = Util.Mat(new Color(0.07f, 0.09f, 0.11f), 0.95f);
            Material tire = Util.Mat(new Color(0.05f, 0.05f, 0.05f), 0.1f);
            Material red = Util.Mat(new Color(0.5f, 0.03f, 0.03f), 0.8f);
            Material lamp = Util.Mat(new Color(0.85f, 0.88f, 0.9f), 0.9f);

            float hw = s.width / 2, hl = s.length / 2;
            float zf = hl - s.length * 0.21f, zr = -hl + s.length * 0.19f;
            Util.BoxMinMax("Body", t, new Vector3(-hw, 0.28f, -hl), new Vector3(hw, s.belt, hl), body);
            Util.BoxMinMax("Glass", t, new Vector3(-hw + 0.07f, s.belt, -hl + s.cabinRear), new Vector3(hw - 0.07f, s.height - 0.05f, hl - s.cabinFront), glass);
            Util.BoxMinMax("Roof", t, new Vector3(-hw + 0.10f, s.height - 0.06f, -hl + s.cabinRear + 0.10f), new Vector3(hw - 0.10f, s.height, hl - s.cabinFront - 0.25f), body);
            Util.BoxMinMax("Light L", t, new Vector3(-hw + 0.08f, s.belt - 0.18f, hl - 0.05f), new Vector3(-hw + 0.40f, s.belt - 0.06f, hl + 0.005f), lamp);
            Util.BoxMinMax("Light R", t, new Vector3(hw - 0.40f, s.belt - 0.18f, hl - 0.05f), new Vector3(hw - 0.08f, s.belt - 0.06f, hl + 0.005f), lamp);
            Util.BoxMinMax("Tail L", t, new Vector3(-hw + 0.06f, s.belt - 0.20f, -hl - 0.005f), new Vector3(-hw + 0.32f, s.belt - 0.06f, -hl + 0.05f), red);
            Util.BoxMinMax("Tail R", t, new Vector3(hw - 0.32f, s.belt - 0.20f, -hl - 0.005f), new Vector3(hw - 0.06f, s.belt - 0.06f, -hl + 0.05f), red);
            foreach (float sx in new[] { -1f, 1f })
            {
                Util.Box("Mirror", t, new Vector3(sx * (hw + 0.08f), s.belt + 0.05f, hl - s.cabinFront - 0.15f), new Vector3(0.16f, 0.12f, 0.08f), body);
                foreach (float z in new[] { zf, zr })
                    Util.Cylinder("Tire", t, new Vector3(sx * (hw - 0.17f), 0.31f, z), 0.31f, 0.20f, tire, Quaternion.Euler(0, 0, 90));
            }

            var bc = root.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, 0.2f + (s.height - 0.2f) / 2, 0);
            bc.size = new Vector3(s.width, s.height - 0.2f, s.length);
            return root;
        }
    }
}
