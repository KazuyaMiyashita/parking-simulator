using UnityEngine;

namespace ParkingSim
{
    /// <summary>目的地の空いている区画を、地面のオレンジの枠と薄い塗りで少し目立たせる</summary>
    public class DestinationMarker : MonoBehaviour
    {
        Material edge, fill;

        public void Init()
        {
            edge = Util.Mat(new Color(1f, 0.5f, 0.1f), 0.6f);
            // 半透明の塗り(Sprites/Default はビルドに常に含まれる、ライティングなしの半透明シェーダー)
            var sh = Shader.Find("Sprites/Default");
            fill = sh ? new Material(sh) { color = new Color(1f, 0.55f, 0.1f, 0.22f) } : Util.Mat(new Color(0.95f, 0.75f, 0.5f), 0f);
            gameObject.SetActive(false);
        }

        public void Show(Place place)
        {
            foreach (Transform t in transform) Util.Kill(t.gameObject);
            gameObject.SetActive(place != null);
            if (place == null) return;
            foreach (var s in place.spots)
            {
                if (!s.destination) continue;
                float hu = s.width / 2 - s.lineWidth / 2 - 0.1f, hw = s.length / 2 - 0.1f;
                Vector3[] q = { s.ToWorld(-hu, -hw), s.ToWorld(hu, -hw), s.ToWorld(hu, hw), s.ToWorld(-hu, hw) };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = q[i], b = q[(i + 1) % 4];
                    a.y = b.y = Kit.LineY + 0.01f;
                    Util.Segment("Edge", transform, a, b, 0.12f, 0.004f, edge, false);
                }
                Util.Quad("Fill", transform, new Vector3(s.center.x, Kit.LineY + 0.008f, s.center.z), new Vector2(hu * 2, hw * 2), fill,
                    Quaternion.Euler(90f, s.yaw, 0f));
            }
        }
    }
}
