using UnityEngine;

namespace ParkingSim
{
    public class LotResult
    {
        public Transform root;
        public TargetSpot target;
        public Vector3 startRearAxle;
        public float startYaw;
        public Vector3 viewCenter;
        public float viewSize = 10f;
    }

    /// <summary>
    /// 設定から駐車場を生成する。
    /// 直角駐車: 通路は X 方向、目標の区画列は z=0~奥行 (z=0 が通路側)、通路は z=-通路幅~0。
    /// 縦列駐車: 縁石面が z=0、停車帯は z&lt;0、進行方向は +X(左側通行)。
    /// </summary>
    public static class ParkingLotBuilder
    {
        public const float LineW = 0.15f;
        const float LineH = 0.006f;
        const int SpotsEachSide = 4;
        const float StopFaceFromBack = 1.0f; // 区画後端から輪止め前面まで
        const float StopDepth = 0.15f, StopHeight = 0.12f, StopLength = 0.6f;
        const float TireContact = 0.25f;     // 輪止めに当たった時のタイヤ中心と輪止め面の水平距離

        static Material lineMat, asphalt, concrete, stopMat, guideMat, curbMat, sidewalk, building, tree, leaves;

        static void InitMaterials()
        {
            lineMat = Util.Mat(new Color(0.95f, 0.95f, 0.93f), 0.1f);
            asphalt = Util.Mat(new Color(0.21f, 0.215f, 0.225f), 0.05f);
            concrete = Util.Mat(new Color(0.62f, 0.62f, 0.6f), 0.1f);
            stopMat = Util.Mat(new Color(0.85f, 0.75f, 0.15f), 0.2f);
            guideMat = Util.Mat(new Color(0.1f, 0.85f, 1f), 0.1f);
            curbMat = Util.Mat(new Color(0.7f, 0.7f, 0.68f), 0.1f);
            sidewalk = Util.Mat(new Color(0.55f, 0.52f, 0.48f), 0.05f);
            building = Util.Mat(new Color(0.78f, 0.74f, 0.66f), 0.1f);
            tree = Util.Mat(new Color(0.35f, 0.25f, 0.15f), 0.1f);
            leaves = Util.Mat(new Color(0.18f, 0.42f, 0.2f), 0.05f);
        }

        public static LotResult Build(ScenarioConfig c, Transform parent)
        {
            InitMaterials();
            var root = new GameObject("Parking Lot").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(c.seed);

            Util.Box("Ground", root, new Vector3(0, -0.05f, 0), new Vector3(400, 0.1f, 400), asphalt, null, false, false);

            var res = c.type == ParkingType.Parallel ? BuildParallel(c, root, rng) : BuildPerpendicular(c, root, rng);
            res.root = root;
            return res;
        }

        // ---------------------------------------------------------------- 直角駐車
        static LotResult BuildPerpendicular(ScenarioConfig c, Transform root, System.Random rng)
        {
            float W = c.spotWidth, D = c.spotDepth, A = Mathf.Max(3.0f, c.aisleWidth);
            bool u = c.lineType == LineType.U;
            float G = u ? c.uGap : 0f;
            float pitch = W + G;
            float rowHalf = (SpotsEachSide + 0.5f) * pitch;

            // 目標側の列
            BuildRow(c, root, rng, +1, pitch, W, G, D, A, true);
            // 後方の壁
            float wallZ = D + c.backSpace + 0.1f;
            Util.Box("Back Wall", root, new Vector3(0, 0.55f, wallZ), new Vector3(rowHalf * 2 + 2, 1.1f, 0.2f), concrete, null, true).layer = Layers.Obstacle;
            Util.Box("Building", root, new Vector3(0, 5f, wallZ + 9f), new Vector3(60, 10f, 8f), building);

            // 対面
            switch (c.opposite)
            {
                case OppositeSide.ParkedCars:
                    BuildRow(c, root, rng, -1, pitch, W, G, D, A, false);
                    Util.Box("Opposite Wall", root, new Vector3(0, 0.55f, -A - D - c.backSpace - 0.1f), new Vector3(rowHalf * 2 + 2, 1.1f, 0.2f), concrete, null, true).layer = Layers.Obstacle;
                    break;
                case OppositeSide.Wall:
                    Util.Box("Opposite Wall", root, new Vector3(0, 0.9f, -A - 0.15f), new Vector3(rowHalf * 2 + 20, 1.8f, 0.3f), concrete, null, true).layer = Layers.Obstacle;
                    break;
                case OppositeSide.Open:
                    Util.Box("Aisle Edge", root, new Vector3(0, LineH / 2, -A - 0.1f), new Vector3(rowHalf * 2, LineH, 0.1f), lineMat, null, false, false);
                    Trees(root, -A - 4f, rowHalf);
                    break;
            }
            Util.Box("Building Far", root, new Vector3(0, 6f, -A - D - 20f), new Vector3(70, 12f, 10f), building);

            // 柱
            if (c.pillar == PillarPos.Left || c.pillar == PillarPos.Both) Pillar(root, -pitch / 2);
            if (c.pillar == PillarPos.Right || c.pillar == PillarPos.Both) Pillar(root, pitch / 2);

            var spot = new TargetSpot
            {
                center = new Vector3(0, 0, D / 2), width = W, length = D, lineWidth = LineW,
                yaw = c.type == ParkingType.BackIn ? 180f : 0f,
                aisleEnd = c.type == ParkingType.BackIn ? +1 : -1,
            };
            GuideOutline(root, spot);

            float dir = c.approachFromLeft ? 1f : -1f;
            return new LotResult
            {
                target = spot,
                startRearAxle = new Vector3(-dir * 9f, 0, -A * 0.5f),
                startYaw = dir > 0 ? 90f : -90f,
                viewCenter = new Vector3(0, 0, (D - A) * 0.5f),
                viewSize = (D + A) * 0.5f + 2.5f,
            };
        }

