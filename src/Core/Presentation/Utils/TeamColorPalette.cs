using System;
using System.Globalization;
using System.Numerics;

namespace Ludots.Core.Presentation.Utils
{
    /// <summary>
    /// 玩家调色板:按 PlayerId(1 基)取色的呈现层数据。由场景宿主按地图/配置注册成服务
    /// (CoreServiceKeys.TeamColorPalette),PresenterBehaviorSystem 的 EntityColor/EntityColorVector
    /// 车道优先读它;未注册时回退 TeamColorResolver 的双色行为,旧场景零影响。
    /// </summary>
    public sealed class TeamColorPalette
    {
        private readonly Vector4[] _byPlayerId;

        public TeamColorPalette(Vector4[] byPlayerId)
        {
            ArgumentNullException.ThrowIfNull(byPlayerId);
            _byPlayerId = byPlayerId;
        }

        public bool TryGet(int playerId, out Vector4 color)
        {
            if (playerId > 0 && playerId < _byPlayerId.Length)
            {
                color = _byPlayerId[playerId];
                return true;
            }

            color = default;
            return false;
        }

        /// <summary>#rrggbb / #rrggbbaa;非法输入抛fail-fast(配置即契约)。</summary>
        public static Vector4 ParseHex(string hex, string context)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#')
            {
                throw new InvalidOperationException($"{context}: 颜色必须是 #rrggbb 形式,实为 '{hex}'。");
            }

            ReadOnlySpan<char> s = hex.AsSpan(1);
            if (s.Length != 6 && s.Length != 8)
            {
                throw new InvalidOperationException($"{context}: 颜色必须是 #rrggbb 形式,实为 '{hex}'。");
            }

            static byte Channel(ReadOnlySpan<char> span, int offset) =>
                byte.Parse(span.Slice(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            float r = Channel(s, 0) / 255f, g = Channel(s, 2) / 255f, b = Channel(s, 4) / 255f;
            float a = s.Length == 8 ? Channel(s, 6) / 255f : 1f;
            return new Vector4(r, g, b, a);
        }
    }
}
