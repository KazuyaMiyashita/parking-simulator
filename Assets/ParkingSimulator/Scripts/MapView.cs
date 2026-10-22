using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 町全体のマップ。真上からの正投影カメラで一度だけ画像にし、その上に自車と目的地の候補を描く。
    /// 目的地の候補は空いている駐車区画(他人の契約区画は除く)。
    /// </summary>
    public class MapView
    {
        /// <summary>マップに映す範囲(x-z 平面)。北(+z)が上</summary>
        public static readonly Rect Area = Rect.MinMaxRect(-86f, -86f, 161f, 86f);
        const float PixelsPerMeter = 8f;

        public RenderTexture texture;
        public readonly List<TargetSpot> destinations = new List<TargetSpot>();
        public int cursor;

        Camera cam;
        Vector2 heldDir;
        float repeatAt;

        public MapView(Transform parent, WorldResult world)
        {
            foreach (var s in world.spots) if (s.destination) destinations.Add(s);

            var go = new GameObject("Map Camera");
            go.transform.SetParent(parent, false);
            cam = go.AddComponent<Camera>();
            cam.enabled = false; // 開いたときに Render() で一度だけ描く
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.5f, 0.3f);
            cam.cullingMask = ~Layers.Mask(Layers.Guide);
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 200f;
            Vector2 c = Area.center;
            cam.transform.SetPositionAndRotation(new Vector3(c.x, 100f, c.y), Quaternion.Euler(90f, 0f, 0f));
        }

        public void Render()
        {
            if (!texture)
            {
                texture = new RenderTexture(Mathf.RoundToInt(Area.width * PixelsPerMeter), Mathf.RoundToInt(Area.height * PixelsPerMeter), 24)
                {
                    antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave,
                };
            }
            cam.targetTexture = texture;
            cam.aspect = Area.width / Area.height;
            cam.orthographicSize = Area.height / 2f;
            cam.Render();
        }

        /// <summary>ワールド座標 → 画面上のマップ領域 r の中の位置</summary>
        public static Vector2 ToScreen(Vector3 p, Rect r) =>
            new Vector2(r.x + (p.x - Area.xMin) / Area.width * r.width, r.y + (1f - (p.z - Area.yMin) / Area.height) * r.height);

        public TargetSpot Selected => cursor >= 0 && cursor < destinations.Count ? destinations[cursor] : null;

        public void SelectNearest(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < destinations.Count; i++)
            {
                float d = (destinations[i].center - p).sqrMagnitude;
                if (d < best) { best = d; cursor = i; }
            }
        }

        /// <summary>画面上の位置 pos に一番近い候補(maxPx 以内)の番号。なければ -1</summary>
        public int Hit(Vector2 pos, Rect r, float maxPx)
        {
            int hit = -1;
            float best = maxPx * maxPx;
            for (int i = 0; i < destinations.Count; i++)
            {
                float d = (ToScreen(destinations[i].center, r) - pos).sqrMagnitude;
                if (d < best) { best = d; hit = i; }
            }
            return hit;
        }

        /// <summary>
        /// 方向入力(x=東, y=北)で、今の候補からその方向にある一番近い候補へ移る。
        /// 倒しっぱなしなら一定間隔でくり返す(時間が止まっていても動くよう実時間で数える)
        /// </summary>
        public void Steer(Vector2 dir)
        {
            if (dir.magnitude < 0.5f) { heldDir = Vector2.zero; return; }
            dir.Normalize();
            float now = Time.unscaledTime;
            bool fresh = heldDir == Vector2.zero || Vector2.Angle(heldDir, dir) > 40f;
            if (!fresh && now < repeatAt) return;
            repeatAt = now + (fresh ? 0.4f : 0.12f);
            heldDir = dir;

            var cur = Selected;
            if (cur == null) { cursor = 0; return; }
            Vector2 from = new Vector2(cur.center.x, cur.center.z);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < destinations.Count; i++)
            {
                if (i == cursor) continue;
                Vector2 d = new Vector2(destinations[i].center.x, destinations[i].center.z) - from;
                float dist = d.magnitude;
                if (dist < 0.01f) continue;
                float ang = Vector2.Angle(d, dir);
                if (ang > 60f) continue;
                // 方向のずれが大きいほど遠いものとして扱う
                float score = dist * (1f + ang / 25f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) cursor = best;
        }

        public void Release()
        {
            if (texture) { texture.Release(); Util.Kill(texture); }
        }
    }
}
