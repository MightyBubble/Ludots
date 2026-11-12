using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Arch.Core;

namespace Ludots.Core.Config;

/// <summary>
/// 批车道组件准入，归一个生成器实例所有。登记的类型用 <see cref="ComponentRegistry"/> 在临时世界上落一次 JSON，
/// 再把那份 struct 抄进批 chunk。JSON 仍是组件形状的唯一来源。探针不进调用方的世界。
/// <see cref="Generation"/> 变化后，生成器里已缓存的模板描述作废重算——登记可以晚于第一次查询。
/// 同名同类型再登记是空操作，给会话重载用；同名不同类型直接抛错。
/// </summary>
internal sealed class TemplateBatchComponentAdmission
{
    private readonly Dictionary<string, Admission> _byName = new(StringComparer.Ordinal);

    public int Generation { get; private set; }

    public void Register<T>(string componentName) where T : unmanaged
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new ArgumentException("Admitted batch component name is required.", nameof(componentName));
        }

        if (_byName.TryGetValue(componentName, out Admission? existing))
        {
            if (existing is Admission<T>)
            {
                return;
            }

            throw new InvalidOperationException($"SPAWN.BATCH.ERR.DuplicateAdmission: '{componentName}' is already admitted.");
        }

        _byName.Add(componentName, new Admission<T>(componentName));
        Generation++;
    }

    public bool Unregister(string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new ArgumentException("Admitted batch component name is required.", nameof(componentName));
        }

        if (!_byName.Remove(componentName))
        {
            return false;
        }

        Generation++;
        return true;
    }

    public bool IsAdmitted(string componentName)
    {
        return !string.IsNullOrWhiteSpace(componentName) && _byName.ContainsKey(componentName);
    }

    public AdmittedComponentSeed[] ProbeAll(
        string templateId,
        IReadOnlyDictionary<string, JsonNode> components)
    {
        List<AdmittedComponentSeed>? seeds = null;
        foreach ((string name, JsonNode node) in components)
        {
            if (!_byName.TryGetValue(name, out Admission? admission))
            {
                continue;
            }

            if (node == null)
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' admitted component '{name}' requires non-null data.");
            }

            seeds ??= new List<AdmittedComponentSeed>(2);
            seeds.Add(admission.Probe($"Entity template '{templateId}'", node));
        }

        return seeds == null || seeds.Count == 0
            ? Array.Empty<AdmittedComponentSeed>()
            : seeds.ToArray();
    }

    private abstract class Admission
    {
        public abstract AdmittedComponentSeed Probe(string context, JsonNode data);
    }

    private sealed class Admission<T> : Admission where T : unmanaged
    {
        private readonly string _name;

        public Admission(string name)
        {
            _name = name;
        }

        public override AdmittedComponentSeed Probe(string context, JsonNode data)
        {
            using World probeWorld = World.Create();
            Entity probe = probeWorld.Create();
            try
            {
                ComponentRegistry.Apply(probe, _name, data, context);
                if (!probeWorld.Has<T>(probe))
                {
                    throw new InvalidOperationException(
                        $"{context}: component '{_name}' is admitted for batch spawn but ComponentRegistry did not add {typeof(T).Name}.");
                }

                T value = probeWorld.Get<T>(probe);
                byte[] bytes = new byte[Unsafe.SizeOf<T>()];
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetArrayDataReference(bytes), value);
                return new AdmittedComponentSeed(Component<T>.Signature, (ref Chunk chunk, int componentIndex) =>
                {
                    chunk.GetSpan<T>()[componentIndex] = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetArrayDataReference(bytes));
                });
            }
            finally
            {
                if (probeWorld.IsAlive(probe))
                {
                    probeWorld.Destroy(probe);
                }
            }
        }
    }
}

internal readonly struct AdmittedComponentSeed
{
    public delegate void WriteDelegate(ref Chunk chunk, int componentIndex);

    private readonly WriteDelegate _write;

    public AdmittedComponentSeed(Signature signature, WriteDelegate write)
    {
        ComponentSignature = signature;
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public Signature ComponentSignature { get; }

    public void Write(ref Chunk chunk, int componentIndex)
    {
        _write(ref chunk, componentIndex);
    }
}
