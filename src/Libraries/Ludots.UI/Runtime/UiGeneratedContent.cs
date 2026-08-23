namespace Ludots.UI.Runtime;

public readonly record struct UiGeneratedContent(UiGeneratedContentKind Kind, string Value)
{
	public static UiGeneratedContent None { get; } = new UiGeneratedContent(UiGeneratedContentKind.None, string.Empty);

	public static UiGeneratedContent Text(string value) => new UiGeneratedContent(UiGeneratedContentKind.Text, value ?? string.Empty);

	public static UiGeneratedContent Url(string value) => new UiGeneratedContent(UiGeneratedContentKind.Url, value ?? string.Empty);

	public bool IsEmpty => Kind == UiGeneratedContentKind.None || string.IsNullOrEmpty(Value);
}
