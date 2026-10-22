using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>駐車目標の区画。ローカル座標: u=目標の向きに対して右, w=目標の向き方向</summary>
    public class TargetSpot
    {
        public string name = "";
        public Vector3 center;
        public float yaw;          // 駐車完了時の車の向き(bothWays なら逆向きも可)
        public float width;        // 左右の線(縁石)の中心間
        public float length;       // 奥行
        public float lineWidth;
        public int aisleEnd;       // +1: w=+length/2 が通路側, -1: w=-length/2 が通路側, 0: 両端をチェック(縦列)
        public bool parallel;
        public bool bothWays;      // 前向き/バックどちらで停めてもよい
        public float curbU = float.NaN; // 縦列: 縁石面の u 座標
        public bool destination = true; // マップで目的地に選べる(空いていて、他人の契約区画でない)

        public Vector3 Forward => CarSpec.Forward(yaw);
        public Vector3 Right => CarSpec.Right(yaw);

        public Vector2 ToLocal(Vector3 p)
        {
            Vector3 d = p - center;
            return new Vector2(Vector3.Dot(d, Right), Vector3.Dot(d, Forward));
        }

        public Vector3 ToWorld(float u, float w) => center + Right * u + Forward * w;

        /// <summary>逆向きに停めた場合の区画として見る</summary>
        public TargetSpot Flipped()
        {
            var s = (TargetSpot)MemberwiseClone();
            s.yaw = yaw + 180f;
            s.aisleEnd = -aisleEnd;
            s.curbU = -curbU;
            return s;
        }
    }

    public struct StartPoint
    {
        public string name;
        public Vector3 rearAxle;
        public float yaw;
    }

    public struct Zone
    {
        public string name;
        public Rect rect; // x-z 平面
        public bool hideOnMap; // マップに名前を出さない(細い道など)

        public bool Contains(Vector3 p) => rect.Contains(new Vector2(p.x, p.z));
    }

    public class WorldResult
    {
        public Transform root;
        public readonly List<TargetSpot> spots = new List<TargetSpot>();
        public readonly List<StartPoint> starts = new List<StartPoint>();
        public readonly List<Zone> zones = new List<Zone>(); // 先に登録したものが優先

        public string ZoneName(Vector3 p)
        {
            foreach (var z in zones) if (z.Contains(p)) return z.name;
            return "公道";
        }

        public TargetSpot NearestSpot(Vector3 p, float maxDist)
        {
            TargetSpot best = null;
            float bd = maxDist * maxDist;
            foreach (var s in spots)
            {
                float d = (s.center - p).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }
    }
}