        /// <summary>区画の列を1つ作る。side=+1: 目標側(z=0~D), -1: 対面(z=-A~-A-D)</summary>
        static void BuildRow(ScenarioConfig c, Transform root, System.Random rng, int side, float pitch,
            float W, float G, float D, float A, bool targetRow)
        {
            var row = new GameObject(targetRow ? "Row (Target)" : "Row (Opposite)").transform;
            row.SetParent(root, false);
            // ローカル z(通路端から奥へ 0~D) をワールドへ
            float Z(float z) => side > 0 ? z : -A - z;

            // 白線
            for (int k = -SpotsEachSide - 1; k <= SpotsEachSide; k++)
            {
                float xb = (k + 0.5f) * pitch;
                if (c.lineType == LineType.I)
                {
                    Line(row, new Vector3(xb, 0, Z(0)), new Vector3(xb, 0, Z(D)));
                }
                else
                {
                    float r = G / 2;
                    Line(row, new Vector3(xb - r, 0, Z(r)), new Vector3(xb - r, 0, Z(D)));
                    Line(row, new Vector3(xb + r, 0, Z(r)), new Vector3(xb + r, 0, Z(D)));
                    const int n = 10;
                    for (int j = 0; j < n; j++)
                    {
                        float a0 = Mathf.PI * j / n, a1 = Mathf.PI * (j + 1) / n;
                        var p0 = new Vector3(xb - r * Mathf.Cos(a0), 0, Z(r - r * Mathf.Sin(a0)));
                        var p1 = new Vector3(xb - r * Mathf.Cos(a1), 0, Z(r - r * Mathf.Sin(a1)));
                        Vector3 d = (p1 - p0).normalized * (LineW * 0.25f);
                        Line(row, p0 - d, p1 + d);
                    }
                }
            }

            for (int i = -SpotsEachSide; i <= SpotsEachSide; i++)
            {
                float x = i * pitch;
                if (c.wheelStops)
                {
                    foreach (float sx in new[] { -0.55f, 0.55f })
                    {
                        float z0 = D - StopFaceFromBack, z1 = z0 + StopDepth;
                        var st = Util.Box("Wheel Stop", row, new Vector3(x + sx, StopHeight / 2, (Z(z0) + Z(z1)) / 2),
                            new Vector3(StopLength, StopHeight, StopDepth), stopMat, null, true);
                        st.layer = Layers.WheelStop;
                    }
                }

                if (targetRow && i == 0) continue; // 目標区画は空ける
                bool occupied;
                if (targetRow && i == -1) occupied = c.leftNeighbor;
                else if (targetRow && i == 1) occupied = c.rightNeighbor;
                else occupied = rng.NextDouble() < (targetRow ? c.otherOccupancy : Mathf.Max(0.5f, c.otherOccupancy));
                if (!occupied) continue;

                bool neighbor = targetRow && Mathf.Abs(i) == 1;
                var kind = VehicleBuilder.PickKind(neighbor ? c.neighborKind : CarKind.Random, rng);
                var car = VehicleBuilder.BuildDummy(row, kind, rng, out var s);
                float maxOff = Mathf.Max(0f, (W - LineW - s.width) / 2 - 0.05f);
                float off = 0f, yawJitter = 0f;
                NeighborBias bias = neighbor ? c.neighborBias : NeighborBias.Random;
                bool pillarSide = neighbor && (c.pillar == PillarPos.Both ||
                                               (i == -1 && c.pillar == PillarPos.Left) ||
                                               (i == 1 && c.pillar == PillarPos.Right));
                if (pillarSide) bias = NeighborBias.Center;
                switch (bias)
                {
                    case NeighborBias.TowardTarget: off = -Mathf.Sign(i) * maxOff; break;
                    case NeighborBias.Random:
                        off = (float)(rng.NextDouble() * 2 - 1) * maxOff * (neighbor ? 0.9f : 0.6f);
                        if (maxOff > 0.12f) yawJitter = (float)(rng.NextDouble() * 2 - 1) * 1.5f;
                        break;
                }

                bool backIn = rng.NextDouble() < 0.75; // 前が通路側を向く
                float fo = s.length * 0.21f, ro = s.length * 0.19f;
                float centerZ; // ローカル z(通路端から)
                if (c.wheelStops)
                {
                    float axle = D - StopFaceFromBack - TireContact;
                    centerZ = backIn ? axle - (s.length / 2 - ro) : axle - (s.length / 2 - fo);
                }
                else centerZ = D - 0.3f - s.length / 2;

                float facing = backIn ? -1f : 1f; // ローカル z の向き
                float worldYaw = (facing * side > 0 ? 0f : 180f) + yawJitter;
                car.transform.localPosition = new Vector3(x + off, 0, Z(centerZ));
                car.transform.localRotation = Quaternion.Euler(0, worldYaw, 0);
            }
        }

