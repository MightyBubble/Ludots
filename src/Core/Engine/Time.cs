namespace Ludots.Core.Engine
{
    public static class Time
    {
        public static double TotalTime { get; internal set; }
        public static float DeltaTime { get; internal set; }
        
        // Fixed Update
        public static double FixedTotalTime { get; internal set; }
        public static float FixedDeltaTime { get; set; } = 0.02f; // 50 Hz default
        
        public static float TimeScale { get; set; } = 1.0f;
    }
}
