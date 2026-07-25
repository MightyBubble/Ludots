using System;
using System.IO;
using System.Text.Json.Nodes;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DStationCameraShowcaseTests
{
    [Test]
    public void Feature_StationCamera_Scenario_EveryPlayerStationHasOneExplicitPose()
    {
        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(LoadOfficialConfig());

        Physics3DShowcaseScene[] scenes = Enum.GetValues<Physics3DShowcaseScene>();
        Assert.That(config.StationCameraPoses, Has.Length.EqualTo(scenes.Length));
        foreach (Physics3DShowcaseScene scene in scenes)
        {
            Physics3DStationCameraShowcaseConfig pose = config.GetStationCameraPose(scene);
            Assert.Multiple(() =>
            {
                Assert.That(pose.Scene, Is.EqualTo(scene));
                Assert.That(pose.DistanceCm, Is.GreaterThan(0f));
                Assert.That(pose.FovYDeg, Is.InRange(20f, 100f));
            });
        }
    }

    [Test]
    public void Feature_StationCamera_Scenario_MissingDuplicateAndInvalidPosesFailLoudly()
    {
        JsonObject missing = LoadOfficialConfig();
        missing["stationCameraPoses"]!.AsArray().RemoveAt(0);
        Assert.That(
            () => Physics3DShowcaseConfig.Load(missing),
            Throws.InvalidOperationException.With.Message.Contains("exactly one station camera pose"));

        JsonObject duplicate = LoadOfficialConfig();
        JsonArray duplicatePoses = duplicate["stationCameraPoses"]!.AsArray();
        duplicatePoses[1]!["scene"] = duplicatePoses[0]!["scene"]!.GetValue<string>();
        Assert.That(
            () => Physics3DShowcaseConfig.Load(duplicate),
            Throws.InvalidOperationException.With.Message.Contains("duplicate station camera poses"));

        JsonObject invalid = LoadOfficialConfig();
        invalid["stationCameraPoses"]!.AsArray()[0]!["distanceCm"] = 0;
        Assert.That(
            () => Physics3DShowcaseConfig.Load(invalid),
            Throws.InvalidOperationException.With.Message.Contains("DistanceCm"));
    }

    private static JsonObject LoadOfficialConfig()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics3DShowcaseMod",
            "assets",
            "CapabilityStandardPhysics3DShowcaseConfig.json");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Physics3D showcase config is missing.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "launcher.config.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "mods")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Ludots repository root.");
    }
}
