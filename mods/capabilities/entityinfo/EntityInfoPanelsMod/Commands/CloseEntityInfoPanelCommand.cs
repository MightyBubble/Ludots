using System.Threading.Tasks;
using Ludots.Core.Commands;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod.Commands;

public sealed class CloseEntityInfoPanelCommand : GameCommand
{
    public string HandleSlotKey { get; init; } = string.Empty;

    public override Task ExecuteAsync(ScriptContext context)
    {
        EntityInfoPanelService? service = context.Get(EntityInfoPanelServiceKeys.Service);
        EntityInfoPanelHandleStore? handles = context.Get(EntityInfoPanelServiceKeys.HandleStore);
        if (service == null || handles == null)
        {
            return Task.CompletedTask;
        }

        if (handles.TryGet(HandleSlotKey, out EntityInfoPanelHandle handle))
        {
            service.Close(handle);
            handles.Remove(HandleSlotKey);
        }

        return Task.CompletedTask;
    }
}
