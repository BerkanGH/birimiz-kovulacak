using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Energy VAD for open-mic mode: a frame is "loud" when its RMS exceeds both the user threshold and the tracked
    /// noise floor + margin. Opens after <see cref="AttackFrames"/> loud frames, stays open for
    /// <see cref="HangoverFrames"/> quiet frames (so word gaps and trailing syllables are not cut).
    /// </summary>
    public sealed class VoiceActivityDetector
    {
        /// <summary>Absolute threshold in dBFS (see <see cref="SetSensitivity"/>).</summary>
        public float ThresholdDb = -45f;
        /// <summary>Required level above the adaptive noise floor (dB).</summary>
        public float NoiseMarginDb = 9f;
        public int AttackFrames = 2;
        public int HangoverFrames = 15; // 300 ms

        float _noiseFloorDb = -70f;
        int _loudRun;
        int _hangover;
        bool _active;

        public bool IsActive => _active;
        public float NoiseFloorDb => _noiseFloorDb;
        public float LastLevelDb { get; private set; } = -100f;

        /// <summary>0 = least sensitive (-25 dBFS threshold), 1 = most sensitive (-60 dBFS).</summary>
        public void SetSensitivity(float sensitivity01)
        {
            float s = sensitivity01 < 0 ? 0 : (sensitivity01 > 1 ? 1 : sensitivity01);
            ThresholdDb = -25f - 35f * s;
        }

        public bool Process(float[] frame, int offset, int count)
        {
            return ProcessLevel(VoiceLevel.RmsDb(frame, offset, count));
        }

        public bool ProcessLevel(float db)
        {
            LastLevelDb = db;
            // Noise floor: falls fast, rises slowly (0.05 dB per frame = 2.5 dB/s) so speech barely moves it.
            if (db < _noiseFloorDb) _noiseFloorDb = Math.Max(db, _noiseFloorDb - 3f);
            else _noiseFloorDb = Math.Min(db, _noiseFloorDb + 0.05f);
            if (_noiseFloorDb < -90f) _noiseFloorDb = -90f;

            bool loud = db > ThresholdDb && db > _noiseFloorDb + NoiseMarginDb;
            if (loud)
            {
                _loudRun++;
                if (_loudRun >= AttackFrames) { _active = true; _hangover = HangoverFrames; }
                else if (_active) _hangover = HangoverFrames;
            }
            else
            {
                _loudRun = 0;
                if (_active)
                {
                    if (_hangover > 0) _hangover--;
                    else _active = false;
                }
            }
            return _active;
        }

        public void Reset()
        {
            _loudRun = 0;
            _hangover = 0;
            _active = false;
            _noiseFloorDb = -70f;
        }
    }
}
