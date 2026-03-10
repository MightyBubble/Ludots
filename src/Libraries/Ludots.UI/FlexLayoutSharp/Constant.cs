namespace FlexLayoutSharp;

internal class Constant
{
	internal const int EdgeCount = 9;

	internal const int ExperimentalFeatureCount = 1;

	internal const int MeasureModeCount = 3;

	internal const int MaxCachedResultCount = 16;

	internal const int measureModeCount = 3;

	internal static readonly string[] measureModeNames = new string[3] { "UNDEFINED", "EXACTLY", "AT_MOST" };

	internal static readonly string[] layoutModeNames = new string[3] { "LAY_UNDEFINED", "LAY_EXACTLY", "LAY_AT_MOST" };

	internal static readonly Node nodeDefaults = Flex.CreateDefaultNode();

	internal static readonly Config configDefaults = Flex.CreateDefaultConfig();

	internal const float defaultFlexGrow = 0f;

	internal const float defaultFlexShrink = 0f;

	internal const float webDefaultFlexShrink = 1f;
}
