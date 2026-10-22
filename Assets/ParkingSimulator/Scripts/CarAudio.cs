using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 自車の音をその場で合成する。目立たせず、運転の邪魔にならない程度にとどめる。
    /// - エンジン: 水平対向4気筒 FB16 + CVT のごく小さなアイドル音。アクセルを踏んでも回転はあまり上げない
    /// - 走行: CVT のかすかなうなりとロードノイズ
    /// - ブレーキ: 急ブレーキのときだけ「キーッ」と鳴く。普段のブレーキは無音
    /// 回転数などはメインスレッドで決め、波形は OnAudioFilterRead(オーディオスレッド)で作る。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public float volume = 0.55f;

        const float IdleRpm = 700f;
        const float EngineLevel = 0.08f;   // エンジン音の大きさ(ごく小さく)
        const float HardBrake = 0.75f;     // これ以上ブレーキを踏み込むと急ブレーキ
        const float SquealMinSpeed = 1.5f; // 急ブレーキで鳴く最低速度(m/s, 約 5km/h)

        CarController car;
        AudioSource src;
        int rate;
        bool isPaused;

        // メインスレッドで決める値(オーディオスレッドが読む)
        volatile float rpm, load, speed, squeal, master;

        // オーディオスレッドだけで使う状態
        double cyclePhase, cvtPhase, squealPhase, wobblePhase;
        int lastSlot = -1;
        float pulse;                                              // 燃焼パルスの包絡
        readonly float[] cylWeight = { 1f, 0.9f, 0.96f, 0.86f };  // 気筒ごとのわずかなばらつき(水平対向の鼓動感)
        readonly System.Random rnd = new System.Random(1);
        float sRpm, sLoad, sSpeed, sSqueal, sMaster;
        readonly Svf body = new Svf(), boom = new Svf(), road = new Svf();
        float dcIn, dcOut;

        /// <summary>メニューなどで時間が止まっている間は音も止める</summary>
        public bool paused
        {
            get => isPaused;
            set
            {
                if (value == isPaused || !src) return;
                isPaused = value;
                if (value) src.Pause(); else src.UnPause();
            }
        }

        public void Init(CarController c)
        {
            car = c;
            rate = AudioSettings.outputSampleRate;
            rpm = IdleRpm;

            src = GetComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = true;
            // 無音のクリップを流し、その上に OnAudioFilterRead で波形を書き込む
            src.clip = AudioClip.Create("CarAudio", rate, 1, rate, false);
            src.Play();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!car || dt <= 0f) return;
            master = volume;
            float v = Mathf.Abs(car.speed);
            speed = v;

            // CVT: 回転は速度よりアクセル開度で決まる。駐車の速度域なので吹け上がりは控えめにする
            bool inGear = car.gear == Gear.D || car.gear == Gear.R;
            float thr = car.throttle;
            float target = IdleRpm + thr * (inGear ? 700f : 400f) + (inGear ? v * 25f : 0f);
            if (inGear && v < 0.05f && thr < 0.01f) target -= 40f; // D/R で止まっているとアイドルが少し下がる
            rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? 900f : 600f) * dt);
            load = Mathf.MoveTowards(load, inGear ? thr : 0f, 3f * dt);

            // 急ブレーキのときだけ鳴く(止まると消える)
            bool hard = car.brake >= HardBrake && v > SquealMinSpeed;
            squeal = hard ? Mathf.Clamp01((car.brake - HardBrake) / (1f - HardBrake) * 0.6f + 0.4f) * Mathf.Clamp01(v / 4f + 0.3f) : 0f;
        }

        float Noise() => (float)(rnd.NextDouble() * 2.0 - 1.0);

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (rate <= 0) return;
            float sr = rate;
            float tc = 1f - Mathf.Exp(-1f / (0.05f * sr));   // パラメーターの平滑化(約50ms)
            float tcSq = 1f - Mathf.Exp(-1f / (0.02f * sr)); // ブレーキ鳴きは立ち上がり・消えを短く
            float pulseDecay = Mathf.Exp(-1f / (0.004f * sr));
            // 共鳴の周波数は固定(排気系と車室のこもった響き)。回転で変わるのは燃焼パルスの間隔だけ
            body.Set(90f, 1.4f, sr);
            boom.Set(45f, 1.8f, sr);
            road.Set(240f, 0.7f, sr);

            for (int i = 0; i < data.Length; i += channels)
            {
                sRpm += (rpm - sRpm) * tc;
                sLoad += (load - sLoad) * tc;
                sSpeed += (speed - sSpeed) * tc;
                sSqueal += (squeal - sSqueal) * tcSq;
                sMaster += (master - sMaster) * tc;

                float s = 0f;

                // ---- エンジン: 2回転で4回の燃焼パルスを、低い共鳴に通したこもった音だけにする
                cyclePhase += sRpm / 120.0 / sr;
                if (cyclePhase >= 1.0) cyclePhase -= 1.0;
                int slot = (int)(cyclePhase * 4.0);
                if (slot != lastSlot)
                {
                    lastSlot = slot;
                    pulse = cylWeight[slot] * (0.8f + 0.2f * sLoad) * (1f + Noise() * 0.06f);
                }
                float e = pulse * (0.85f + 0.15f * Noise());
                pulse *= pulseDecay;
                s += (body.Band(e) + boom.Band(e) * 0.7f) * EngineLevel;

                // ---- CVT のかすかなうなりとロードノイズ(速度に比例)
                if (sSpeed > 0.02f)
                {
                    cvtPhase += (900.0 + sSpeed * 110.0) / sr;
                    if (cvtPhase >= 1.0) cvtPhase -= 1.0;
                    s += Mathf.Sin((float)(cvtPhase * 2.0 * Mathf.PI)) * 0.003f * Mathf.Clamp01(sSpeed / 3f);
                    s += road.Low(Noise()) * 0.1f * Mathf.Clamp01(sSpeed / 8f);
                }

                // 直流分を除く(エンジンとロードノイズだけ。ブレーキ鳴きは高音なので後で足す)
                float y = s - dcIn + 0.995f * dcOut;
                dcIn = s; dcOut = y;

                // ---- 急ブレーキの鳴き: 高い音がわずかに揺れながら「キーッ」と続く
                if (sSqueal > 0.001f)
                {
                    wobblePhase += 7.0 / sr;
                    if (wobblePhase >= 1.0) wobblePhase -= 1.0;
                    float wob = Mathf.Sin((float)(wobblePhase * 2.0 * Mathf.PI));
                    squealPhase += 3150.0 * (1.0 + 0.012 * wob) / sr;
                    if (squealPhase >= 1.0) squealPhase -= 1.0;
                    float ph = (float)(squealPhase * 2.0 * Mathf.PI);
                    float tone = Mathf.Sin(ph) + 0.35f * Mathf.Sin(2f * ph) + 0.12f * Mathf.Sin(3.03f * ph);
                    y += tone * (0.75f + 0.25f * wob) * sSqueal * 0.07f;
                }

                y *= sMaster * 2.2f;
                y = y / (1f + Mathf.Abs(y));
                for (int ch = 0; ch < channels; ch++) data[i + ch] = y;
            }
        }

        /// <summary>状態変数フィルター(Chamberlin)。Band = 帯域通過, Low = 低域通過</summary>
        class Svf
        {
            float f, q, low, band;

            public void Set(float freq, float res, float sr)
            {
                f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(freq, sr / 6f) / sr);
                q = 1f / res;
            }

            void Tick(float x)
            {
                low += f * band;
                float high = x - low - q * band;
                band += f * high;
                if (float.IsNaN(band) || Mathf.Abs(band) > 1e4f) low = band = 0f;
            }

            public float Band(float x) { Tick(x); return band; }
            public float Low(float x) { Tick(x); return low; }
        }
    }
}
