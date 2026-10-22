using UnityEngine;

namespace ParkingSim
{
    public enum ParkingType { BackIn, FrontIn, Parallel }
    public enum LineType { U, I }
    public enum NeighborBias { Center, TowardTarget, Random }
    public enum OppositeSide { ParkedCars, Wall, Open }
    public enum PillarPos { None, Left, Right, Both }
    public enum CarKind { Random, Kei, Compact, Sedan, Minivan, SUV, LargeMinivan }

    /// <summary>駐車ケースの設定</summary>
    [System.Serializable]
    public class ScenarioConfig
    {
        public string name = "カスタム";
        public ParkingType type = ParkingType.BackIn;
        public LineType lineType = LineType.U;
        public float spotWidth = 2.5f;      // 区画幅(自区画の左右の線の中心間)
        public float uGap = 0.6f;           // U字の2本線の間隔(中心間) = 隣の区画との余裕
        public float spotDepth = 5.0f;      // 区画の奥行
        public float aisleWidth = 6.0f;     // 区画前方のスペース(通路幅/道路幅)
        public float backSpace = 0.5f;      // 区画後端から壁まで
        public bool wheelStops = true;
        public bool leftNeighbor = true;    // 通路から区画を見て左隣
        public bool rightNeighbor = true;   // 通路から区画を見て右隣
        public CarKind neighborKind = CarKind.Random;
        public NeighborBias neighborBias = NeighborBias.Random;
        public float otherOccupancy = 0.6f; // 隣以外の区画の埋まり具合
        public OppositeSide opposite = OppositeSide.ParkedCars;
        public PillarPos pillar = PillarPos.None;
        public bool approachFromLeft = true; // 区画を左手に見ながら進入
        public float parallelGap = 7.0f;     // 縦列: 前後の車の間隔
        public float parallelLaneWidth = 2.3f; // 縦列: 停車帯の幅
        public int seed = 1;

        public ScenarioConfig Clone() => (ScenarioConfig)MemberwiseClone();

        public static readonly string[] PresetNames =
        {
            "標準 U字2.5m 通路6.0m",
            "ゆったり U字2.8m 通路7.0m",
            "コンビニ I字2.5m 通路8.0m",
            "狭い I字2.3m 通路5.0m 隣が寄せ",
            "激狭 I字2.2m 通路4.5m 大型車",
            "地下 I字2.4m 柱あり 通路5.5m",
            "前向き駐車 U字2.5m 通路5.5m",
            "縦列駐車 車間7.0m",
            "縦列駐車(狭) 車間6.0m 道路4.0m",
        };

        public static ScenarioConfig Preset(int i)
        {
            var c = new ScenarioConfig { name = PresetNames[Mathf.Clamp(i, 0, PresetNames.Length - 1)] };
            switch (i)
            {
                case 0: break;
                case 1:
                    c.spotWidth = 2.8f; c.uGap = 0.8f; c.aisleWidth = 7.0f; c.neighborBias = NeighborBias.Center;
                    break;
                case 2:
                    c.lineType = LineType.I; c.aisleWidth = 8.0f; c.opposite = OppositeSide.Open;
                    c.otherOccupancy = 0.4f;
                    break;
                case 3:
                    c.lineType = LineType.I; c.spotWidth = 2.3f; c.aisleWidth = 5.0f;
                    c.neighborBias = NeighborBias.TowardTarget;
                    break;
                case 4:
                    c.lineType = LineType.I; c.spotWidth = 2.2f; c.aisleWidth = 4.5f;
                    c.neighborKind = CarKind.LargeMinivan; c.neighborBias = NeighborBias.TowardTarget;
                    c.otherOccupancy = 0.9f;
                    break;
                case 5:
                    c.lineType = LineType.I; c.spotWidth = 2.4f; c.aisleWidth = 5.5f;
                    c.pillar = PillarPos.Left; c.opposite = OppositeSide.Wall; c.backSpace = 0.3f;
                    break;
                case 6:
                    c.type = ParkingType.FrontIn; c.aisleWidth = 5.5f;
                    break;
                case 7:
                    c.type = ParkingType.Parallel; c.parallelGap = 7.0f; c.aisleWidth = 5.5f;
                    c.opposite = OppositeSide.Open;
                    break;
                case 8:
                    c.type = ParkingType.Parallel; c.parallelGap = 6.0f; c.aisleWidth = 4.0f;
                    c.opposite = OppositeSide.ParkedCars;
                    break;
            }
            return c;
        }

        public static string TypeName(ParkingType t) =>
            t == ParkingType.BackIn ? "バック駐車" : t == ParkingType.FrontIn ? "前向き駐車" : "縦列駐車";

        public string Describe()
        {
            if (type == ParkingType.Parallel)
                return $"{TypeName(type)} / 車間 {parallelGap:0.00}m / 停車帯 {parallelLaneWidth:0.00}m / 道路幅 {aisleWidth:0.00}m";
            string line = lineType == LineType.U ? $"U字(間隔{uGap:0.00}m)" : "I字";
            return $"{TypeName(type)} / {line} / 区画幅 {spotWidth:0.00}m / 奥行 {spotDepth:0.00}m / 通路 {aisleWidth:0.00}m";
        }
    }

    /// <summary>駐車目標の区画。ローカル座標: u=目標の向きに対して右, w=目標の向き方向</summary>
    public class TargetSpot
    {
        public Vector3 center;
        public float yaw;          // 駐車完了時の車の向き
        public float width;        // 左右の線の中心間
        public float length;       // 奥行
        public float lineWidth;
        public int aisleEnd;       // +1: w=+length/2 が通路側, -1: w=-length/2 が通路側, 0: 両端をチェック(縦列)
        public bool parallel;
        public float curbU = float.NaN; // 縦列: 縁石面の u 座標

        public Vector3 Forward => CarSpec.Forward(yaw);
        public Vector3 Right => CarSpec.Right(yaw);

        public Vector2 ToLocal(Vector3 p)
        {
            Vector3 d = p - center;
            return new Vector2(Vector3.Dot(d, Right), Vector3.Dot(d, Forward));
        }

        public Vector3 ToWorld(float u, float w) => center + Right * u + Forward * w;
    }
}
