using Arch.Core;
using Ludots.Core.Config;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorAuthoredEntity
{
    public required string InstanceId { get; init; }
    public required EntitySpawnData SpawnData { get; init; }
    public Entity Entity { get; set; } = Entity.Null;
    public int ReceiptId { get; set; }
    public bool Removed { get; set; }
}
