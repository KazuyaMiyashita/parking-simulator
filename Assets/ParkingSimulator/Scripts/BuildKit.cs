using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkingSim
{
    /// <summary>町を組み立てる部品</summary>
    public static class Kit
    {
        public const float RoadY = 0.01f;   // 舗装面の高さ(芝生の地面より少し上)
        public const float LineY = 0.02f;   // 白線の上面
        public const float CurbH = 0.15f;   // 縁石・歩道の高さ
        public const float LineW = 0.15f;

        public static Material Asphalt, LineMat, YellowMat, SidewalkMat, CurbMat, Grass, Concrete, StopMat, GuideMat,
            PoleRed, PoleWhite, Fence, Dark, Roof, Glass;

        public static void InitMaterials()
        {
            Asphalt = Util.Mat(new Color(0.21f, 0.215f, 0.225f), 0.05f);
            LineMat = Util.Mat(new Color(0.95f, 0.95f, 0.93f), 0.1f);
            YellowMat = Util.Mat(new Color(0.95f, 0.75f, 0.1f), 0.2f);
            SidewalkMat = Util.Mat(new Color(0.58f, 0.56f, 0.53f), 0.05f);
            CurbMat = Util.Mat(new Color(0.78f, 0.78f, 0.76f), 0.1f);
            Grass = Util.Mat(new Color(0.30f, 0.45f, 0.22f), 0.02f);
            Concrete = Util.Mat(new Color(0.65f, 0.65f, 0.63f), 0.1f);
            StopMat = Util.Mat(new Color(0.85f, 0.75f, 0.15f), 0.2f);
            GuideMat = Util.Mat(new Color(0.1f, 0.85f, 1f), 0.1f);
            PoleRed = Util.Mat(new Color(0.85f, 0.12f, 0.1f), 0.3f);
            PoleWhite = Util.Mat(new Color(0.92f, 0.92f, 0.92f), 0.3f);
            Fence = Util.Mat(new Color(0.18f, 0.32f, 0.22f), 0.2f);
            Dark = Util.Mat(new Color(0.12f, 0.12f, 0.13f), 0.3f);
            Roof = Util.Mat(new Color(0.25f, 0.22f, 0.22f), 0.2f);
            Glass = Util.Mat(new Color(0.15f, 0.22f, 0.28f), 0.9f);
        }

        // ---------------------------------------------------------------- 面
        /// <summary>舗装などの平らな面(当たり判定なし)。上面の高さ top</summary>
        public static GameObject Surface(Transform p, string name, float x0, float z0, float x1, float z1, Material m, float top = RoadY)
        {
            const float t = 0.01f;
            return Util.Box(name, p, new Vector3((x0 + x1) / 2, top - t / 2, (z0 + z1) / 2),
                new Vector3(Mathf.Abs(x1 - x0), t, Mathf.Abs(z1 - z0)), m, null, false, false);
        }

        /// <summary>歩道・植え込みなど一段高い部分(タイヤが当たる)</summary>
        public static GameObject Raised(Transform p, string name, float x0, float z0, float x1, float z1, Material m)
        {
            float xa = Mathf.Min(x0, x1), xb = Mathf.Max(x0, x1), za = Mathf.Min(z0, z1), zb = Mathf.Max(z0, z1);
            var go = Util.BoxMinMax(name, p, new Vector3(xa, 0, za), new Vector3(xb, CurbH, zb), m, true, false);
            go.layer = Layers.Curb;
            return go;
        }

        /// <summary>中心点と周囲の点で作る平らな多角形(扇形の歩道の角など。見た目のみ)</summary>
        public static GameObject Fan(Transform p, string name, Vector3 center, IList<Vector3> rim, float y, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(p, false);
            var verts = new List<Vector3> { new Vector3(center.x, y, center.z) };
            foreach (var r in rim) verts.Add(new Vector3(r.x, y, r.z));
            var tris = new List<int>();
            for (int i = 1; i < verts.Count - 1; i++)
            {
                tris.Add(0); tris.Add(i); tris.Add(i + 1);
                tris.Add(0); tris.Add(i + 1); tris.Add(i); // 両面
            }
            var normals = new List<Vector3>();
            foreach (var _ in verts) normals.Add(Vector3.up);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>左右の縁の点列で作る帯状の面(カーブした道路など。見た目のみ)</summary>
        public static GameObject Ribbon(Transform p, string name, IList<Vector3> left, IList<Vector3> right, float y, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(p, false);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < left.Count; i++)
            {
                verts.Add(new Vector3(left[i].x, y, left[i].z));
                verts.Add(new Vector3(right[i].x, y, right[i].z));
                normals.Add(Vector3.up); normals.Add(Vector3.up);
                if (i == 0) continue;
                int a = (i - 1) * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.AddRange(new[] { a, c, b, b, c, d, a, b, c, b, d, c }); // 両面
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // ---------------------------------------------------------------- 線
        public static void Line(Transform p, Vector3 a, Vector3 b, float w = LineW, Material m = null)
        {
            const float h = 0.006f;
            a.y = b.y = LineY - h / 2;
            Util.Segment("Line", p, a, b, w, h, m ? m : LineMat, false);
        }

        public static void Dashed(Transform p, Vector3 a, Vector3 b, float dash, float gap, float w = LineW)
        {
            float len = Vector3.Distance(a, b);
            Vector3 dir = (b - a).normalized;
            for (float s = 0; s < len; s += dash + gap)
                Line(p, a + dir * s, a + dir * Mathf.Min(len, s + dash), w);
        }

        /// <summary>横断歩道。center から along(車の進行方向)に length、across 方向に width の範囲を縞にする</summary>
        public static void Zebra(Transform p, Vector3 center, Vector3 along, float length, float width)
        {
            Vector3 across = Vector3.Cross(Vector3.up, along).normalized;
            const float stripe = 0.45f;
            int n = Mathf.FloorToInt(width / (stripe * 2));
            float start = -(n * stripe * 2 - stripe) / 2;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = center + across * (start + i * stripe * 2);
                Line(p, c - along * length / 2, c + along * length / 2, stripe);
            }
        }

        // ---------------------------------------------------------------- 縁石・壁・ポール
        /// <summary>点列に沿った縁石(タイヤが当たる)</summary>
        public static void Curb(Transform p, IList<Vector3> pts, float width = 0.25f)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                a.y = b.y = CurbH / 2;
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 1e-3f) continue;
                var go = Util.Box("Curb", p, (a + b) / 2, new Vector3(width, CurbH, len + width * 0.5f), CurbMat,
                    Quaternion.LookRotation(d), true, false);
                go.layer = Layers.Curb;
            }
        }

        /// <summary>a から b への壁・フェンス(車体が当たる)</summary>
        public static GameObject Wall(Transform p, Vector3 a, Vector3 b, float height, float thick, Material m)
        {
            a.y = b.y = height / 2;
            Vector3 d = b - a;
            var go = Util.Box("Wall", p, (a + b) / 2, new Vector3(thick, height, d.magnitude), m,
                Quaternion.LookRotation(d), true);
            go.layer = Layers.Obstacle;
            return go;
        }

        public static GameObject Pole(Transform p, Vector3 pos, float radius, float height, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Pole";
            go.transform.SetParent(p, false);
            go.transform.localPosition = new Vector3(pos.x, height / 2, pos.z);
            go.transform.localScale = new Vector3(radius * 2, height / 2, radius * 2);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.layer = Layers.Obstacle;
            return go;
        }

        // ---------------------------------------------------------------- 経路
        /// <summary>中心 c, 半径 r の円弧(角度は +X から +Z 向きに測る度数)</summary>
        public static List<Vector3> Arc(Vector3 c, float r, float a0, float a1, int seg)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / seg) * Mathf.Deg2Rad;
                pts.Add(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r);
            }
            return pts;
        }

        /// <summary>進行方向の右へ d だけずらした点列(角はマイター結合)</summary>
        public static List<Vector3> Offset(IList<Vector3> path, float d)
        {
            var res = new List<Vector3>();
            int n = path.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 r0 = i > 0 ? RightOf(path[i] - path[i - 1]) : Vector3.zero;
                Vector3 r1 = i < n - 1 ? RightOf(path[i + 1] - path[i]) : Vector3.zero;
                Vector3 r = i == 0 ? r1 : i == n - 1 ? r0 : (r0 + r1).normalized;
                float k = i == 0 || i == n - 1 ? 1f : 1f / Mathf.Max(0.3f, Vector3.Dot(r, r1));
                res.Add(path[i] + r * d * k);
            }
            return res;
        }

        static Vector3 RightOf(Vector3 dir)
        {
            dir.y = 0;
            dir.Normalize();
            return new Vector3(dir.z, 0, -dir.x);
        }

        /// <summary>直線と円弧をつないで中心線を作る(heading: 0=北(+Z), 90=東(+X))</summary>
        public class PathBuilder
        {
            public readonly List<Vector3> pts = new List<Vector3>();
            public Vector3 pos;
            public float heading;

            public PathBuilder(Vector3 start, float heading)
            {
                pos = start; this.heading = heading;
                pts.Add(start);
            }

            public PathBuilder Straight(float len)
            {
                pos += CarSpec.Forward(heading) * len;
                pts.Add(pos);
                return this;
            }

            /// <summary>半径 r で angle 度曲がる(正=右)</summary>
            public PathBuilder Turn(float r, float angle)
            {
                int seg = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(angle) / 5f));
                float step = angle / seg;
                for (int i = 0; i < seg; i++)
                {
                    Vector3 rear = pos; float h = heading;
                    float ds = r * Mathf.Abs(step) * Mathf.Deg2Rad;
                    // 二輪モデルの式を流用: 舵角に相当する曲率で ds 進む
                    float steer = Mathf.Atan(CarSpec.Wheelbase / r) * Mathf.Rad2Deg * Mathf.Sign(angle);
                    CarSpec.Advance(ref rear, ref h, ds, steer);
                    pos = rear; heading = h;
                    pts.Add(pos);
                }
                return this;
            }
        }

        // ---------------------------------------------------------------- 車
        public static GameObject ParkedCar(Transform p, Vector3 center, float yaw, CarKind kind, System.Random rng,
            out VehicleBuilder.DummySpec spec)
        {
            var car = VehicleBuilder.BuildDummy(p, VehicleBuilder.PickKind(kind, rng), rng, out spec);
            car.transform.localPosition = new Vector3(center.x, RoadY, center.z);
            car.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return car;
        }

        public static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ---------------------------------------------------------------- 駐車区画の列
        public struct RowSpec
        {
            public string name;
            public Vector3 origin;     // 通路側の端で、列の最初の境界線の位置
            public Vector3 along;      // 列の並ぶ方向(単位ベクトル)
            public Vector3 into;       // 通路から区画の奥へ向かう方向(単位ベクトル)
            public int count;
            public float width, depth, uGap; // uGap>0 なら U字(ダブルライン)
            public bool wheelStops;
            public float occupancy;
            public int keepEmpty;      // 必ず空ける区画(-1 なら無し)
            public bool fillNeighbors; // keepEmpty の両隣は必ず埋める
            public CarKind kind;
        }

        const float StopFaceFromBack = 1.0f, TireContact = 0.25f;

        /// <summary>区画の列を作り、各区画を TargetSpot として返す</summary>
        public static List<TargetSpot> SpotRow(Transform p, RowSpec r, System.Random rng)
        {
            var row = new GameObject(r.name).transform;
            row.SetParent(p, false);
            var spots = new List<TargetSpot>();
            float G = r.uGap, W = r.width, D = r.depth, pitch = W + G;
            Vector3 P(float s, float d) => r.origin + r.along * s + r.into * d;

            // 白線
            for (int k = 0; k <= r.count; k++)
            {
                float sb = k * pitch;
                if (G <= 0f)
                {
                    Line(row, P(sb, 0), P(sb, D));
                }
                else
                {
                    float rad = G / 2;
                    float sc = sb;
                    Line(row, P(sc - rad, rad), P(sc - rad, D));
                    Line(row, P(sc + rad, rad), P(sc + rad, D));
                    const int n = 10;
                    for (int j = 0; j < n; j++)
                    {
                        float a0 = Mathf.PI * j / n, a1 = Mathf.PI * (j + 1) / n;
                        Vector3 p0 = P(sc - rad * Mathf.Cos(a0), rad - rad * Mathf.Sin(a0));
                        Vector3 p1 = P(sc - rad * Mathf.Cos(a1), rad - rad * Mathf.Sin(a1));
                        Vector3 e = (p1 - p0).normalized * (LineW * 0.25f);
                        Line(row, p0 - e, p1 + e);
                    }
                }
            }

            float outYaw = YawOf(-r.into); // 前が通路を向く向き
            for (int i = 0; i < r.count; i++)
            {
                float s = G / 2 + i * pitch + W / 2;
                var spot = new TargetSpot
                {
                    name = $"{r.name} {i + 1}番",
                    center = P(s, D / 2), yaw = outYaw, width = W, length = D, lineWidth = LineW,
                    aisleEnd = +1, bothWays = true,
                };
                spots.Add(spot);

                if (r.wheelStops)
                {
                    foreach (float sx in new[] { -0.55f, 0.55f })
                    {
                        Vector3 c = P(s + sx, D - StopFaceFromBack + 0.075f);
                        var st = Util.Box("Wheel Stop", row, new Vector3(c.x, 0.06f, c.z), new Vector3(0.6f, 0.12f, 0.15f), StopMat,
                            Quaternion.LookRotation(r.into), true);
                        st.layer = Layers.WheelStop;
                    }
                }

                bool occupied = i != r.keepEmpty &&
                                ((r.fillNeighbors && Mathf.Abs(i - r.keepEmpty) == 1) || rng.NextDouble() < r.occupancy);
                spot.destination = !occupied;
                if (!occupied) continue;

                var kind = VehicleBuilder.PickKind(r.kind, rng);
                var spec = VehicleBuilder.Spec(kind);
                float maxOff = Mathf.Max(0f, (W - LineW - spec.width) / 2 - 0.05f);
                float off = (float)(rng.NextDouble() * 2 - 1) * maxOff * 0.8f;
                float jitter = maxOff > 0.12f ? (float)(rng.NextDouble() * 2 - 1) * 1.5f : 0f;
                bool backIn = rng.NextDouble() < 0.7;
                float fo = spec.length * 0.21f, ro = spec.length * 0.19f;
                float cd = r.wheelStops
                    ? (D - StopFaceFromBack - TireContact) - (spec.length / 2 - (backIn ? ro : fo))
                    : D - 0.3f - spec.length / 2;
                float yaw = (backIn ? outYaw : outYaw + 180f) + jitter;
                ParkedCar(row, P(s + off, cd), yaw, kind, rng, out _);
            }
            return spots;
        }

        /// <summary>区画の奥に輪止めを2つ置く(SpotRow と同じ位置)</summary>
        public static void WheelStops(Transform p, TargetSpot s)
        {
            foreach (float u in new[] { -0.55f, 0.55f })
            {
                Vector3 c = s.ToWorld(u, -s.length / 2 + StopFaceFromBack - 0.075f);
                var st = Util.Box("Wheel Stop", p, new Vector3(c.x, 0.06f, c.z), new Vector3(0.6f, 0.12f, 0.15f), StopMat,
                    Quaternion.LookRotation(-s.Forward), true);
                st.layer = Layers.WheelStop;
            }
        }

        /// <summary>区画にバックで停めた(後輪が輪止めに当たった)状態の自車の後軸位置</summary>
        public static Vector3 ParkedRearAxle(TargetSpot s, bool wheelStops)
        {
            float back = s.length / 2;
            float rearFromBack = wheelStops ? StopFaceFromBack + TireContact + 0.02f : 0.3f + CarSpec.RearOverhang;
            return s.ToWorld(0, -back + rearFromBack);
        }

        /// <summary>俯瞰視点でのみ見える区画の枠</summary>
        public static void GuideOutline(Transform p, TargetSpot s)
        {
            var g = Util.Child("Target Guide", p, Vector3.zero);
            g.layer = Layers.Guide;
            float hu = s.width / 2 - s.lineWidth / 2 - 0.08f, hw = s.length / 2 - 0.08f;
            Vector3[] q = { s.ToWorld(-hu, -hw), s.ToWorld(hu, -hw), s.ToWorld(hu, hw), s.ToWorld(-hu, hw) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = q[i], b = q[(i + 1) % 4];
                a.y = b.y = LineY + 0.005f;
                Util.Segment("Guide", g.transform, a, b, 0.05f, 0.002f, GuideMat, false);
            }
        }
    }
}
