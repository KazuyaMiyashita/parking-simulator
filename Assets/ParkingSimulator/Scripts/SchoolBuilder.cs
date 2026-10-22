using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 自動車教習所(道路Aの北・道路Bの東)。広い舗装の敷地に S字・クランク・方向転換・縦列駐車・駐車場がある。
    /// コースの縁石にタイヤが当たると脱輪扱い。
    /// </summary>
    public static class SchoolBuilder
    {
        const float X0 = 14f, X1 = 138f, Z0 = 12f, Z1 = 104f;
        const float GateA = 24f, GateB = 32f;

        public static void Build(Transform root, WorldResult w, System.Random rng)
        {
            var s = new GameObject("Driving School").transform;
            s.SetParent(root, false);
            float walk = TownBuilder.Walk;

            // 敷地と入口
            Kit.Surface(s, "Yard", X0, Z0, X1, Z1, Kit.Asphalt);
            Kit.Surface(s, "Gate Road", GateA, walk, GateB, Z0, Kit.Asphalt);
            Kit.Raised(s, "Grass", walk, walk, GateA, Z0, Kit.Grass);
            Kit.Raised(s, "Grass", GateB, walk, X1 + 2f, Z0, Kit.Grass);
            Kit.Wall(s, new Vector3(X0, 0, Z0), new Vector3(GateA, 0, Z0), 1.8f, 0.08f, Kit.Fence);
            Kit.Wall(s, new Vector3(GateB, 0, Z0), new Vector3(X1, 0, Z0), 1.8f, 0.08f, Kit.Fence);
            Kit.Wall(s, new Vector3(X0, 0, Z0), new Vector3(X0, 0, Z1), 1.8f, 0.08f, Kit.Fence);
            Kit.Wall(s, new Vector3(X0, 0, Z1), new Vector3(X1, 0, Z1), 1.8f, 0.08f, Kit.Fence);
            Kit.Wall(s, new Vector3(X1, 0, Z0), new Vector3(X1, 0, Z1), 1.8f, 0.08f, Kit.Fence);
            Material gate = Util.Mat(new Color(0.75f, 0.75f, 0.72f), 0.2f);
            Kit.Pole(s, new Vector3(GateA - 0.3f, 0, Z0), 0.3f, 2.2f, gate);
            Kit.Pole(s, new Vector3(GateB + 0.3f, 0, Z0), 0.3f, 2.2f, gate);

            // 校舎
            var bld = Util.BoxMinMax("School Building", s, new Vector3(16f, 0, 86f), new Vector3(40f, 7f, 102f), Util.Mat(new Color(0.9f, 0.9f, 0.87f), 0.2f), true);
            bld.layer = Layers.Obstacle;
            Util.BoxMinMax("Windows", s, new Vector3(17f, 4.2f, 85.95f), new Vector3(39f, 5.6f, 86.0f), Kit.Glass);
            Util.BoxMinMax("Windows", s, new Vector3(17f, 1.2f, 85.95f), new Vector3(39f, 2.6f, 86.0f), Kit.Glass);

            SCurve(s, w);
            Crank(s, w);
            TurnAround(s, w);
            Parallel(s, w);

            // 教習所の駐車場(バック駐車の練習)
            var row = Kit.SpotRow(s, new Kit.RowSpec
            {
                name = "教習所 駐車場", origin = new Vector3(44f, 0, 95f), along = Vector3.right, into = Vector3.forward,
                count = 8, width = 2.5f, depth = 5f, uGap = 0f, wheelStops = true, occupancy = 0.4f, keepEmpty = -1,
                kind = CarKind.Compact,
            }, rng);
            w.spots.AddRange(row);

            w.starts.Add(new StartPoint { name = "教習所 入口", rearAxle = new Vector3(28f, 0, 8.6f), yaw = 0f });
            w.starts.Add(new StartPoint { name = "教習所 S字", rearAxle = new Vector3(55f, 0, 17f), yaw = 0f });
            w.starts.Add(new StartPoint { name = "教習所 クランク", rearAxle = new Vector3(100f, 0, 15.5f), yaw = 0f });
            w.starts.Add(new StartPoint { name = "教習所 方向転換", rearAxle = new Vector3(52f, 0, 66.5f), yaw = 90f });
            w.starts.Add(new StartPoint { name = "教習所 縦列駐車", rearAxle = new Vector3(99f, 0, 66f), yaw = 90f });
            w.starts.Add(new StartPoint { name = "教習所 駐車場", rearAxle = new Vector3(46f, 0, 90f), yaw = 90f });

            w.zones.Add(new Zone { name = "教習所 S字", rect = Rect.MinMaxRect(48f, 20f, 94f, 60f) });
            w.zones.Add(new Zone { name = "教習所 クランク", rect = Rect.MinMaxRect(94f, 18f, 136f, 60f) });
            w.zones.Add(new Zone { name = "教習所 方向転換", rect = Rect.MinMaxRect(46f, 60f, 94f, 80f) });
            w.zones.Add(new Zone { name = "教習所 縦列駐車", rect = Rect.MinMaxRect(94f, 60f, 138f, 80f) });
            w.zones.Add(new Zone { name = "教習所 駐車場", rect = Rect.MinMaxRect(42f, 84f, 70f, 104f) });
            w.zones.Add(new Zone { name = "自動車教習所", rect = Rect.MinMaxRect(X0, walk, X1, Z1) });
        }

        /// <summary>中心線に沿って幅 width の道を縁石で作る</summary>
        static void Lane(Transform p, List<Vector3> center, float width)
        {
            float o = width / 2 + 0.125f; // 縁石の中心
            Kit.Curb(p, Kit.Offset(center, o));
            Kit.Curb(p, Kit.Offset(center, -o));
            Kit.Line(p, Kit.Offset(center, o)[0], Kit.Offset(center, -o)[0], 0.3f); // 入口の白線
            Kit.Line(p, Kit.Offset(center, o)[center.Count - 1], Kit.Offset(center, -o)[center.Count - 1], 0.3f);
        }

        // ---------------------------------------------------------------- S字(道幅 3.6m)
        static void SCurve(Transform s, WorldResult w)
        {
            var g = Util.Child("S Curve", s, Vector3.zero).transform;
            var path = new Kit.PathBuilder(new Vector3(55f, 0, 26f), 0f)
                .Straight(5f).Turn(8f, 100f).Turn(8f, -100f).Straight(5f).pts;
            Lane(g, path, 3.6f);
        }

        // ---------------------------------------------------------------- クランク(道幅 3.5m)
        static void Crank(Transform s, WorldResult w)
        {
            var g = Util.Child("Crank", s, Vector3.zero).transform;
            var path = new List<Vector3>
            {
                new Vector3(100f, 0, 24f), new Vector3(100f, 0, 34f), new Vector3(110f, 0, 34f),
                new Vector3(110f, 0, 44f), new Vector3(100f, 0, 44f), new Vector3(100f, 0, 56f),
            };
            Lane(g, path, 3.5f);
        }

        // ---------------------------------------------------------------- 方向転換(車庫入れ)
        static void TurnAround(Transform s, WorldResult w)
        {
            var g = Util.Child("Turn Around", s, Vector3.zero).transform;
            const float zs = 64f, zn = 69f, bx0 = 68.6f, bx1 = 71.4f, bz = 75f;
            Kit.Curb(g, new[] { new Vector3(48f, 0, zs), new Vector3(92f, 0, zs) });
            Kit.Curb(g, new[] { new Vector3(48f, 0, zn), new Vector3(bx0, 0, zn), new Vector3(bx0, 0, bz),
                                new Vector3(bx1, 0, bz), new Vector3(bx1, 0, zn), new Vector3(92f, 0, zn) });
            Kit.CoursePole(g, new Vector3(bx0 - 0.15f, 0, zn - 0.15f));
            Kit.CoursePole(g, new Vector3(bx1 + 0.15f, 0, zn - 0.15f));
            var spot = new TargetSpot
            {
                name = "方向転換の車庫", center = new Vector3((bx0 + bx1) / 2, 0, (zn + bz) / 2), yaw = 180f,
                width = bx1 - bx0 - 0.25f, length = bz - zn, lineWidth = 0f, aisleEnd = +1, bothWays = true,
            };
            w.spots.Add(spot);
            Kit.GuideOutline(g, spot);
        }

        // ---------------------------------------------------------------- 縦列駐車
        static void Parallel(Transform s, WorldResult w)
        {
            var g = Util.Child("Parallel Parking", s, Vector3.zero).transform;
            const float zs = 63f, zn = 69f, bx0 = 109f, bx1 = 117f, bz = 71.5f;
            Kit.Curb(g, new[] { new Vector3(96f, 0, zs), new Vector3(136f, 0, zs) });
            Kit.Curb(g, new[] { new Vector3(96f, 0, zn), new Vector3(bx0, 0, zn), new Vector3(bx0, 0, bz),
                                new Vector3(bx1, 0, bz), new Vector3(bx1, 0, zn), new Vector3(136f, 0, zn) });
            foreach (var p in new[] { new Vector3(bx0 - 0.15f, 0, zn - 0.15f), new Vector3(bx1 + 0.15f, 0, zn - 0.15f),
                                      new Vector3(bx0 + 0.2f, 0, bz - 0.2f), new Vector3(bx1 - 0.2f, 0, bz - 0.2f) })
                Kit.CoursePole(g, p);
            var spot = new TargetSpot
            {
                name = "縦列駐車の枠", center = new Vector3((bx0 + bx1) / 2, 0, (zn + bz) / 2), yaw = 90f,
                width = bz - zn - 0.125f, length = bx1 - bx0 - 0.25f, lineWidth = 0f, aisleEnd = 0, parallel = true,
            };
            spot.curbU = spot.ToLocal(new Vector3(spot.center.x, 0, bz - 0.125f)).x;
            w.spots.Add(spot);
            Kit.GuideOutline(g, spot);
        }
    }
}
