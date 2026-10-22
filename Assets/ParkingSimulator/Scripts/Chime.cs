using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 知らせる音の波形を作る。純音のピー音ではなく、倍音を少し混ぜて立ち上がりと減衰をなめらかにした「ポン」系の音にする
    /// </summary>
    public static class Chime
    {
        const int Rate = 44100;

        /// <summary>
        /// 1回鳴らす音。基音に、早く減衰する倍音と少しずれた上音を重ねる(ベルのような響き)。
        /// length: 長さ(秒), tau: 基音の減衰の時定数(秒)
        /// </summary>
        public static AudioClip Clip(string name, float freq, float length, float tau)
        {
            int n = Mathf.RoundToInt(Rate * length);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float attack = Mathf.Clamp01(t / 0.004f);
                float release = Mathf.Clamp01((length - t) / 0.02f);
                float w = 2f * Mathf.PI * freq * t;
                float v = Mathf.Sin(w) * Mathf.Exp(-t / tau)
                        + 0.28f * Mathf.Sin(2f * w) * Mathf.Exp(-t / (tau * 0.5f))
                        + 0.1f * Mathf.Sin(2.76f * w) * Mathf.Exp(-t / (tau * 0.35f));
                data[i] = v * attack * release * 0.75f;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// くり返し鳴らす連続音(1秒)。周波数は整数 Hz に丸めて、つなぎ目で波形が切れないようにする。
        /// freq2 を指定すると近い高さの音を重ねて、ゆっくりうなる音にする
        /// </summary>
        public static AudioClip Loop(string name, float freq, float freq2 = 0f)
        {
            int n = Rate;
            float f1 = Mathf.Round(freq), f2 = Mathf.Round(freq2);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float v = Tone(f1, t);
                if (f2 > 0f) v = (v + Tone(f2, t)) * 0.5f;
                data[i] = v * 0.7f;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Tone(float f, float t)
        {
            float w = 2f * Mathf.PI * f * t;
            return (Mathf.Sin(w) + 0.2f * Mathf.Sin(2f * w) + 0.06f * Mathf.Sin(3f * w)) / 1.26f;
        }
    }
}
