using System;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

foreach (var (name, spread) in new[] { ("聚集成阵(约100分块)", 1), ("全散开(每实体独立分块)", 0) })
{
    using var arch = World.Create();
    var partition = new ChunkedGridSpatialPartitionWorld();
    long before = GC.GetTotalMemory(true);
    var rnd = new Random(11);
    for (int i = 0; i < 10_000; i++)
    {
        Entity e = arch.Create();
        int cellX, cellY;
        if (spread == 0) { cellX = i * 64 + rnd.Next(3); cellY = i * 64 + rnd.Next(3); }
        else if (spread == 1) { cellX = i % 10; cellY = i / 1000; }
        else { cellX = (i % 50) * 130 + rnd.Next(3); cellY = (i / 50) * 130 + rnd.Next(3); }
        partition.Add(e, cellX, cellY);
    }
    long after = GC.GetTotalMemory(true);
    var dict = typeof(ChunkedGridSpatialPartitionWorld)
        .GetField("_chunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .GetValue(partition) as System.Collections.IDictionary;
    Console.WriteLine($"{name}: {(after - before) / 1024.0 / 1024.0:F1}MB, 分块数={dict!.Count}, 每实体≈{(after - before) / 10_000.0:F0}B");
}
