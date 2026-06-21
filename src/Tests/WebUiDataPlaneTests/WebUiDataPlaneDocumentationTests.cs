using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiDataPlaneDocumentationTests
{
	[Test]
	public void BrowserRuntimeDocs_RecordDataPlaneAsHigherLayer_NotBrowserSurfaceResponsibility()
	{
		string docs = ReadRepoFile("docs", "architecture", "browser_ui_runtime.md");

		Assert.That(docs, Does.Contain("Ludots.WebUI` owns the WebUI DataPlane"));
		Assert.That(docs, Does.Contain("The DataPlane is not another browser runtime"));
		Assert.That(docs, Does.Contain("`IBrowserMessageBridge` is only the browser transport"));
		Assert.That(docs, Does.Contain("`EntityCollectionStore`"));
		Assert.That(docs, Does.Contain("`MinimapMarkerBuffer`"));
	}

	[Test]
	public void AdrRecordsExternalUe5Ownership_AndDualTransportPath()
	{
		string adr = ReadRepoFile("docs", "adr", "ADR-0003-browser-ui-runtime-contract.md");

		Assert.That(adr, Does.Contain("external UE5 BLUI"));
		Assert.That(adr, Does.Contain("UE5 BLUI remains an adapter concern"));
		Assert.That(adr, Does.Contain("Ludots-started CEF"));
		Assert.That(adr, Does.Contain("two transport paths"));
	}

	[Test]
	public void DataPlaneArchitectureDocs_ForbidBrowserAdaptersFromOwningGameplaySemantics()
	{
		string docs = ReadRepoFile("docs", "architecture", "webui_dataplane_architecture.md");

		Assert.That(docs, Does.Contain("Do not create a parallel WebUI entity-list store"));
		Assert.That(docs, Does.Contain("must not become selection truth"));
		Assert.That(docs, Does.Contain("Browser providers must not introduce new gameplay truth"));
		Assert.That(docs, Does.Contain("UE5 BLUI is therefore a reference transport shape, not a Core dependency"));
	}

	private static string ReadRepoFile(params string[] segments)
	{
		string current = TestContext.CurrentContext.TestDirectory;
		while (!string.IsNullOrEmpty(current))
		{
			string candidate = Path.Combine(new[] { current }.Concat(segments).ToArray());
			if (File.Exists(candidate))
			{
				return File.ReadAllText(candidate);
			}

			current = Directory.GetParent(current)?.FullName ?? string.Empty;
		}

		throw new FileNotFoundException("Could not find repository file.", Path.Combine(segments));
	}
}
