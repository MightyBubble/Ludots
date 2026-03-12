namespace InteractionShowcaseMod.Runtime
{
    internal sealed class InteractionShowcaseStressTelemetry
    {
        public const string GlobalKey = "InteractionShowcaseMod.StressTelemetry";

        public bool IsActive { get; set; }
        public int DesiredPerSide { get; set; }
        public int RequestedRed { get; set; }
        public int RequestedBlue { get; set; }
        public int LiveRed { get; set; }
        public int LiveBlue { get; set; }
        public int ProjectileCount { get; set; }
        public int PeakProjectileCount { get; set; }
        public int OrdersIssued { get; set; }
        public int WavesDispatched { get; set; }
        public int QueueDepth { get; set; }
        public float RedAnchorHealth { get; set; }
        public float BlueAnchorHealth { get; set; }

        public void Reset(int desiredPerSide)
        {
            DesiredPerSide = desiredPerSide;
            IsActive = false;
            RequestedRed = 0;
            RequestedBlue = 0;
            LiveRed = 0;
            LiveBlue = 0;
            ProjectileCount = 0;
            PeakProjectileCount = 0;
            OrdersIssued = 0;
            WavesDispatched = 0;
            QueueDepth = 0;
            RedAnchorHealth = 0f;
            BlueAnchorHealth = 0f;
        }
    }
}
