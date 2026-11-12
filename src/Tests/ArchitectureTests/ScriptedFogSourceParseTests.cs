using System;
using System.Text.Json.Nodes;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class ScriptedFogSourceParseTests
{
    [Test]
    public void UnknownKey_AndUnknownMotion_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ScriptedFogSource.Parse(JsonNode.Parse(ValidJson("\"cellSizeCm\": 400,"))!));
        var motion = ValidJson(string.Empty).Replace("\"kind\": \"ellipse\"", "\"kind\": \"orbit\"", StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ScriptedFogSource.Parse(JsonNode.Parse(motion)!));
    }

    [Test]
    public void Ellipse_UsesConfiguredSeedsAndMotion()
    {
        ScriptedFogSource source = ScriptedFogSource.Parse(JsonNode.Parse(ValidJson(string.Empty))!);
        Assert.That(source.AgeVisibleToExplored, Is.True);
        Assert.That(source.SeedExplored, Has.Length.EqualTo(2));
        Assert.That(source.SeedExplored[1].X, Is.EqualTo(1023));
        var at = source.PositionAt(0f);
        Assert.That(at.X, Is.EqualTo(204800 + 60000).Within(0.01f));
        Assert.That(at.Y, Is.EqualTo(204800).Within(0.01f));
        Assert.That(source.Palette.Unseen.A, Is.EqualTo(232));
        Assert.That(source.Palette.Explored.A, Is.EqualTo(148));
        Assert.That(source.Palette.Visible.A, Is.EqualTo(0));
        Assert.That(source.Palette.Denied.R, Is.EqualTo(64));
        Assert.That(source.Palette.Denied.A, Is.EqualTo(214));
    }

    private static string ValidJson(string extra) => $$"""
    {
      {{extra}}
      "mapId": "fog_terrain_decal_showcase",
      "layerId": "fog-terrain-decal-ground",
      "scopeKeyId": 316,
      "overlayStableId": 7316,
      "centerXCm": 204800,
      "centerYCm": 204800,
      "visionRadiusCm": 24000,
      "ageVisibleToExplored": true,
      "seedExplored": [ { "x": 0, "y": 0 }, { "x": 1023, "y": 1023 } ],
      "motion": { "kind": "ellipse", "radiusXCm": 60000, "radiusYCm": 45000, "angularSpeedX": 0.09, "angularSpeedY": 0.13 },
      "ringHeight": 1.2,
      "borderWidth": 0.06,
      "fill": [0.15, 0.65, 1, 0.10],
      "border": [0.25, 0.85, 1, 0.75],
      "fogDecal": {
        "unseen": [0, 0, 0, 232],
        "explored": [0, 0, 0, 148],
        "visible": [0, 0, 0, 0],
        "denied": [64, 8, 16, 214]
      }
    }
    """;
}
