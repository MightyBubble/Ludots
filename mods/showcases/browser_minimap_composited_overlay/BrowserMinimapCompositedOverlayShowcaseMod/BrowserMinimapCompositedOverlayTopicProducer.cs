using System.Text.Json;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

internal sealed class BrowserMinimapCompositedOverlayTopicProducer : IWebUiTopicProducer
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public string Topic => BrowserMinimapCompositedOverlayPanelKitIds.Topic;

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
			new
			{
				manifestId = BrowserMinimapCompositedOverlayPanelKitIds.ManifestId,
				panelId = BrowserMinimapCompositedOverlayPanelKitIds.PanelId,
				panelType = BrowserMinimapCompositedOverlayPanelKitIds.PanelType,
				commands = new[] { BrowserMinimapCompositedOverlayPanelKitIds.FocusMinimapCommand },
				webOwns = new[] { "panel-frame", "drag-handle", "focus-click" },
				nativeOwns = new[] { "marker-projection", "skia-clip", "camera-jump" }
			},
			JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			"application/json",
			context.RequestId);
		return true;
	}
}
