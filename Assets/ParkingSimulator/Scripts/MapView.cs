using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 町全体のマップ(カーナビ相当)。真上からの正投影カメラで一度だけ画像にし、その上に自車・目的地・経路を描く。
    /// </summary>
    public class MapView
    {
        /// <summary>マップに映す範囲(x-z 平面)。北(+z)が上</summary>
        public static readonly Rect Area = Rect.MinMaxRect(-86f, -86f, 161f, 86f);
        const float PixelsPerMeter = 8f;

        public RenderTexture texture;
        public int cursor;         // 選択中の行(places の番号。places.Count は「目的地を設定しない」)

        Camera cam;

        public MapView(Transform parent)
        {
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

        public void Release()
        {
            if (texture) { texture.Release(); Util.Kill(texture); }
        }
    }
}
