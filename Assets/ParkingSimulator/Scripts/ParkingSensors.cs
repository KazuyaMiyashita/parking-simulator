using UnityEngine;

namespace ParkingSim
{
    /// <summary>コーナー/バックセンサー(前後各4個)と、接近・接触・後退の知らせる音</summary>
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
        bool nearestFront;

        CarController car;
        AudioSource beeps, tone, alarm, reverse;
        AudioClip beepFront, beepRear, reverseChime;
        float beepTimer, reverseTimer;
        float alarmTime;              // 今回の接触で警告音を鳴らしている時間
        const float AlarmMax = 3f;    // 接触中でも3秒で止める
        const float ToneDist = 0.3f;  // これより近いと連続音
        int mask;

        public void Init(CarController c)
        {
            car = c;
            mask = Layers.Mask(Layers.Obstacle);
            beeps = NewSource(0.32f, false);
            tone = NewSource(0f, true);
            alarm = NewSource(0f, true);
            reverse = NewSource(0.28f, false);
            // 前のセンサーは高め、後ろは低めの音(どちらが近いか耳で分かるように)
            beepFront = Chime.Clip("Beep Front", 2093f, 0.13f, 0.045f);
            beepRear = Chime.Clip("Beep Rear", 1568f, 0.13f, 0.045f);
            tone.clip = Chime.Loop("Proximity Tone", 1568f);
            // 接触: はっきりした連続音(ピーーー)。2つの近い高さを重ねて少しうなりを持たせ、耳障りな純音を避ける
            alarm.clip = Chime.Loop("Contact Alarm", 2349f, 2357f);
            // R のときの車内のお知らせ音(ポーン・ポーン)
            reverseChime = Chime.Clip("Reverse Chime", 1318.5f, 0.45f, 0.16f);
        }

        AudioSource NewSource(float vol, bool loop)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 0f;
            a.volume = vol;
            a.loop = loop;
            return a;
        }

        void LateUpdate()
        {
            Measure();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 接触の警告音(3秒で止める)。鳴っている間はセンサー音を止める
            bool touching = car && car.touchingBody;
            alarmTime = touching ? alarmTime + dt : 0f;
            Fade(alarm, touching && alarmTime < AlarmMax ? 0.3f : 0f, dt);

            bool sensing = sound && !touching && !float.IsInfinity(nearest);
            Fade(tone, sensing && nearest < ToneDist ? 0.26f : 0f, dt);
            if (sensing && nearest >= ToneDist)
            {
                float interval = nearest < 0.5f ? 0.16f : nearest < 0.8f ? 0.3f : 0.55f;
                beepTimer -= dt;
                if (beepTimer <= 0f)
                {
                    beeps.PlayOneShot(nearestFront ? beepFront : beepRear);
                    beepTimer = interval;
                }
            }
            else beepTimer = 0f;

            // R のお知らせ音: センサーが鳴っていないときだけ、約 1 秒ごと
            if (car && car.gear == Gear.R && !sensing && !touching)
            {
                reverseTimer -= dt;
                if (reverseTimer <= 0f)
                {
                    reverse.PlayOneShot(reverseChime);
                    reverseTimer = 1.1f;
                }
            }
            else reverseTimer = 0f;
        }

        /// <summary>連続音の音量を短く上げ下げする(ブツッという音を避ける)</summary>
        static void Fade(AudioSource a, float target, float dt)
        {
            a.volume = Mathf.MoveTowards(a.volume, target, dt * 8f);
            if (a.volume > 0f && !a.isPlaying) a.Play();
            else if (a.volume <= 0f && a.isPlaying) a.Stop();
        }

        public void Measure()
        {
            nearest = float.PositiveInfinity;
            if (!car) return;
            bool slow = Mathf.Abs(car.speed) < 2.8f; // 約 10km/h 未満で前のセンサーが働く
            bool rearOn = car.gear == Gear.R;
            bool frontOn = (car.gear == Gear.D && slow) || rearOn;
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
                if (dist[i] < nearest) { nearest = dist[i]; nearestFront = d.front; }
            }
        }
    }
}
