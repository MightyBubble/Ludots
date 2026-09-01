using System.IO;
using System.Linq;
using System.Numerics;
using Ludots.Raylib.SceneKit;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [Category("raylib-field")]
    public sealed class EngineSceneEditorTests
    {
        private static string RepoRoot()
        {
            string? directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "showcase.registry.json")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static string ScenePath(string id)
        {
            return Path.Combine(RepoRoot(), "projects", "engine_gallery", "scenes", $"{id}.scene.json");
        }

        [Test]
        public void SceneFiles_NoOpLoadSave_AreByteStable()
        {
            string[] scenes = Directory.GetFiles(Path.Combine(RepoRoot(), "projects", "engine_gallery", "scenes"), "*.scene.json");
            Assert.That(scenes.Length, Is.EqualTo(22));

            foreach (string path in scenes)
            {
                string original = File.ReadAllText(path);
                string rewritten = EngineSceneJson.WriteCanonical(EngineSceneJson.LoadNode(path));
                Assert.That(rewritten, Is.EqualTo(original), $"no-op 装载→保存必须字节一致：{Path.GetFileName(path)}");
            }
        }

        [Test]
        public void EditorModel_EnumeratesStaticMeshInstances()
        {
            var model = new EngineSceneEditorModel(ScenePath("composition"));
            Assert.That(model.EnumerateStaticMeshInstances().Count, Is.EqualTo(36));
        }

        [Test]
        public void EditorModel_MoveInstance_SaveChangesOnlyThatPositionLine()
        {
            string source = ScenePath("composition");
            string temp = Path.Combine(Path.GetTempPath(), "ludots-edit-" + Guid.NewGuid().ToString("N") + ".scene.json");
            File.Copy(source, temp);
            try
            {
                var model = new EngineSceneEditorModel(temp);
                EngineEditableInstance first = model.EnumerateStaticMeshInstances()[0];
                first.PositionArray[0] = System.Text.Json.Nodes.JsonValue.Create(123.45);
                first.PositionArray[1] = System.Text.Json.Nodes.JsonValue.Create(6.0);
                first.PositionArray[2] = System.Text.Json.Nodes.JsonValue.Create(-7.25);
                model.Save();

                string before = File.ReadAllText(source);
                string after = File.ReadAllText(temp);
                string[] beforeLines = before.Split('\n');
                string[] afterLines = after.Split('\n');
                Assert.That(afterLines.Length, Is.EqualTo(beforeLines.Length), "行数不变");
                int[] changedIndexes = Enumerable.Range(0, beforeLines.Length).Where(i => beforeLines[i] != afterLines[i]).ToArray();
                Assert.That(changedIndexes.Length, Is.InRange(1, 3), "position 数组至多三行变化（值相同则同行省略）");
                foreach (int i in changedIndexes)
                {
                    Assert.That(afterLines[i].Trim(), Does.Match(@"^-?[0-9]+(\.[0-9]+)?,?$"), "changed=" + afterLines[i]);
                }

                EngineProject.ParseSceneDocument(after, temp);
                var reloaded = new EngineSceneEditorModel(temp);
                EngineEditableInstance moved = reloaded.EnumerateStaticMeshInstances()[0];
                Assert.That(moved.Position.X, Is.EqualTo(123.45f).Within(0.001f));
                Assert.That(moved.Position.Z, Is.EqualTo(-7.25f).Within(0.001f));
            }
            finally
            {
                File.Delete(temp);
                File.Delete(temp + ".bak");
            }
        }

        [Test]
        public void EditorModel_Save_RefusesExternalModification()
        {
            string source = ScenePath("composition");
            string temp = Path.Combine(Path.GetTempPath(), "ludots-edit-conflict-" + Guid.NewGuid().ToString("N") + ".scene.json");
            File.Copy(source, temp);
            try
            {
                var model = new EngineSceneEditorModel(temp);
                File.WriteAllText(temp, File.ReadAllText(temp) + "");
                File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddSeconds(5));
                Assert.That(
                    () => model.Save(),
                    Throws.TypeOf<InvalidDataException>().With.Message.Contains("fail closed"));
            }
            finally
            {
                File.Delete(temp);
            }
        }

        [Test]
        public void CameraMath_ScreenCenterRay_PointsAtTarget()
        {
            var camera = new Raylib_cs.Camera3D
            {
                position = new Vector3(0f, 10f, -20f),
                target = Vector3.Zero,
                up = Vector3.UnitY,
                fovy = 45f,
                projection = Raylib_cs.CameraProjection.CAMERA_PERSPECTIVE,
            };

            (Vector3 origin, Vector3 direction) = EngineCameraMath.ScreenToRay(
                new Vector2(800f, 450f), camera, 1600f, 900f);

            Vector3 expected = Vector3.Normalize(camera.target - camera.position);
            Assert.That(Vector3.Dot(direction, expected), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(origin - camera.position, expected), Is.GreaterThan(0f), "射线原点在相机前方");
        }

        [Test]
        public void CameraMath_RayBox_HitAndMiss()
        {
            float? hit = EngineCameraMath.RayAabbIntersect(
                new Vector3(0f, 0f, -10f), Vector3.UnitZ, new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit!.Value, Is.EqualTo(9f).Within(0.001f));

            float? miss = EngineCameraMath.RayAabbIntersect(
                new Vector3(5f, 0f, -10f), Vector3.UnitZ, new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
            Assert.That(miss, Is.Null);
        }

        [Test]
        public void CameraMath_RayObb_RespectsYaw()
        {
            // 45° 旋转的扁盒（X 方向长）：斜着射的射线应命中未旋转时命不中的方向
            Quaternion yaw45 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
            float? hit = EngineCameraMath.RayObbIntersect(
                new Vector3(0f, 0f, -10f), Vector3.UnitZ, Vector3.Zero, new Vector3(3f, 0.5f, 0.5f), yaw45);
            Assert.That(hit, Is.Not.Null);

            float? missZeroYaw = EngineCameraMath.RayObbIntersect(
                new Vector3(1.5f, 0f, -10f), new Vector3(0.9f, 0f, 0.44f).AsNormalized(), Vector3.Zero, new Vector3(3f, 0.5f, 0.5f), Quaternion.Identity);
            Assert.That(missZeroYaw, Is.Null);
        }
    }

    internal static class VectorExtensions
    {
        public static Vector3 AsNormalized(this Vector3 v) => Vector3.Normalize(v);
    }
}
