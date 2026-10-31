// Compile-only stand-in for Game/Assets/Game/Core/Line/Engine/** (python -I Tools/scripts/compile_check.py --engine-stubs).
// It lists the public engine API that Unity scripts may use; the real engine must provide exactly these members.
// Every method throws: never ship or run this file.
using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public static class LineEngineFactory
    {
        public static ILineEngine Create(LineSetup setup, LineConfig config = null) { throw new NotImplementedException(); }
    }

    public static class LineCodec
    {
        public static byte[] EncodeView(LineView v) { throw new NotImplementedException(); }
        public static LineView DecodeView(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeAction(LineAction a) { throw new NotImplementedException(); }
        public static LineAction DecodeAction(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeEvents(List<LineEvent> e) { throw new NotImplementedException(); }
        public static List<LineEvent> DecodeEvents(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeResult(LineActionResult r) { throw new NotImplementedException(); }
        public static LineActionResult DecodeResult(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeLobby(LineLobbyView l) { throw new NotImplementedException(); }
        public static LineLobbyView DecodeLobby(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeWelcome(LineWelcome w) { throw new NotImplementedException(); }
        public static LineWelcome DecodeWelcome(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeConnection(LineConnectionPayload p) { throw new NotImplementedException(); }
        public static LineConnectionPayload DecodeConnection(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeMessages(List<LineMessage> m) { throw new NotImplementedException(); }
        public static List<LineMessage> DecodeMessages(byte[] b) { throw new NotImplementedException(); }
    }

    public enum LineBotPolicy : byte { Talk = 0, NoComm = 1, Greedy = 2 }

    public sealed class LineBot
    {
        public LineBot(int seat, int seed, LineBotPolicy policy = LineBotPolicy.Talk) { }
        public LineAction Next(LineView view, LineConfig config, double now, IReadOnlyList<LineView> shared = null) { throw new NotImplementedException(); }
    }

    public static class LineSchedule
    {
        public static bool Validate(LineConfig c, int players, WeekLength length, string managerId, out string reasonKey) { throw new NotImplementedException(); }
        public static List<double> PredictMeetingStarts(LineConfig c, int players, WeekLength length, bool firstSession, bool firstTimeTasks) { throw new NotImplementedException(); }
    }

    /// <summary>Player-authored text rules (docs/CONTRACTS_V4.md §5), shared by the engine and the host's name sanitiser.</summary>
    public static class LineText
    {
        public static string Sanitize(string text, int maxChars, bool allowNewlines, out LineFailure failure) { throw new NotImplementedException(); }
    }
}
