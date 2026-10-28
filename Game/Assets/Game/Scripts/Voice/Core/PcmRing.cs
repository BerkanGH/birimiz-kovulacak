using System;
using System.Threading;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Lock-free single-producer ring of mono float samples between the main thread (decoder, producer) and the
    /// audio thread (OnAudioFilterRead, consumer). Capacity is a power of two. Consumers advance the read index with
    /// a CAS so the main-thread "no audio device" drain (<see cref="Discard"/>) can never corrupt a concurrent read.
    /// </summary>
    public sealed class PcmRing
    {
        readonly float[] _buf;
        readonly int _mask;
        long _write;
        long _read;

        public PcmRing(int capacityPow2)
        {
            int cap = 1;
            while (cap < capacityPow2) cap <<= 1;
            _buf = new float[cap];
            _mask = cap - 1;
        }

        public int Capacity => _buf.Length;
        public int Count => (int)(Volatile.Read(ref _write) - Volatile.Read(ref _read));
        public int Free => _buf.Length - Count;

        /// <summary>Producer: appends up to <paramref name="count"/> samples; returns how many fit.</summary>
        public int Write(float[] src, int offset, int count)
        {
            long w = Volatile.Read(ref _write);
            int free = _buf.Length - (int)(w - Volatile.Read(ref _read));
            int n = count < free ? count : free;
            for (int i = 0; i < n; i++) _buf[(int)((w + i) & _mask)] = src[offset + i];
            Volatile.Write(ref _write, w + n);
            return n;
        }

        /// <summary>Consumer: copies up to <paramref name="count"/> samples out; returns how many were read.</summary>
        public int Read(float[] dst, int offset, int count)
        {
            while (true)
            {
                long r = Volatile.Read(ref _read);
                int avail = (int)(Volatile.Read(ref _write) - r);
                int n = count < avail ? count : avail;
                for (int i = 0; i < n; i++) dst[offset + i] = _buf[(int)((r + i) & _mask)];
                if (Interlocked.CompareExchange(ref _read, r + n, r) == r) return n;
            }
        }

        /// <summary>Consumer: drops up to <paramref name="count"/> samples (used when no audio device consumes).</summary>
        public int Discard(int count)
        {
            while (true)
            {
                long r = Volatile.Read(ref _read);
                int avail = (int)(Volatile.Read(ref _write) - r);
                int n = count < avail ? count : avail;
                if (Interlocked.CompareExchange(ref _read, r + n, r) == r) return n;
            }
        }

        public void Clear() { Discard(int.MaxValue); }
    }

    /// <summary>
    /// Audio-thread side of a voice emitter: pulls mono samples from a <see cref="PcmRing"/> at the ring's rate,
    /// resamples linearly to the output rate and multiplies them into Unity's interleaved buffer (which carries a
    /// constant-1 "flatline" clip already spatialised by the AudioSource). Allocation-free.
    /// </summary>
    public sealed class PcmPlayoutReader
    {
        readonly float[] _scratch;
        double _step = 1.0;   // ring samples per output sample
        double _pos;          // position between _a and _b
        float _a, _b;
        bool _primed;
        int _pendHead, _pendCount;
        int _underruns;
        long _callbacks;

        public PcmPlayoutReader(int scratchSize = 16384) { _scratch = new float[scratchSize]; }

        public int Underruns => Volatile.Read(ref _underruns);
        public long Callbacks => Interlocked.Read(ref _callbacks);

        /// <summary>Main thread: ring sample rate / device output rate.</summary>
        public void SetRates(int ringRate, int outputRate)
        {
            if (ringRate <= 0 || outputRate <= 0) return;
            Volatile.Write(ref _step, (double)ringRate / outputRate);
        }

        /// <summary>Audio thread. data is interleaved with <paramref name="channels"/> channels.</summary>
        public void MultiplyInto(PcmRing ring, float[] data, int channels)
        {
            Interlocked.Increment(ref _callbacks);
            if (channels <= 0) return;
            int frames = data.Length / channels;
            double step = Volatile.Read(ref _step);
            if (Math.Abs(step - 1.0) < 1e-9)
            {
                int done = 0;
                bool hadData = ring.Count > 0;
                while (done < frames)
                {
                    int chunk = Math.Min(frames - done, _scratch.Length);
                    int got = ring.Read(_scratch, 0, chunk);
                    for (int i = 0; i < got; i++)
                    {
                        float s = _scratch[i];
                        int baseIdx = (done + i) * channels;
                        for (int c = 0; c < channels; c++) data[baseIdx + c] *= s;
                    }
                    done += got;
                    if (got < chunk) break;
                }
                if (done < frames)
                {
                    if (hadData) Interlocked.Increment(ref _underruns);
                    for (int i = done * channels; i < data.Length; i++) data[i] = 0f;
                }
                return;
            }

            // Fractional path (e.g. 48 kHz ring → 44.1 kHz device). Unused samples are carried to the next callback.
            int need = (int)(_pos + frames * step) + (_primed ? 1 : 3);
            int want = need - _pendCount;
            if (want > 0)
            {
                if (_pendHead > 0 && _pendCount > 0) Array.Copy(_scratch, _pendHead, _scratch, 0, _pendCount);
                _pendHead = 0;
                int space = _scratch.Length - _pendCount;
                _pendCount += ring.Read(_scratch, _pendCount, want < space ? want : space);
            }
            bool hadAny = _pendCount > 0 || _primed;
            if (!_primed && _pendCount >= 2)
            {
                _a = _scratch[_pendHead++];
                _b = _scratch[_pendHead++];
                _pendCount -= 2;
                _pos = 0;
                _primed = true;
            }
            int f = 0;
            for (; f < frames && _primed; f++)
            {
                while (_pos >= 1.0)
                {
                    if (_pendCount == 0) { _primed = false; break; }
                    _a = _b;
                    _b = _scratch[_pendHead++];
                    _pendCount--;
                    _pos -= 1.0;
                }
                if (!_primed) break;
                float s = _a + (_b - _a) * (float)_pos;
                int baseIdx = f * channels;
                for (int c = 0; c < channels; c++) data[baseIdx + c] *= s;
                _pos += step;
            }
            if (f < frames)
            {
                if (hadAny && f > 0) Interlocked.Increment(ref _underruns);
                for (int i = f * channels; i < data.Length; i++) data[i] = 0f;
            }
        }
    }
}
