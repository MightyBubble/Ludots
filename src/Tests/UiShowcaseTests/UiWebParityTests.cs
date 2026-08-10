using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiWebParityTests
{
	private const float TolerancePx = 2.5f;

	private static readonly string FixtureDir = FindFixtureDir();

	private static readonly HashSet<string> FontMetricSensitiveIds = new HashSet<string>(StringComparer.Ordinal)
	{
		"rail-title",
		"rail-body",
		"tip-body",
		"menu-subtitle"
	};

	[Test]
	public void SameHtml_MatchesChromeLayout_AcrossDesktopTabletPhone()
	{
		string html = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.html"));
		string css = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.css"));
		string goldenJson = File.ReadAllText(Path.Combine(FixtureDir, "chrome-layout.golden.json"));
		ChromeParityGolden golden = JsonSerializer.Deserialize<ChromeParityGolden>(goldenJson, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		}) ?? throw new InvalidOperationException("chrome-layout.golden.json failed to deserialize.");

		UiScene scene = new UiMarkupLoader().LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		List<string> failures = new List<string>();

		foreach (ChromeParityViewport viewport in golden.Viewports)
		{
			scene.Layout(viewport.Width, viewport.Height);
			foreach (KeyValuePair<string, ChromeParityBox> pair in viewport.Boxes)
			{
				if (FontMetricSensitiveIds.Contains(pair.Key))
				{
					continue;
				}

				UiNode? node = scene.FindByElementId(pair.Key);
				if (node == null)
				{
					failures.Add($"{viewport.Name}/{pair.Key}: missing in Ludots scene");
					continue;
				}

				ChromeParityBox expected = pair.Value;
				UiRect actual = node.LayoutRect;
				if (!Within(actual.X, expected.X) ||
					!Within(actual.Y, expected.Y) ||
					!Within(actual.Width, expected.Width) ||
					!Within(actual.Height, expected.Height))
				{
					failures.Add(
						$"{viewport.Name}/{pair.Key}: Ludots=({actual.X:F1},{actual.Y:F1},{actual.Width:F1},{actual.Height:F1}) Chrome=({expected.X:F1},{expected.Y:F1},{expected.Width:F1},{expected.Height:F1})");
				}
			}
		}

		Assert.That(failures, Is.Empty, "Web↔Ludots layout parity failures:\n" + string.Join("\n", failures));
	}

	[Test]
	public void SameHtml_ResizeChangesShellGeometry_ButKeepsStructure()
	{
		string html = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.html"));
		string css = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.css"));
		UiScene scene = new UiMarkupLoader().LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);

		scene.Layout(1280f, 720f);
		UiRect desktopShell = scene.FindByElementId("menu-shell")!.LayoutRect;
		scene.Layout(390f, 844f);
		UiRect phoneShell = scene.FindByElementId("menu-shell")!.LayoutRect;

		Assert.That(scene.FindByElementId("menu-card"), Is.Not.Null);
		Assert.That(scene.FindByElementId("side-rail"), Is.Not.Null);
		Assert.That(scene.FindByElementId("btn-resume"), Is.Not.Null);
		Assert.That(phoneShell.Width, Is.LessThan(desktopShell.Width - 10f));
		Assert.That(phoneShell.Height, Is.GreaterThan(desktopShell.Height + 10f));
	}

	[Test]
	public void SameHtml_FlexButtons_AnonymousTextBoxIsCentered()
	{
		string html = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.html"));
		string css = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.css"));
		UiScene scene = new UiMarkupLoader().LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		scene.Layout(1280f, 720f);

		UiNode button = scene.FindByElementId("btn-resume")!;
		Assert.That(scene.FindByElementId("hero-crest"), Is.Not.Null);
		Assert.That(scene.FindByElementId("tip-card"), Is.Not.Null);
		Assert.That(button.Style.JustifyContent, Is.EqualTo(UiJustifyContent.Center));
		Assert.That(button.Style.AlignItems, Is.EqualTo(UiAlignItems.Center));

		UiNode? label = FindDescendantText(button, "继续冒险");
		Assert.That(label, Is.Not.Null, "primary button should expose centered label text");
		UiRect cluster = UnionChildBounds(button);
		float buttonCenterX = button.LayoutRect.X + button.LayoutRect.Width * 0.5f;
		float buttonCenterY = button.LayoutRect.Y + button.LayoutRect.Height * 0.5f;
		float clusterCenterX = cluster.X + cluster.Width * 0.5f;
		float clusterCenterY = cluster.Y + cluster.Height * 0.5f;
		Assert.That(Math.Abs(clusterCenterX - buttonCenterX), Is.LessThanOrEqualTo(TolerancePx));
		Assert.That(Math.Abs(clusterCenterY - buttonCenterY), Is.LessThanOrEqualTo(TolerancePx));
	}

	[Test]
	public void SameHtml_PhoneRail_GrowsTitleAndWrapsBody()
	{
		string html = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.html"));
		string css = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.css"));
		UiScene scene = new UiMarkupLoader().LoadScene(new WrappingTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		scene.Layout(390f, 844f);

		UiNode title = scene.FindByElementId("rail-title")!;
		UiNode body = scene.FindByElementId("rail-body")!;
		Assert.That(title.LayoutRect.Width, Is.LessThan(55f));
		Assert.That(title.LayoutRect.Height, Is.GreaterThan(40f), "narrow phone rail title must grow with wrapped CJK");
		Assert.That(body.LayoutRect.Y, Is.GreaterThan(title.LayoutRect.Bottom - 0.5f));
		Assert.That(body.LayoutRect.Height, Is.GreaterThan(100f));
	}

	[Test]
	public void SameHtml_StatChip_PadsAndStacksLabelAboveValue()
	{
		string html = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.html"));
		string css = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.css"));
		UiScene scene = new UiMarkupLoader().LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		scene.Layout(1280f, 720f);

		UiNode chip = scene.FindByElementId("stat-hp")!;
		Assert.That(chip.Children.Count, Is.EqualTo(2), "stat chip should stack readout row above meter");
		UiNode top = chip.Children[0];
		UiNode meter = chip.Children[1];
		Assert.That(top.LayoutRect.X, Is.GreaterThanOrEqualTo(chip.LayoutRect.X + chip.Style.Padding.Left + chip.Style.BorderWidth - 0.5f));
		Assert.That(top.LayoutRect.Y, Is.GreaterThanOrEqualTo(chip.LayoutRect.Y + chip.Style.Padding.Top + chip.Style.BorderWidth - 0.5f));
		Assert.That(meter.LayoutRect.Y, Is.GreaterThan(top.LayoutRect.Bottom - 0.5f));
		Assert.That(meter.LayoutRect.Height, Is.GreaterThanOrEqualTo(6f));
		float contentTop = chip.LayoutRect.Y + chip.Style.BorderWidth + chip.Style.Padding.Top;
		float contentBottom = chip.LayoutRect.Bottom - chip.Style.BorderWidth - chip.Style.Padding.Bottom;
		float stackTop = top.LayoutRect.Y;
		float stackBottom = meter.LayoutRect.Bottom;
		float topSlack = stackTop - contentTop;
		float bottomSlack = contentBottom - stackBottom;
		Assert.That(Math.Abs(topSlack - bottomSlack), Is.LessThanOrEqualTo(TolerancePx), "stat chip column should be vertically centered by justify-content");
	}

	[Test]
	public void SameHtml_AmbientMotion_AdvancesOrbOpacity()
	{
		string html = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.html"));
		string css = File.ReadAllText(Path.Combine(FixtureDir, "parity_menu.css"));
		UiScene scene = new UiMarkupLoader().LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		scene.Layout(1280f, 720f);

		UiNode orb = scene.FindByElementId("orb-a")!;
		float before = orb.RenderStyle.Opacity;
		Assert.That(scene.AdvanceTime(0.9f), Is.True, "ambient orb animation should dirty render style");
		float after = orb.RenderStyle.Opacity;
		Assert.That(Math.Abs(after - before), Is.GreaterThan(0.01f));
	}

	[Test]
	public void Showcase_ViewportChips_SwitchStageClass()
	{
		UiScene scene = UiShowcaseCoreMod.Showcase.UiShowcaseFactory.CreateWebParityShowcaseScene(
			new ConstantTextMeasurer(),
			new ConstantImageSizeProvider());
		scene.Layout(1280f, 720f);
		Assert.That(scene.FindByElementId("stage")!.HasClass("vp-desktop"), Is.True);

		UiNode tabletChip = scene.FindByElementId("vp-tablet")!;
		Assert.That(scene.Dispatch(new Ludots.UI.Runtime.Events.UiPointerEvent(
			Ludots.UI.Runtime.Events.UiPointerEventType.Click,
			0,
			tabletChip.LayoutRect.X + 2,
			tabletChip.LayoutRect.Y + 2,
			tabletChip.Id)).Handled, Is.True);
		scene.Layout(1280f, 720f);
		Assert.That(scene.FindByElementId("stage")!.HasClass("vp-tablet"), Is.True);
		Assert.That(scene.FindByElementId("preview-label")!.TextContent, Does.Contain("平板"));
	}

	private static bool Within(float actual, float expected) => Math.Abs(actual - expected) <= TolerancePx;

	private static UiNode? FindDescendantText(UiNode root, string expected)
	{
		if (string.Equals(root.TextContent, expected, StringComparison.Ordinal))
		{
			return root;
		}
		for (int i = 0; i < root.Children.Count; i++)
		{
			UiNode? match = FindDescendantText(root.Children[i], expected);
			if (match != null)
			{
				return match;
			}
		}
		return null;
	}

	private static UiRect UnionChildBounds(UiNode host)
	{
		Assert.That(host.Children.Count, Is.GreaterThan(0));
		float left = host.Children[0].LayoutRect.X;
		float top = host.Children[0].LayoutRect.Y;
		float right = host.Children[0].LayoutRect.Right;
		float bottom = host.Children[0].LayoutRect.Bottom;
		for (int i = 1; i < host.Children.Count; i++)
		{
			UiRect rect = host.Children[i].LayoutRect;
			left = Math.Min(left, rect.X);
			top = Math.Min(top, rect.Y);
			right = Math.Max(right, rect.Right);
			bottom = Math.Max(bottom, rect.Bottom);
		}
		return new UiRect(left, top, right - left, bottom - top);
	}

	private static string FindFixtureDir()
	{
		string? dir = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			string candidate = Path.Combine(dir, "fixtures", "ui-web-parity");
			if (File.Exists(Path.Combine(candidate, "chrome-layout.golden.json")))
			{
				return candidate;
			}
			dir = Directory.GetParent(dir)?.FullName;
		}

		string workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
		string fallback = Path.Combine(workspace, "fixtures", "ui-web-parity");
		if (File.Exists(Path.Combine(fallback, "chrome-layout.golden.json")))
		{
			return fallback;
		}

		throw new DirectoryNotFoundException("fixtures/ui-web-parity not found from test base directory.");
	}

	private sealed class ChromeParityGolden
	{
		public List<ChromeParityViewport> Viewports { get; set; } = new();
	}

	private sealed class ChromeParityViewport
	{
		public string Name { get; set; } = string.Empty;
		public float Width { get; set; }
		public float Height { get; set; }
		public Dictionary<string, ChromeParityBox> Boxes { get; set; } = new(StringComparer.Ordinal);
	}

	private sealed class ChromeParityBox
	{
		public float X { get; set; }
		public float Y { get; set; }
		public float Width { get; set; }
		public float Height { get; set; }
	}

	private sealed class ConstantTextMeasurer : IUiTextMeasurer
	{
		public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		{
			float width = (text?.Length ?? 0) * style.FontSize * 0.5f;
			float lineHeight = style.FontSize * 1.4f;
			return new UiTextLayoutResult(new[] { text ?? string.Empty }, width, lineHeight, lineHeight, style.FontSize, Math.Max(0f, lineHeight - style.FontSize));
		}

		public float MeasureWidth(string? text, UiStyle style) => (text?.Length ?? 0) * style.FontSize * 0.5f;
	}

	private sealed class WrappingTextMeasurer : IUiTextMeasurer
	{
		public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		{
			string value = text ?? string.Empty;
			float lineHeight = style.FontSize * 1.4f;
			if (!constrainWidth || availableWidth <= 0.01f || float.IsInfinity(availableWidth))
			{
				float width = MeasureWidth(value, style);
				return new UiTextLayoutResult(new[] { value }, width, lineHeight, lineHeight, style.FontSize, Math.Max(0f, lineHeight - style.FontSize));
			}

			List<string> lines = new List<string>();
			StringBuilder current = new StringBuilder();
			float currentWidth = 0f;
			foreach (Rune rune in value.EnumerateRunes())
			{
				float glyphWidth = GlyphWidth(rune, style.FontSize);
				if (current.Length > 0 && currentWidth + glyphWidth > availableWidth)
				{
					lines.Add(current.ToString());
					current.Clear();
					currentWidth = 0f;
				}
				current.Append(rune.ToString());
				currentWidth += glyphWidth;
			}
			if (current.Length > 0 || lines.Count == 0)
			{
				lines.Add(current.ToString());
			}
			float maxWidth = 0f;
			for (int i = 0; i < lines.Count; i++)
			{
				maxWidth = Math.Max(maxWidth, MeasureWidth(lines[i], style));
			}
			return new UiTextLayoutResult(lines.ToArray(), maxWidth, lineHeight * lines.Count, lineHeight, style.FontSize, Math.Max(0f, lineHeight - style.FontSize));
		}

		public float MeasureWidth(string? text, UiStyle style)
		{
			if (string.IsNullOrEmpty(text))
			{
				return 0f;
			}
			float width = 0f;
			foreach (Rune rune in text.EnumerateRunes())
			{
				width += GlyphWidth(rune, style.FontSize);
			}
			return width;
		}

		private static float GlyphWidth(Rune rune, float fontSize) => rune.Value > 0x7F ? fontSize : fontSize * 0.5f;
	}

	private sealed class ConstantImageSizeProvider : IUiImageSizeProvider
	{
		public bool TryGetSize(string? source, out float width, out float height)
		{
			width = 16f;
			height = 16f;
			return true;
		}
	}
}
