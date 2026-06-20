using NUnit.Framework;
using System.Text;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserContractDependencyTests
{
	[Test]
	public void BrowserContracts_DoNotReferenceSkiaOrEngineAdapters()
	{
		string assemblyPath = typeof(Ludots.UI.Browser.IBrowserRuntime).Assembly.Location;
		byte[] bytes = File.ReadAllBytes(assemblyPath);

		Assert.That(ContainsAscii(bytes, "SkiaSharp"), Is.False);
		Assert.That(ContainsAscii(bytes, "Ludots.UI.Skia"), Is.False);
		Assert.That(ContainsAscii(bytes, "Unreal"), Is.False);
		Assert.That(ContainsAscii(bytes, "UE5"), Is.False);
		Assert.That(ContainsAscii(bytes, "CEF"), Is.False);
		Assert.That(ContainsAscii(bytes, "Ludots.WebUI"), Is.False);
	}

	private static bool ContainsAscii(byte[] haystack, string needle)
	{
		byte[] needleBytes = Encoding.ASCII.GetBytes(needle);
		for (int i = 0; i <= haystack.Length - needleBytes.Length; i++)
		{
			bool matched = true;
			for (int j = 0; j < needleBytes.Length; j++)
			{
				if (haystack[i + j] != needleBytes[j])
				{
					matched = false;
					break;
				}
			}

			if (matched)
			{
				return true;
			}
		}

		return false;
	}
}
