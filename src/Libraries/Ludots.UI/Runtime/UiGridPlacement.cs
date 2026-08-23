namespace Ludots.UI.Runtime;

public readonly struct UiGridPlacement
{
	public static UiGridPlacement Auto { get; } = new UiGridPlacement(0, 1);

	public int Start { get; }

	public int Span { get; }

	public UiGridPlacement(int start, int span)
	{
		Start = start < 0 ? 0 : start;
		Span = span < 1 ? 1 : span;
	}

	public bool IsAuto => Start <= 0;
}
