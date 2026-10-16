using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.Camera;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// 地形 LOD 夹具的镜头数值锁在关键帧采样上:拉远与再入场的插值只占相位前段,
/// 其余帧保持到达姿态。计帧单位是固定步 tick。
/// </summary>
[TestFixture]
public sealed class CameraPoseScriptSamplingTests
{
    [Test]
    public void TerrainLodFixture_MatchesPreviousPoseLaw()
    {
        string path = Path.Combine(FindRepoRoot(), "mods", "fixtures", "terrain", "TerrainLodCullingAcceptanceMod", "assets", "Camera", "pose_script.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        CameraPoseScript script = CameraPoseScript.Parse(node);

        Expect(script, 0, 90000, 90000, 40, 46, 5200);
        Expect(script, 300, 320000, 320000, 40, 46, 5200);
        Expect(script, 301, 204800, 204800, Lerp(210, 30, 1f / 180f), 36, 26000);
        Expect(script, 480, 204800, 204800, 30, 36, 26000);
        Expect(script, 481, 204800, 204800, 40, Lerp(36, 62, 1f / 60f), Lerp(26000, 900000, Smooth(1f / 60f)));
        Expect(script, 540, 204800, 204800, 40, 62, 900000);
        Expect(script, 1020, 204800, 204800, 40, 62, 900000);
        Expect(script, 1021, 204800, 204800, 40, Lerp(62, 46, 1f / 12f), Lerp(900000, 5200, Smooth(1f / 12f)));
        Expect(script, 1032, 204800, 204800, 40, 46, 5200);
        Expect(script, 1080, 204800, 204800, 40, 46, 5200);
        Expect(script, 1081, 204800, 204800, 40, 46, 5200);
    }

    [Test]
    public void UnknownKey_IsRejected()
    {
        const string json = """
        {
          "mapId": "m",
          "virtualCameraId": "cam",
          "logEveryFrames": 1,
          "keyframes": [
            {
              "endFrame": 1,
              "target": { "from": [0, 0], "to": [1, 1], "ease": "linear" },
              "yaw": { "from": 0, "to": 1, "ease": "linear" },
              "pitch": { "from": 0, "to": 1, "ease": "linear" },
              "distance": { "from": 1, "to": 2, "ease": "linear" }
            }
          ]
        }
        """;
        var node = JsonNode.Parse(json)!;
        Assert.Throws<InvalidOperationException>(() => CameraPoseScript.Parse(node));
    }

    private static void Expect(CameraPoseScript script, int frame, float x, float y, float yaw, float pitch, float distance)
    {
        CameraPoseSample sample = script.Sample(frame);
        Assert.That(sample.X, Is.EqualTo(x).Within(0.02f), $"frame {frame} x");
        Assert.That(sample.Y, Is.EqualTo(y).Within(0.02f), $"frame {frame} y");
        Assert.That(sample.Yaw, Is.EqualTo(yaw).Within(0.02f), $"frame {frame} yaw");
        Assert.That(sample.Pitch, Is.EqualTo(pitch).Within(0.02f), $"frame {frame} pitch");
        Assert.That(sample.DistanceCm, Is.EqualTo(distance).Within(0.05f), $"frame {frame} distance");
    }

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
