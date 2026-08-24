using System.Text.Json;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;

namespace Ludots.Tests.WebUiPanelKit;

/// <summary>
/// WPK-10: one panel type per small showcase folder; RTS/4X semantics live in profile JSON only.
/// </summary>
[TestFixture]
public sealed class PanelKitSmallShowcaseTests
{
	private static readonly (string Folder, string Mod, string PanelType)[] RequiredShowcases =
	[
		("panel_kit_resource_showcase", "PanelKitResourceShowcaseMod", "resource-bar"),
		("panel_kit_command_deck_showcase", "PanelKitCommandDeckShowcaseMod", "command-deck"),
		("panel_kit_production_worker_showcase", "PanelKitProductionWorkerShowcaseMod", "production-overview"),
		("panel_kit_task_objective_showcase", "PanelKitTaskObjectiveShowcaseMod", "objective"),
		("panel_kit_notification_showcase", "PanelKitNotificationShowcaseMod", "notification"),
		("panel_kit_tooltip_showcase", "PanelKitTooltipShowcaseMod", "tooltip"),
		("panel_kit_techtree_progression_showcase", "PanelKitTechTreeProgressionShowcaseMod", "techtree")
	];

	[Test]
	public void RequiredPanelShowcases_Exist_WithSoloManifestAndDualProfiles()
	{
		foreach ((string folder, string mod, string panelType) in RequiredShowcases)
		{
			string modRoot = ResolveRepoPath(Path.Combine("mods", "showcases", folder, mod));
			Assert.That(Directory.Exists(modRoot), Is.True, $"Missing showcase mod '{modRoot}'.");

			string manifestPath = Path.Combine(modRoot, "Assets", "PanelKit", "panel_manifest.json");
			string rtsPath = Path.Combine(modRoot, "Assets", "PanelKit", "profile.rts.json");
			string fourxPath = Path.Combine(modRoot, "Assets", "PanelKit", "profile.fourx.json");
			string readmePath = Path.Combine(modRoot, "README.md");

			Assert.That(File.Exists(manifestPath), Is.True, $"Missing manifest for {folder}.");
			Assert.That(File.Exists(rtsPath), Is.True, $"Missing RTS profile for {folder}.");
			Assert.That(File.Exists(fourxPath), Is.True, $"Missing 4X profile for {folder}.");
			Assert.That(File.Exists(readmePath), Is.True, $"Missing README for {folder}.");

			using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
			JsonElement panels = manifest.RootElement.GetProperty("panels");
			Assert.That(panels.GetArrayLength(), Is.EqualTo(1), $"{folder} must declare exactly one panel.");
			Assert.That(
				panels[0].GetProperty("panelType").GetString(),
				Is.EqualTo(panelType),
				$"{folder} must focus on '{panelType}'.");

			using JsonDocument rts = JsonDocument.Parse(File.ReadAllText(rtsPath));
			using JsonDocument fourx = JsonDocument.Parse(File.ReadAllText(fourxPath));
			Assert.That(rts.RootElement.GetProperty("genre").GetString(), Is.EqualTo("rts"));
			Assert.That(fourx.RootElement.GetProperty("genre").GetString(), Is.EqualTo("fourx"));
			Assert.That(rts.RootElement.TryGetProperty("tokens", out _), Is.True);
			Assert.That(fourx.RootElement.TryGetProperty("tokens", out _), Is.True);
			Assert.That(rts.RootElement.TryGetProperty("coachLine", out _), Is.True);
			Assert.That(fourx.RootElement.TryGetProperty("coachLine", out _), Is.True);
		}
	}

	[Test]
	public void MegaBrowserPanelKitFamily_IsNotPresent()
	{
		string mega = ResolveRepoPath(Path.Combine("mods", "showcases", "browser_panel_kit"));
		Assert.That(Directory.Exists(mega), Is.False, "browser_panel_kit mega-showcase must not remain.");

		string launcherConfig = File.ReadAllText(ResolveRepoPath("launcher.config.json"));
		string launcherPresets = File.ReadAllText(ResolveRepoPath("launcher.presets.json"));
		Assert.That(launcherConfig, Does.Not.Contain("panel_kit_rts_showcase"));
		Assert.That(launcherConfig, Does.Not.Contain("panel_kit_fourx_showcase"));
		Assert.That(launcherConfig, Does.Not.Contain("browser_panel_kit"));
		Assert.That(launcherPresets, Does.Not.Contain("panel_kit_rts_cef_raylib"));
		Assert.That(launcherPresets, Does.Not.Contain("panel_kit_fourx_cef_raylib"));
	}

