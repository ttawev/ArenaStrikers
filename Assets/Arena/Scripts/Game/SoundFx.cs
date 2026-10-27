// Placeholder sounds synthesised in code (no audio files needed yet). Stage 3 replaces them with real ones.
using UnityEngine;

namespace Arena.Game
{
    public static class SoundFx
    {
        /// <summary>Ball reception: a soft leather "thump" — low pitch drop plus a short click.</summary>
        public static AudioClip Receive()
        {
            const int rate = 44100; const float len = 0.16f;
            int n = (int)(rate * len);
            var data = new float[n];
            var rng = new System.Random(7);
            float phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = Mathf.Lerp(170, 85, Mathf.Clamp01(t / 0.08f));
                phase += 2 * Mathf.PI * f / rate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * 32);
                float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 260) * 0.55f;
                float attack = Mathf.Clamp01(t / 0.002f);
                data[i] = (body * 0.9f + click) * attack * 0.8f;
            }
            var clip = AudioClip.Create("receive", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
