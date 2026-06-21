namespace Ludots.Core.MassCrowd.Runtime;

public sealed class MassFlowArrivalTuning
{
    public bool Enabled { get; set; } = true;
    public int TimeoutMs { get; set; } = 1500;
    public int TimeoutMinMs { get; set; } = 250;
    public int TimeoutMaxMs { get; set; } = 10000;
    public int ProgressDistanceCm { get; set; } = 60;
    public int ProgressDistanceMinCm { get; set; } = 10;
    public int ProgressDistanceMaxCm { get; set; } = 500;
    public int WakePushDistanceCm { get; set; } = 80;
    public int WakePushDistanceMinCm { get; set; } = 10;
    public int WakePushDistanceMaxCm { get; set; } = 500;
    public int MaxRetryCountMin { get; set; }
    public int MaxRetryCountMax { get; set; } = 16;
    public int MaxRetryCount { get; set; } = 2;

    public float TimeoutSeconds => TimeoutMs / 1000f;

    public void Validate()
    {
        if (TimeoutMs <= 0)
        {
            throw new System.InvalidOperationException("Mass-nav arrival requires TimeoutMs > 0.");
        }

        RequirePositive(ProgressDistanceCm, nameof(ProgressDistanceCm));
        RequirePositive(TimeoutMinMs, nameof(TimeoutMinMs));
        RequirePositive(TimeoutMaxMs, nameof(TimeoutMaxMs));
        RequirePositive(ProgressDistanceMinCm, nameof(ProgressDistanceMinCm));
        RequirePositive(ProgressDistanceMaxCm, nameof(ProgressDistanceMaxCm));
        RequirePositive(WakePushDistanceCm, nameof(WakePushDistanceCm));
        RequirePositive(WakePushDistanceMinCm, nameof(WakePushDistanceMinCm));
        RequirePositive(WakePushDistanceMaxCm, nameof(WakePushDistanceMaxCm));
        RequireRange(TimeoutMinMs, TimeoutMaxMs, nameof(TimeoutMinMs), nameof(TimeoutMaxMs));
        RequireRange(ProgressDistanceMinCm, ProgressDistanceMaxCm, nameof(ProgressDistanceMinCm), nameof(ProgressDistanceMaxCm));
        RequireRange(WakePushDistanceMinCm, WakePushDistanceMaxCm, nameof(WakePushDistanceMinCm), nameof(WakePushDistanceMaxCm));
        if (MaxRetryCount < 0)
        {
            throw new System.InvalidOperationException("Mass-nav arrival requires MaxRetryCount >= 0.");
        }

        if (MaxRetryCountMin < 0)
        {
            throw new System.InvalidOperationException("Mass-nav arrival requires MaxRetryCountMin >= 0.");
        }

        RequireRange(MaxRetryCountMin, MaxRetryCountMax, nameof(MaxRetryCountMin), nameof(MaxRetryCountMax));
        RequireInside(TimeoutMs, TimeoutMinMs, TimeoutMaxMs, nameof(TimeoutMs));
        RequireInside(ProgressDistanceCm, ProgressDistanceMinCm, ProgressDistanceMaxCm, nameof(ProgressDistanceCm));
        RequireInside(WakePushDistanceCm, WakePushDistanceMinCm, WakePushDistanceMaxCm, nameof(WakePushDistanceCm));
        RequireInside(MaxRetryCount, MaxRetryCountMin, MaxRetryCountMax, nameof(MaxRetryCount));
    }

    public void AdjustTimeoutMs(int delta)
    {
        TimeoutMs = System.Math.Clamp(TimeoutMs + delta, TimeoutMinMs, TimeoutMaxMs);
    }

    public void AdjustProgressDistanceCm(int delta)
    {
        ProgressDistanceCm = System.Math.Clamp(ProgressDistanceCm + delta, ProgressDistanceMinCm, ProgressDistanceMaxCm);
    }

    public void AdjustWakePushDistanceCm(int delta)
    {
        WakePushDistanceCm = System.Math.Clamp(WakePushDistanceCm + delta, WakePushDistanceMinCm, WakePushDistanceMaxCm);
    }

    public void AdjustMaxRetryCount(int delta)
    {
        MaxRetryCount = System.Math.Clamp(MaxRetryCount + delta, MaxRetryCountMin, MaxRetryCountMax);
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new System.InvalidOperationException($"Mass-nav arrival requires {name} > 0.");
        }
    }

    private static void RequireRange(int min, int max, string minName, string maxName)
    {
        if (min > max)
        {
            throw new System.InvalidOperationException($"Mass-nav arrival requires {minName} <= {maxName}.");
        }
    }

    private static void RequireInside(int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            throw new System.InvalidOperationException($"Mass-nav arrival requires {name} inside its configured adjustment range.");
        }
    }
}
