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
public sealed class EntityCommandPanelAuthoringAcceptanceTests
{
    private const string MapId = "fourx_entry";
    private const float DeltaTime = 1f / 60f;

    [Test]
    public void EntityCommand_AuthoredAbilityBar_OpensViaTriggerAndProjectsAbilitySlots()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(
                repoRoot,
                new[] { "LudotsCoreMod", "EntityInfoPanelsMod", "EntityCommandPanelMod", "FourXDemoMod" }),
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
                "MapLoaded triggers must open both authored cards: governor info card and ability bar.");
            PanelInstanceHandle panel = FindPanel(panelHost, "panel.cmdcard.abilityBar");

            Assert.That(panelHost.TryGetValues(panel, out PanelVariableSet values), Is.True);
            Assert.That(values.Get("ready"), Is.EqualTo(1f).Within(0.001f),
                "Ability bar supply graph must publish its ready pin.");

            Assert.That(
                panelHost.TryGetListProjections(panel, out IReadOnlyList<PanelListProjection> lists),
                Is.True);
            Assert.That(lists.Count, Is.EqualTo(1));
            Assert.That(lists[0].TotalCount, Is.EqualTo(6),
                "AbilitySlotCollection output must project one row per resolved ability slot.");

            Assert.That(
                panelHost.TryProjectListWindow(panel, "slots", PanelListViewWindow.All, out PanelListProjection slots),
                Is.True);
            Assert.That(slots.Items.Count, Is.EqualTo(6));
            string[] names = new string[slots.Items.Count];
            for (int i = 0; i < slots.Items.Count; i++)
            {
                names[i] = slots.Items[i].Strings["displayName"];
            }
            Assert.That(string.Join("|", names), Does.Contain("BuildOutpost"),
                "Ability slot rows must resolve ability display names. Got: " + string.Join("|", names));
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
