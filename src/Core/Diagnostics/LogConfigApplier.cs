using System;
using System.Reflection;

namespace Ludots.Core.Diagnostics
{
    public static class LogConfigApplier
    {
        public static void Apply(LogConfig config)
        {
            if (config == null) return;

            if (TryParseLevel(config.GlobalLevel, out var globalLevel))
                Log.SetGlobalLevel(globalLevel);

            if (config.ChannelLevels != null)
            {
                // Apply per-channel overrides by matching channel name to LogChannels fields
                foreach (var kvp in config.ChannelLevels)
                {
                    if (!TryParseLevel(kvp.Value, out var channelLevel))
                        continue;

                    var field = typeof(LogChannels).GetField(kvp.Key,
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                    if (field != null && field.FieldType == typeof(LogChannel))
                    {
                        var channel = (LogChannel)field.GetValue(null)!;
                        Log.SetChannelLevel(in channel, channelLevel);
                    }
                }
            }
        }

        private static bool TryParseLevel(string? value, out LogLevel level)
        {
            level = LogLevel.Info;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return Enum.TryParse(value, ignoreCase: true, out level);
        }
    }
}
