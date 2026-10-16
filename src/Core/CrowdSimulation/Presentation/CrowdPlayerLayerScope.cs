using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 模板层和半径窗只决定之后的框选、全选和空选择下令还看不看得见这些单位。
/// 不删单位，也不暂停。已经选中的成员不在这里重滤。
/// </summary>
public static class CrowdPlayerLayerScope
{
    public static bool Included(bool templateEnabled, bool radiusInRange) => templateEnabled && radiusInRange;

    public static void Apply(Arch.Core.World world, Entity entity, int tagId, bool included)
    {
        if (!world.Has<GameplayTagContainer>(entity))
        {
            if (included) return;
            world.Add(entity, new GameplayTagContainer());
        }

        ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(entity);
        bool tagged = tags.HasTag(tagId);
        if (included)
        {
            if (tagged) tags.RemoveTag(tagId);
        }
        else if (!tagged)
        {
            tags.AddTag(tagId);
        }
    }
}
