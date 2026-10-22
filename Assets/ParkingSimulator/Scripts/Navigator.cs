using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// カーナビ相当の経路探索。WorldResult.navLines(道の中心線)を約4m ごとの点に分けてつなぎ、
    /// 各線の端点は近くの別の線へつなぐ。現在地から目的地の入口までの最短経路を返す。
    /// </summary>
    public class Navigator
    {
        const float Step = 4f, Snap = 5f;

        readonly List<Vector3> nodes = new List<Vector3>();
        readonly List<List<(int to, float cost)>> edges = new List<List<(int, float)>>();

        public Navigator(WorldResult w)
        {
            var lines = new List<List<int>>();
            foreach (var l in w.navLines)
            {
                var ids = new List<int>();
                for (int i = 0; i < l.Count; i++)
                {
                    if (i == 0) { ids.Add(AddNode(l[0])); continue; }
                    Vector3 a = l[i - 1], b = l[i];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / Step));
                    for (int k = 1; k <= n; k++)
                    {
                        int id = AddNode(Vector3.Lerp(a, b, (float)k / n));
                        Link(ids[ids.Count - 1], id);
                        ids.Add(id);
                    }
                }
                lines.Add(ids);
            }

            // 端点を、他の線の一番近い区間上の点へつなぐ
            for (int li = 0; li < lines.Count; li++)
            {
                foreach (int end in new[] { lines[li][0], lines[li][lines[li].Count - 1] })
                {
                    Vector3 p = nodes[end];
                    int bestA = -1, bestB = -1;
                    float best = Snap;
                    Vector3 bestQ = default;
                    for (int lj = 0; lj < lines.Count; lj++)
                    {
                        if (lj == li) continue;
                        var ids = lines[lj];
                        for (int k = 0; k < ids.Count - 1; k++)
                        {
                            Vector3 q = Closest(nodes[ids[k]], nodes[ids[k + 1]], p);
                            float d = Vector3.Distance(p, q);
                            if (d < best) { best = d; bestA = ids[k]; bestB = ids[k + 1]; bestQ = q; }
                        }
                    }
                    if (bestA < 0) continue;
                    int mid = AddNode(bestQ);
                    Link(bestA, mid); Link(mid, bestB); Link(end, mid);
                }
            }
        }

        int AddNode(Vector3 p)
        {
            nodes.Add(new Vector3(p.x, 0f, p.z));
            edges.Add(new List<(int, float)>());
            return nodes.Count - 1;
        }

        void Link(int a, int b)
        {
            float c = Vector3.Distance(nodes[a], nodes[b]);
            edges[a].Add((b, c));
            edges[b].Add((a, c));
        }

        static Vector3 Closest(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 d = b - a;
            float t = d.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
            return a + d * t;
        }

        int Nearest(Vector3 p)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = (nodes[i] - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>from から to までの経路(from と to を含む点列)を route に入れ、道のり(m)を返す</summary>
        public float Route(Vector3 from, Vector3 to, List<Vector3> route)
        {
            route.Clear();
            from.y = to.y = 0f;
            int s = Nearest(from), g = Nearest(to);
            if (s < 0 || g < 0) return 0f;

            var dist = new float[nodes.Count];
            var prev = new int[nodes.Count];
            var done = new bool[nodes.Count];
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[s] = 0f;
            // 点の数が少ない(数百)ので単純な O(n^2) のダイクストラで十分
            while (true)
            {
                int u = -1;
                float ud = float.MaxValue;
                for (int i = 0; i < dist.Length; i++)
                    if (!done[i] && dist[i] < ud) { ud = dist[i]; u = i; }
                if (u < 0 || u == g) break;
                done[u] = true;
                foreach (var (v, c) in edges[u])
                    if (ud + c < dist[v]) { dist[v] = ud + c; prev[v] = u; }
            }
            if (dist[g] == float.MaxValue) return 0f;

            for (int v = g; v >= 0; v = prev[v]) route.Add(nodes[v]);
            route.Add(from);
            route.Reverse();
            route.Add(to);
            // 一番近い点が後ろにあると、いったん戻ってから進む経路になる。次の点へ直接向かう方が近ければ飛ばす
            while (route.Count >= 3 && Vector3.Distance(route[0], route[2]) <= Vector3.Distance(route[1], route[2]))
                route.RemoveAt(1);
            float len = 0f;
            for (int i = 1; i < route.Count; i++) len += Vector3.Distance(route[i - 1], route[i]);
            return len;
        }
    }
}
