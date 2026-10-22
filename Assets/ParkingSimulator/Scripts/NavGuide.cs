using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// カーナビの曲がり角案内。経路(現在地から目的地まで)を単純な折れ線にして、向きが大きく変わる所を右左折とする。
    /// 次の曲がり角までの道のりで画面の表示を決め、決まった距離で音声案内を出す
    /// </summary>
    public class NavGuide
    {
        public struct Maneuver
        {
            public Vector3 pos;
            public int turn;      // +1 右, -1 左
            public float along;   // 現在地からの道のり(m)
        }

        public readonly List<Maneuver> maneuvers = new List<Maneuver>();
        /// <summary>画面に出す次の案内(null なら目的地まで曲がり角なし)</summary>
        public Maneuver? Next => maneuvers.Count > 0 ? maneuvers[0] : (Maneuver?)null;

        readonly HashSet<string> spoken = new HashSet<string>();
        readonly List<Vector3> simple = new List<Vector3>();
        string lastNextKey;

        const float TurnAngle = 35f;   // これ以上向きが変わる所を曲がり角とする
        const float FarCall = 120f, NearCall = 25f;

        public static string TurnWord(int turn) => turn > 0 ? "右方向" : "左方向";

        /// <summary>案内を最初からやり直す(目的地を決めたとき)</summary>
        public void Reset()
        {
            spoken.Clear();
            maneuvers.Clear();
            lastNextKey = null;
        }

        /// <summary>経路から曲がり角を求め、必要なら音声案内の文を返す(話すことがなければ null)</summary>
        public string Update(List<Vector3> route, float routeLength, bool nearDestination, bool justStarted)
        {
            FindManeuvers(route);
            if (nearDestination)
            {
                // 入口の手前に曲がり角があれば一緒に言う(駐車場へ入る右左折など)。その曲がり角の「まもなく」は言わない
                if (Next is Maneuver turn) spoken.Add(Key(turn.pos) + ":near");
                string near = Once("near", Next is Maneuver last ? $"まもなく{TurnWord(last.turn)}、その先が目的地です。" : "まもなく目的地です。");
                return justStarted ? "ルート案内を開始します。" + (near ?? "") : near;
            }

            var next = Next;
            string say = null;
            if (next is Maneuver m)
            {
                string key = Key(m.pos);
                string word = TurnWord(m.turn);
                bool changed = key != lastNextKey;
                lastNextKey = key;
                if (m.along <= NearCall) say = Once(key + ":near", $"まもなく{word}です。");
                else if (m.along <= FarCall) say = Once(key + ":far", $"およそ{Round10(m.along)}メートル先、{word}です。");
                else if ((changed || justStarted) && m.along > FarCall + 30f) say = Once(key + ":straight", $"この先、およそ{Round10(m.along)}メートル直進です。");
            }
            else
            {
                bool changed = lastNextKey != "goal";
                lastNextKey = "goal";
                if (changed || justStarted) say = Once("goal:straight", $"目的地まで、およそ{Round10(routeLength)}メートル、直進です。");
            }
            if (justStarted) say = "ルート案内を開始します。" + (say ?? "");
            return say;
        }

        string Once(string key, string text) => spoken.Add(key) ? text : null;

        static string Key(Vector3 p) => $"{Mathf.RoundToInt(p.x / 6f)},{Mathf.RoundToInt(p.z / 6f)}";

        static int Round10(float d) => Mathf.Max(10, Mathf.RoundToInt(d / 10f) * 10);

        void FindManeuvers(List<Vector3> route)
        {
            maneuvers.Clear();
            if (route.Count < 3) return;
            simple.Clear();
            Simplify(route, 0, route.Count - 1, 2f, simple);
            simple.Add(route[route.Count - 1]);

            float along = 0f;
            for (int i = 1; i < simple.Count - 1; i++)
            {
                along += Vector3.Distance(simple[i - 1], simple[i]);
                Vector3 inDir = simple[i] - simple[i - 1], outDir = simple[i + 1] - simple[i];
                if (inDir.sqrMagnitude < 0.01f || outDir.sqrMagnitude < 0.01f) continue;
                float ang = Vector3.SignedAngle(inDir, outDir, Vector3.up); // 正 = 右に曲がる
                if (Mathf.Abs(ang) < TurnAngle || along < 3f) continue;
                int turn = ang > 0 ? 1 : -1;
                // 続けて同じ向きに曲がる短い折れ(隅切りなど)は1回にまとめる
                if (maneuvers.Count > 0 && along - maneuvers[maneuvers.Count - 1].along < 6f && maneuvers[maneuvers.Count - 1].turn == turn) continue;
                maneuvers.Add(new Maneuver { pos = simple[i], turn = turn, along = along });
            }
        }

        /// <summary>Douglas–Peucker 法で点列を減らす(終点は呼び出し側で足す)</summary>
        static void Simplify(List<Vector3> pts, int a, int b, float tol, List<Vector3> outPts)
        {
            float best = -1f;
            int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = DistToSegment(pts[i], pts[a], pts[b]);
                if (d > best) { best = d; idx = i; }
            }
            if (best > tol)
            {
                Simplify(pts, a, idx, tol, outPts);
                Simplify(pts, idx, b, tol, outPts);
            }
            else outPts.Add(pts[a]);
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float t = d.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
            return Vector3.Distance(p, a + d * t);
        }
    }
}
