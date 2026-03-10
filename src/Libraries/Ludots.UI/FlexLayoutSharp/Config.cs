using System;

namespace FlexLayoutSharp;

public class Config
{
	public bool UseWebDefaults = false;

	public object Context = null;

	public LoggerFunc Logger = DefaultLog;

	internal readonly bool[] experimentalFeatures = new bool[2];

	internal bool UseLegacyStretchBehaviour = false;

	internal float PointScaleFactor = 1f;

	public static int DefaultLog(Config config, Node node, LogLevel level, string format, params object[] args)
	{
		switch (level)
		{
		case LogLevel.Error:
		case LogLevel.Fatal:
			Console.WriteLine(format, args);
			return 0;
		default:
			Console.WriteLine(format, args);
			return 0;
		}
	}

	public static void Copy(Config dest, Config src)
	{
		dest.UseWebDefaults = src.UseWebDefaults;
		dest.UseLegacyStretchBehaviour = src.UseLegacyStretchBehaviour;
		dest.PointScaleFactor = src.PointScaleFactor;
		dest.Logger = src.Logger;
		dest.Context = src.Context;
		for (int i = 0; i < src.experimentalFeatures.Length; i++)
		{
			dest.experimentalFeatures[i] = src.experimentalFeatures[i];
		}
	}

	public void SetExperimentalFeatureEnabled(ExperimentalFeature feature, bool enabled)
	{
		experimentalFeatures[(int)feature] = enabled;
	}

	public bool IsExperimentalFeatureEnabled(ExperimentalFeature feature)
	{
		return experimentalFeatures[(int)feature];
	}

	public void SetPointScaleFactor(float pixelsInPoint)
	{
		assertWithConfig(this, pixelsInPoint >= 0f, "Scale factor should not be less than zero");
		if (pixelsInPoint == 0f)
		{
			PointScaleFactor = 0f;
		}
		else
		{
			PointScaleFactor = pixelsInPoint;
		}
	}

	internal static void assertWithConfig(Config config, bool condition, string message)
	{
		if (!condition)
		{
			throw new Exception(message);
		}
	}
}
