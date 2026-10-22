using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 自車の音(エンジン・セルモーター・ブレーキ鳴き・電動パーキングブレーキ)をその場で合成する。
    /// エンジンはスバルの水平対向4気筒(FB20)+ CVT を想定。
    /// 回転数などはメインスレッドで決め、波形は OnAudioFilterRead(オーディオスレッド)で作る。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public float volume = 0.5f;

        const float IdleRpm = 750f;
        const float CrankTime = 0.65f;   // セルを回している時間
        const float EpbTime = 0.8f;      // 電動パーキングブレーキのモーター音の長さ

        CarController car;
        int rate;

        // メインスレッドで決める値(オーディオスレッドが読む)
        float rpm;           // エンジン回転数
        float combustion;    // 燃焼の強さ(0=止まっている/セルで回しているだけ)
        float load;          // アクセル開度に応じた負荷
        float starter;       // セルモーターの音量
        float squeal;        // ブレーキ鳴きの音量
        float epb;           // パーキングブレーキのモーター音量
        float epbPitch;
        float creak;         // 停止した瞬間の「ギュッ」

        // メインスレッドだけで使う状態
        float crankTimer, epbTimer, creakTimer;
        float prevSpeed;

        // オーディオスレッドだけで使う状態
        double cyclePhase, starterPhase, squealPhase, epbPhase, rumblePhase, creakPhase;
        int lastSlot = -1;
        readonly float[] pulseWeight = { 1f, 0.8f, 0.92f, 0.7f };
        readonly System.Random rnd = new System.Random(1);
        float sRpm, sComb, sLoad, sStarter, sSqueal, sEpb, sEpbPitch, sCreak;
        float lp, lp2, dcIn, dcOut, noiseLp, creakLp;

        public void Init(CarController c)
        {
            car = c;
            rate = AudioSettings.outputSampleRate;
            car.engineChanged += OnEngine;
            car.parkingBrakeChanged += _ => epbTimer = EpbTime;

            var src = GetComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = true;
            // 無音のクリップを流し、その上に OnAudioFilterRead で波形を書き込む
            src.clip = AudioClip.Create("CarAudio", rate, 1, rate, false);
            src.Play();
        }

        void OnEngine(bool on)
        {
            if (on) crankTimer = CrankTime;
            else crankTimer = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!car || dt <= 0f) return;

            UpdateEngine(dt);
            UpdateBrake(dt);
        }

        void UpdateEngine(float dt)
        {
            if (!car.engineOn)
            {
                // 停止: 回転がすっと落ちる
                rpm = Mathf.MoveTowards(rpm, 0f, 1800f * dt);
                combustion = 0f;
                starter = 0f;
                load = 0f;
                return;
            }
            if (crankTimer > 0f)
            {
                // セルで回している(キュルキュル)
                crankTimer -= dt;
                rpm = 220f;
                combustion = 0f;
                starter = 1f;
                if (crankTimer <= 0f)
                {
                    // 初爆で吹け上がってからアイドルに落ち着く
                    rpm = 1500f;
                    starter = 0f;
                }
                return;
            }
            starter = 0f;
            combustion = 1f;

            // CVT: 速度ではなくアクセル開度で回転数が決まり、加速中もほぼ一定に保たれる
            // (P/N では負荷がないので空ぶかしで高く回る)
            bool inGear = car.gear == Gear.D || car.gear == Gear.R;
            float thr = car.throttle;
            float target = IdleRpm + thr * thr * (inGear ? 2400f : 3600f) + (inGear ? Mathf.Abs(car.speed) * 45f : 0f);
            // D/R で止まっているときはトルコンの負荷でアイドルが少し下がる
            if (inGear && Mathf.Abs(car.speed) < 0.05f && thr < 0.01f) target -= 50f;
            float rateUp = 3000f, rateDown = rpm > IdleRpm + 300f ? 1400f : 600f;
            rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? rateUp : rateDown) * dt);
            load = Mathf.MoveTowards(load, thr, 4f * dt);
        }

        void UpdateBrake(float dt)
        {
            float v = Mathf.Abs(car.speed);
            // 止まる直前の「キーッ」というブレーキ鳴き(低速でじわっと踏んでいるとき)
            float target = 0f;
            if (car.brake > 0.25f && v > 0.08f && v < 2.2f)
                target = Mathf.Clamp01((car.brake - 0.25f) * 2f) * Mathf.Clamp01((2.2f - v) / 1.2f);
            squeal = Mathf.MoveTowards(squeal, target, 3f * dt);

            // 停止した瞬間の「ギュッ」
            if (prevSpeed > 0.3f && v < 0.02f && car.brake > 0.2f) creakTimer = 0.18f;
            prevSpeed = v;
            creakTimer = Mathf.Max(0f, creakTimer - dt);
            creak = creakTimer > 0f ? creakTimer / 0.18f : 0f;

            // 電動パーキングブレーキのモーター音(ウィーン)
            epbTimer = Mathf.Max(0f, epbTimer - dt);
            float k = 1f - epbTimer / EpbTime;
            epb = epbTimer > 0f ? Mathf.Clamp01(k * 12f) * Mathf.Clamp01(epbTimer * 10f) : 0f;
            epbPitch = 1f + 0.35f * k * (car.parkingBrake ? 1f : -0.6f);
        }

        float Noise() => (float)(rnd.NextDouble() * 2.0 - 1.0);

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (rate <= 0) return;
            float sr = rate;
            float tc = 1f - Mathf.Exp(-1f / (0.03f * sr)); // パラメーターの平滑化(約30ms)
            float tRpm, tComb, tLoad, tStarter, tSqueal, tEpb, tEpbPitch, tCreak;
            tRpm = rpm; tComb = combustion; tLoad = load; tStarter = starter;
            tSqueal = squeal; tEpb = epb; tEpbPitch = epbPitch; tCreak = creak;
            float gain = volume;

            for (int i = 0; i < data.Length; i += channels)
            {
                sRpm += (tRpm - sRpm) * tc;
                sComb += (tComb - sComb) * tc;
                sLoad += (tLoad - sLoad) * tc;
                sStarter += (tStarter - sStarter) * tc;
                sSqueal += (tSqueal - sSqueal) * tc;
                sEpb += (tEpb - sEpb) * tc;
                sEpbPitch += (tEpbPitch - sEpbPitch) * tc;
                sCreak += (tCreak - sCreak) * tc * 4f;

                float s = 0f;

                // ---- エンジン: 4気筒4ストロークは1回転に2回爆発。720°を4つの爆発に分け、
                // 爆発ごとに強さを少しずつ変えて水平対向らしい「ドロドロ」としたムラを出す
                if (sRpm > 1f)
                {
                    cyclePhase += sRpm / 120.0 / sr;
                    if (cyclePhase >= 1.0) cyclePhase -= 1.0;
                    float x4 = (float)cyclePhase * 4f;
                    int slot = Mathf.Min(3, (int)x4);
                    float p = x4 - slot;
                    if (slot != lastSlot)
                    {
                        lastSlot = slot;
                        // 爆発ごとのばらつき(アイドルほど大きい)
                        float jitter = Mathf.Lerp(0.12f, 0.04f, Mathf.Clamp01((sRpm - IdleRpm) / 1500f));
                        pulseWeight[slot] = (slot == 0 ? 1f : slot == 1 ? 0.78f : slot == 2 ? 0.92f : 0.7f) * (1f + Noise() * jitter);
                    }
                    float decay = 5f + sLoad * 3f;
                    float pulse = pulseWeight[slot] * Mathf.Exp(-p * decay);
                    // 排気の圧力波: 減衰するパルスと、サイクル周期(爆発の 1/4)の低いうなり
                    rumblePhase += sRpm / 120.0 / sr;
                    if (rumblePhase >= 1.0) rumblePhase -= 1.0;
                    float rumble = Mathf.Sin((float)(rumblePhase * 2.0 * Mathf.PI));
                    float exhaust = pulse + rumble * 0.25f;

                    // 吸気・メカノイズ(踏むほど大きい)
                    noiseLp += (Noise() - noiseLp) * 0.25f;
                    exhaust += noiseLp * (0.08f + sLoad * 0.35f) * pulse;

                    // 回転と負荷で明るさが変わるローパス(2段)
                    float fc = 180f + sRpm * 0.22f + sLoad * 900f;
                    float a = 1f - Mathf.Exp(-2f * Mathf.PI * fc / sr);
                    lp += (exhaust - lp) * a;
                    lp2 += (lp - lp2) * a;

                    float amp = Mathf.Lerp(0.18f, 1f, sComb) * (0.55f + 0.45f * sLoad + 0.15f * Mathf.Clamp01((sRpm - IdleRpm) / 2000f));
                    amp *= Mathf.Clamp01(sRpm / 400f); // 止まりかけは小さく
                    s += lp2 * amp * 1.1f;
                }

                // 直流分を取り除く
                dcOut = s - dcIn + 0.995f * dcOut;
                dcIn = s;
                s = dcOut;

                // ---- セルモーター(キュルキュル): 圧縮のたびに負荷がかかって音程と音量が揺れる
                if (sStarter > 0.001f)
                {
                    float comp = 0.5f + 0.5f * Mathf.Sin((float)(cyclePhase * 4.0 * 2.0 * Mathf.PI));
                    starterPhase += (190.0 - comp * 35.0) / sr;
                    if (starterPhase >= 1.0) starterPhase -= 1.0;
                    float ph = (float)starterPhase;
                    float whine = Mathf.Sin(ph * 2f * Mathf.PI) * 0.6f + Mathf.Sin(ph * 6f * Mathf.PI) * 0.25f + (ph * 2f - 1f) * 0.2f;
                    s += whine * sStarter * (0.35f + 0.25f * comp);
                }

                // ---- ブレーキ鳴き(キーッ): 高い音に小さな揺らぎ
                if (sSqueal > 0.001f)
                {
                    squealPhase += (3150.0 + Mathf.Sin((float)(squealPhase * 0.0021)) * 40.0) / sr;
                    if (squealPhase >= 1e6) squealPhase -= 1e6;
                    float sq = Mathf.Sin((float)((squealPhase % 1.0) * 2.0 * Mathf.PI));
                    s += sq * sSqueal * 0.07f;
                }

                // ---- 停止の瞬間の「ギュッ」: 低いざらついた音
                if (sCreak > 0.001f)
                {
                    // パッドが張り付いては滑る(スティックスリップ)周期で断続させたこもった雑音
                    creakPhase += 38.0 / sr;
                    if (creakPhase >= 1.0) creakPhase -= 1.0;
                    creakLp += (Noise() - creakLp) * 0.08f;
                    float grip = Mathf.Max(0f, Mathf.Sin((float)(creakPhase * 2.0 * Mathf.PI)));
                    s += creakLp * grip * sCreak * 0.5f;
                }

                // ---- 電動パーキングブレーキのモーター(ウィーン)
                if (sEpb > 0.001f)
                {
                    epbPhase += 330.0 * sEpbPitch / sr;
                    if (epbPhase >= 1.0) epbPhase -= 1.0;
                    float ph = (float)epbPhase;
                    float motor = Mathf.Sin(ph * 2f * Mathf.PI) * 0.5f + Mathf.Sin(ph * 4f * Mathf.PI) * 0.3f + Noise() * 0.1f;
                    s += motor * sEpb * 0.12f;
                }

                // ソフトクリップ
                s *= gain;
                s = s / (1f + Mathf.Abs(s));
                for (int ch = 0; ch < channels; ch++) data[i + ch] = s;
            }
        }
    }
}
