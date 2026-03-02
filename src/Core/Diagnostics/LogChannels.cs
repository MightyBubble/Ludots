namespace Ludots.Core.Diagnostics
{
    public static class LogChannels
    {
        public static readonly LogChannel Engine = Log.RegisterChannel("Engine");
        public static readonly LogChannel ModLoader = Log.RegisterChannel("ModLoader");
        public static readonly LogChannel Config = Log.RegisterChannel("Config");
        public static readonly LogChannel GAS = Log.RegisterChannel("GAS");
        public static readonly LogChannel Physics = Log.RegisterChannel("Physics");
        public static readonly LogChannel Nav2D = Log.RegisterChannel("Nav2D");
        public static readonly LogChannel Map = Log.RegisterChannel("Map");
        public static readonly LogChannel Input = Log.RegisterChannel("Input");
        public static readonly LogChannel Presentation = Log.RegisterChannel("Presentation");
        public static readonly LogChannel AI = Log.RegisterChannel("AI");
    }
}
