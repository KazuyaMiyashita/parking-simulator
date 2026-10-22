using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 現在のハンドル角での予想進路(車体の四隅の軌跡)を地面に常に描く。
    /// R のときは後ろへ、それ以外は前へ進んだときの線。バックカメラにも後ろの角の軌跡を使う。
    /// </summary>
    public class PathGuide : MonoBehaviour
    {
        public float length = 6f;
        const int Steps = 48;

        CarController car;
        readonly Ribbon[] corners = new Ribbon[4];
        public readonly List<Vector3>[] paths = { new List<Vector3>(), new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
        /// <summary>後退時の左後/右後コーナー軌跡(バックカメラ用)</summary>
        public readonly List<Vector3> rearLeft = new List<Vector3>(), rearRight = new List<Vector3>();
        readonly Vector3[] c4 = new Vector3[4];

        public void Init(CarController c)
        {
            car = c;
            var lead = Util.Mat(new Color(1f, 0.85f, 0.1f), 0f);
            var trail = Util.Mat(new Color(0.2f, 0.8f, 1f), 0f);
            for (int i = 0; i < 4; i++)
                corners[i] = new Ribbon("Path " + i, transform, i < 2 ? lead : trail, Layers.Guide);
        }

        public static void Predict(Vector3 rear, float yaw, float steerDeg, float dirSign, float length, int steps,
            List<Vector3>[] cornerPaths, Vector3[] tmp)
        {
            foreach (var l in cornerPaths) l.Clear();
            float ds = dirSign * length / steps;
            for (int i = 0; i <= steps; i++)
            {
                CarSpec.Corners(CarSpec.CenterFromRear(rear, yaw), yaw, tmp);
                for (int k = 0; k < 4; k++) cornerPaths[k].Add(tmp[k]);
                CarSpec.Advance(ref rear, ref yaw, ds, steerDeg);
            }
        }

        void LateUpdate()
        {
            if (!car) return;
            float dir = car.gear == Gear.R ? -1f : 1f;
            Predict(car.rearAxle, car.yaw, car.SteerDeg, dir, length, Steps, paths, c4);

            rearLeft.Clear(); rearRight.Clear();
            if (dir < 0) { rearLeft.AddRange(paths[3]); rearRight.AddRange(paths[2]); }

            // 進行方向側の角を黄色、反対側を水色で
            int[] order = dir < 0 ? new[] { 3, 2, 0, 1 } : new[] { 0, 1, 3, 2 };
            for (int i = 0; i < 4; i++) corners[i].Set(paths[order[i]], i < 2 ? 0.07f : 0.04f);
        }
    }
}
