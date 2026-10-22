using UnityEngine;

namespace ParkingSim
{
    /// <summary>目的地の区画の目印: 地面のオレンジの枠と、上に浮かんで回るひし形(どの視点からも見える)</summary>
    public class DestinationMarker : MonoBehaviour
    {
        Transform outline, gem;
        CarController car;

        public void Init(CarController c)
        {
            car = c;
            var mat = Util.Mat(new Color(1f, 0.45f, 0.05f), 0.6f);
            outline = Util.Child("Outline", transform, Vector3.zero).transform;
            gem = Util.Box("Gem", transform, Vector3.zero, new Vector3(0.55f, 0.55f, 0.55f), mat, null, false, false).transform;
            gameObject.SetActive(false);
        }

        public void Show(TargetSpot s)
        {
            if (s == null) { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            foreach (Transform t in outline) Util.Kill(t.gameObject);
            var mat = Util.Mat(new Color(1f, 0.45f, 0.05f), 0.6f);
            float hu = s.width / 2 - s.lineWidth / 2 - 0.12f, hw = s.length / 2 - 0.12f;
            Vector3[] q = { s.ToWorld(-hu, -hw), s.ToWorld(hu, -hw), s.ToWorld(hu, hw), s.ToWorld(-hu, hw) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = q[i], b = q[(i + 1) % 4];
                a.y = b.y = Kit.LineY + 0.01f;
                Util.Segment("Edge", outline, a, b, 0.12f, 0.004f, mat, false);
            }
            gem.position = s.center + Vector3.up * 3.2f;
        }

        void Update()
        {
            if (!gem) return;
            // 車が真下近くまで来たらひし形は消す(運転席から区画が見えにくくならないように)
            Vector3 c = gem.position; c.y = 0f;
            bool near = car && (car.Center - c).sqrMagnitude < 4.5f * 4.5f;
            gem.gameObject.SetActive(!near);
            float t = Time.time;
            gem.rotation = Quaternion.Euler(45f, t * 90f, 45f);
            gem.position = new Vector3(gem.position.x, 3.2f + Mathf.Sin(t * 2.5f) * 0.25f, gem.position.z);
        }
    }
}
