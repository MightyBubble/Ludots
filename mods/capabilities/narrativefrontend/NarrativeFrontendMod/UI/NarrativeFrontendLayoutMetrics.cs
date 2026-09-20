using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Engine;

namespace NarrativeFrontendMod.UI;

internal sealed class NarrativeFrontendLayoutMetrics
{
    public float SafeAreaMargin { get; set; }
    public float BottomLaneGap { get; set; }
    public float StandingImageAspect { get; set; }
    public float StandingCardGap { get; set; }
    public float StandingCardMinWidth { get; set; }

    public static NarrativeFrontendLayoutMetrics Load(GameEngine engine)
    {
        const string path = "NarrativeFrontendMod:assets/UI/layout_metrics.json";
        if (engine.VFS == null ||
            !engine.VFS.TryResolveFullPath(path, out string resolved) ||
            !File.Exists(resolved))
        {
            throw new InvalidOperationException(
                $"Narrative frontend layout metrics '{path}' are required.");
        }

        using FileStream stream = File.OpenRead(resolved);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        NarrativeFrontendLayoutMetrics metrics =
            JsonSerializer.Deserialize<NarrativeFrontendLayoutMetrics>(stream, options)
            ?? throw new InvalidOperationException(
                $"Narrative frontend layout metrics '{path}' parsed to null.");
        metrics.Validate(path);
        return metrics;
    }

    private void Validate(string path)
    {
        var invalid = new List<string>(5);
        RequirePositive(SafeAreaMargin, "safeAreaMargin", invalid);
        RequirePositive(BottomLaneGap, "bottomLaneGap", invalid);
        RequirePositive(StandingImageAspect, "standingImageAspect", invalid);
        RequirePositive(StandingCardGap, "standingCardGap", invalid);
        RequirePositive(StandingCardMinWidth, "standingCardMinWidth", invalid);
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Narrative frontend layout metrics '{path}' require positive finite values for: {string.Join(", ", invalid)}.");
        }
    }

    private static void RequirePositive(float value, string name, List<string> invalid)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            invalid.Add(name);
        }
    }
}
