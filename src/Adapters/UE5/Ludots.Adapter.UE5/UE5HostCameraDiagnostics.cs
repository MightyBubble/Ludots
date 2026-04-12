namespace Ludots.Adapter.UE5
{
    public readonly record struct UE5HostCameraDiagnosticsSnapshot(
        string CurrentWorldName,
        string CurrentLevelPath,
        string[] SummaryLines,
        string[] OwnershipLines,
        string[] PcmLines,
        string[] FinalViewLines,
        string[] PawnLines,
        string[] VerdictLines,
        string[] ProbeLines)
    {
        public static UE5HostCameraDiagnosticsSnapshot Empty { get; } = new(
            string.Empty,
            string.Empty,
            [],
            [],
            [],
            [],
            [],
            [],
            []);

        public bool IsActive =>
            !string.IsNullOrWhiteSpace(CurrentWorldName) ||
            SummaryLines.Length > 0 ||
            OwnershipLines.Length > 0 ||
            PcmLines.Length > 0 ||
            FinalViewLines.Length > 0 ||
            PawnLines.Length > 0 ||
            VerdictLines.Length > 0 ||
            ProbeLines.Length > 0;
    }

    public sealed class UE5HostCameraDiagnosticsCommandState
    {
        public bool EnablePcmPulseProbe { get; set; }

        public bool KeepPanelVisible { get; set; }

        public string LastTriggerSource { get; set; } = string.Empty;

        public void Reset()
        {
            EnablePcmPulseProbe = false;
            KeepPanelVisible = false;
            LastTriggerSource = string.Empty;
        }
    }
}
