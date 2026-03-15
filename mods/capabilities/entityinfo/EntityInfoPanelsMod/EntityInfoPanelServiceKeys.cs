using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod;

public static class EntityInfoPanelServiceKeys
{
    public static readonly ServiceKey<EntityInfoPanelService> Service =
        new("EntityInfoPanelsMod.Service");

    public static readonly ServiceKey<EntityInfoPanelHandleStore> HandleStore =
        new("EntityInfoPanelsMod.HandleStore");
}
