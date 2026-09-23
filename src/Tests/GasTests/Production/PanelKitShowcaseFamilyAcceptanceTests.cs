using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
public sealed class PanelKitShowcaseFamilyAcceptanceTests
{
	private const string FamilyRelativePath = "mods/showcases/panel_kit_profiles/family.json";

	private static readonly string[] RequiredCategories =
	[
		"resource-attribute",
		"command-deck",
		"production-overview",
		"tooltip",
		"quest-objective",
		"notification",
		"minimap-web-shell",
		"techtree-progression"
	];

	private static readonly string[] ForbiddenGenericPanelKitTokens =
	[
		"Minerals",
		"Vespene",
		"Spice",
		"Infantry",
		"Marine",
		"Stellaris",
		"群星",
		"Age of Empires",
		"StarCraft",
		"TechTreeStore"
	];

	[Test]
	public void Family_EnumeratesOneShowcasePerPanelCategory()
	{
		string repoRoot = FindRepoRoot();
		using JsonDocument family = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, FamilyRelativePath)));
		JsonElement showcases = family.RootElement.GetProperty("showcases");
		string[] categories = showcases.EnumerateArray()
			.Select(item => item.GetProperty("category").GetString() ?? string.Empty)
			.ToArray();

		Assert.That(categories, Is.EquivalentTo(RequiredCategories));
		Assert.That(categories, Is.Unique);
		Assert.That(showcases.GetArrayLength(), Is.EqualTo(RequiredCategories.Length));
	}

	[Test]
	public void EachShowcase_HasManifestProfileTopicsAndPlayerFacingUat()
	{
		string repoRoot = FindRepoRoot();
		using JsonDocument family = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, FamilyRelativePath)));
		string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));

		foreach (JsonElement showcase in family.RootElement.GetProperty("showcases").EnumerateArray())
		{
			string id = showcase.GetProperty("id").GetString()!;
			string relativePath = showcase.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar);
			string showcaseRoot = Path.Combine(repoRoot, relativePath);
			string manifestPath = Path.Combine(showcaseRoot, showcase.GetProperty("manifestPath").GetString()!.Replace('/', Path.DirectorySeparatorChar));
			string profilePath = Path.Combine(showcaseRoot, showcase.GetProperty("profilePath").GetString()!.Replace('/', Path.DirectorySeparatorChar));
			string uatPath = Path.Combine(showcaseRoot, showcase.GetProperty("uatPath").GetString()!.Replace('/', Path.DirectorySeparatorChar));
			string launcherBinding = showcase.GetProperty("launcherBinding").GetString()!;
			string[] topics = showcase.GetProperty("topics").EnumerateArray()
				.Select(item => item.GetString() ?? string.Empty)
				.ToArray();

			Assert.That(Directory.Exists(showcaseRoot), Is.True, $"Missing showcase root for '{id}'.");
			Assert.That(File.Exists(manifestPath), Is.True, $"Missing WPK manifest for '{id}'.");
			Assert.That(File.Exists(profilePath), Is.True, $"Missing showcase profile for '{id}'.");
			Assert.That(File.Exists(uatPath), Is.True, $"Missing UAT doc for '{id}'.");
			Assert.That(topics, Is.Not.Empty, $"Showcase '{id}' must declare topics.");
			Assert.That(topics, Is.All.Not.Null.And.Not.Empty);
			Assert.That(launcherBinding, Is.Not.Null.And.Not.Empty);

			using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
			using JsonDocument profile = JsonDocument.Parse(File.ReadAllText(profilePath));
			JsonElement panels = manifest.RootElement.GetProperty("panels");
			Assert.That(panels.GetArrayLength(), Is.EqualTo(1), $"Showcase '{id}' must stay single-panel, not a mega dashboard.");

			JsonElement panel = panels.EnumerateArray().Single();
			string panelTopic = panel.GetProperty("topic").GetString()!;
			Assert.That(topics, Does.Contain(panelTopic), $"Family topics for '{id}' must include manifest topic '{panelTopic}'.");
			Assert.That(profile.RootElement.GetProperty("topic").GetString(), Is.EqualTo(panelTopic));
			Assert.That(profile.RootElement.GetProperty("profileId").GetString(), Is.EqualTo(panel.GetProperty("profileId").GetString()));
			Assert.That(manifest.RootElement.GetProperty("manifestId").GetString(), Is.Not.Null.And.Not.Empty);
			Assert.That(manifest.RootElement.GetProperty("hostOwnerId").GetString(), Is.Not.Null.And.Not.Empty);

			string uat = File.ReadAllText(uatPath);
			Assert.That(uat, Does.Contain("Feature:"));
			Assert.That(uat, Does.Contain("Scenario:"));
			Assert.That(uat, Does.Contain("Given "));
			Assert.That(uat, Does.Contain("When "));
			Assert.That(uat, Does.Contain("Then "));
			Assert.That(Regex.IsMatch(uat, @"\bAnd\b"), Is.True, $"UAT for '{id}' should include And feedback steps.");

			if (string.Equals(showcase.GetProperty("category").GetString(), "minimap-web-shell", StringComparison.Ordinal))
			{
				Assert.That(launcherConfig, Does.Contain($"\"{launcherBinding}\""));
				string runtimeModPath = showcase.GetProperty("runtimeModPath").GetString()!.Replace('/', Path.DirectorySeparatorChar);
				Assert.That(Directory.Exists(Path.Combine(repoRoot, runtimeModPath)), Is.True);
			}
		}
	}

	[Test]
	public void GenericPanelKitCode_StaysGameAgnostic_AcrossShowcaseFamily()
	{
		string repoRoot = FindRepoRoot();
		string panelKitRoot = Path.Combine(repoRoot, "src", "Libraries", "Ludots.WebUI.PanelKit");
		IEnumerable<string> sources = Directory.EnumerateFiles(panelKitRoot, "*.cs", SearchOption.AllDirectories);
		foreach (string file in sources)
		{
			string text = File.ReadAllText(file);
			Assert.That(text, Does.Not.Contain("class TechTreeStore"));
			Assert.That(text, Does.Not.Contain("new TechTreeStore"));
			foreach (string token in ForbiddenGenericPanelKitTokens.Where(item => item != "TechTreeStore"))
			{
				Assert.That(text, Does.Not.Contain(token), $"{Path.GetFileName(file)} must not hardcode '{token}'.");
			}
		}

		using JsonDocument family = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, FamilyRelativePath)));
		foreach (JsonElement showcase in family.RootElement.GetProperty("showcases").EnumerateArray())
		{
			string relativePath = showcase.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar);
			string showcaseRoot = Path.Combine(repoRoot, relativePath);
			foreach (string file in Directory.EnumerateFiles(showcaseRoot, "*.json", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(file);
				Assert.That(text, Does.Not.Contain("class TechTreeStore"));
				Assert.That(text, Does.Not.Contain("new TechTreeStore"));
				Assert.That(text, Does.Not.Contain("\"Unknown\""));
			}
		}
	}

	[Test]
	public void Family_IsNotOneGiantAllInOneDashboard()
	{
		string repoRoot = FindRepoRoot();
		using JsonDocument family = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, FamilyRelativePath)));
		Assert.That(family.RootElement.GetProperty("showcases").GetArrayLength(), Is.GreaterThan(1));

		foreach (JsonElement showcase in family.RootElement.GetProperty("showcases").EnumerateArray())
		{
			string relativePath = showcase.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar);
			string manifestPath = Path.Combine(
				repoRoot,
				relativePath,
				showcase.GetProperty("manifestPath").GetString()!.Replace('/', Path.DirectorySeparatorChar));
			using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
			Assert.That(manifest.RootElement.GetProperty("panels").GetArrayLength(), Is.EqualTo(1));
		}
	}

	private static string FindRepoRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
			    Directory.Exists(Path.Combine(current.FullName, "src")) &&
			    Directory.Exists(Path.Combine(current.FullName, "mods")))
			{
				return current.FullName;
			}

			current = current.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
	}
}
