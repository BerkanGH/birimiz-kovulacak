using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    // PLACEHOLDERS written by the lead so the Unity side compiles against the contract. The engine track (Codex)
    // replaces the bodies of everything in Core/Line/Engine/** — keep these public signatures.

    public static class LineEngineFactory
    {
        public static ILineEngine Create(LineSetup setup, LineConfig config)
        {
            throw new NotImplementedException("v3 engine not merged yet");
        }
    }

    /// <summary>Compact binary wire format for every v3 message (deterministic, versioned, no reflection).</summary>
    public static class LineCodec
    {
        public static byte[] EncodeView(LineView v) { throw new NotImplementedException(); }
        public static LineView DecodeView(byte[] payload) { throw new NotImplementedException(); }
        public static byte[] EncodeAction(LineAction a) { throw new NotImplementedException(); }
        public static LineAction DecodeAction(byte[] payload) { throw new NotImplementedException(); }
        public static byte[] EncodeResult(LineActionResult r) { throw new NotImplementedException(); }
        public static LineActionResult DecodeResult(byte[] payload) { throw new NotImplementedException(); }
        public static byte[] EncodeEvents(List<LineEvent> events) { throw new NotImplementedException(); }
        public static List<LineEvent> DecodeEvents(byte[] payload) { throw new NotImplementedException(); }
        public static byte[] EncodeLobby(LineLobbyView lobby) { throw new NotImplementedException(); }
        public static LineLobbyView DecodeLobby(byte[] payload) { throw new NotImplementedException(); }
        public static byte[] EncodeWelcome(LineWelcome w) { throw new NotImplementedException(); }
        public static LineWelcome DecodeWelcome(byte[] payload) { throw new NotImplementedException(); }
        public static byte[] EncodeConnection(LineConnectionPayload p) { throw new NotImplementedException(); }
        public static LineConnectionPayload DecodeConnection(byte[] payload) { throw new NotImplementedException(); }
    }

    /// <summary>
    /// Automated player used by host-side bots, network test clients and the balance simulator. Sees only its own
    /// <see cref="LineView"/>. Deterministic for a given seed and view sequence.
    /// </summary>
    public sealed class LineBot
    {
        public LineBot(int player, int seed) { }

        /// <summary>Next action to send now, or null to wait. Called every ~0.25 s.</summary>
        public LineAction Next(LineView view, LineConfig config, double now) { return null; }
    }
}
