using System;
using Arch.Core;

namespace Ludots.Core.Gameplay.Spawning;

/// <summary>
/// 批车道物化之后的逐实例写入。组件必须已经在 <see cref="AppendSignature"/> 放进原型，
/// <see cref="WriteChunk"/> 只改 chunk 里的值，不再 Add/Remove。
/// 单条车道没有这张原型时，<see cref="ApplyEntity"/> 可以 Add。
/// 这不是第二条物化：实体仍由 <see cref="RuntimeEntitySpawnSystem"/> 创建。
/// </summary>
public interface IRuntimeSpawnInstanceInitializer
{
    /// <summary>
    /// 把本初始化器要写的组件并进批原型。签名加法会去掉重复类型。
    /// </summary>
    Signature AppendSignature(Signature signature);

    void WriteChunk(World world, ReadOnlySpan<Entity> created, ReadOnlySpan<int> instanceIndices);

    void ApplyEntity(World world, Entity entity, int instanceIndex);
}
