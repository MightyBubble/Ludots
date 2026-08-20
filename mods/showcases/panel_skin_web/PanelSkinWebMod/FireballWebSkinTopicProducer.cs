using System.Text.Json;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.WebUI.DataPlane;

namespace PanelSkinWebMod;

/// <summary>
/// Publishes the live fireball status panel values as a LatestWins DataPlane topic
/// snapshot. Values come exclusively from PanelHost projection; no parallel store.
/// </summary>
internal sealed class FireballWebSkinTopicProducer : IWebUiTopicProducer
{
    private const string TemplateId = "panel.fireball.status";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PanelHost _panelHost;

    public FireballWebSkinTopicProducer(PanelHost panelHost)
    {
        _panelHost = panelHost ?? throw new ArgumentNullException(nameof(panelHost));
    }

    public string Topic => "ludots.showcase.fireball.status";

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        PanelInstanceHandle handle = FindPanelInstance();
        PanelVariableSet values = null!;
        bool ready = handle != default && _panelHost.TryGetValues(handle, out values);
        object payload = ready
            ? new
            {
                ready = true,
                health = values.Get("health"),
                healthBase = values.Get("healthBase"),
                mana = values.Get("mana"),
                manaBase = values.Get("manaBase"),
                attack = values.Get("attack")
            }
            : new { ready = false };

        packet = new WebUiOutboundPacket(
            context.SessionId,
            Topic,
            WebUiPacketKind.Snapshot,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions),
            "application/json",
            context.RequestId);
        return true;
    }

    private PanelInstanceHandle FindPanelInstance()
    {
        foreach (PanelHostInstanceInfo info in _panelHost.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, TemplateId, System.StringComparison.Ordinal))
            {
                return info.Handle;
            }
        }

        return default;
    }
}