	[Test]
	public void MinimapWebShell_UsesDedicatedShowcaseAndNativeHotPath()
	{
		string familyDoc = File.ReadAllText(ResolveRepoPath("docs/architecture/webui_panel_kit_showcase_family.md"));
		Assert.That(
			Directory.Exists(ResolveRepoPath(Path.Combine(
				"mods",
				"showcases",
				"browser_minimap_composited_overlay",
				"BrowserMinimapCompositedOverlayShowcaseMod"))),
			Is.True);
		Assert.That(
			File.Exists(ResolveRepoPath(Path.Combine(
				"mods",
				"showcases",
				"browser_minimap_composited_overlay",
				"BrowserMinimapCompositedOverlayShowcaseMod",
				"Assets",
				"panel-kit",
				"minimap_panel_manifest.json"))),
			Is.True);
		Assert.That(familyDoc, Does.Contain("browser_minimap_composited_overlay"));
		Assert.That(familyDoc, Does.Contain("Core/Skia"));
		Assert.That(familyDoc, Does.Contain("panel_kit_resource_showcase"));
		Assert.That(familyDoc, Does.Contain("panel_kit_techtree_progression_showcase"));
		Assert.That(familyDoc, Does.Not.Contain("blocked by #607"));
	}

	[Test]
	public void GenericPanelKitLibrary_DoesNotHardcodeShowcaseDisplayNouns()
	{
		string[] forbidden =
		[
			"Ore", "Power", "Supply", "Scout", "Depot", "Militia",
			"Influence", "Authority", "Colony Charter", "Orbital Yard", "Foundations"
		];

		string[] sources =
		[
			"src/Libraries/Ludots.WebUI.PanelKit/WebUiPanelKitContracts.cs",
			"src/Libraries/Ludots.WebUI.PanelKit/WebUiPanelKitSampleCatalog.cs",
			"src/Libraries/Ludots.WebUI.PanelKit/WebUiTechTreeContracts.cs",
			"src/Libraries/Ludots.WebUI.PanelKit/WebUiTechTreeSampleCatalog.cs"
		];

		foreach (string relative in sources)
		{
			string text = File.ReadAllText(ResolveRepoPath(relative));
			foreach (string noun in forbidden)
			{
				Assert.That(text, Does.Not.Contain(noun), $"{relative} must not hardcode '{noun}'.");
			}
		}
	}

	[Test]
	public void SampleManifest_StillLoadsSixPanels_WithoutGameFlavor()
	{
		var registered = new HashSet<string>(WebUiPanelKitSampleCatalog.SampleTopics, StringComparer.Ordinal);
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(registered.Contains);
		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(
			WebUiPanelKitSampleCatalog.SampleManifestPath(),
			catalog);

		Assert.That(manifest.Panels, Has.Count.EqualTo(6));
		Assert.That(manifest.Panels.Select(p => p.PanelType), Does.Contain("notification"));
		Assert.That(manifest.Panels.Select(p => p.PanelType), Does.Contain("production-overview"));
		Assert.That(manifest.Panels.Select(p => p.PanelType), Does.Contain("techtree"));
	}

	private static string ResolveRepoPath(string relative)
	{
		string dir = TestContext.CurrentContext.TestDirectory;
		for (int i = 0; i < 12; i++)
		{
			string candidate = Path.GetFullPath(Path.Combine(dir, relative));
			if (File.Exists(candidate) || Directory.Exists(candidate))
			{
				return candidate;
			}

			string? parent = Directory.GetParent(dir)?.FullName;
			if (parent == null)
			{
				break;
			}

			dir = parent;
		}

		return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, relative));
	}
}
