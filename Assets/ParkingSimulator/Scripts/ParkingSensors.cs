using UnityEngine;

namespace ParkingSim
{
    /// <summary>コーナー/バックセンサー(前後各4個)と警告音</summary>
    public class ParkingSensors : MonoBehaviour
    {
        public struct Def
        {
            public Vector3 local; public float yaw; public float range; public bool front;
        }

        public static readonly Def[] Defs =
        {
            new Def { local = new Vector3(-0.80f, 0.5f, CarSpec.HalfLength - 0.12f), yaw = -50f, range = 0.8f, front = true },
            new Def { local = new Vector3(-0.35f, 0.5f, CarSpec.HalfLength), yaw = 0f, range = 1.5f, front = true },
            new Def { local = new Vector3(0.35f, 0.5f, CarSpec.HalfLength), yaw = 0f, range = 1.5f, front = true },
            new Def { local = new Vector3(0.80f, 0.5f, CarSpec.HalfLength - 0.12f), yaw = 50f, range = 0.8f, front = true },
            new Def { local = new Vector3(-0.80f, 0.5f, -CarSpec.HalfLength + 0.12f), yaw = 230f, range = 0.8f, front = false },
            new Def { local = new Vector3(-0.35f, 0.5f, -CarSpec.HalfLength), yaw = 180f, range = 1.5f, front = false },
            new Def { local = new Vector3(0.35f, 0.5f, -CarSpec.HalfLength), yaw = 180f, range = 1.5f, front = false },
            new Def { local = new Vector3(0.80f, 0.5f, -CarSpec.HalfLength + 0.12f), yaw = 130f, range = 0.8f, front = false },
        };

        public readonly float[] dist = new float[Defs.Length];
        public readonly bool[] active = new bool[Defs.Length];
        public bool sound = true;
        public float nearest = float.PositiveInfinity;

        CarController car;
        AudioSource src;
        float beepTimer;
        int mask;

        public void Init(CarController c)
        {
            car = c;
            mask = Layers.Mask(Layers.Obstacle);
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0.35f;
            src.clip = MakeBeep();
        }

        static AudioClip MakeBeep()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.07f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Clamp01(i / 200f) * Mathf.Clamp01((n - i) / 400f);
                data[i] = Mathf.Sin(2 * Mathf.PI * 2100f * t) * env;
            }
            var clip = AudioClip.Create("Beep", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void LateUpdate()
        {
            Measure();
            if (!sound || !src || float.IsInfinity(nearest)) { beepTimer = 0; return; }
            float interval = nearest < 0.3f ? 0.08f : nearest < 0.5f ? 0.15f : nearest < 0.8f ? 0.3f : 0.55f;
            beepTimer -= Time.deltaTime;
            if (beepTimer <= 0f)
            {
                src.Play();
                beepTimer = interval;
            }
        }

        public void Measure()
        {
            nearest = float.PositiveInfinity;
            if (!car) return;
            bool moving = Mathf.Abs(car.speed) < 2.8f;
            bool rearOn = car.gear == Gear.R;
            bool frontOn = (car.gear == Gear.D && moving) || rearOn;
            for (int i = 0; i < Defs.Length; i++)
            {
                var d = Defs[i];
                active[i] = d.front ? frontOn : rearOn;
                dist[i] = float.PositiveInfinity;
                if (!active[i]) continue;
                Vector3 dirLocal = Quaternion.Euler(0, d.yaw, 0) * Vector3.forward;
                Vector3 origin = transform.TransformPoint(d.local - dirLocal * 0.05f);
                for (int k = -2; k <= 2; k++)
                {
                    Vector3 dir = transform.TransformDirection(Quaternion.Euler(0, k * 15f, 0) * dirLocal);
                    if (Physics.Raycast(origin, dir, out var hit, d.range + 0.05f, mask, QueryTriggerInteraction.Ignore))
                        dist[i] = Mathf.Min(dist[i], Mathf.Max(0f, hit.distance - 0.05f));
                }
                nearest = Mathf.Min(nearest, dist[i]);
            }
        }
    }
}
