using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SeatDiagScratchTests
    {
        [Test]
        public void DiagnoseRoadShowcaseSeats()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "assets")) &&
                    Directory.Exists(Path.Combine(dir, "mods")))
                {
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            string repoRoot = dir!;

            var modPaths = new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"),
                Path.Combine(repoRoot, "mods", "showcases", "road_network", "RoadNetworkShowcaseMod"),
            };

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
            engine.Start();

            TestContext.Out.WriteLine($"HasStartupLocalSeats={engine.MergedConfig?.HasStartupLocalSeats}");
            TestContext.Out.WriteLine($"StartupLocalSeats.Count={engine.MergedConfig?.StartupLocalSeats?.Count}");
            engine.LoadStartupMap();

            var seats = ClientLocalSeatAccess.RequireRegistry(engine);
            TestContext.Out.WriteLine($"seatCount={seats.Count}");
            TestContext.Out.WriteLine($"sessionLocalSeats={(engine.CurrentMapSession?.LocalSeats?.Count ?? -1)}");
            var lookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup);
            TestContext.Out.WriteLine($"playerLookupType={(lookup?.GetType().FullName ?? "null")}");
            TestContext.Out.WriteLine($"role={engine.GetService(CoreServiceKeys.NetworkProcessRole)}");
            Assert.That(seats.Count, Is.EqualTo(1));
        }
    }
}
