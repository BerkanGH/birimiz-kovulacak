using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    // CONTRACT STUB — owned by the Codex work package.

    /// <summary>
    /// Compact binary encoding for every message that crosses the network (sent by the Unity adapter as byte[]).
    /// Little-endian, UTF-8 strings, every top-level message starts with a 1-byte message tag and the 2-byte
    /// <see cref="Protocol.Version"/>; Decode throws <see cref="FormatException"/> on a tag/version mismatch or truncation.
    /// Round-trip must be lossless for every field defined in the contract types.
    /// </summary>
    public static class WireCodec
    {
        public static byte[] EncodeView(PlayerView view) { throw new NotImplementedException(); }
        public static PlayerView DecodeView(byte[] data) { throw new NotImplementedException(); }

        public static byte[] EncodeRequest(ActionRequest request) { throw new NotImplementedException(); }
        public static ActionRequest DecodeRequest(byte[] data) { throw new NotImplementedException(); }

        public static byte[] EncodeResult(ActionResult result) { throw new NotImplementedException(); }
        public static ActionResult DecodeResult(byte[] data) { throw new NotImplementedException(); }

        public static byte[] EncodeEvents(List<GameEvent> events) { throw new NotImplementedException(); }
        public static List<GameEvent> DecodeEvents(byte[] data) { throw new NotImplementedException(); }

        public static byte[] EncodeLobby(LobbyView lobby) { throw new NotImplementedException(); }
        public static LobbyView DecodeLobby(byte[] data) { throw new NotImplementedException(); }

        public static byte[] EncodeConnectionPayload(ConnectionPayload payload) { throw new NotImplementedException(); }
        public static ConnectionPayload DecodeConnectionPayload(byte[] data) { throw new NotImplementedException(); }

        public static byte[] EncodeWelcome(WelcomeMessage welcome) { throw new NotImplementedException(); }
        public static WelcomeMessage DecodeWelcome(byte[] data) { throw new NotImplementedException(); }

        /// <summary>Deterministic content hash used for MatchResultView.ResultHash (hex, stable across processes).</summary>
        public static string HashResult(MatchResultView result) { throw new NotImplementedException(); }
    }
}
