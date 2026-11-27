using System;

namespace DDEN.LoongNova
{
    // Original procedural sound. All partials and modulation finish whole cycles at the loop seam.
    internal static class ChargeTone
    {
        internal const int SampleRate = 12000;
        internal static float[] Samples()
        {
            var samples = new float[SampleRate / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = Sample((double)i / SampleRate);
            return samples;
        }
        internal static float Sample(double seconds)
        {
            double phase = seconds * Math.PI * 2;
            double hum = Math.Sin(180 * phase) * 0.55 + Math.Sin(360 * phase) * 0.2
                + Math.Sin(720 * phase) * 0.08 + Math.Sin(1200 * phase) * 0.04;
            return (float)(hum * (0.8 + Math.Sin(6 * phase) * 0.2));
        }
        private static float Clamp(float progress) => float.IsNaN(progress) ? 0 : Math.Max(0, Math.Min(1, progress));
        internal static float Pitch(float progress) => 0.7f + Clamp(progress) * 1.1f;
        internal const float OthersGain = 0.6f;
        internal const float HearingRange = 30;
        internal static float Gain(float progress) => 0.045f + Clamp(progress) * 0.045f;
        internal static float ListenerGain(float progress, bool owner, float distance)
        {
            if (owner) return Gain(progress);
            if (float.IsNaN(distance) || float.IsInfinity(distance)) return 0;
            float attenuation = 1 - Clamp((distance - 2) / (HearingRange - 2));
            return Gain(progress) * OthersGain * attenuation;
        }
    }
}
