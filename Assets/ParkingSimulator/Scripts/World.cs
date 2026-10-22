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
        public bool destination = true; // 空いている(他の車が停まっていない)
        public Place place;              // この区画がある場所(縦列駐車帯などは null)

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

    /// <summary>目的地にできる場所(自宅・お店など)。区画のどれかに停めてエンジンを切ると「到着」</summary>
    public class Place
    {
        public string id;          // 記録の保存に使う
        public string name;
        public string note;        // マップに出す説明(駐車スペースの広さなど)
        public bool far;           // 遠くのエリア(近くの目的地を全部回ると行けるようになる)
        public bool home;
        /// <summary>経路案内の行き先(道路から敷地に入ったところ)。複数あれば近い方へ案内する</summary>
        public readonly List<Vector3> entrances = new List<Vector3>();
        public readonly List<TargetSpot> spots = new List<TargetSpot>();

        public Vector3 Center
        {
            get
            {
                if (spots.Count == 0) return entrances.Count > 0 ? entrances[0] : Vector3.zero;
                Vector3 c = Vector3.zero;
                foreach (var s in spots) c += s.center;
                return c / spots.Count;
            }
        }

        public void Add(IEnumerable<TargetSpot> list)
        {
            foreach (var s in list) { s.place = this; spots.Add(s); }
        }
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
        public readonly List<Zone> zones = new List<Zone>(); // 先に登録したものが優先
        public readonly List<Place> places = new List<Place>();
        /// <summary>経路案内に使う道の中心線。端点は近くの別の線につながる</summary>
        public readonly List<List<Vector3>> navLines = new List<List<Vector3>>();
        /// <summary>遠くのエリアへの道をふさぐ通行止めの柵(行けるようになったら消す)</summary>
        public readonly List<GameObject> gates = new List<GameObject>();
        public Vector3 homeRearAxle;
        public float homeYaw;

        public Place PlaceById(string id) => places.Find(p => p.id == id);

        public string ZoneName(Vector3 p)
        {
            foreach (var z in zones) if (z.Contains(p)) return z.name;
            return "公道";
        }
    }
}
