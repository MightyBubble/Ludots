using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Arch.Core;

namespace Ludots.Core.Config;

/// <summary>
/// 批车道组件准入。登记的类型用 <see cref="ComponentRegistry"/> 在探针实体上落一次 JSON，
/// 再把那份 struct 抄进批 chunk。JSON 仍是组件形状的唯一来源，不另写一套解析。
/// <see cref="Generation"/> 变化后，生成器里已缓存的模板描述作废重算——登记可以晚于第一次查询。
/// </summary>
internal static class TemplateBatchComponentAdmission
{
    private static readonly Dictionary<string, Admission> ByName = new(StringComparer.Ordinal);

    static TemplateBatchComponentAdmission()
    {
        Register<Spatial.SpatialBounds>("SpatialBounds");
        Register<Spatial.SpatialBox3D>("SpatialBox3D");
        Register<Spatial.SpatialFootprint2D>("SpatialFootprint2D");
    }

    public static int Generation { get; private set; }

    public static void Register<T>(string componentName) where T : unmanaged
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new ArgumentException("Admitted batch component name is required.", nameof(componentName));
        }

        if (ByName.ContainsKey(componentName))
        {
            throw new InvalidOperationException($"SPAWN.BATCH.ERR.DuplicateAdmission: '{componentName}' is already admitted.");
        }

        ByName.Add(componentName, new Admission<T>(componentName));
        Generation++;
    }

    public static bool IsAdmitted(string componentName)
    {
        return !string.IsNullOrWhiteSpace(componentName) && ByName.ContainsKey(componentName);
    }

    public static AdmittedComponentSeed[] ProbeAll(
        World world,
        string templateId,
        IReadOnlyDictionary<string, JsonNode> components)
    {
        List<AdmittedComponentSeed>? seeds = null;
        foreach ((string name, JsonNode node) in components)
        {
            if (!ByName.TryGetValue(name, out Admission? admission))
            {
                continue;
            }

            if (node == null)
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' admitted component '{name}' requires non-null data.");
            }

            seeds ??= new List<AdmittedComponentSeed>(2);
            seeds.Add(admission.Probe(world, $"Entity template '{templateId}'", node));
        }

        return seeds == null || seeds.Count == 0
            ? Array.Empty<AdmittedComponentSeed>()
            : seeds.ToArray();
    }

    private abstract class Admission
    {
        public abstract AdmittedComponentSeed Probe(World world, string context, JsonNode data);
    }

    private sealed class Admission<T> : Admission where T : unmanaged
    {
        private readonly string _name;

        public Admission(string name)
        {
            _name = name;
        }

        public override AdmittedComponentSeed Probe(World world, string context, JsonNode data)
        {
            Entity probe = world.Create();
            try
            {
                ComponentRegistry.Apply(probe, _name, data, context);
                if (!world.Has<T>(probe))
                {
                    throw new InvalidOperationException(
                        $"{context}: component '{_name}' is admitted for batch spawn but ComponentRegistry did not add {typeof(T).Name}.");
                }

                T value = world.Get<T>(probe);
                byte[] bytes = new byte[Unsafe.SizeOf<T>()];
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetArrayDataReference(bytes), value);
                return new AdmittedComponentSeed(Component<T>.Signature, (ref Chunk chunk, int componentIndex) =>
                {
                    chunk.GetSpan<T>()[componentIndex] = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetArrayDataReference(bytes));
                });
            }
            finally
            {
                if (world.IsAlive(probe))
                {
                    world.Destroy(probe);
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
