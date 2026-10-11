using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class GenreInfoPanelAuthoringAcceptanceTests
{
    private const string MapId = "fourx_entry";
    private const float DeltaTime = 1f / 60f;

    [Test]
    public void EntityInfo_AuthoredGovernorCard_OpensViaTriggerAndProjectsEntitySubject()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "EntityCommandPanelMod", "FourXDemoMod", "EntityInfoPanelsMod" }),
            assetsRoot);
        PresentationAcceptanceUiHostInstaller.Install(engine, 1920f, 1080f);
        try
        {
            engine.Start();
            engine.LoadMap(MapId);
            Tick(engine, 8);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                string.Join(" | ", engine.TriggerManager.Errors));

            PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
                ?? throw new InvalidOperationException("PanelHost service missing.");
            Assert.That(panelHost.Count, Is.EqualTo(2),
                "MapLoaded trigger graphs must open the authored governor card and ability bar with zero C#.");
            PanelInstanceHandle panel = FindPanel(panelHost, "panel.fourx.governorCard");

            Assert.That(panelHost.TryGetValues(panel, out PanelVariableSet values), Is.True);
            Assert.That(values.Get("count"), Is.EqualTo(1f).Within(0.001f),
                "Governor card supply graph must count the single fourx_governor instance.");

            Assert.That(
                panelHost.TryGetListProjections(panel, out IReadOnlyList<PanelListProjection> lists),
                Is.True);
            Assert.That(lists.Count, Is.EqualTo(1));
            Assert.That(lists[0].TotalCount, Is.EqualTo(1),
                "EntityCollection output must project the governor as the single card row.");

            Assert.That(
                panelHost.TryProjectListWindow(panel, "governor", PanelListViewWindow.All, out PanelListProjection rows),
                Is.True);
            Assert.That(rows.Items.Count, Is.EqualTo(1));
            Assert.That(rows.Items[0].Strings["displayName"], Is.EqualTo("Governor"),
                "Entity subject must resolve the template Name component.");
            Assert.That(rows.Items[0].Floats["health"], Is.EqualTo(100f).Within(0.001f),
                "Capability row graph LoadSelfAttribute must read the governor template Health base.");
            Assert.That(rows.Items[0].Floats["healthMax"], Is.EqualTo(100f).Within(0.001f));
        }
        finally
        {
            engine.Dispose();
        }
    }

    private static PanelInstanceHandle FindPanel(PanelHost host, string templateId)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (info.TemplateId == templateId)
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException($"No panel '{templateId}' mounted.");
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(DeltaTime);
        }
    }

    private static string FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "mods")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Repository root not found.");
    }
}
