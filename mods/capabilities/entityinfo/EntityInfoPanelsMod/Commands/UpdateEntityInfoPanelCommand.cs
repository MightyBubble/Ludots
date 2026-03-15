using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Commands;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod.Commands;

public sealed class UpdateEntityInfoPanelCommand : GameCommand
{
    public string HandleSlotKey { get; init; } = string.Empty;
    public string ContextEntityKey { get; init; } = string.Empty;
    public bool? Visible { get; init; }
    public EntityInfoPanelLayout? Layout { get; init; }
    public EntityInfoPanelTarget? Target { get; init; }
    public EntityInfoGasDetailFlags? GasDetailFlags { get; init; }

    public override Task ExecuteAsync(ScriptContext context)
    {
        EntityInfoPanelService? service = context.Get(EntityInfoPanelServiceKeys.Service);
        EntityInfoPanelHandleStore? handles = context.Get(EntityInfoPanelServiceKeys.HandleStore);
        if (service == null || handles == null || !handles.TryGet(HandleSlotKey, out EntityInfoPanelHandle handle))
        {
            return Task.CompletedTask;
        }

        if (Visible.HasValue)
        {
            service.SetVisible(handle, Visible.Value);
        }

        if (Layout.HasValue)
        {
            service.UpdateLayout(handle, Layout.Value);
        }

        EntityInfoPanelTarget? resolvedTarget = Target;
        if (!string.IsNullOrWhiteSpace(ContextEntityKey))
        {
            Entity entity = context.Get<Entity>(ContextEntityKey);
            if (entity != Entity.Null)
            {
                resolvedTarget = EntityInfoPanelTarget.Fixed(entity);
            }
        }

        if (resolvedTarget.HasValue)
        {
            service.UpdateTarget(handle, resolvedTarget.Value);
        }

        if (GasDetailFlags.HasValue)
        {
            service.UpdateGasDetailFlags(handle, GasDetailFlags.Value);
        }

        return Task.CompletedTask;
    }
}
