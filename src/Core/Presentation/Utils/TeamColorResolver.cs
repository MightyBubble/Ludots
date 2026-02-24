using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;

namespace Ludots.Core.Presentation.Utils
{
    /// <summary>
    /// 通用的队伍/玩家颜色解析。
    /// 优先读取 Team 组件，回退到 PlayerOwner。
    /// </summary>
    public static class TeamColorResolver
    {
        public static readonly Vector4 Team1Color = new Vector4(0.2f, 0.9f, 0.2f, 1f);
        public static readonly Vector4 Team2Color = new Vector4(0.9f, 0.2f, 0.2f, 1f);
        public static readonly Vector4 DefaultColor = new Vector4(1f, 1f, 1f, 1f);

        /// <summary>
        /// 根据实体的 Team 或 PlayerOwner 组件返回队伍颜色。
        /// localTeamId 为本地玩家的队伍 ID（通常为 1），对应 Team1Color。
        /// </summary>
        public static Vector4 Resolve(World world, Entity entity, int localTeamId = 1)
        {
            if (world.TryGet(entity, out Team team))
            {
                return team.Id == localTeamId ? Team1Color : Team2Color;
            }
            if (world.TryGet(entity, out PlayerOwner owner))
            {
                return owner.PlayerId == localTeamId ? Team1Color : Team2Color;
            }
            return DefaultColor;
        }
    }
}
