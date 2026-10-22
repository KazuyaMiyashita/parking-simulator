using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 町(T字路の多い住宅街)。外周道路で囲み、中央に東西の道路A × 南北の道路B の信号つき十字路。
    /// 各ブロックに1車線の生活道路を通し、その一部が S字・クランク。行き止まりはない。左側通行・片側1車線。
    /// 座標: x=東, z=北。十字路の中心が原点。
    /// </summary>
    public static class TownBuilder
    {
        public const float HalfRoad = 3.75f;               // 車道の半幅(車線3.25 + 路肩0.5)
        public const float SideW = 3.0f;                   // 歩道の幅
        public const float Walk = HalfRoad + SideW;        // 歩道の外側 6.75
        const float CornerR = 6f;                          // 交差点の隅切りの半径
        const float CornerC = HalfRoad + CornerR;          // 9.75
        const float CrossNear = 10f, CrossFar = 14f, StopLine = 16f;
        public const float RingW = -75f, RingE = 150f, RingS = -75f, RingN = 75f; // 外周道路の中心線
        const float NarrowW = 3.6f;                        // S字・クランクの道幅
        const float LaneW = 4.0f;                          // 生活道路の道幅
        const float Flare = 2.5f, FunnelLen = 4f;          // 生活道路の入口の広がり(片側)と長さ
        const float HomeLaneX = -15f;                      // 自宅前の生活道路

        // ---------------------------------------------------------------- 道路網のデータ
        class Node
        {
            public Vector3 p;
            public readonly bool[] arms = new bool[4]; // 0=北 1=東 2=南 3=西
            public bool cross;                         // 信号のある十字路
            public float Clear => cross ? CrossFar : CornerC;
        }

        class Seg { public Node a, b; public bool alongX; } // a → b は座標が増える向き

        class LanePath { public string name; public List<Vector3> pts; public float width; }

        /// <summary>縁石を置かない範囲(交差部・車庫の入口など)。o から ax 方向に a0~a1、横に ±half</summary>
        struct Clear
        {
            public Vector3 o, ax;
            public float a0, a1, half;

            public bool Contains(Vector3 p)
            {
                Vector3 d = p - o; d.y = 0;
                Vector3 ac = Vector3.Cross(Vector3.up, ax);
                float u = Vector3.Dot(d, ax), v = Vector3.Dot(d, ac);
                return u >= a0 && u <= a1 && Mathf.Abs(v) <= half;
            }
        }

        /// <summary>幹線道路の歩道の切れ目(生活道路の入口・駐車場の出入口)</summary>
        struct Mouth { public Vector3 edge, into; public float half; public Material fill; }

        static readonly Vector3[] Dirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };

        static List<Node> nodes;
        static List<Seg> segs;
        static List<LanePath> lanes;
        static List<Clear> clears;
        static List<Mouth> mouths;
        static List<(Vector3 a, Vector3 b)> funnels;
        static List<Rect> blocked; // 家を置かない範囲(x-z)

        public static void Build(Transform root, WorldResult w, System.Random rng)
        {
            var t = new GameObject("Town").transform;
            t.SetParent(root, false);
            nodes = new List<Node>(); segs = new List<Seg>(); lanes = new List<LanePath>();
            clears = new List<Clear>(); mouths = new List<Mouth>(); funnels = new List<(Vector3, Vector3)>();
            blocked = new List<Rect>();

            Network();
            DefineLanes(w);
            // 区画や出入口を先に決めてから(縁石の切れ目・歩道の切れ目を登録)道路を作る
            Homes(t, w, rng);
            ShopLot(t, w, rng);
            Store(t, w, rng);
            Supermarket(t, w, rng);
            ParallelBays(t, w, rng);
            Roads(t);
            Sidewalks(t);
            Markings(t);
            Signals(t);
            BuildLanes(t);
            StreetParking(t, rng);
            Houses(t, rng);
            UtilityPoles(t);

            foreach (var sg in segs) w.navLines.Add(new List<Vector3> { sg.a.p, sg.b.p });
            foreach (var l in lanes) w.navLines.Add(l.pts);
        }

        // ---------------------------------------------------------------- 幹線道路の網
        static void Network()
        {
            Node N(float x, float z, bool cross = false)
            {
                var n = new Node { p = new Vector3(x, 0, z), cross = cross };
                nodes.Add(n);
                return n;
            }
            void S(Node a, Node b)
            {
                bool ax = Mathf.Abs(b.p.x - a.p.x) > 0.1f;
                segs.Add(new Seg { a = a, b = b, alongX = ax });
                a.arms[ax ? 1 : 0] = true;
                b.arms[ax ? 3 : 2] = true;
            }
            var c = N(0, 0, true);
            Node aw = N(RingW, 0), ae = N(RingE, 0), bn = N(0, RingN), bs = N(0, RingS);
            Node nw = N(RingW, RingN), ne = N(RingE, RingN), sw = N(RingW, RingS), se = N(RingE, RingS);
            S(aw, c); S(c, ae); S(bs, c); S(c, bn);         // 道路A・B
            S(nw, bn); S(bn, ne); S(sw, bs); S(bs, se);     // 外周 北・南
            S(sw, aw); S(aw, nw); S(se, ae); S(ae, ne);     // 外周 西・東
        }

        // 区間の向きに沿った座標(u)と直交する座標(v)
        static float U(Seg s, Vector3 p) => s.alongX ? p.x : p.z;
        static float V(Seg s, Vector3 p) => s.alongX ? p.z : p.x;
        static Vector3 W(Seg s, float u, float v) => s.alongX ? new Vector3(u, 0, v) : new Vector3(v, 0, u);
        static int SideArm(Seg s, float side) => s.alongX ? (side > 0 ? 0 : 2) : (side > 0 ? 1 : 3);

        /// <summary>区間の片側の歩道の範囲(交差点の隅切りがある側は手前で止め、ない側は交差点の先まで伸ばす)</summary>
        static void SideRange(Seg s, float side, out float ua, out float ub)
        {
            int arm = SideArm(s, side);
            ua = U(s, s.a.p) + (s.a.arms[arm] ? CornerC : -Walk);
            ub = U(s, s.b.p) - (s.b.arms[arm] ? CornerC : -Walk);
        }

        static void SurfaceUV(Transform p, Seg s, string name, float u0, float v0, float u1, float v1, Material m, float top = Kit.RoadY)
        {
            Vector3 a = W(s, u0, v0), b = W(s, u1, v1);
            Kit.Surface(p, name, a.x, a.z, b.x, b.z, m, top);
        }

        // ---------------------------------------------------------------- 車道
        static void Roads(Transform t)
        {
            var g = Util.Child("Roads", t, Vector3.zero).transform;
            foreach (var s in segs)
            {
                float v = V(s, s.a.p);
                SurfaceUV(g, s, "Road", U(s, s.a.p), v - HalfRoad, U(s, s.b.p), v + HalfRoad, Kit.Asphalt);
            }
            foreach (var n in nodes)
            {
                Kit.Surface(g, "Junction", n.p.x - HalfRoad, n.p.z - HalfRoad, n.p.x + HalfRoad, n.p.z + HalfRoad, Kit.Asphalt);
                foreach (var (sx, sz) in Quadrants(n))
                    Kit.Surface(g, "Corner Road", n.p.x + sx * HalfRoad, n.p.z + sz * HalfRoad, n.p.x + sx * CornerC, n.p.z + sz * CornerC, Kit.Asphalt);
            }
        }

        /// <summary>隅切りのある角(両側に道がある 90° の角)</summary>
        static IEnumerable<(float sx, float sz)> Quadrants(Node n)
        {
            if (n.arms[0] && n.arms[1]) yield return (1, 1);
            if (n.arms[1] && n.arms[2]) yield return (1, -1);
            if (n.arms[2] && n.arms[3]) yield return (-1, -1);
            if (n.arms[3] && n.arms[0]) yield return (-1, 1);
        }

        // ---------------------------------------------------------------- 歩道(タイヤが乗り上げられない)
        static void Sidewalks(Transform t)
        {
            var g = Util.Child("Sidewalks", t, Vector3.zero).transform;
            foreach (var s in segs)
            {
                float v = V(s, s.a.p);
                foreach (float side in new[] { 1f, -1f })
                {
                    SideRange(s, side, out float ua, out float ub);
                    Vector3 sideDir = Dirs[SideArm(s, side)];
                    float edge = v + side * HalfRoad;
                    var gaps = new List<(float a, float b, Material m)>();
                    foreach (var m in mouths)
                    {
                        if (Vector3.Dot(m.into, sideDir) < 0.9f || Mathf.Abs(V(s, m.edge) - edge) > 0.3f) continue;
                        float u = U(s, m.edge);
                        if (u > ua && u < ub) gaps.Add((u - m.half, u + m.half, m.fill));
                    }
                    gaps.Sort((x, y) => x.a.CompareTo(y.a));
                    Run(g, s, side, edge, ua, ub, gaps);
                }
            }

            // 交差点の隅切り(扇形の歩道 + 円弧の縁石)
            foreach (var n in nodes)
                foreach (var (sx, sz) in Quadrants(n))
                {
                    var c = n.p + new Vector3(sx * CornerC, 0, sz * CornerC);
                    float a0 = sx > 0 ? (sz > 0 ? 180f : 90f) : (sz > 0 ? 270f : 0f); // 交差点側を向いた 90° の円弧
                    var arc = Kit.Arc(c, CornerR, a0, a0 + 90f, 12);
                    Kit.Fan(g, "Corner Walk", c, arc, Kit.CurbH, Kit.SidewalkMat);
                    Kit.Curb(g, arc);
                    Kit.Surface(g, "Corner Walk Fill", c.x, c.z, n.p.x + sx * Walk, n.p.z + sz * Walk, Kit.SidewalkMat, Kit.CurbH);
                }
        }

        /// <summary>u0~u1 の歩道。gaps の範囲は車の出入口(歩道を切り下げて車道と同じ高さにする)</summary>
        static void Run(Transform p, Seg s, float side, float edge, float u0, float u1, List<(float a, float b, Material m)> gaps)
        {
            float outer = edge + side * SideW;
            void Walkway(float a, float b)
            {
                if (b - a < 0.05f) return;
                Vector3 pa = W(s, a, edge), pb = W(s, b, outer);
                Kit.Raised(p, "Sidewalk", pa.x, pa.z, pb.x, pb.z, Kit.SidewalkMat);
                // 車道側の縁石の見た目
                Vector3 ca = W(s, a, edge), cb = W(s, b, edge + side * 0.2f);
                Util.BoxMinMax("Curb Edge", p, new Vector3(Mathf.Min(ca.x, cb.x), 0, Mathf.Min(ca.z, cb.z)),
                    new Vector3(Mathf.Max(ca.x, cb.x), Kit.CurbH + 0.003f, Mathf.Max(ca.z, cb.z)), Kit.CurbMat, false, false);
            }
            float u = u0;
            foreach (var gp in gaps)
            {
                Walkway(u, gp.a);
                SurfaceUV(p, s, "Driveway", gp.a, edge, gp.b, outer, gp.m);
                u = Mathf.Max(u, gp.b);
            }
            Walkway(u, u1);
        }

        // ---------------------------------------------------------------- 路面標示
        static void Markings(Transform t)
        {
            var m = Util.Child("Markings", t, Vector3.zero).transform;
            const float edge = 3.25f;
            foreach (var s in segs)
            {
                float ua = U(s, s.a.p), ub = U(s, s.b.p), v = V(s, s.a.p);
                foreach (float side in new[] { 1f, -1f })
                {
                    int arm = SideArm(s, side);
                    float e0 = ua + (s.a.arms[arm] ? s.a.Clear : -edge);
                    float e1 = ub - (s.b.arms[arm] ? s.b.Clear : -edge);
                    Kit.Line(m, W(s, e0, v + side * edge), W(s, e1, v + side * edge));
                }
                // 中央線: 十字路の近くは実線(はみ出し禁止)、それ以外は破線
                float c0 = ua + s.a.Clear, c1 = ub - s.b.Clear;
                if (s.a.cross)
                {
                    Kit.Line(m, W(s, c0, v), W(s, ua + 45f, v));
                    Kit.Dashed(m, W(s, ua + 45f, v), W(s, c1, v), 5f, 5f);
                }
                else if (s.b.cross)
                {
                    Kit.Dashed(m, W(s, c0, v), W(s, ub - 45f, v), 5f, 5f);
                    Kit.Line(m, W(s, ub - 45f, v), W(s, c1, v));
                }
                else Kit.Dashed(m, W(s, c0, v), W(s, c1, v), 5f, 5f);
            }

            foreach (var n in nodes)
            {
                int count = 0;
                foreach (bool a in n.arms) if (a) count++;
                if (count != 3) continue;
                // T字路: 突き当たる側の道に停止線(交差点に向かう車線 = 進行方向の左側)
                for (int k = 0; k < 4; k++)
                {
                    if (!n.arms[k] || n.arms[(k + 2) % 4]) continue;
                    Vector3 c = n.p + Dirs[k] * (CornerC + 1.5f);
                    Vector3 left = -CarSpec.Right(Kit.YawOf(-Dirs[k]));
                    Kit.Line(m, c + left * 0.1f, c + left * edge, 0.45f);
                }
            }

            // 十字路: 横断歩道と停止線
            foreach (float s in new[] { -1f, 1f })
            {
                Kit.Zebra(m, new Vector3(s * (CrossNear + CrossFar) / 2, 0, 0), Vector3.right, CrossFar - CrossNear, 2 * HalfRoad - 0.5f);
                Kit.Zebra(m, new Vector3(0, 0, s * (CrossNear + CrossFar) / 2), Vector3.forward, CrossFar - CrossNear, 2 * HalfRoad - 0.5f);
                // 左側通行: 西から来る車は北側, 東から来る車は南側 / 南から来る車は西側, 北から来る車は東側
                float laneZ = s < 0 ? 1f : -1f;
                Kit.Line(m, new Vector3(s * StopLine, 0, 0.1f * laneZ), new Vector3(s * StopLine, 0, edge * laneZ), 0.45f);
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

        // ---------------------------------------------------------------- 生活道路(1車線・縁石)
        static void DefineLanes(WorldResult w)
        {
            // S字の抜け道: 道路A → 北の外周。途中の S字で東へ約21mずれる
            var sp = new Kit.PathBuilder(new Vector3(70f, 0, HalfRoad), 0f).Straight(18f).Turn(9f, 100f).Turn(9f, -100f);
            float sx = sp.pos.x;
            var sPts = new List<Vector3>(sp.pts) { new Vector3(sx, 0, RingN - HalfRoad) };
            AddLane("S字の抜け道", sPts, NarrowW);
            MainMouth(new Vector3(70f, 0, HalfRoad), Vector3.forward, NarrowW / 2);
            MainMouth(new Vector3(sx, 0, RingN - HalfRoad), Vector3.back, NarrowW / 2);

            // 北の生活道路: 道路B → S字の抜け道(縦列駐車帯がある)
            AddLane("北の生活道路", new List<Vector3> { new Vector3(HalfRoad, 0, 48f), new Vector3(sx, 0, 48f) }, LaneW);
            MainMouth(new Vector3(HalfRoad, 0, 48f), Vector3.right, LaneW / 2);
            LaneMouth(new Vector3(sx, 0, 48f), Vector3.left, NarrowW / 2, LaneW / 2);

            // クランクの路地(南東): 南の外周 → 道路A
            AddLane("クランクの路地(南東)", new List<Vector3>
            {
                new Vector3(100f, 0, RingS + HalfRoad), new Vector3(100f, 0, -44f), new Vector3(114f, 0, -44f),
                new Vector3(114f, 0, -28f), new Vector3(100f, 0, -28f), new Vector3(100f, 0, -HalfRoad),
            }, NarrowW);
            MainMouth(new Vector3(100f, 0, RingS + HalfRoad), Vector3.forward, NarrowW / 2);
            MainMouth(new Vector3(100f, 0, -HalfRoad), Vector3.back, NarrowW / 2);

            // コンビニ裏の生活道路: 道路B → クランクの路地(途中でゆるく南へ曲がる)
            var bp = new Kit.PathBuilder(new Vector3(HalfRoad, 0, -46f), 90f).Straight(46.25f).Turn(10f, 60f).Turn(10f, -60f);
            float bz = bp.pos.z;
            var bPts = new List<Vector3>(bp.pts) { new Vector3(100f, 0, bz) };
            AddLane("コンビニ裏の生活道路", bPts, LaneW);
            MainMouth(new Vector3(HalfRoad, 0, -46f), Vector3.right, LaneW / 2);
            LaneMouth(new Vector3(100f, 0, bz), Vector3.left, NarrowW / 2, LaneW / 2);

            // クランクの路地(北西): 西の外周 → 道路B(横向きのクランク)
            AddLane("クランクの路地(北西)", new List<Vector3>
            {
                new Vector3(RingW + HalfRoad, 0, 38f), new Vector3(-52f, 0, 38f), new Vector3(-52f, 0, 52f),
                new Vector3(-34f, 0, 52f), new Vector3(-34f, 0, 38f), new Vector3(-HalfRoad, 0, 38f),
            }, NarrowW);
            MainMouth(new Vector3(RingW + HalfRoad, 0, 38f), Vector3.right, NarrowW / 2);
            MainMouth(new Vector3(-HalfRoad, 0, 38f), Vector3.left, NarrowW / 2);

            // 自宅前の生活道路: 南の外周 → 道路A
            AddLane("自宅前の生活道路", new List<Vector3> { new Vector3(HomeLaneX, 0, RingS + HalfRoad), new Vector3(HomeLaneX, 0, -HalfRoad) }, LaneW);
            MainMouth(new Vector3(HomeLaneX, 0, RingS + HalfRoad), Vector3.forward, LaneW / 2);
            MainMouth(new Vector3(HomeLaneX, 0, -HalfRoad), Vector3.back, LaneW / 2);

            w.zones.Add(new Zone { name = "S字の抜け道", rect = Rect.MinMaxRect(66f, Walk, sx + 4f, RingN - Walk) });
            w.zones.Add(new Zone { name = "クランクの路地(南東)", rect = Rect.MinMaxRect(97f, RingS + Walk, 117f, -Walk) });
            w.zones.Add(new Zone { name = "クランクの路地(北西)", rect = Rect.MinMaxRect(RingW + Walk, 35f, -Walk, 55f) });
            w.zones.Add(new Zone { name = "コンビニ裏の生活道路", rect = Rect.MinMaxRect(Walk, bz - 3f, 97f, -43f), hideOnMap = true });
        }

        static void AddLane(string name, List<Vector3> pts, float width) => lanes.Add(new LanePath { name = name, pts = pts, width = width });

        /// <summary>敷地の入口。道路の端(road)から敷地に入ったところ(inside)までを経路案内の行き先にする</summary>
        static void Entrance(WorldResult w, Place p, Vector3 road, Vector3 inside)
        {
            p.entrances.Add(inside);
            w.navLines.Add(new List<Vector3> { road, inside });
        }

        /// <summary>
        /// 生活道路が幹線道路に出る所。edge は幹線道路の車道の端(生活道路の中心線上)、into は生活道路へ入る向き。
        /// 歩道を切り下げ、入口を片側 Flare ずつ広げた縁石で漏斗状にする
        /// </summary>
        static void MainMouth(Vector3 edge, Vector3 into, float half)
        {
            Vector3 ac = Vector3.Cross(Vector3.up, into);
            clears.Add(new Clear { o = edge, ax = into, a0 = -HalfRoad, a1 = SideW + FunnelLen, half = half + Flare + 0.3f });
            mouths.Add(new Mouth { edge = edge, into = into, half = half + Flare, fill = Kit.Asphalt });
            foreach (float s in new[] { -1f, 1f })
                funnels.Add((edge + into * SideW + ac * s * (half + Flare), edge + into * (SideW + FunnelLen) + ac * s * (half + 0.125f)));
        }

        /// <summary>生活道路が別の生活道路に突き当たる所(T字路)。through はぶつかる側の道の中心線上の点</summary>
        static void LaneMouth(Vector3 through, Vector3 into, float throughHalf, float half)
        {
            Vector3 ac = Vector3.Cross(Vector3.up, into);
            Vector3 edge = through + into * throughHalf;
            clears.Add(new Clear { o = edge, ax = into, a0 = -throughHalf, a1 = FunnelLen, half = half + Flare + 0.3f });
            foreach (float s in new[] { -1f, 1f })
                funnels.Add((edge + into * 0.125f + ac * s * (half + Flare), edge + into * FunnelLen + ac * s * (half + 0.125f)));
        }

        static void BuildLanes(Transform t)
        {
            var g = Util.Child("Lanes", t, Vector3.zero).transform;
            foreach (var l in lanes)
            {
                var lt = Util.Child(l.name, g, Vector3.zero).transform;
                float h = l.width / 2;
                Kit.Ribbon(lt, "Lane", Kit.Offset(l.pts, -h), Kit.Offset(l.pts, h), Kit.RoadY, Kit.Asphalt);
                CurbPieces(lt, Kit.Offset(l.pts, h + 0.125f));
                CurbPieces(lt, Kit.Offset(l.pts, -h - 0.125f));
            }
            foreach (var (a, b) in funnels) Kit.Curb(g, new[] { a, b });
        }

        /// <summary>点列に沿った縁石を 1m 以下に刻み、交差部などの範囲にかかる部分を抜いて置く</summary>
        static void CurbPieces(Transform p, List<Vector3> pts)
        {
            var seg = new Vector3[2];
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1f));
                for (int k = 0; k < n; k++)
                {
                    seg[0] = Vector3.Lerp(a, b, (float)k / n);
                    seg[1] = Vector3.Lerp(a, b, (float)(k + 1) / n);
                    Vector3 mid = (seg[0] + seg[1]) * 0.5f;
                    bool skip = false;
                    foreach (var c in clears) if (c.Contains(mid)) { skip = true; break; }
                    if (!skip) Kit.Curb(p, seg);
                }
            }
        }

        // ---------------------------------------------------------------- 自宅(戸建て・カーポート)と近所の家(南西)
        static void Homes(Transform t, WorldResult w, System.Random rng)
        {
            var g = new GameObject("Homes").transform;
            g.SetParent(t, false);
            Material block = Util.Mat(new Color(0.7f, 0.68f, 0.64f), 0.1f);
            Material post = Util.Mat(new Color(0.55f, 0.52f, 0.48f), 0.4f, 0.3f);
            const float width = 2.5f, depth = 5.0f;                 // 駐車スペース(中)
            float laneEdge = HomeLaneX - LaneW / 2;                // 生活道路の西端
            float xf = laneEdge - 1.0f, xb = xf - depth;           // 枠の入口・奥(入口の前に 1m の土間)
            float[] gzs = { -12f, -26f, -40f, -54f };
            const int home = 1;
            var homePlace = new Place
            {
                id = "home", name = "自宅", note = "カーポート(駐車スペース 中)", home = true,
            };
            for (int i = 0; i < gzs.Length; i++)
            {
                float gz = gzs[i];
                var h = Util.Child(i == home ? "My Home" : "House With Carport", g, Vector3.zero).transform;
                Kit.Surface(h, "Carport Floor", xb - 0.3f, gz - width / 2 - 0.5f, laneEdge, gz + width / 2 + 0.5f, Kit.Concrete);
                // 奥のブロック塀、家の側(南)に柱2本と梁(屋根は真上・外からの視点で車が隠れるので付けない)
                Kit.Wall(h, new Vector3(xb - 0.375f, 0, gz - width / 2 - 0.5f), new Vector3(xb - 0.375f, 0, gz + width / 2 + 0.5f), 1.2f, 0.15f, block);
                float px = gz - width / 2 - 0.35f;
                foreach (float x in new[] { xb + 0.6f, xf - 0.4f })
                    Util.Box("Carport Post", h, new Vector3(x, 1.1f, px), new Vector3(0.1f, 2.2f, 0.1f), post, null, true).layer = Layers.Obstacle;
                Util.Box("Carport Beam", h, new Vector3((xb + 0.6f + xf - 0.4f) / 2, 2.25f, px), new Vector3(depth - 1.0f + 0.1f, 0.1f, 0.1f), post);
                // 道路側の縁石は入口だけ切る
                clears.Add(new Clear { o = new Vector3(laneEdge - 0.125f, 0, gz), ax = Vector3.right, a0 = -0.5f, a1 = 0.5f, half = width / 2 + 1.5f });

                var spot = new TargetSpot
                {
                    name = i == home ? "自宅のカーポート" : "お隣のカーポート",
                    center = new Vector3((xb + xf) / 2, 0, gz), yaw = 90f, width = width, length = depth, lineWidth = Kit.LineW,
                    aisleEnd = +1, bothWays = true, destination = i == home,
                };
                Kit.Line(h, spot.ToWorld(-width / 2, -depth / 2), spot.ToWorld(-width / 2, depth / 2));
                Kit.Line(h, spot.ToWorld(width / 2, -depth / 2), spot.ToWorld(width / 2, depth / 2));
                Kit.WheelStops(h, spot);
                w.spots.Add(spot);
                // カーポートの南隣に家(玄関は生活道路側)
                House(h, new Vector3(-26.25f, 0, gz - width / 2 - 0.95f - 4.5f), 90f, rng, 9f, false,
                    i == home ? new Color(0.96f, 0.93f, 0.85f) : (Color?)null);
                if (i == home)
                {
                    homePlace.Add(new[] { spot });
                    // 自宅の目印(奥の塀に青い札)
                    Util.Box("My Home Sign", h, new Vector3(xb - 0.29f, 0.9f, gz), new Vector3(0.05f, 0.35f, 0.6f), Util.Mat(new Color(0.1f, 0.35f, 0.85f), 0.4f));
                    w.homeRearAxle = Kit.ParkedRearAxle(spot, true);
                    w.homeYaw = spot.yaw;
                }
                else
                {
                    Kit.ParkedCar(h, new Vector3(spot.center.x - 0.3f, 0, gz), rng.NextDouble() < 0.7 ? 90f : 270f, CarKind.Random, rng, out _);
                }
            }
            w.places.Add(homePlace);
            // 案内は生活道路からカーポートに入るところまで
            Entrance(w, homePlace, new Vector3(HomeLaneX, 0, gzs[home]), new Vector3(laneEdge - 0.5f, 0, gzs[home]));
            blocked.Add(Rect.MinMaxRect(-31f, -66f, laneEdge, -Walk));
            w.zones.Add(new Zone { name = "自宅", rect = Rect.MinMaxRect(-31f, -66f, laneEdge, -Walk) });
            w.zones.Add(new Zone { name = "自宅前の生活道路", rect = Rect.MinMaxRect(laneEdge, RingS + Walk, HomeLaneX + LaneW / 2 + 1f, -Walk), hideOnMap = true });
        }

        // ---------------------------------------------------------------- よく行くお店(南西。駐車スペースが狭い)
        static void ShopLot(Transform t, WorldResult w, System.Random rng)
        {
            var lot = new GameObject("Shop 1").transform;
            lot.SetParent(t, false);
            const float x0 = -49f, x1 = -31f, z0 = -40f, z1 = -Walk;
            Kit.Surface(lot, "Lot", x0, z0, x1, z1, Kit.Asphalt);
            Material block = Util.Mat(new Color(0.7f, 0.68f, 0.64f), 0.1f);
            Kit.Wall(lot, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), 1.0f, 0.15f, block);
            Kit.Wall(lot, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), 1.0f, 0.15f, block);
            Kit.Wall(lot, new Vector3(x0, 0, z1 - 0.1f), new Vector3(-44.2f, 0, z1 - 0.1f), 0.8f, 0.12f, block);
            Kit.Wall(lot, new Vector3(-35.8f, 0, z1 - 0.1f), new Vector3(x1, 0, z1 - 0.1f), 0.8f, 0.12f, block);
            mouths.Add(new Mouth { edge = new Vector3(-40f, 0, -HalfRoad), into = Vector3.back, half = 4f, fill = Kit.Concrete });

            // 店(駐車場の奥)
            var shop = Util.BoxMinMax("Shop", lot, new Vector3(x0, 0, z0), new Vector3(x1, 4f, -34.5f), Util.Mat(new Color(0.86f, 0.8f, 0.7f), 0.2f), true);
            shop.layer = Layers.Obstacle;
            Util.BoxMinMax("Shop Glass", lot, new Vector3(-44f, 0.2f, -34.55f), new Vector3(-36f, 2.5f, -34.48f), Kit.Glass);
            Util.BoxMinMax("Shop Awning", lot, new Vector3(-45f, 2.7f, -35.6f), new Vector3(-35f, 2.8f, -34.5f), Util.Mat(new Color(0.7f, 0.25f, 0.2f), 0.3f));

            Kit.RowSpec Row(string name, float ox, Vector3 into, int keep) => new Kit.RowSpec
            {
                name = name, origin = new Vector3(ox, 0, -8.5f), along = Vector3.back, into = into,
                count = 10, width = 2.3f, depth = 5f, uGap = 0f, wheelStops = true, occupancy = 0.6f,
                keepEmpty = keep, fillNeighbors = true, kind = CarKind.Random,
            };
            var place = new Place
            {
                id = "shop1", name = "よく行くお店", note = "駐車スペース 狭い(幅 2.3m)",
            };
            place.Add(Kit.SpotRow(lot, Row("よく行くお店 西", -43f, Vector3.left, 4), rng));
            place.Add(Kit.SpotRow(lot, Row("よく行くお店 東", -37f, Vector3.right, 6), rng));
            w.spots.AddRange(place.spots);
            w.places.Add(place);
            Entrance(w, place, new Vector3(-40f, 0, -HalfRoad), new Vector3(-40f, 0, -Walk - 0.75f));

            w.zones.Add(new Zone { name = "よく行くお店", rect = Rect.MinMaxRect(x0, z0, x1, z1) });
            blocked.Add(Rect.MinMaxRect(x0 - 0.5f, z0 - 0.5f, x1, z1));
        }

        // ---------------------------------------------------------------- 近所のコンビニ(南東。駐車スペースが広い)
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
            Kit.Wall(st, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), 0.7f, 0.5f, hedge);
            foreach (var (a, b) in new[] { (x0, 16f), (24f, 34f), (42f, x1) })
                Kit.Wall(st, new Vector3(a, 0, z1 - 0.3f), new Vector3(b, 0, z1 - 0.3f), 0.7f, 0.5f, hedge);
            mouths.Add(new Mouth { edge = new Vector3(20f, 0, -HalfRoad), into = Vector3.back, half = 4f, fill = Kit.Concrete });
            mouths.Add(new Mouth { edge = new Vector3(38f, 0, -HalfRoad), into = Vector3.back, half = 4f, fill = Kit.Concrete });

            var row = Kit.SpotRow(st, new Kit.RowSpec
            {
                name = "コンビニ駐車場", origin = new Vector3(14.6f, 0, -19f), along = Vector3.right, into = Vector3.back,
                count = 9, width = 3.0f, depth = 5f, uGap = 0f, wheelStops = true, occupancy = 0.35f, keepEmpty = -1,
                kind = CarKind.Random,
            }, rng);
            var place = new Place
            {
                id = "conveni", name = "近所のコンビニ", note = "駐車スペース 広い(幅 3.0m)",
            };
            place.Add(row);
            w.spots.AddRange(row);
            w.places.Add(place);
            Entrance(w, place, new Vector3(20f, 0, -HalfRoad), new Vector3(20f, 0, -Walk - 0.75f));
            Entrance(w, place, new Vector3(38f, 0, -HalfRoad), new Vector3(38f, 0, -Walk - 0.75f));
            Kit.Raised(st, "Grass", Walk, z0, x0 - 0.25f, z1, Kit.Grass);
            w.zones.Add(new Zone { name = "近所のコンビニ", rect = Rect.MinMaxRect(x0, z0, x1, z1) });
            blocked.Add(Rect.MinMaxRect(Walk, z0 - 0.5f, x1 + 0.5f, z1));
        }

        // ---------------------------------------------------------------- ちょっと遠くのスーパー(北東。駐車スペースは中くらい)
        static void Supermarket(Transform t, WorldResult w, System.Random rng)
        {
            var st = new GameObject("Supermarket").transform;
            st.SetParent(t, false);
            const float x0 = 97f, x1 = 143f, z0 = Walk, z1 = 50f;
            Kit.Surface(st, "Lot", x0, z0, x1, z1, Kit.Asphalt);
            var shop = Util.BoxMinMax("Supermarket", st, new Vector3(100f, 0, z1), new Vector3(141f, 6f, 63f), Util.Mat(new Color(0.94f, 0.92f, 0.86f), 0.2f), true);
            shop.layer = Layers.Obstacle;
            Util.BoxMinMax("Glass", st, new Vector3(108f, 0.2f, z1 - 0.05f), new Vector3(133f, 3f, z1 + 0.02f), Kit.Glass);
            Util.BoxMinMax("Band", st, new Vector3(100f, 4.4f, z1 - 0.06f), new Vector3(141f, 5.4f, z1 + 0.03f), Util.Mat(new Color(0.8f, 0.2f, 0.18f), 0.3f));
            Material hedge = Util.Mat(new Color(0.2f, 0.38f, 0.18f), 0.05f);
            Kit.Wall(st, new Vector3(x0 + 0.25f, 0, z0 + 0.3f), new Vector3(x0 + 0.25f, 0, z1), 0.7f, 0.5f, hedge);
            Kit.Wall(st, new Vector3(x1 - 0.25f, 0, z0 + 0.3f), new Vector3(x1 - 0.25f, 0, z1), 0.7f, 0.5f, hedge);
            foreach (var (a, b) in new[] { (x0, 106f), (114f, 128f), (136f, x1) })
                Kit.Wall(st, new Vector3(a, 0, z0 + 0.3f), new Vector3(b, 0, z0 + 0.3f), 0.7f, 0.5f, hedge);
            mouths.Add(new Mouth { edge = new Vector3(110f, 0, HalfRoad), into = Vector3.forward, half = 4f, fill = Kit.Concrete });
            mouths.Add(new Mouth { edge = new Vector3(132f, 0, HalfRoad), into = Vector3.forward, half = 4f, fill = Kit.Concrete });

            Kit.RowSpec Row(string name, float ox, float oz, Vector3 into, int count) => new Kit.RowSpec
            {
                name = name, origin = new Vector3(ox, 0, oz), along = Vector3.right, into = into, count = count,
                width = 2.5f, depth = 5f, uGap = 0f, wheelStops = true, occupancy = 0.45f, keepEmpty = -1, kind = CarKind.Random,
            };
            var place = new Place
            {
                id = "super", name = "ちょっと遠くのスーパー", note = "駐車スペース 中(幅 2.5m)",
            };
            place.Add(Kit.SpotRow(st, Row("スーパー A列", 99f, 44.5f, Vector3.forward, 15), rng));
            place.Add(Kit.SpotRow(st, Row("スーパー B列", 102f, 37.5f, Vector3.back, 14), rng));
            place.Add(Kit.SpotRow(st, Row("スーパー C列", 102f, 27.5f, Vector3.forward, 14), rng));
            place.Add(Kit.SpotRow(st, Row("スーパー D列", 102f, 20.5f, Vector3.back, 14), rng));
            w.spots.AddRange(place.spots);
            w.places.Add(place);
            Entrance(w, place, new Vector3(110f, 0, HalfRoad), new Vector3(110f, 0, Walk + 0.75f));
            Entrance(w, place, new Vector3(132f, 0, HalfRoad), new Vector3(132f, 0, Walk + 0.75f));
            w.zones.Add(new Zone { name = "ちょっと遠くのスーパー", rect = Rect.MinMaxRect(x0, z0, x1, 63f) });
            blocked.Add(Rect.MinMaxRect(x0 - 0.5f, z0, x1, 63.5f));
        }

        // ---------------------------------------------------------------- 縦列駐車帯(北の生活道路の北側)
        static void ParallelBays(Transform t, WorldResult w, System.Random rng)
        {
            var g = new GameObject("Parallel Bays").transform;
            g.SetParent(t, false);
            const float laneZ = 48f, bx0 = 16f, len = 6.5f, depth = 2.3f, taper = 3f;
            const int count = 5;
            float zEdge = laneZ + LaneW / 2, zBack = zEdge + depth, bx1 = bx0 + len * count;
            Kit.Surface(g, "Bays", bx0 - taper, zEdge, bx1 + taper, zBack, Kit.Asphalt);
            Kit.Curb(g, new[]
            {
                new Vector3(bx0 - taper, 0, zEdge + 0.125f), new Vector3(bx0, 0, zBack + 0.125f),
                new Vector3(bx1, 0, zBack + 0.125f), new Vector3(bx1 + taper, 0, zEdge + 0.125f),
            });
            clears.Add(new Clear { o = new Vector3((bx0 + bx1) / 2, 0, zEdge + 0.125f), ax = Vector3.forward, a0 = -0.4f, a1 = 0.4f, half = len * count / 2 + taper });
            for (int k = 0; k <= count; k++)
                Kit.Line(g, new Vector3(bx0 + k * len, 0, zEdge), new Vector3(bx0 + k * len, 0, zBack));
            Kit.Line(g, new Vector3(bx0, 0, zEdge + 0.08f), new Vector3(bx1, 0, zEdge + 0.08f));

            for (int k = 0; k < count; k++)
            {
                float cx = bx0 + len * k + len / 2;
                var spot = new TargetSpot
                {
                    name = $"縦列駐車帯 {k + 1}番", center = new Vector3(cx, 0, (zEdge + zBack) / 2), yaw = 90f,
                    width = depth, length = len, lineWidth = Kit.LineW, aisleEnd = 0, parallel = true,
                };
                spot.curbU = spot.ToLocal(new Vector3(cx, 0, zBack)).x;
                bool occupied = k != 2 && rng.NextDouble() < 0.4;
                spot.destination = !occupied;
                if (occupied) Kit.ParkedCar(g, spot.center + Vector3.forward * 0.1f, 90f, CarKind.Random, rng, out _);
                w.spots.Add(spot);
            }
            w.zones.Add(new Zone { name = "縦列駐車帯", rect = Rect.MinMaxRect(bx0 - taper, laneZ - 2f, bx1 + taper, zBack) });
            w.zones.Add(new Zone { name = "北の生活道路", rect = Rect.MinMaxRect(Walk, laneZ - 3f, 88f, laneZ + 3f), hideOnMap = true });
            blocked.Add(Rect.MinMaxRect(bx0 - taper, zEdge, bx1 + taper, zBack + 1f));
        }

        // ---------------------------------------------------------------- 路上駐車の車
        static void StreetParking(Transform t, System.Random rng)
        {
            var g = new GameObject("Street Parking").transform;
            g.SetParent(t, false);
            // 左側通行なので、各車線の左端に停まっている
            Kit.ParkedCar(g, new Vector3(2.55f, 0, -60f), 180f, CarKind.Random, rng, out _);
            Kit.ParkedCar(g, new Vector3(-60f, 0, -2.55f), -90f, CarKind.Minivan, rng, out _);
            Kit.ParkedCar(g, new Vector3(40f, 0, RingN - 2.55f), -90f, CarKind.Random, rng, out _);
            Kit.ParkedCar(g, new Vector3(RingE - 2.55f, 0, -40f), 0f, CarKind.Random, rng, out _);
            Kit.ParkedCar(g, new Vector3(-2.55f, 0, 62f), 0f, CarKind.Kei, rng, out _);
        }

        // ---------------------------------------------------------------- 家並み
        /// <summary>幹線道路と生活道路(直線の区間)に沿って、道路・駐車場・他の家に重ならない所へ家を並べる</summary>
        static void Houses(Transform t, System.Random rng)
        {
            var g = new GameObject("Houses").transform;
            g.SetParent(t, false);
            var placed = new List<Rect>();

            // 幹線道路の範囲
            var roads = new List<Rect>();
            foreach (var s in segs)
            {
                Vector3 a = W(s, U(s, s.a.p) - Walk, V(s, s.a.p) - Walk), b = W(s, U(s, s.b.p) + Walk, V(s, s.a.p) + Walk);
                roads.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)));
            }
            foreach (var n in nodes)
                roads.Add(Rect.MinMaxRect(n.p.x - CornerC, n.p.z - CornerC, n.p.x + CornerC, n.p.z + CornerC));

            // 生活道路の中心線を 0.5m ごとに
            var lanePts = new List<(Vector2 p, float half)>();
            foreach (var l in lanes)
                for (int i = 0; i < l.pts.Count - 1; i++)
                {
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(l.pts[i], l.pts[i + 1]) / 0.5f));
                    for (int k = 0; k <= n; k++)
                    {
                        Vector3 q = Vector3.Lerp(l.pts[i], l.pts[i + 1], (float)k / n);
                        lanePts.Add((new Vector2(q.x, q.z), l.width / 2));
                    }
                }

            void Try(Vector3 c, Vector3 toRoad)
            {
                // 家の敷地(正面のブロック塀まで): 道路側 7m, 奥 4.6m, 左右 6.3m
                Vector3 side = Vector3.Cross(Vector3.up, toRoad);
                Vector3 p0 = c + toRoad * 7f + side * 6.3f, p1 = c - toRoad * 4.6f - side * 6.3f;
                var r = Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.z, p1.z), Mathf.Max(p0.x, p1.x), Mathf.Max(p0.z, p1.z));
                foreach (var o in roads) if (o.Overlaps(r)) return;
                foreach (var o in blocked) if (o.Overlaps(r)) return;
                foreach (var o in placed) if (o.Overlaps(r)) return;
                foreach (var (p, half) in lanePts)
                {
                    float m = half + 0.6f;
                    if (p.x > r.xMin - m && p.x < r.xMax + m && p.y > r.yMin - m && p.y < r.yMax + m) return;
                }
                placed.Add(r);
                House(g, c, Kit.YawOf(toRoad), rng);
            }

            foreach (var s in segs)
            {
                float v = V(s, s.a.p);
                foreach (float side in new[] { 1f, -1f })
                {
                    SideRange(s, side, out float ua, out float ub);
                    Vector3 toRoad = -Dirs[SideArm(s, side)];
                    for (float u = ua + 7f; u + 7f <= ub; u += 14f)
                        Try(W(s, u, v + side * (Walk + 8f)), toRoad);
                }
            }
            foreach (var l in lanes)
                for (int i = 0; i < l.pts.Count - 1; i++)
                {
                    Vector3 a = l.pts[i], d = l.pts[i + 1] - a;
                    float len = d.magnitude;
                    if (len < 14f) continue;
                    d /= len;
                    Vector3 right = Vector3.Cross(Vector3.up, d);
                    foreach (float side in new[] { 1f, -1f })
                        for (float k = 7f; k + 7f <= len; k += 14f)
                            Try(a + d * k + right * side * (l.width / 2 + 8f), -right * side);
                }
        }

        static readonly Color[] WallColors =
        {
            new Color(0.92f, 0.9f, 0.84f), new Color(0.82f, 0.76f, 0.66f), new Color(0.75f, 0.78f, 0.8f),
            new Color(0.9f, 0.86f, 0.78f), new Color(0.6f, 0.55f, 0.5f), new Color(0.85f, 0.85f, 0.85f),
        };

        /// <summary>家1軒。facing は家から道路へ向かう向き(玄関の向き)。width を指定すると間口を固定する</summary>
        static void House(Transform p, Vector3 pos, float facing, System.Random rng, float width = 0f, bool frontWall = true, Color? color = null)
        {
            var h = Util.Child("House", p, pos, Quaternion.Euler(0, facing, 0)).transform;
            float wdt = width > 0f ? width : 7.5f + (float)rng.NextDouble() * 2f, dep = 7f + (float)rng.NextDouble() * 2f, ht = 5.6f;
            var wall = Util.Mat(color ?? WallColors[rng.Next(WallColors.Length)], 0.1f);
            Util.Box("Body", h, new Vector3(0, ht / 2, 0), new Vector3(wdt, ht, dep), wall, null, true).layer = Layers.Obstacle; // 外部視点のカメラが隠れないか調べるのにも使う
            float half = dep / 2 + 0.4f, rise = 1.6f;
            float slope = Mathf.Atan2(rise, half) * Mathf.Rad2Deg, len = Mathf.Sqrt(half * half + rise * rise);
            Util.Box("Roof N", h, new Vector3(0, ht + rise / 2, dep / 4 + 0.2f), new Vector3(wdt + 0.6f, 0.15f, len), Kit.Roof, Quaternion.Euler(slope, 0, 0));
            Util.Box("Roof S", h, new Vector3(0, ht + rise / 2, -dep / 4 - 0.2f), new Vector3(wdt + 0.6f, 0.15f, len), Kit.Roof, Quaternion.Euler(-slope, 0, 0));
            Util.Box("Window", h, new Vector3(-wdt / 4, ht * 0.68f, dep / 2 + 0.01f), new Vector3(1.8f, 1.1f, 0.05f), Kit.Glass);
            Util.Box("Door", h, new Vector3(wdt / 4, 1.0f, dep / 2 + 0.01f), new Vector3(0.9f, 2.0f, 0.05f), Kit.Dark);
            if (!frontWall) return;
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
            foreach (var s in segs)
            {
                const float side = -1f; // 南側・西側の歩道に立てる
                SideRange(s, side, out float ua, out float ub);
                float v = V(s, s.a.p);
                for (float u = ua + 6f; u < ub - 6f; u += 28f)
                {
                    Vector3 pos = W(s, u, v + side * (Walk - 0.35f));
                    bool nearMouth = false;
                    foreach (var m in mouths) if (Vector3.Distance(m.edge, pos) < m.half + 5f) nearMouth = true;
                    if (!nearMouth) Kit.Pole(g, pos, 0.17f, 10f, concrete);
                }
            }
        }
    }
}
