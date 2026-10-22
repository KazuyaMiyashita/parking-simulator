using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>現在のハンドル角での予想進路(四隅の軌跡)を地面に描く。俯瞰/外部視点とバックモニターで使用。</summary>
    public class PathGuide : MonoBehaviour
    {
        public bool show = true;
        public float length = 6f;
        const int Steps = 48;

        CarController car;
        readonly Ribbon[] ribbons = new Ribbon[4];
        public readonly List<Vector3>[] paths = { new List<Vector3>(), new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
        /// <summary>後退時の左後/右後コーナー軌跡(バックモニター用)</summary>
        public readonly List<Vector3> rearLeft = new List<Vector3>(), rearRight = new List<Vector3>();
        readonly Vector3[] c4 = new Vector3[4];

        public void Init(CarController c)
        {
            car = c;
            var lead = Util.Mat(new Color(1f, 0.85f, 0.1f), 0f);
            var trail = Util.Mat(new Color(0.2f, 0.8f, 1f), 0f);
            for (int i = 0; i < 4; i++)
                ribbons[i] = new Ribbon("Path " + i, transform, i < 2 ? lead : trail, Layers.Guide);
        }

        public static void Predict(Vector3 rear, float yaw, float steerDeg, float dirSign, float length, int steps,
            List<Vector3>[] corners, Vector3[] tmp)
        {
            foreach (var l in corners) l.Clear();
            float ds = dirSign * length / steps;
            for (int i = 0; i <= steps; i++)
            {
                CarSpec.Corners(CarSpec.CenterFromRear(rear, yaw), yaw, tmp);
                for (int k = 0; k < 4; k++) corners[k].Add(tmp[k]);
                CarSpec.Advance(ref rear, ref yaw, ds, steerDeg);
            }
        }

        void LateUpdate()
        {
            if (!car) return;
            float dir = car.gear == Gear.R ? -1f : car.gear == Gear.D ? 1f : 0f;
            var tmpLists = paths;
            if (dir != 0f) Predict(car.rearAxle, car.yaw, car.SteerDeg, dir, length, Steps, tmpLists, c4);
            else foreach (var l in tmpLists) l.Clear();

            rearLeft.Clear(); rearRight.Clear();
            if (dir < 0) { rearLeft.AddRange(paths[3]); rearRight.AddRange(paths[2]); }

            bool vis = show && dir != 0f;
            // 進行方向側の角を黄色、反対側を水色で
            int[] order = dir < 0 ? new[] { 3, 2, 0, 1 } : new[] { 0, 1, 3, 2 };
            for (int i = 0; i < 4; i++)
            {
                ribbons[i].SetVisible(vis);
                if (vis) ribbons[i].Set(paths[order[i]], i < 2 ? 0.07f : 0.04f);
            }
        }
    }
}
