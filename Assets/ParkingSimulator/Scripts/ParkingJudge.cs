using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    public class JudgeResult
    {
        public bool nearTarget;
        public bool inside;
        public float leftMargin, rightMargin; // 車の左右から白線内側までの余白(負=線にかかっている)
        public float angleErr;                // 目標の向きとのずれ(度)
        public float protrusion;              // 通路側/前後へのはみ出し量(正=はみ出し)
        public float curbDist = float.NaN;    // 縦列: 縁石までの距離
        public float centerOffset;            // 区画中心からの左右ずれ(正=右寄り)
        public int collisions, switches;
        public float time;
        public int score;
        public string rank = "-";
        public readonly List<string> notes = new List<string>();
    }

    public static class ParkingJudge
    {
        static readonly Vector3[] c4 = new Vector3[4];

        public static JudgeResult Measure(TargetSpot s, CarController car)
        {
            var r = new JudgeResult();
            CarSpec.Corners(car.Center, car.yaw, c4);
            float minU = float.MaxValue, maxU = float.MinValue, minW = float.MaxValue, maxW = float.MinValue;
            foreach (var p in c4)
            {
                Vector2 l = s.ToLocal(p);
                minU = Mathf.Min(minU, l.x); maxU = Mathf.Max(maxU, l.x);
                minW = Mathf.Min(minW, l.y); maxW = Mathf.Max(maxW, l.y);
            }
            float inner = s.width / 2 - s.lineWidth / 2;
            float halfL = s.length / 2;
            r.leftMargin = minU + inner;
            r.rightMargin = inner - maxU;
            r.centerOffset = (r.leftMargin - r.rightMargin) * 0.5f;
            r.angleErr = Mathf.DeltaAngle(s.yaw, car.yaw);
            if (s.aisleEnd > 0) r.protrusion = maxW - halfL;
            else if (s.aisleEnd < 0) r.protrusion = -halfL - minW;
            else r.protrusion = Mathf.Max(maxW - (halfL - s.lineWidth / 2), (-halfL + s.lineWidth / 2) - minW);
            if (s.parallel) r.curbDist = minU - s.curbU;

            Vector2 cl = s.ToLocal(car.Center);
            r.nearTarget = Mathf.Abs(cl.x) < s.width / 2 + 1.5f && Mathf.Abs(cl.y) < s.length / 2 + 1.5f &&
                           Mathf.Abs(r.angleErr) < 45f;
            r.inside = r.leftMargin >= 0 && r.rightMargin >= 0 && r.protrusion <= 0;
            return r;
        }

        public static JudgeResult Evaluate(TargetSpot s, CarController car)
        {
            var r = Measure(s, car);
            r.collisions = car.collisions;
            r.switches = car.directionChanges;
            r.time = car.driveTime;

            if (!r.nearTarget)
            {
                r.score = 0; r.rank = "-";
                r.notes.Add(Mathf.Abs(r.angleErr) >= 45f && Mathf.Abs(r.angleErr) < 135f
                    ? "目標区画に対して向きが大きくずれています"
                    : Mathf.Abs(r.angleErr) >= 135f ? "車の向きが逆です(駐車方法の設定を確認)" : "目標区画に入っていません");
                return r;
            }

            float score = 100f;
            float ang = Mathf.Abs(r.angleErr);
            score -= Mathf.Min(30f, ang * 4f);
            if (ang > 3f) r.notes.Add($"車が {ang:0.0}° 傾いています");

            if (!s.parallel)
            {
                float off = Mathf.Abs(r.centerOffset);
                score -= Mathf.Min(25f, off * 50f);
                if (off > 0.15f) r.notes.Add($"区画の中心から{(r.centerOffset > 0 ? "右" : "左")}に {off * 100:0} cm 寄っています");
            }
            else
            {
                if (r.curbDist > 0.3f)
                {
                    score -= Mathf.Min(25f, (r.curbDist - 0.3f) * 60f);
                    r.notes.Add($"縁石から {r.curbDist * 100:0} cm 離れています(30cm 以内が目安)");
                }
            }

            float minMargin = Mathf.Min(r.leftMargin, r.rightMargin);
            if (minMargin < -s.lineWidth)
            {
                score -= 30f;
                r.notes.Add("白線から車体がはみ出しています");
            }
            else if (minMargin < 0f)
            {
                score -= 12f;
                r.notes.Add("白線を踏んでいます(真上から見て車体が線にかかっている)");
            }

            if (r.protrusion > 0f)
            {
                score -= Mathf.Min(30f, 10f + r.protrusion * 40f);
                r.notes.Add(s.parallel ? $"枠の前後から {r.protrusion * 100:0} cm はみ出しています"
                                       : $"通路に {r.protrusion * 100:0} cm はみ出しています");
            }

            if (r.collisions > 0)
            {
                score -= 25f * r.collisions;
                r.notes.Add($"接触 {r.collisions} 回");
            }

            int free = s.parallel ? 2 : 1;
            int extra = Mathf.Max(0, r.switches - free);
            if (extra > 0)
            {
                score -= extra * 4f;
                r.notes.Add($"切り返し {r.switches} 回");
            }

            r.score = Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);
            r.rank = r.score >= 95 ? "S" : r.score >= 85 ? "A" : r.score >= 70 ? "B" : r.score >= 50 ? "C" : "D";
            if (r.notes.Count == 0) r.notes.Add("完璧です!");
            return r;
        }
    }
}
