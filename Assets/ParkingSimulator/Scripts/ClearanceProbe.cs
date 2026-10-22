using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 自車のまわりの他の車・壁・柱までの距離(オプション)。車体の外周の点から各障害物の一番近い点を探し、
    /// 近いものから数組を HUD が 3D の線と数字で描く。
    /// </summary>
    public class ClearanceProbe : MonoBehaviour
    {
        public struct Pair { public Vector3 car, obstacle; public float dist; }

        public bool show;
        public readonly List<Pair> pairs = new List<Pair>();

        const float Range = 1.2f, Height = 0.6f, MirrorHeight = 1.02f;
        const int MaxPairs = 4;

        CarController car;
        int mask;
        readonly Collider[] hits = new Collider[32];
        readonly List<Vector3> outline = new List<Vector3>();

        public void Init(CarController c)
        {
            car = c;
            mask = Layers.Mask(Layers.Obstacle);
            // 車体の外周(ローカル座標)を約 25cm ごとに。ミラーの先端も加える
            float hw = CarSpec.HalfWidth, hl = CarSpec.HalfLength;
            void Edge(Vector3 a, Vector3 b)
            {
                int n = Mathf.CeilToInt(Vector3.Distance(a, b) / 0.25f);
                for (int i = 0; i < n; i++) outline.Add(Vector3.Lerp(a, b, (float)i / n));
            }
            Edge(new Vector3(-hw, Height, hl), new Vector3(hw, Height, hl));
            Edge(new Vector3(hw, Height, hl), new Vector3(hw, Height, -hl));
            Edge(new Vector3(hw, Height, -hl), new Vector3(-hw, Height, -hl));
            Edge(new Vector3(-hw, Height, -hl), new Vector3(-hw, Height, hl));
            outline.Add(new Vector3(-CarSpec.MirrorHalfWidth, MirrorHeight, 0.53f));
            outline.Add(new Vector3(CarSpec.MirrorHalfWidth, MirrorHeight, 0.53f));
        }

        void LateUpdate()
        {
            pairs.Clear();
            if (!show || !car) return;
            var t = car.transform;
            Quaternion q = Quaternion.Euler(0, car.yaw, 0);
            Vector3 half = new Vector3(CarSpec.MirrorHalfWidth + Range, 1f, CarSpec.HalfLength + Range);
            int n = Physics.OverlapBoxNonAlloc(car.Center + Vector3.up * 1f, half, hits, q, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = hits[i];
                if (col.transform.IsChildOf(t)) continue;
                var best = new Pair { dist = float.MaxValue };
                foreach (var lp in outline)
                {
                    Vector3 p = t.TransformPoint(lp);
                    Vector3 c = col.ClosestPoint(p);
                    Vector3 d = c - p; d.y = 0f;
                    float dist = d.magnitude;
                    if (dist < best.dist) best = new Pair { car = p, obstacle = new Vector3(c.x, p.y, c.z), dist = dist };
                }
                if (best.dist <= Range) pairs.Add(best);
            }
            pairs.Sort((a, b) => a.dist.CompareTo(b.dist));
            if (pairs.Count > MaxPairs) pairs.RemoveRange(MaxPairs, pairs.Count - MaxPairs);
        }
    }
}
