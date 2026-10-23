using System;
using System.Collections.Generic;
namespace BirimizKovulacak.Core
{
    // SplitMix64, including specified wrapping arithmetic and unbiased integer sampling.
    internal sealed class DeterministicRandom
    {
        private ulong state;
        internal DeterministicRandom(int seed) { state = unchecked((ulong)(long)seed); }
        internal ulong Next()
        {
            unchecked {
                ulong z = (state += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
        internal int Index(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            ulong n = (ulong)count, threshold = unchecked(0UL - n) % n, value;
            do { value = Next(); } while (value < threshold);
            return (int)(value % n);
        }
        internal double Between(double min, double max) { return min + (max - min) * ((Next() >> 11) * (1.0 / 9007199254740992.0)); }
        internal void Shuffle<T>(List<T> list) { for (int i = list.Count - 1; i > 0; i--) { int j = Index(i + 1); T t = list[i]; list[i] = list[j]; list[j] = t; } }
    }
}
