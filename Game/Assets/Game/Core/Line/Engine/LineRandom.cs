using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    // Independent v3 SplitMix64 implementation: fixed overflow and rejection sampling on every runtime.
    internal sealed class LineRandom
    {
        private ulong state;
        internal LineRandom(int seed) { state = unchecked((ulong)(long)seed); }
        internal ulong Next()
        {
            unchecked
            {
                ulong z = (state += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
        internal int Index(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            ulong n = (ulong)count, threshold = unchecked(0UL - n) % n, x;
            do { x = Next(); } while (x < threshold);
            return (int)(x % n);
        }
        internal double Unit() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
        internal void Shuffle<T>(List<T> a)
        {
            for (int i = a.Count - 1; i > 0; --i) { int j = Index(i + 1); T x = a[i]; a[i] = a[j]; a[j] = x; }
        }
    }
}
