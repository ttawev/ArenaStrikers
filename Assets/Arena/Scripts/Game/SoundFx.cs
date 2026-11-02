// Звуки, синтезированные кодом (файлы не нужны): удары, способности, свисток, интерфейс.
// Трибуны и музыка — файлы в Resources/Audio (см. MatchAudio); пока их нет, здесь есть временный гул трибун.
using System;
using UnityEngine;

namespace Arena.Game
{
    public static class SoundFx
    {
        const int Rate = 44100;
        static System.Random rng = new System.Random(7);
        static float Noise() => (float)(rng.NextDouble() * 2 - 1);
        static float A(float fc) => 1 - Mathf.Exp(-2 * Mathf.PI * fc / Rate); // коэффициент однополюсного фильтра
        const float TAU = Mathf.PI * 2;

        static AudioClip Clip(string name, float[] d, float gain = 0.9f)
        {
            float peak = 0; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0) for (int i = 0; i < d.Length; i++) d[i] = d[i] / peak * gain;
            for (int i = 0; i < Mathf.Min(64, d.Length); i++) d[d.Length - 1 - i] *= i / 64f; // без щелчка в конце
            var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c;
        }
        static float[] Buf(float len) => new float[(int)(Rate * len)];

        // ---------- кирпичики ----------
        /// <summary>Сине с падающей высотой: «тело» удара.</summary>
        static void Thump(float[] d, float f0, float f1, float drop, float decay, float amp, float start = 0)
        {
            float ph = 0; int s = (int)(start * Rate);
            for (int i = s; i < d.Length; i++)
            {
                float t = (i - s) / (float)Rate; float f = Mathf.Lerp(f1, f0, Mathf.Exp(-t * drop));
                ph += TAU * f / Rate; d[i] += Mathf.Sin(ph) * Mathf.Exp(-t * decay) * amp * Mathf.Clamp01(t / 0.0015f);
            }
        }
        /// <summary>Шум через полосовой фильтр [lo..hi] с огибающей exp(-decay·t).</summary>
        static void NoiseBurst(float[] d, float lo, float hi, float decay, float amp, float start = 0, float attack = 0.001f)
        {
            float l1 = 0, l2 = 0, aLo = A(lo), aHi = A(hi); int s = (int)(start * Rate);
            for (int i = s; i < d.Length; i++)
            {
                float t = (i - s) / (float)Rate; float x = Noise();
                l1 += aHi * (x - l1); l2 += aLo * (l1 - l2); // l1 — ниже hi, l1-l2 — выше lo
                d[i] += (l1 - l2) * Mathf.Exp(-t * decay) * amp * Mathf.Clamp01(t / attack);
            }
        }
        /// <summary>Свист воздуха: полоса шума, центр которой плывёт f0 → f1, огибающая — горб.</summary>
        static void Whoosh(float[] d, float len, float f0, float f1, float amp, float start = 0)
        {
            float l1 = 0, l2 = 0; int s = (int)(start * Rate), n = (int)(len * Rate);
            for (int i = 0; i < n && s + i < d.Length; i++)
            {
                float k = i / (float)n; float fc = Mathf.Lerp(f0, f1, k);
                float x = Noise(); l1 += A(fc * 1.6f) * (x - l1); l2 += A(fc * 0.6f) * (l1 - l2);
                float env = Mathf.Sin(Mathf.PI * k); env *= env;
                d[s + i] += (l1 - l2) * env * amp;
            }
        }
        /// <summary>Металл/стекло: набор негармоничных частот с разным затуханием.</summary>
        static void Ring(float[] d, float[] f, float[] amp, float[] decay, float start = 0)
        {
            int s = (int)(start * Rate);
            for (int k = 0; k < f.Length; k++)
                for (int i = s; i < d.Length; i++)
                {
                    float t = (i - s) / (float)Rate;
                    d[i] += Mathf.Sin(TAU * f[k] * t + k) * amp[k] * Mathf.Exp(-t * decay[k]) * Mathf.Clamp01(t / 0.001f);
                }
        }
        static void Crackle(float[] d, float from, float to, int count, float amp)
        {
            for (int c = 0; c < count; c++)
            {
                int s = (int)(Mathf.Lerp(from, to, (float)rng.NextDouble()) * Rate); float a = amp * (0.4f + (float)rng.NextDouble() * 0.6f);
                for (int i = 0; i < 220 && s + i < d.Length; i++) d[s + i] += Noise() * a * Mathf.Exp(-i / 40f);
            }
        }

