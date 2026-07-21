using System;

namespace RogueEssence.Content
{
    public static class AudioVolume
    {
        /// <summary>
        /// Converts engine-facing values to the finite range accepted by Android audio.
        /// </summary>
        public static float Sanitize(float value)
        {
            if (float.IsNaN(value))
                return 0f;
            if (float.IsPositiveInfinity(value))
                return 1f;
            if (float.IsNegativeInfinity(value))
                return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