        static void Pillar(Transform root, float x)
        {
            var p = Util.Box("Pillar", root, new Vector3(x, 1.5f, 0.5f), new Vector3(0.5f, 3f, 0.8f), concrete, null, true);
            p.layer = Layers.Obstacle;
            var stripe = Util.Mat(new Color(0.95f, 0.75f, 0.1f), 0.2f);
            Util.Box("Pillar Stripe", p.transform, new Vector3(0, -0.35f, 0), new Vector3(1.02f, 0.06f, 1.02f), stripe, null, false, false);
        }

        static void Trees(Transform root, float z, float half)
        {
            for (float x = -half; x <= half; x += 6f)
            {
                var t = Util.Child("Tree", root, new Vector3(x, 0, z)).transform;
                Util.Cylinder("Trunk", t, new Vector3(0, 1.0f, 0), 0.12f, 2.0f, tree);
                Util.Sphere("Leaves", t, new Vector3(0, 2.6f, 0), new Vector3(2.2f, 2.0f, 2.2f), leaves);
            }
        }

        // ---------------------------------------------------------------- 縦列駐車
        static LotResult BuildParallel(ScenarioConfig c, Transform root, System.Random rng)
        {
            float gap = c.parallelGap, Wp = c.parallelLaneWidth, A = Mathf.Max(2.8f, c.aisleWidth);
            const float span = 40f;

            // 縁石と歩道(タイヤが当たる)
            var curb = Util.BoxMinMax("Curb & Sidewalk", root, new Vector3(-span, 0, 0), new Vector3(span, 0.15f, 3.0f), sidewalk, true);
            curb.layer = Layers.WheelStop;
            Util.BoxMinMax("Curb Edge", root, new Vector3(-span, 0, 0), new Vector3(span, 0.152f, 0.15f), curbMat);
            Util.BoxMinMax("Fence", root, new Vector3(-span, 0.15f, 2.9f), new Vector3(span, 1.2f, 3.0f), concrete, true).layer = Layers.Obstacle;
            Util.Box("Building", root, new Vector3(0, 5f, 9f), new Vector3(70, 10f, 8f), building);

            // 停車帯の外側線
            Line(root, new Vector3(-span, 0, -Wp), new Vector3(span, 0, -Wp));
            // 目標の枠
            float bx = gap / 2 - 0.1f;
            const float curbLineZ = -0.075f;
            Line(root, new Vector3(-bx, 0, curbLineZ), new Vector3(bx, 0, curbLineZ));
            Line(root, new Vector3(-bx, 0, curbLineZ), new Vector3(-bx, 0, -Wp));
            Line(root, new Vector3(bx, 0, curbLineZ), new Vector3(bx, 0, -Wp));

            // 前後の車(縁石から 0.25m)。edge = 置いた車の遠い側の端
            float edge = 0;
            void Park(float nearX, bool ahead, CarKind k)
            {
                var car = VehicleBuilder.BuildDummy(root, VehicleBuilder.PickKind(k, rng), rng, out var s);
                float cx = ahead ? nearX + s.length / 2 : nearX - s.length / 2;
                car.transform.localPosition = new Vector3(cx, 0, -0.25f - s.width / 2);
                car.transform.localRotation = Quaternion.Euler(0, 90f, 0);
                edge = ahead ? cx + s.length / 2 : cx - s.length / 2;
            }
            Park(gap / 2, true, c.neighborKind);
            float fx = edge;
            for (int k = 0; k < 3; k++)
            {
                if (rng.NextDouble() < 0.7) { Park(fx + 1.2f + (float)rng.NextDouble(), true, CarKind.Random); fx = edge; }
                else fx += 5.5f;
            }
            Park(-gap / 2, false, c.neighborKind);
            float rx = edge;
            for (int k = 0; k < 2; k++)
            {
                if (rng.NextDouble() < 0.7) { Park(rx - 1.2f - (float)rng.NextDouble(), false, CarKind.Random); rx = edge; }
                else rx -= 5.5f;
            }

            // 道路の反対側
            float farZ = -Wp - A;
            switch (c.opposite)
            {
                case OppositeSide.ParkedCars:
                    Util.BoxMinMax("Far Curb", root, new Vector3(-span, 0, farZ - 3f - 2.2f), new Vector3(span, 0.15f, farZ - 2.2f), sidewalk, true).layer = Layers.WheelStop;
                    for (float x = -span + 4; x < span - 4; x += 6.5f)
                    {
                        if (rng.NextDouble() > 0.6) continue;
                        var car = VehicleBuilder.BuildDummy(root, VehicleBuilder.PickKind(CarKind.Random, rng), rng, out var s);
                        car.transform.localPosition = new Vector3(x, 0, farZ - 2.2f + 0.25f + s.width / 2);
                        car.transform.localRotation = Quaternion.Euler(0, -90f, 0);
                    }
                    Line(root, new Vector3(-span, 0, farZ), new Vector3(span, 0, farZ));
                    break;
                case OppositeSide.Wall:
                    Util.BoxMinMax("Far Wall", root, new Vector3(-span, 0, farZ - 0.3f), new Vector3(span, 1.8f, farZ), concrete, true).layer = Layers.Obstacle;
                    break;
                case OppositeSide.Open:
                    Line(root, new Vector3(-span, 0, farZ), new Vector3(span, 0, farZ));
                    Trees(root, farZ - 6f, span);
                    break;
            }

            var spot = new TargetSpot
            {
                center = new Vector3(0, 0, (curbLineZ - Wp) / 2),
                width = Wp + curbLineZ,
                length = gap - 0.2f,

                lineWidth = LineW,
                yaw = 90f,
                aisleEnd = 0,
                parallel = true,
            };
            spot.curbU = spot.ToLocal(Vector3.zero).x;
            GuideOutline(root, spot);

            float laneZ = -Wp - Mathf.Min(A, 3.6f) * 0.5f;
            return new LotResult
            {
                target = spot,
                startRearAxle = new Vector3(-gap / 2 - 9f, 0, laneZ),
                startYaw = 90f,
                viewCenter = new Vector3(0, 0, -(Wp + A) * 0.5f + 1f),
                viewSize = (Wp + A) * 0.5f + 4f,
            };
        }

        // ---------------------------------------------------------------- 共通
        static void Line(Transform parent, Vector3 a, Vector3 b)
        {
            a.y = b.y = LineH / 2;
            Util.Segment("Line", parent, a, b, LineW, LineH, lineMat, false);
        }

        /// <summary>俯瞰視点でのみ見える目標区画の枠</summary>
        static void GuideOutline(Transform root, TargetSpot s)
        {
            var g = Util.Child("Target Guide", root, Vector3.zero);
            g.layer = Layers.Guide;
            float hu = s.width / 2 - LineW / 2 - 0.08f, hw = s.length / 2 - 0.08f;
            if (!s.parallel) hw = s.length / 2 - 0.05f;
            Vector3[] p = { s.ToWorld(-hu, -hw), s.ToWorld(hu, -hw), s.ToWorld(hu, hw), s.ToWorld(-hu, hw) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = p[i], b = p[(i + 1) % 4];
                a.y = b.y = 0.009f;
                Util.Segment("Guide", g.transform, a, b, 0.04f, 0.002f, guideMat, false);
            }
        }
    }
}