        // ---------- звуки ----------
        public static AudioClip Receive()
        {
            var d = Buf(0.16f); Thump(d, 170, 85, 25, 32, 0.9f); NoiseBurst(d, 800, 6000, 260, 0.5f);
            return Clip("receive", d, 0.75f);
        }
        public static AudioClip KickSoft() { var d = Buf(0.22f); Thump(d, 180, 80, 30, 24, 1); NoiseBurst(d, 1200, 7000, 220, 0.55f); return Clip("kick_soft", d); }
        public static AudioClip KickHard()
        {
            var d = Buf(0.5f); Thump(d, 140, 48, 18, 11, 1); NoiseBurst(d, 1800, 9000, 70, 0.9f); NoiseBurst(d, 120, 800, 25, 0.5f);
            return Clip("kick_hard", d, 0.95f);
        }
        public static AudioClip Lob() { var d = Buf(0.35f); Thump(d, 230, 120, 25, 18, 1); NoiseBurst(d, 900, 5000, 150, 0.4f); Whoosh(d, 0.3f, 500, 1400, 0.25f, 0.03f); return Clip("lob", d, 0.8f); }
        public static AudioClip Spin() { var d = Buf(0.6f); Whoosh(d, 0.6f, 900, 2600, 0.8f); Thump(d, 900, 1500, 3, 6, 0.08f); return Clip("spin", d, 0.5f); }
        public static AudioClip FireIgnite() { var d = Buf(0.5f); Whoosh(d, 0.5f, 300, 1800, 1); Crackle(d, 0.05f, 0.45f, 26, 0.5f); return Clip("fire_ignite", d, 0.7f); }
        public static AudioClip FireBoom()
        {
            var d = Buf(1.0f); Thump(d, 110, 38, 10, 5, 1); NoiseBurst(d, 200, 2500, 7, 0.7f, 0, 0.01f); Crackle(d, 0.02f, 0.9f, 50, 0.45f); NoiseBurst(d, 2000, 9000, 60, 0.6f);
            return Clip("fire_boom", d, 1f);
        }
        public static AudioClip HookThrow() { var d = Buf(0.45f); Whoosh(d, 0.4f, 1600, 500, 1); Ring(d, new[] { 1900f, 3100f }, new[] { 0.12f, 0.08f }, new[] { 30f, 40f }); return Clip("hook_throw", d, 0.6f); }
        public static AudioClip HookClank()
        {
            var d = Buf(0.6f); Ring(d, new[] { 620f, 1580f, 2730f, 3900f }, new[] { 1f, 0.6f, 0.4f, 0.25f }, new[] { 9f, 14f, 20f, 28f }); NoiseBurst(d, 2000, 9000, 200, 0.6f);
            return Clip("hook_clank", d, 0.75f);
        }
        public static AudioClip Teleport(bool back)
        {
            var d = Buf(0.42f); float ph = 0, ph2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float k = i / (float)d.Length; float f = back ? Mathf.Lerp(1700, 280, k * k) : Mathf.Lerp(260, 1700, Mathf.Sqrt(k));
                ph += TAU * f / Rate; ph2 += TAU * f * 1.5f / Rate;
                float env = Mathf.Clamp01(k / 0.05f) * (1 - k) * (0.75f + 0.25f * Mathf.Sin(k * 90));
                d[i] = (Mathf.Sin(ph) + 0.35f * Mathf.Sin(ph2)) * env;
            }
            NoiseBurst(d, 3000, 10000, 12, 0.15f);
            return Clip(back ? "tp_back" : "tp", d, 0.55f);
        }
        public static AudioClip Vanish()
        {
            var d = Buf(0.6f); NoiseBurst(d, 150, 1400, 6, 1, 0, 0.03f); float ph = 0;
            for (int i = 0; i < d.Length; i++) { float k = i / (float)d.Length; ph += TAU * Mathf.Lerp(700, 140, k) / Rate; d[i] += Mathf.Sin(ph) * 0.25f * (1 - k) * Mathf.Clamp01(k / 0.05f); }
            return Clip("vanish", d, 0.6f);
        }
        public static AudioClip Reveal() { var d = Buf(0.2f); NoiseBurst(d, 300, 3000, 30, 1); Thump(d, 320, 180, 30, 25, 0.5f); return Clip("reveal", d, 0.5f); }
        public static AudioClip IceCrack()
        {
            var d = Buf(1.2f);
            for (int c = 0; c < 7; c++) NoiseBurst(d, 2500, 11000, 90, 0.8f * (1 - c * 0.08f), c * 0.05f + (float)rng.NextDouble() * 0.02f);
            Thump(d, 70, 40, 6, 5, 0.8f);
            Ring(d, new[] { 2637f, 3520f, 4186f, 5274f }, new[] { 0.25f, 0.2f, 0.16f, 0.1f }, new[] { 4f, 5f, 6f, 8f }, 0.06f);
            return Clip("ice", d, 0.85f);
        }
        public static AudioClip IceShatter()
        {
            var d = Buf(1.0f); for (int c = 0; c < 12; c++) NoiseBurst(d, 3000, 12000, 60, 0.7f, (float)rng.NextDouble() * 0.25f);
            Ring(d, new[] { 3100f, 4400f, 5900f }, new[] { 0.3f, 0.25f, 0.2f }, new[] { 7f, 9f, 11f }, 0.02f);
            return Clip("ice_shatter", d, 0.8f);
        }
        public static AudioClip Dash() { var d = Buf(0.32f); Whoosh(d, 0.3f, 600, 2200, 1); return Clip("dash", d, 0.55f); }
        public static AudioClip BodyHit() { var d = Buf(0.4f); Thump(d, 95, 42, 14, 11, 1); NoiseBurst(d, 100, 600, 30, 0.6f); NoiseBurst(d, 1500, 6000, 120, 0.3f); return Clip("hit", d, 0.9f); }
        public static AudioClip Post() { var d = Buf(0.7f); Ring(d, new[] { 880f, 2210f, 3490f, 5100f }, new[] { 1f, 0.5f, 0.35f, 0.2f }, new[] { 7f, 12f, 18f, 26f }); NoiseBurst(d, 2000, 9000, 250, 0.5f); return Clip("post", d, 0.8f); }
        public static AudioClip Board() { var d = Buf(0.22f); Thump(d, 150, 90, 30, 22, 1); NoiseBurst(d, 300, 1800, 60, 0.4f); return Clip("board", d, 0.7f); }
        public static AudioClip Bounce() { var d = Buf(0.14f); Thump(d, 130, 80, 35, 34, 1); return Clip("bounce", d, 0.6f); }
        public static AudioClip NetSwish() { var d = Buf(0.8f); NoiseBurst(d, 1500, 6000, 5, 1, 0, 0.02f); NoiseBurst(d, 200, 900, 9, 0.5f, 0, 0.01f); return Clip("net", d, 0.7f); }
        public static AudioClip Horn()
        {   // стадионный гудок: аккорд пилообразных волн с вибрато
            var d = Buf(1.5f); float[] f = { 220, 277.2f, 329.6f, 110 }; float[] ph = new float[4]; float lp = 0, lp2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate, v = 0;
                for (int k = 0; k < 4; k++) { ph[k] += f[k] * (1 + 0.006f * Mathf.Sin(TAU * 5.5f * t + k)) / Rate; v += (ph[k] % 1f) * 2 - 1; }
                lp += A(1700) * (v - lp); lp2 += A(1700) * (lp - lp2);
                float env = Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((1.5f - t) / 0.35f);
                d[i] = lp2 * env;
            }
            return Clip("horn", d, 0.7f);
        }
        /// <summary>Судейский свисток: коротких — count, последний длиннее (long).</summary>
        public static AudioClip Whistle(int count, bool longLast)
        {
            float[] lens = new float[count]; float total = 0;
            for (int k = 0; k < count; k++) { lens[k] = k == count - 1 && longLast ? 0.75f : 0.22f; total += lens[k] + 0.09f; }
            var d = Buf(total + 0.05f); int pos = 0;
            for (int k = 0; k < count; k++)
            {
                int n = (int)(lens[k] * Rate); float ph = 0, l = 0;
                for (int i = 0; i < n && pos + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    ph += TAU * (2850 + 60 * Mathf.Sin(TAU * 4 * t)) / Rate;
                    float trill = 0.6f + 0.4f * Mathf.Sin(TAU * 31 * t); // «горошина» внутри свистка
                    l += A(4000) * (Noise() - l);
                    float env = Mathf.Clamp01(t / 0.015f) * Mathf.Clamp01((lens[k] - t) / 0.03f);
                    d[pos + i] = (Mathf.Sin(ph) * trill + l * 0.12f) * env;
                }
                pos += n + (int)(0.09f * Rate);
            }
            return Clip("whistle" + count, d, 0.6f);
        }
        static AudioClip Blips(string name, float[] f, float each, float gain)
        {
            var d = Buf(each * f.Length + 0.03f);
            for (int k = 0; k < f.Length; k++) Ring(d, new[] { f[k], f[k] * 2 }, new[] { 1f, 0.25f }, new[] { 38f, 60f }, k * each);
            return Clip(name, d, gain);
        }
        public static AudioClip UiMove() => Blips("ui_move", new[] { 1250f }, 0.06f, 0.35f);
        public static AudioClip UiConfirm() => Blips("ui_ok", new[] { 880f, 1320f }, 0.07f, 0.45f);
        public static AudioClip UiBack() => Blips("ui_back", new[] { 1100f, 740f }, 0.07f, 0.4f);
        public static AudioClip Denied()
        {
            var d = Buf(0.18f);
            for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; d[i] = (Mathf.Sign(Mathf.Sin(TAU * 110 * t)) + Mathf.Sign(Mathf.Sin(TAU * 117 * t))) * 0.5f * Mathf.Exp(-t * 9); }
            float lp = 0; for (int i = 0; i < d.Length; i++) { lp += A(900) * (d[i] - lp); d[i] = lp; }
            return Clip("denied", d, 0.4f);
        }

        // ---------- временные трибуны (пока нет файлов) ----------
        /// <summary>Гул трибун: много «голосов» — полосы шума с медленно плавающей громкостью. Бесшовная петля.</summary>
        public static AudioClip CrowdLoop()
        {
            const float len = 6f, xf = 1f; var d = Buf(len + xf);
            float[] lo = { 220, 330, 480, 650 }, hi = { 700, 1100, 1600, 2400 }; float[] l1 = new float[4], l2 = new float[4];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate, v = 0;
                for (int k = 0; k < 4; k++)
                {
                    float x = Noise(); l1[k] += A(hi[k]) * (x - l1[k]); l2[k] += A(lo[k]) * (l1[k] - l2[k]);
                    float mod = 0.65f + 0.35f * Mathf.PerlinNoise(t * (0.7f + k * 0.4f), k * 3.1f);
                    v += (l1[k] - l2[k]) * mod;
                }
                d[i] = v;
            }
            int n = (int)(len * Rate), x0 = (int)(xf * Rate);
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = d[i];
            for (int i = 0; i < x0; i++) { float k = i / (float)x0; o[i] = d[i] * k + d[n + i] * (1 - k); } // начало сливается с концом
            return Clip("crowd_loop_synth", o, 0.5f);
        }
        public static AudioClip CrowdCheer()
        {
            var d = Buf(3.5f); float l1 = 0, l2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate; float x = Noise();
                l1 += A(3200) * (x - l1); l2 += A(450) * (l1 - l2);
                float env = Mathf.Clamp01(t / 0.25f) * Mathf.Exp(-Mathf.Max(0, t - 0.6f) * 1.1f) * (0.85f + 0.15f * Mathf.PerlinNoise(t * 9, 0));
                d[i] = (l1 - l2) * env;
            }
            return Clip("crowd_cheer_synth", d, 0.9f);
        }
        public static AudioClip CrowdOoh()
        {
            var d = Buf(1.6f); float l1 = 0, l2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate, k = t / 1.6f; float fc = Mathf.Lerp(350, 700, Mathf.Sin(Mathf.PI * k));
                float x = Noise(); l1 += A(fc * 1.5f) * (x - l1); l2 += A(fc * 0.6f) * (l1 - l2);
                d[i] = (l1 - l2) * Mathf.Sin(Mathf.PI * k);
            }
            return Clip("crowd_ooh_synth", d, 0.7f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { rng = new System.Random(7); }
    }
}
