using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// SUBARU インプレッサ SPORT (GT2 / F54C, FF, CVT) の寸法。単位は m。
    /// 全長・全幅・最小回転半径はユーザー指定値、それ以外はカタログ値相当の想定値。
    /// </summary>
    public static class CarSpec
    {
        public const string DisplayName = "SUBARU IMPREZA SPORT";
        public const string ModelCode = "GT2F54C (FF / CVT)";
        public const string ColorName = "クリスタルホワイト・パール";
        public static readonly Color BodyColor = new Color(0.93f, 0.93f, 0.915f);

        public const float Length = 4.475f;           // 全長
        public const float Width = 1.775f;            // 全幅(ミラー除く)
        public const float Height = 1.480f;           // 全高
        public const float Wheelbase = 2.670f;        // ホイールベース
        public const float TrackFront = 1.540f;       // トレッド 前
        public const float TrackRear = 1.545f;        // トレッド 後
        public const float FrontOverhang = 1.005f;    // フロントオーバーハング
        public const float RearOverhang = Length - Wheelbase - FrontOverhang; // 0.800
        public const float MinTurningRadius = 5.3f;   // 最小回転半径(外側前輪中心の軌跡)
        public const float TireRadius = 0.316f;       // 205/55R16 想定
        public const float TireWidth = 0.205f;
        public const float SteeringRatio = 13.0f;     // ステアリングギア比
        public const float MirrorHalfWidth = 1.01f;   // ミラーを含む半幅
        public const float BodyBottom = 0.25f;        // 当たり判定に使うボディ下端(輪止めの上を通過できる高さ)

        public const float HalfLength = Length * 0.5f;
        public const float HalfWidth = Width * 0.5f;
        /// <summary>後軸中心から車体中心までの距離</summary>
        public const float RearAxleToCenter = HalfLength - RearOverhang;
        /// <summary>車体中心基準のローカル Z</summary>
        public const float FrontAxleZ = HalfLength - FrontOverhang;
        public const float RearAxleZ = -RearAxleToCenter;

        /// <summary>最小回転半径 5.3m となる後軸中心の旋回半径</summary>
        public static readonly float MinRearAxleRadius =
            Mathf.Sqrt(MinTurningRadius * MinTurningRadius - Wheelbase * Wheelbase) - TrackFront * 0.5f;

        /// <summary>二輪モデル換算の最大舵角(約35°)</summary>
        public static readonly float MaxSteerDeg = Mathf.Atan(Wheelbase / MinRearAxleRadius) * Mathf.Rad2Deg;

        /// <summary>ハンドル最大切れ角(片側, 度)</summary>
        public static readonly float MaxWheelDeg = MaxSteerDeg * SteeringRatio;

        public static Vector3 Forward(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        public static Vector3 Right(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
        }

        /// <summary>後軸中心を基準とした運動学(二輪)モデルで ds[m] 進める。steerDeg: 正=右</summary>
        public static void Advance(ref Vector3 rearAxle, ref float yawDeg, float ds, float steerDeg)
        {
            if (Mathf.Abs(steerDeg) < 0.01f)
            {
                rearAxle += Forward(yawDeg) * ds;
                return;
            }
            float radius = Wheelbase / Mathf.Tan(steerDeg * Mathf.Deg2Rad);
            float dth = ds / radius;
            float lx = radius * (1f - Mathf.Cos(dth));
            float lz = radius * Mathf.Sin(dth);
            rearAxle += Right(yawDeg) * lx + Forward(yawDeg) * lz;
            yawDeg += dth * Mathf.Rad2Deg;
        }

        public static Vector3 CenterFromRear(Vector3 rearAxle, float yawDeg) =>
            rearAxle + Forward(yawDeg) * RearAxleToCenter;

        public static Vector3 RearFromCenter(Vector3 center, float yawDeg) =>
            center - Forward(yawDeg) * RearAxleToCenter;

        /// <summary>車体四隅 [0]=左前 [1]=右前 [2]=右後 [3]=左後</summary>
        public static void Corners(Vector3 center, float yawDeg, Vector3[] c)
        {
            Vector3 f = Forward(yawDeg) * HalfLength, r = Right(yawDeg) * HalfWidth;
            c[0] = center + f - r;
            c[1] = center + f + r;
            c[2] = center - f + r;
            c[3] = center - f - r;
        }

        /// <summary>現在の舵角での外側前輪の旋回半径(直進時は Infinity)</summary>
        public static float OuterFrontRadius(float steerDeg)
        {
            float a = Mathf.Abs(steerDeg);
            if (a < 0.05f) return float.PositiveInfinity;
            float rrc = Wheelbase / Mathf.Tan(a * Mathf.Deg2Rad);
            float x = rrc + TrackFront * 0.5f;
            return Mathf.Sqrt(x * x + Wheelbase * Wheelbase);
        }

        /// <summary>アッカーマン配置での左右前輪の実舵角</summary>
        public static void Ackermann(float steerDeg, out float left, out float right)
        {
            float a = Mathf.Abs(steerDeg);
            if (a < 0.01f) { left = right = 0f; return; }
            float rrc = Wheelbase / Mathf.Tan(a * Mathf.Deg2Rad);
            float inner = Mathf.Atan(Wheelbase / Mathf.Max(0.01f, rrc - TrackFront * 0.5f)) * Mathf.Rad2Deg;
            float outer = Mathf.Atan(Wheelbase / (rrc + TrackFront * 0.5f)) * Mathf.Rad2Deg;
            if (steerDeg > 0) { right = inner; left = outer; }
            else { left = -inner; right = -outer; }
        }
    }
}
