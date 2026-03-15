using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Commands;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod.Commands;

public sealed class OpenEntityInfoPanelCommand : GameCommand
{
    public string HandleSlotKey { get; init; } = string.Empty;
    public string ContextEntityKey { get; init; } = string.Empty;
    public EntityInfoPanelRequest Request { get; init; }

    public override Task ExecuteAsync(ScriptContext context)
    {
        EntityInfoPanelService? service = context.Get(EntityInfoPanelServiceKeys.Service);
        EntityInfoPanelHandleStore? handles = context.Get(EntityInfoPanelServiceKeys.HandleStore);
        if (service == null || handles == null)
        {
            return Task.CompletedTask;
        }

        EntityInfoPanelRequest request = Request;
        if (!string.IsNullOrWhiteSpace(ContextEntityKey))
        {
            Entity entity = context.Get<Entity>(ContextEntityKey);
            if (entity != Entity.Null)
            {
                request = request with { Target = EntityInfoPanelTarget.Fixed(entity) };
            }
        }

        EntityInfoPanelHandle handle = service.Open(in request);
        if (handle.IsValid && !string.IsNullOrWhiteSpace(HandleSlotKey))
        {
            handles.Set(HandleSlotKey, handle);
        }

        return Task.CompletedTask;
    }
}
