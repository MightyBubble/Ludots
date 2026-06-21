using System.Text;
using System.Text.Json;
using System.Reflection;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiDataPlaneContractTests
{
	[Test]
	public void DataPlaneAssembly_DoesNotReferenceConcreteBrowserOrAdapterStacks()
	{
		string assemblyPath = typeof(IWebUiDataTransport).Assembly.Location;
		byte[] bytes = File.ReadAllBytes(assemblyPath);

		Assert.That(ContainsAscii(bytes, "CefSharp"), Is.False);
		Assert.That(ContainsAscii(bytes, "Raylib"), Is.False);
		Assert.That(ContainsAscii(bytes, "SkiaSharp"), Is.False);
		Assert.That(ContainsAscii(bytes, "Unreal"), Is.False);
		Assert.That(ContainsAscii(bytes, "UE5"), Is.False);
		Assert.That(ContainsAscii(bytes, "BLUI"), Is.False);
	}

	[Test]
	public void WebUiContextKeys_OnlyExposeTheFactoryKey()
	{
		Type type = typeof(Ludots.WebUI.WebUIContextKeys);

		Assert.That(type.GetField("BridgeFactory", BindingFlags.Public | BindingFlags.Static), Is.Not.Null);
		Assert.That(type.GetField("Bridge", BindingFlags.Public | BindingFlags.Static), Is.Null);
	}

	[Test]
	public void ControlEnvelope_RoundTripsRequiredFields_AndRejectsUnknownSchemaVersion()
	{
		WebUiControlEnvelope envelope = WebUiDataPlaneProtocol.CreateControlEnvelope(
			"session-a",
			42,
			"subscribe",
			"topic.units",
			new { window = new { start = 0, count = 64 } });

		byte[] bytes = WebUiDataPlaneProtocol.SerializeControlEnvelope(envelope);

		Assert.That(WebUiDataPlaneProtocol.TryParseControlEnvelope(bytes, out WebUiControlEnvelope parsed, out string error), Is.True, error);
		Assert.That(parsed.SchemaVersion, Is.EqualTo(WebUiDataPlaneProtocol.CurrentSchemaVersion));
		Assert.That(parsed.SessionId, Is.EqualTo("session-a"));
		Assert.That(parsed.RequestId, Is.EqualTo(42));
		Assert.That(parsed.Kind, Is.EqualTo("subscribe"));
		Assert.That(parsed.Topic, Is.EqualTo("topic.units"));
		Assert.That(parsed.Payload.GetProperty("window").GetProperty("count").GetInt32(), Is.EqualTo(64));

		byte[] future = JsonSerializer.SerializeToUtf8Bytes(parsed with { SchemaVersion = 999 }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		Assert.That(WebUiDataPlaneProtocol.TryParseControlEnvelope(future, out _, out error), Is.False);
		Assert.That(error, Does.Contain("schema version 999"));
	}

	[Test]
	public void TransportCapabilities_DescribeStringBinarySharedMemoryAndPacketBudget()
	{
		var stringOnly = WebUiTransportCapabilities.StringBridge(maxPacketBytes: 4096);
		var native = new WebUiTransportCapabilities(
			SupportsBinary: true,
			SupportsSharedMemory: true,
			SupportsReliableOrdered: true,
			SupportsLatestWins: true,
			MaxPacketBytes: 16 * 1024 * 1024);

		Assert.That(stringOnly.SupportsBinary, Is.False);
		Assert.That(stringOnly.SupportsSharedMemory, Is.False);
		Assert.That(stringOnly.SupportsReliableOrdered, Is.True);
		Assert.That(stringOnly.SupportsLatestWins, Is.True);
		Assert.That(stringOnly.MaxPacketBytes, Is.EqualTo(4096));
		Assert.That(native.SupportsBinary, Is.True);
		Assert.That(native.SupportsSharedMemory, Is.True);
	}

	[Test]
	public void PacketKinds_MarkCommandReliableOrdered_AndStateLatestWins()
	{
		var command = new WebUiOutboundPacket(
			"s",
			"orders",
			WebUiPacketKind.Command,
			WebUiDeliverySemantics.ReliableOrdered,
			Encoding.UTF8.GetBytes("{}"));
		var delta = command with
		{
			Topic = "world.units",
			Kind = WebUiPacketKind.Delta,
			Delivery = WebUiDeliverySemantics.LatestWins
		};

		Assert.That(command.Delivery, Is.EqualTo(WebUiDeliverySemantics.ReliableOrdered));
		Assert.That(delta.Delivery, Is.EqualTo(WebUiDeliverySemantics.LatestWins));
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
