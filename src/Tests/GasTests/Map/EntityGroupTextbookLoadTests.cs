using System;
using System.IO;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// 切A 验收：教科书 showcase 的 templates.json + map 不新增任何模板类型即可装载——
    /// 营地是一个普通 EntityTemplate，递归 children 展开、localId 路径累积、attach 标记解析。
    /// </summary>
    [TestFixture]
    public sealed class EntityGroupTextbookLoadTests
    {
        private const string MapId = "eg_camp_site";

        [Test]
        public void Textbook_TemplatesAndMap_LoadWithoutThrowAndExpandChildren()
        {
            string repoRoot = FindRepoRoot();
            string modAssets = Path.Combine(
                repoRoot, "mods", "showcases", "entity_group_textbook", "EntityGroupTextbookMod", "assets");
            Assert.That(Directory.Exists(modAssets), Is.True, "textbook showcase assets must exist");

            using var world = World.Create();
            MapLoader loader = CreateLoader(world, modAssets);
            MapConfig map = LoadMapConfig(System.IO.File.ReadAllText(Path.Combine(modAssets, "Maps", "eg_camp_site.json")));

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.That(index.Count, Is.EqualTo(2), "two camp placements share one flat InstanceId namespace");
            Assert.That(index.LocalPathCount, Is.EqualTo(26), "13 addressable descendants per camp × 2 camps");

            Assert.That(index.TryGetByLocalPath("mule.harbor.hq.chest.cargo", out Entity cargo), Is.True);
            Assert.That(index.TryGetByLocalPath("mule.alpine.tower.light", out Entity light), Is.True);
            Assert.That(index.TryGetByLocalPath("mule.harbor.guard2", out Entity guard2), Is.True);
            Assert.That(index.TryGetByLocalPath("mule.alpine.barricade", out Entity barricade), Is.True);

            Assert.That(world.IsAlive(cargo) && world.IsAlive(light) && world.IsAlive(guard2) && world.IsAlive(barricade), Is.True);
            Assert.That(world.Get<Name>(cargo).Value, Is.EqualTo("Rope"));
            Assert.That(world.Get<Name>(light).Value, Is.EqualTo("Spotlight"));
            Assert.That(world.Get<Name>(guard2).Value, Is.EqualTo("Mule Guard"));
            Assert.That(world.Get<Name>(barricade).Value, Is.EqualTo("Barricade"));

            // 切B：overridePaths 向后代做字段 deep-merge（未覆盖字段保留），模板无组件时作为新组件写入。
            Assert.That(world.Get<Health>(cargo).Current, Is.EqualTo(8), "harbor path set keeps the unset deep field");
            Assert.That(world.Get<Health>(cargo).Max, Is.EqualTo(12), "harbor path set overwrites only the set field");
            Assert.That(index.TryGetByLocalPath("mule.harbor.tower.light", out Entity harborLight), Is.True);
            Assert.That(world.Get<Health>(harborLight).Current, Is.EqualTo(3), "path set writes Health as a new component on the spotlight");
            Assert.That(world.Get<Health>(harborLight).Max, Is.EqualTo(3));

            Assert.That(index.TryGetByLocalPath("mule.alpine.guard", out Entity alpineGuard), Is.True);
            Assert.That(world.Get<Health>(alpineGuard).Current, Is.EqualTo(30), "alpine absolute path set keeps the unset deep field");
            Assert.That(world.Get<Health>(alpineGuard).Max, Is.EqualTo(50), "alpine absolute path set overwrites only the set field");

            // Parent before child: the deep cargo entity was spawned after its hq.chest ancestor.
            Assert.That(index.TryGetByLocalPath("mule.harbor.hq.chest", out Entity chest), Is.True);
            Assert.That(chest.Id, Is.LessThan(cargo.Id));
        }

        private static MapLoader CreateLoader(World world, string modAssets)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_EntityGroupTextbookLoadTests",
                Guid.NewGuid().ToString("N"));
            try
            {
                string entities = Path.Combine(root, "Entities");
                string maps = Path.Combine(root, "Maps");
                Directory.CreateDirectory(entities);
                Directory.CreateDirectory(maps);
                File.Copy(Path.Combine(modAssets, "Entities", "templates.json"), Path.Combine(entities, "templates.json"));
                File.Copy(Path.Combine(modAssets, "Maps", "eg_camp_site.json"), Path.Combine(maps, "eg_camp_site.json"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                var loader = new MapLoader(world, new WorldMap(), pipeline);
                loader.LoadTemplates(ConfigCatalogLoader.Load(pipeline));
                return loader;
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static MapConfig LoadMapConfig(string json)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MapConfig>(json, options)
                ?? throw new InvalidOperationException("textbook map deserialized to null");
        }

        private static string FindRepoRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "mods", "LudotsCoreMod", "mod.json")) &&
                    File.Exists(Path.Combine(current, "mods", "CoreInputMod", "mod.json")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repo root.");
        }
    }
}
