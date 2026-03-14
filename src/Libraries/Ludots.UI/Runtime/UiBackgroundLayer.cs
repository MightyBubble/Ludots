using System;

namespace Ludots.UI.Runtime;

public sealed record UiBackgroundLayer
{
	public UiColor Color { get; init; } = UiColor.Transparent;

	public UiLinearGradient? Gradient { get; init; }

	public string? ImageSource { get; init; }

	public bool IsVisible => Color != UiColor.Transparent || Gradient != null || !string.IsNullOrWhiteSpace(ImageSource);

	public static UiBackgroundLayer FromColor(UiColor color)
	{
		return new UiBackgroundLayer
		{
			Color = color
		};
	}

	public static UiBackgroundLayer FromGradient(UiLinearGradient gradient)
	{
		ArgumentNullException.ThrowIfNull(gradient, "gradient");
		return new UiBackgroundLayer
		{
			Gradient = gradient
		};
	}

	public static UiBackgroundLayer FromImage(string imageSource)
	{
		if (string.IsNullOrWhiteSpace(imageSource))
		{
			throw new ArgumentException("Image source is required.", "imageSource");
		}
		return new UiBackgroundLayer
		{
			ImageSource = imageSource.Trim()
		};
	}
}
