using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 公道(東西の道路A × 南北の道路B の十字路)と沿道。左側通行・片側1車線。
    /// 座標: x=東, z=北。交差点の中心が原点。
    /// </summary>
    public static class TownBuilder
    {
        public const float HalfRoad = 3.75f;               // 車道の半幅(車線3.25 + 路肩0.5)
        public const float SideW = 3.0f;                   // 歩道の幅
        public const float Walk = HalfRoad + SideW;        // 歩道の外側 6.75
        const float CornerR = 6f;                          // 交差点の隅切りの半径
        const float CornerC = HalfRoad + CornerR;          // 9.75
        public const float AWest = -150f, AEast = 150f, BSouth = -120f, BNorth = 160f;
        const float CrossNear = 10f, CrossFar = 14f, StopLine = 16f;

        public static void Build(Transform root, WorldResult w, System.Random rng)
        {
            var t = new GameObject("Town").transform;
            t.SetParent(root, false);
            Roads(t);
            Sidewalks(t);
            Markings(t);
            Signals(t);
            HomeLot(t, w, rng);
            Store(t, w, rng);
            StreetParking(t, rng);
            Houses(t, rng);
            UtilityPoles(t);
            Barriers(t);
        }

        // ---------------------------------------------------------------- 車道
        static void Roads(Transform t)
        {
            Kit.Surface(t, "Road A", AWest, -HalfRoad, AEast, HalfRoad, Kit.Asphalt);
            Kit.Surface(t, "Road B", -HalfRoad, BSouth, HalfRoad, BNorth, Kit.Asphalt);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Kit.Surface(t, "Corner Road", sx * HalfRoad, sz * HalfRoad, sx * CornerC, sz * CornerC, Kit.Asphalt);
        }

        // ---------------------------------------------------------------- 歩道(タイヤが乗り上げられない)
        static void Sidewalks(Transform t)
        {
            var s = new GameObject("Sidewalks").transform;
            s.SetParent(t, false);
            // 道路A 北側 / 南側(車の出入口は切り下げ)
            RunX(s, HalfRoad, Walk, AWest, -CornerC);
            RunX(s, HalfRoad, Walk, CornerC, AEast, (24f, 32f));                 // 教習所の入口
            RunX(s, -Walk, -HalfRoad, AWest, -CornerC, (-44f, -36f));            // 自宅の月極駐車場
            RunX(s, -Walk, -HalfRoad, CornerC, AEast, (16f, 24f), (34f, 42f));  // コンビニ
            // 道路B 西側 / 東側
            RunZ(s, -Walk, -HalfRoad, CornerC, BNorth);
            RunZ(s, -Walk, -HalfRoad, BSouth, -CornerC);
            RunZ(s, HalfRoad, Walk, CornerC, BNorth);
            RunZ(s, HalfRoad, Walk, BSouth, -CornerC);

            // 交差点の隅切り(扇形の歩道 + 円弧の縁石)
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var c = new Vector3(sx * CornerC, 0, sz * CornerC);
                    // 交差点側を向いた 90° の円弧
                    float a0 = sx > 0 ? (sz > 0 ? 180f : 90f) : (sz > 0 ? 270f : 0f);
                    var arc = Kit.Arc(c, CornerR, a0, a0 + 90f, 12);
                    Kit.Fan(s, "Corner Walk", c, arc, Kit.CurbH, Kit.SidewalkMat);
                    Kit.Curb(s, arc);
                    // 歩道の外側の四角い残り
                    Kit.Surface(s, "Corner Walk Fill", c.x, c.z, sx * Walk, sz * Walk, Kit.SidewalkMat, Kit.CurbH);
                }
        }

        /// <summary>X 方向に延びる歩道。gaps の範囲は車の出入口</summary>
        static void RunX(Transform p, float z0, float z1, float x0, float x1, params (float a, float b)[] gaps)
        {
            float x = x0;
            foreach (var g in gaps)
            {
                if (g.a > x) Kit.Raised(p, "Sidewalk", x, z0, g.a, z1, Kit.SidewalkMat);
                Kit.Surface(p, "Driveway", g.a, z0, g.b, z1, Kit.Concrete);
                x = g.b;
            }
            if (x1 > x) Kit.Raised(p, "Sidewalk", x, z0, x1, z1, Kit.SidewalkMat);
            float edge = Mathf.Abs(z0) < Mathf.Abs(z1) ? z0 : z1; // 車道側の縁
            float inward = Mathf.Sign(z1 + z0) * 0.1f;
            x = x0;
            foreach (var g in gaps) { CurbEdgeX(p, edge + inward, x, g.a); x = g.b; }
            CurbEdgeX(p, edge + inward, x, x1);
        }

        static void RunZ(Transform p, float x0, float x1, float z0, float z1, params (float a, float b)[] gaps)
        {
            float z = z0;
            foreach (var g in gaps)
            {
                if (g.a > z) Kit.Raised(p, "Sidewalk", x0, z, x1, g.a, Kit.SidewalkMat);
                Kit.Surface(p, "Driveway", x0, g.a, x1, g.b, Kit.Concrete);
                z = g.b;
            }
            if (z1 > z) Kit.Raised(p, "Sidewalk", x0, z, x1, z1, Kit.SidewalkMat);
            float edge = Mathf.Abs(x0) < Mathf.Abs(x1) ? x0 : x1;
            float inward = Mathf.Sign(x1 + x0) * 0.1f;
            Util.BoxMinMax("Curb Edge", p, new Vector3(edge + inward - 0.1f, 0, z0), new Vector3(edge + inward + 0.1f, Kit.CurbH + 0.003f, z1), Kit.CurbMat, false, false);
        }

        static void CurbEdgeX(Transform p, float z, float x0, float x1)
        {
            if (x1 - x0 < 0.1f) return;
            Util.BoxMinMax("Curb Edge", p, new Vector3(x0, 0, z - 0.1f), new Vector3(x1, Kit.CurbH + 0.003f, z + 0.1f), Kit.CurbMat, false, false);
        }

        // ---------------------------------------------------------------- 路面標示
        static void Markings(Transform t)
        {
            var m = new GameObject("Markings").transform;
            m.SetParent(t, false);
            const float edge = 3.25f;
            // 道路A
            foreach (float s in new[] { -1f, 1f })
            {
                float near = s * CrossFar, far = s > 0 ? AEast : AWest;
                Kit.Line(m, new Vector3(near, 0, edge), new Vector3(far, 0, edge));
                Kit.Line(m, new Vector3(near, 0, -edge), new Vector3(far, 0, -edge));
                Kit.Line(m, new Vector3(near, 0, 0), new Vector3(s * 45f, 0, 0));        // 交差点付近は実線
                Kit.Dashed(m, new Vector3(s * 45f, 0, 0), new Vector3(far, 0, 0), 5f, 5f);
                Kit.Zebra(m, new Vector3(s * (CrossNear + CrossFar) / 2, 0, 0), Vector3.right, CrossFar - CrossNear, 2 * HalfRoad - 0.5f);
                // 停止線は交差点に向かう車線(左側通行: 西から来る車は北側, 東から来る車は南側)
                float laneZ = s < 0 ? 1f : -1f;
                Kit.Line(m, new Vector3(s * StopLine, 0, 0.1f * laneZ), new Vector3(s * StopLine, 0, edge * laneZ), 0.45f);
            }
            // 道路B
            foreach (float s in new[] { -1f, 1f })
            {
                float near = s * CrossFar, far = s > 0 ? BNorth : BSouth;
                Kit.Line(m, new Vector3(edge, 0, near), new Vector3(edge, 0, far));
                Kit.Line(m, new Vector3(-edge, 0, near), new Vector3(-edge, 0, far));
                Kit.Line(m, new Vector3(0, 0, near), new Vector3(0, 0, s * 45f));
                Kit.Dashed(m, new Vector3(0, 0, s * 45f), new Vector3(0, 0, far), 5f, 5f);
                Kit.Zebra(m, new Vector3(0, 0, s * (CrossNear + CrossFar) / 2), Vector3.forward, CrossFar - CrossNear, 2 * HalfRoad - 0.5f);
                // 南から来る車(北向き)は西側, 北から来る車(南向き)は東側
                float laneX = s < 0 ? -1f : 1f;
                Kit.Line(m, new Vector3(0.1f * laneX, 0, s * StopLine), new Vector3(edge * laneX, 0, s * StopLine), 0.45f);
            }
        }

        // ---------------------------------------------------------------- 信号機(消灯)
        static void Signals(Transform t)
        {
            var g = new GameObject("Signals").transform;
            g.SetParent(t, false);
            Material pole = Util.Mat(new Color(0.55f, 0.56f, 0.58f), 0.4f, 0.3f);
            Material housing = Util.Mat(new Color(0.15f, 0.16f, 0.18f), 0.4f);
            Material[] lens =
            {
                Util.Mat(new Color(0.05f, 0.18f, 0.12f), 0.9f), // 青(消灯)
                Util.Mat(new Color(0.22f, 0.17f, 0.04f), 0.9f), // 黄(消灯)
                Util.Mat(new Color(0.22f, 0.05f, 0.05f), 0.9f), // 赤(消灯)
            };
            // 各方向の車から見て「交差点の向こう側・左側」の角に設置
            foreach (float heading in new[] { 0f, 90f, 180f, 270f })
            {
                Vector3 f = CarSpec.Forward(heading), left = -CarSpec.Right(heading);
                Vector3 pos = (f + left) * 7.5f;
                var p = Kit.Pole(g, pos, 0.14f, 6.2f, pole);
                p.name = "Signal Pole";
                // 車道の上に張り出したアーム
                var root = Util.Child("Signal", g, new Vector3(pos.x, 5.4f, pos.z), Quaternion.LookRotation(-f));
                Util.Segment("Arm", root.transform, Vector3.zero, root.transform.InverseTransformDirection(-left) * 6.0f, 0.12f, 0.12f, pole);
                var head = Util.Child("Head", root.transform, root.transform.InverseTransformDirection(-left) * 5.6f + Vector3.up * 0.1f);
                Util.Box("Housing", head.transform, Vector3.zero, new Vector3(1.25f, 0.42f, 0.25f), housing);
                for (int i = 0; i < 3; i++)
                {
                    // 日本の横型信号: 左から 青・黄・赤(車から見て)
                    float x = (1 - i) * 0.4f;
                    Util.Cylinder("Lens", head.transform, new Vector3(x, 0, 0.13f), 0.15f, 0.04f, lens[i], Quaternion.Euler(90, 0, 0));
                    Util.Box("Visor", head.transform, new Vector3(x, 0.16f, 0.2f), new Vector3(0.34f, 0.03f, 0.16f), housing);
                }
                // 歩行者用信号(横断歩道の向こう側を向く)
                var ped = Util.Child("Pedestrian Signal", g, new Vector3(pos.x, 2.7f, pos.z), Quaternion.LookRotation(left));
                Util.Box("Ped Housing", ped.transform, new Vector3(0, 0, 0.2f), new Vector3(0.32f, 0.6f, 0.2f), housing);
            }
        }

        // ---------------------------------------------------------------- 自宅の月極駐車場(南西)
        static void HomeLot(Transform t, WorldResult w, System.Random rng)
        {
            var lot = new GameObject("Home Parking").transform;
            lot.SetParent(t, false);
            const float x0 = -49f, x1 = -31f, z0 = -40f, z1 = -Walk;
            Kit.Surface(lot, "Lot", x0, z0, x1, z1, Kit.Asphalt);
            Material block = Util.Mat(new Color(0.7f, 0.68f, 0.64f), 0.1f);
            Kit.Wall(lot, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), 1.0f, 0.15f, block);
            Kit.Wall(lot, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), 1.0f, 0.15f, block);
            Kit.Wall(lot, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), 1.0f, 0.15f, block);
            Kit.Wall(lot, new Vector3(x0, 0, z1 - 0.1f), new Vector3(-44.2f, 0, z1 - 0.1f), 0.8f, 0.12f, block);
            Kit.Wall(lot, new Vector3(-35.8f, 0, z1 - 0.1f), new Vector3(x1, 0, z1 - 0.1f), 0.8f, 0.12f, block);

            const int own = 4;
            var west = Kit.SpotRow(lot, new Kit.RowSpec
            {
                name = "自宅駐車場 西", origin = new Vector3(-43f, 0, -8.5f), along = Vector3.back, into = Vector3.left,
                count = 9, width = 2.5f, depth = 5f, uGap = 0.5f, wheelStops = true, occupancy = 0.8f,
                keepEmpty = own, fillNeighbors = true, kind = CarKind.Random,
            }, rng);
            var east = Kit.SpotRow(lot, new Kit.RowSpec
            {
                name = "自宅駐車場 東", origin = new Vector3(-37f, 0, -8.5f), along = Vector3.back, into = Vector3.right,
                count = 9, width = 2.5f, depth = 5f, uGap = 0.5f, wheelStops = true, occupancy = 0.75f,
                keepEmpty = -1, kind = CarKind.Random,
            }, rng);

            var mine = west[own];
            mine.name = "自分の区画";
            Kit.GuideOutline(lot, mine);
            // 自分の区画の目印(区画の奥の壁に青い札)
            Vector3 sign = mine.ToWorld(0, -mine.length / 2 - 0.9f);
            Util.Box("My Spot Sign", lot, new Vector3(sign.x, 0.75f, sign.z), new Vector3(0.05f, 0.35f, 0.6f), Util.Mat(new Color(0.1f, 0.35f, 0.85f), 0.4f));
            w.spots.AddRange(west);
            w.spots.AddRange(east);

            w.starts.Insert(0, new StartPoint { name = "自宅の駐車スペース", rearAxle = Kit.ParkedRearAxle(mine, true), yaw = mine.yaw });
            w.starts.Add(new StartPoint { name = "自宅駐車場の出口", rearAxle = new Vector3(-40f, 0, -24f), yaw = 0f });
            w.zones.Add(new Zone { name = "自宅の月極駐車場", rect = Rect.MinMaxRect(x0, z0, x1, z1) });
        }

        // ---------------------------------------------------------------- コンビニ(南東)
        static void Store(Transform t, WorldResult w, System.Random rng)
        {
            var st = new GameObject("Convenience Store").transform;
            st.SetParent(t, false);
            const float x0 = 12f, x1 = 46f, z0 = -34f, z1 = -Walk;
            Kit.Surface(st, "Lot", x0, z0, x1, z1, Kit.Asphalt);
            var shop = Util.BoxMinMax("Store", st, new Vector3(15f, 0, z0), new Vector3(43f, 4.2f, -25f), Util.Mat(new Color(0.92f, 0.92f, 0.9f), 0.2f), true);
            shop.layer = Layers.Obstacle;
            Util.BoxMinMax("Store Glass", st, new Vector3(20f, 0.2f, -25.05f), new Vector3(38f, 2.6f, -24.98f), Kit.Glass);
            Util.BoxMinMax("Store Band", st, new Vector3(15f, 3.2f, -25.06f), new Vector3(43f, 3.9f, -24.97f), Util.Mat(new Color(0.15f, 0.45f, 0.75f), 0.3f));
            Material hedge = Util.Mat(new Color(0.2f, 0.38f, 0.18f), 0.05f);
            Kit.Wall(st, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), 0.7f, 0.5f, hedge);
            Kit.Wall(st, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), 0.7f, 0.5f, hedge);
            foreach (var (a, b) in new[] { (x0, 16f), (24f, 34f), (42f, x1) })
                Kit.Wall(st, new Vector3(a, 0, z1 - 0.3f), new Vector3(b, 0, z1 - 0.3f), 0.7f, 0.5f, hedge);

            var row = Kit.SpotRow(st, new Kit.RowSpec
            {
                name = "コンビニ駐車場", origin = new Vector3(14f, 0, -19f), along = Vector3.right, into = Vector3.back,
                count = 12, width = 2.5f, depth = 5f, uGap = 0f, wheelStops = true, occupancy = 0.35f, keepEmpty = -1,
                kind = CarKind.Random,
            }, rng);
            w.spots.AddRange(row);
            Kit.Raised(st, "Grass", Walk, z0, x0 - 0.25f, z1, Kit.Grass);
            w.starts.Add(new StartPoint { name = "コンビニ駐車場", rearAxle = new Vector3(17f, 0, -12.5f), yaw = 90f });
            w.zones.Add(new Zone { name = "コンビニ駐車場", rect = Rect.MinMaxRect(x0, z0, x1, z1) });
        }

        // ---------------------------------------------------------------- 路上駐車の車
        static void StreetParking(Transform t, System.Random rng)
        {
            var g = new GameObject("Street Parking").transform;
            g.SetParent(t, false);
            // 左側通行なので、各車線の左端に停まっている
            Kit.ParkedCar(g, new Vector3(2.55f, 0, -48f), 180f, CarKind.Random, rng, out _);
            Kit.ParkedCar(g, new Vector3(2.55f, 0, -62f), 180f, CarKind.Minivan, rng, out _);
            Kit.ParkedCar(g, new Vector3(-85f, 0, -2.55f), -90f, CarKind.Random, rng, out _);
            Kit.ParkedCar(g, new Vector3(95f, 0, 2.55f), 90f, CarKind.Random, rng, out _);
            Kit.ParkedCar(g, new Vector3(-2.55f, 0, 75f), 0f, CarKind.Kei, rng, out _);
        }

        // ---------------------------------------------------------------- 家並み
        static void Houses(Transform t, System.Random rng)
        {
            var g = new GameObject("Houses").transform;
            g.SetParent(t, false);
            // 道路A 北側(西)
            for (float x = -140f; x < -14f; x += 14f) House(g, new Vector3(x + 7f, 0, Walk + 8f), 180f, rng);
            // 道路A 南側(自宅駐車場より西)
            for (float x = -140f; x < -56f; x += 14f) House(g, new Vector3(x + 7f, 0, -Walk - 8f), 0f, rng);
            // 道路A 南側(コンビニより東)
            for (float x = 54f; x < 145f; x += 14f) House(g, new Vector3(x + 7f, 0, -Walk - 8f), 0f, rng);
            // 道路B 西側
            for (float z = 22f; z < 150f; z += 14f) House(g, new Vector3(-Walk - 8f, 0, z + 7f), 90f, rng);
            for (float z = -112f; z < -48f; z += 14f) House(g, new Vector3(-Walk - 8f, 0, z + 7f), 90f, rng);
            // 道路B 東側(コンビニより南, 教習所より北)
            for (float z = -112f; z < -40f; z += 14f) House(g, new Vector3(Walk + 8f, 0, z + 7f), -90f, rng);
            for (float z = 112f; z < 150f; z += 14f) House(g, new Vector3(Walk + 8f, 0, z + 7f), -90f, rng);
        }

        static readonly Color[] WallColors =
        {
            new Color(0.92f, 0.9f, 0.84f), new Color(0.82f, 0.76f, 0.66f), new Color(0.75f, 0.78f, 0.8f),
            new Color(0.9f, 0.86f, 0.78f), new Color(0.6f, 0.55f, 0.5f), new Color(0.85f, 0.85f, 0.85f),
        };

        /// <summary>家1軒。facing は家から道路へ向かう向き(玄関の向き)</summary>
        static void House(Transform p, Vector3 pos, float facing, System.Random rng)
        {
            var h = Util.Child("House", p, pos, Quaternion.Euler(0, facing, 0)).transform;
            float wdt = 7.5f + (float)rng.NextDouble() * 2f, dep = 7f + (float)rng.NextDouble() * 2f, ht = 5.6f;
            var wall = Util.Mat(WallColors[rng.Next(WallColors.Length)], 0.1f);
            Util.Box("Body", h, new Vector3(0, ht / 2, 0), new Vector3(wdt, ht, dep), wall);
            float half = dep / 2 + 0.4f, rise = 1.6f;
            float slope = Mathf.Atan2(rise, half) * Mathf.Rad2Deg, len = Mathf.Sqrt(half * half + rise * rise);
            Util.Box("Roof N", h, new Vector3(0, ht + rise / 2, dep / 4 + 0.2f), new Vector3(wdt + 0.6f, 0.15f, len), Kit.Roof, Quaternion.Euler(slope, 0, 0));
            Util.Box("Roof S", h, new Vector3(0, ht + rise / 2, -dep / 4 - 0.2f), new Vector3(wdt + 0.6f, 0.15f, len), Kit.Roof, Quaternion.Euler(-slope, 0, 0));
            Util.Box("Window", h, new Vector3(-wdt / 4, ht * 0.68f, dep / 2 + 0.01f), new Vector3(1.8f, 1.1f, 0.05f), Kit.Glass);
            Util.Box("Door", h, new Vector3(wdt / 4, 1.0f, dep / 2 + 0.01f), new Vector3(0.9f, 2.0f, 0.05f), Kit.Dark);
            // 道路側のブロック塀
            Material block = Util.Mat(new Color(0.7f, 0.68f, 0.64f), 0.1f);
            Util.Box("Block Wall", h, new Vector3(0, 0.5f, dep / 2 + 2.5f), new Vector3(wdt + 3f, 1.0f, 0.15f), block);
        }

        // ---------------------------------------------------------------- 電柱
        static void UtilityPoles(Transform t)
        {
            var g = new GameObject("Utility Poles").transform;
            g.SetParent(t, false);
            Material concrete = Util.Mat(new Color(0.6f, 0.6f, 0.58f), 0.2f);
            for (float x = AWest + 10f; x < AEast; x += 28f)
            {
                if (Mathf.Abs(x) < 20f || (x > -50f && x < -30f) || (x > 10f && x < 48f)) continue;
                Kit.Pole(g, new Vector3(x, 0, -Walk + 0.35f), 0.17f, 10f, concrete);
            }
            for (float z = BSouth + 10f; z < BNorth; z += 28f)
            {
                if (Mathf.Abs(z) < 20f) continue;
                Kit.Pole(g, new Vector3(-Walk + 0.35f, 0, z), 0.17f, 10f, concrete);
            }
        }

        // ---------------------------------------------------------------- 道路の端(通行止め)
        static void Barriers(Transform t)
        {
            var g = new GameObject("Barriers").transform;
            g.SetParent(t, false);
            void Barrier(Vector3 a, Vector3 b)
            {
                var wall = Kit.Wall(g, a, b, 1.0f, 0.3f, Kit.YellowMat);
                Vector3 d = b - a;
                int n = Mathf.FloorToInt(d.magnitude / 0.8f);
                for (int i = 0; i < n; i += 2)
                {
                    Vector3 c = a + d.normalized * (i + 0.5f) * 0.8f;
                    Util.Box("Stripe", g, new Vector3(c.x, 0.5f, c.z), new Vector3(0.32f, 1.01f, 0.32f), Kit.Dark,
                        Quaternion.LookRotation(d));
                }
            }
            Barrier(new Vector3(AWest + 2f, 0, -Walk), new Vector3(AWest + 2f, 0, Walk));
            Barrier(new Vector3(AEast - 2f, 0, -Walk), new Vector3(AEast - 2f, 0, Walk));
            Barrier(new Vector3(-Walk, 0, BSouth + 2f), new Vector3(Walk, 0, BSouth + 2f));
            Barrier(new Vector3(-Walk, 0, BNorth - 2f), new Vector3(Walk, 0, BNorth - 2f));
        }
    }
}
