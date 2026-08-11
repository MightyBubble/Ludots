namespace Y5kGrandStrategyMod.Runtime;

public sealed class Y5kDemoState
{
	public string PhaseId { get; set; } = "boot";
	public string PhaseTitle { get; set; } = "开局";
	public string PhaseDetail { get; set; } = string.Empty;
	public int StepIndex { get; set; }
	public IReadOnlyList<string> BulletinLines { get; set; } = Array.Empty<string>();
}
