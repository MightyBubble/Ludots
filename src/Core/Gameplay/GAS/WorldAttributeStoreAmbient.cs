using System;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// 世界属性列存的进程级绑定（照 ModRegistryAmbient 形状）。单 World 引擎（GameEngine）
    /// 在 InitializeCoreSystems 装载期 Bind 一次；测试用 Bind/Reset 做隔离。
    /// null 容忍：未绑定（纯单元测试/旧路径）时高槽位通道不可用，调用方按需 fail-closed。
    /// </summary>
    public static class WorldAttributeStoreAmbient
    {
        private static WorldAttributeStore? _current;

        public static WorldAttributeStore? Current => _current;

        public static void Bind(WorldAttributeStore store)
        {
            _current = store ?? throw new ArgumentNullException(nameof(store));
        }

        public static void Reset()
        {
            _current = null;
        }
    }
}
