using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Shared temp navmesh.json fixture wiring (VFS mount → config catalog → loader) so bake
    /// contract tests stop hand-rolling their own loader bootstrap — one fixture, per the
    /// anti-wheel-reinvention clause.
    /// </summary>
    internal static class NavBakeConfigLoaderTestHelpers
    {
        public static AgentProfileRegistry DefaultProfiles() => new(new[]
        {
            new AgentProfileConfig { Id = "Small", RadiusCm = 30, HeightCm = 180, ClearanceCm = 40, Mass = 1, Layer = 0 }
        });

        public static string CreateTempNavConfig(string navmeshJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-bake-service-" + Guid.NewGuid().ToString("N"));
            string configs = tempRoot;
            Directory.CreateDirectory(Path.Combine(configs, "Navigation"));
            File.WriteAllText(Path.Combine(configs, "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" }
                ]
                """);
            File.WriteAllText(Path.Combine(configs, "Navigation", "navmesh.json"), navmeshJson);
            return tempRoot;
        }

        public static NavMeshBakeConfig Load(string root, AgentProfileRegistry? profiles = null)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return new NavMeshBakeConfigLoader(pipeline, profiles ?? DefaultProfiles()).Load(catalog);
        }
    }
}
