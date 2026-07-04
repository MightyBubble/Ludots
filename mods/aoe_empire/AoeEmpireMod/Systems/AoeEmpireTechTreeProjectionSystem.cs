using System;
using Arch.System;
using Ludots.Core.Engine;

namespace AoeEmpireMod.Systems;

/// <summary>
/// Projects faction tech-tree state into GlobalContext for Web UI DataPlane consumers.
/// </summary>
public sealed class AoeEmpireTechTreeProjectionSystem : ISystem<float>
{
    public const string TechTreeContextKey = "AoeEmpireMod.TechTreeProjection";
    private readonly GameEngine _engine;
    private int _tick;

    public AoeEmpireTechTreeProjectionSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        _tick++;
        if (_tick % 30 != 0)
        {
            return;
        }

        _engine.GlobalContext[TechTreeContextKey] = new AoeEmpireTechTreeProjection(
            MapId: _engine.CurrentMapSession?.MapConfig?.Id ?? string.Empty,
            Nodes:
            [
                new AoeEmpireTechNode("dark-age", "Dark Age", true, 0),
                new AoeEmpireTechNode("feudal-age", "Feudal Age", false, 1),
                new AoeEmpireTechNode("castle-age", "Castle Age", false, 2),
                new AoeEmpireTechNode("imperial-age", "Imperial Age", false, 3),
            ]);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }
}

public sealed record AoeEmpireTechTreeProjection(string MapId, AoeEmpireTechNode[] Nodes);

public sealed record AoeEmpireTechNode(string Id, string Label, bool Completed, int Tier);
