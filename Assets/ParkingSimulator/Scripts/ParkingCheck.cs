using UnityEngine;

namespace ParkingSim
{
    /// <summary>エンジンを切ったときに、車体が駐車枠に収まっているかを調べる(点数は付けない)</summary>
    public static class ParkingCheck
    {
        public enum Fit { None, Partial, Inside }

        static readonly Vector3[] c4 = new Vector3[4];

        /// <summary>車体の四隅がすべて枠(白線の内側)に入っていれば Inside、一部だけ入っていれば Partial</summary>
        public static Fit Check(TargetSpot s, CarController car)
        {
            CarSpec.Corners(car.Center, car.yaw, c4);
            float hu = s.width / 2 - s.lineWidth / 2, hw = s.length / 2;
            // 縦列は前後の線も内側まで
            float hwIn = s.parallel ? hw - s.lineWidth / 2 : hw;
            int inside = 0, touching = 0;
            foreach (var p in c4)
            {
                Vector2 l = s.ToLocal(p);
                if (Mathf.Abs(l.x) <= hu && Mathf.Abs(l.y) <= hwIn) inside++;
                if (Mathf.Abs(l.x) <= s.width / 2 + s.lineWidth / 2 && Mathf.Abs(l.y) <= hw) touching++;
            }
            Vector2 c = s.ToLocal(car.Center);
            bool centerIn = Mathf.Abs(c.x) <= s.width / 2 && Mathf.Abs(c.y) <= hw;
            if (inside == 4) return Fit.Inside;
            return touching > 0 || centerIn ? Fit.Partial : Fit.None;
        }

        /// <summary>枠に収まっている区画(なければ一部だけ入っている区画)を探す</summary>
        public static Fit Find(WorldResult w, CarController car, out TargetSpot spot)
        {
            spot = null;
            var best = Fit.None;
            foreach (var s in w.spots)
            {
                if ((s.center - car.Center).sqrMagnitude > 64f) continue;
                var f = Check(s, car);
                if (f == Fit.Inside) { spot = s; return f; }
                if (f == Fit.Partial && best == Fit.None) { best = f; spot = s; }
            }
            return best;
        }
    }
}
