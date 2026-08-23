using System;
using System.Collections.Generic;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// shaderKey → 车道着色程序的注册表。材质经 shaderKey 选择着色行为；
    /// 未注册的 key 在分派时 fail-loud。注册方负责着色程序的生命周期（卸载）。
    /// </summary>
    public sealed class RaylibShaderCatalog
    {
        private readonly Dictionary<string, RaylibLaneShader> _instancingByKey = new(StringComparer.Ordinal);

        /// <summary>注册实例化合批车道可用的着色程序（必须满足 instancing 接线契约）。</summary>
        public void RegisterInstancing(string shaderKey, RaylibLaneShader laneShader)
        {
            if (string.IsNullOrWhiteSpace(shaderKey))
            {
                throw new ArgumentException("shaderKey must not be empty.", nameof(shaderKey));
            }

            if (!_instancingByKey.TryAdd(shaderKey, laneShader))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibShaderCatalog)} shaderKey '{shaderKey}' is already registered.");
            }
        }

        public RaylibLaneShader RequireInstancing(string shaderKey)
        {
            if (!_instancingByKey.TryGetValue(shaderKey, out RaylibLaneShader? laneShader))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibShaderCatalog)} has no instancing shader registered for shaderKey '{shaderKey}'.");
            }

            return laneShader;
        }

        public IEnumerable<RaylibLaneShader> InstancingShaders => _instancingByKey.Values;
    }

    /// <summary>默认 shaderKey 的单一来源转发，避免车道代码散落字符串字面量。</summary>
    public static class RaylibShaderKeys
    {
        public const string Lit = MaterialAssetDescriptor.DefaultShaderKey;
    }
}
