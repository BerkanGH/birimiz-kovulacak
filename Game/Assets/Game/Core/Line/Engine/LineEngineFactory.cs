namespace BirimizKovulacak.Core
{
    public static class LineEngineFactory
    {
        public static ILineEngine Create(LineSetup setup, LineConfig config = null) { return new LineEngine(setup, config); }
    }
}
