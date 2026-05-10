namespace MassNavigationMod.Runtime;

public sealed class MassFlowArrivalTuning
{
    public bool Enabled { get; set; } = true;
    public int TimeoutMs { get; set; } = 1500;
    public int ProgressDistanceCm { get; set; } = 60;
    public int WakePushDistanceCm { get; set; } = 80;
    public int MaxRetryCount { get; set; } = 2;

    public float TimeoutSeconds => TimeoutMs / 1000f;

    public void AdjustTimeoutMs(int delta)
    {
        TimeoutMs = System.Math.Clamp(TimeoutMs + delta, 250, 10000);
    }

    public void AdjustProgressDistanceCm(int delta)
    {
        ProgressDistanceCm = System.Math.Clamp(ProgressDistanceCm + delta, 10, 500);
    }

    public void AdjustWakePushDistanceCm(int delta)
    {
        WakePushDistanceCm = System.Math.Clamp(WakePushDistanceCm + delta, 10, 500);
    }

    public void AdjustMaxRetryCount(int delta)
    {
        MaxRetryCount = System.Math.Clamp(MaxRetryCount + delta, 0, 16);
    }
}

