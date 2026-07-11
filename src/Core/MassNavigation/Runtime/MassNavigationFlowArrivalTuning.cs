using System.Text.Json.Serialization;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationFlowArrivalTuning
{
    [JsonRequired] public bool Enabled { get; set; }
    [JsonRequired] public int TimeoutMs { get; set; }
    [JsonRequired] public int ProgressDistanceCm { get; set; }
    [JsonRequired] public int WakePushDistanceCm { get; set; }
    [JsonRequired] public int MaxRetryCount { get; set; }

    public float TimeoutSeconds => TimeoutMs / 1000f;

    public void CopyFrom(MassNavigationFlowArrivalTuning source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        Enabled = source.Enabled;
        TimeoutMs = source.TimeoutMs;
        ProgressDistanceCm = source.ProgressDistanceCm;
        WakePushDistanceCm = source.WakePushDistanceCm;
        MaxRetryCount = source.MaxRetryCount;
    }

    public void Validate()
    {
        if (TimeoutMs <= 0)
        {
            throw new System.InvalidOperationException("MassNavigation arrival requires TimeoutMs > 0.");
        }

        RequirePositive(ProgressDistanceCm, nameof(ProgressDistanceCm));
        RequirePositive(WakePushDistanceCm, nameof(WakePushDistanceCm));
        if (MaxRetryCount < 0)
        {
            throw new System.InvalidOperationException("MassNavigation arrival requires MaxRetryCount >= 0.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new System.InvalidOperationException($"MassNavigation arrival requires {name} > 0.");
        }
    }

}
