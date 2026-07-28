using Arch.Core;
using GraphWorkbenchShowcaseMod.Domain;

namespace GraphWorkbenchShowcaseMod.DataPlane;

public sealed class GraphWorkbenchRuntimeBridge
{
    private const string Source = "ecs-runtime";
    private readonly World _world;
    private readonly Entity[] _entities;
    private string _selectedEntityId;
    private int _tick;
    private int _appliedRevision;

    public GraphWorkbenchRuntimeBridge(
        World world,
        GraphWorkbenchDocument document,
        int appliedRevision)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _appliedRevision = appliedRevision;
        _entities =
        [
            _world.Create(
                new GraphWorkbenchRuntimeIdentity(
                    "entity.level-director",
                    "Level Director",
                    "Level Blueprint",
                    "level.open_gate_impl",
                    string.Empty,
                    string.Empty,
                    42,
                    30),
                GraphWorkbenchRuntimeCursor.Empty),
            _world.Create(
                new GraphWorkbenchRuntimeIdentity(
                    "entity.fire-mage",
                    "Fire Mage",
                    "GAS Skill Graph",
                    "gas.fireball_cost_impl",
                    string.Empty,
                    string.Empty,
                    70,
                    58),
                GraphWorkbenchRuntimeCursor.Empty),
            _world.Create(
                new GraphWorkbenchRuntimeIdentity(
                    "entity.rifle-squad",
                    "Rifle Squad",
                    "RTS FSM + BT",
                    "fsm.return_fire_impl",
                    "rts.stance",
                    "unit.assault_bt",
                    58,
                    76),
                GraphWorkbenchRuntimeCursor.Empty)
        ];
        _selectedEntityId = "entity.level-director";
        Refresh(document);
    }

    public int Tick => _tick;

    public bool TrySelectEntity(string entityId)
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            ref GraphWorkbenchRuntimeIdentity identity = ref _world.Get<GraphWorkbenchRuntimeIdentity>(_entities[i]);
            if (string.Equals(identity.Id, entityId, StringComparison.Ordinal))
            {
                _selectedEntityId = entityId;
                return true;
            }
        }

        return false;
    }

    public void ApplyDocument(GraphWorkbenchDocument document, int appliedRevision)
    {
        _appliedRevision = appliedRevision;
        Refresh(document);
    }

    public void Advance(GraphWorkbenchDocument document, int appliedRevision, float dt)
    {
        _appliedRevision = appliedRevision;
        _tick++;
        Refresh(document);
    }

    public GraphWorkbenchRuntimeSnapshot CreateSnapshot(GraphWorkbenchDocument document, int appliedRevision)
    {
        _appliedRevision = appliedRevision;
        var rows = new GraphWorkbenchEntityDebug[_entities.Length];
        GraphWorkbenchEntityDebug selected = default!;
        for (int i = 0; i < _entities.Length; i++)
        {
            Entity entity = _entities[i];
            ref GraphWorkbenchRuntimeIdentity identity = ref _world.Get<GraphWorkbenchRuntimeIdentity>(entity);
            ref GraphWorkbenchRuntimeCursor cursor = ref _world.Get<GraphWorkbenchRuntimeCursor>(entity);
            rows[i] = new GraphWorkbenchEntityDebug(
                identity.Id,
                identity.Label,
                identity.Domain,
                identity.X,
                identity.Y,
                cursor.CurrentGraphId,
                cursor.CurrentGraphNodeId,
                cursor.CurrentGraphLabel,
                cursor.CurrentStateMachineId,
                cursor.CurrentStateNodeId,
                cursor.CurrentStateLabel,
                cursor.CurrentBehaviorTreeId,
                cursor.CurrentBehaviorNodeId,
                cursor.CurrentBehaviorLabel);
            if (string.Equals(identity.Id, _selectedEntityId, StringComparison.Ordinal))
            {
                selected = rows[i];
            }
        }

        selected ??= rows[0];
        return new GraphWorkbenchRuntimeSnapshot(
            Source,
            _selectedEntityId,
            _appliedRevision,
            selected.CurrentGraphId,
            selected.CurrentGraphNodeId,
            selected.CurrentStateMachineId,
            selected.CurrentStateNodeId,
            selected.CurrentBehaviorTreeId,
            selected.CurrentBehaviorNodeId,
            rows,
            CountByDomain(rows));
    }

    private void Refresh(GraphWorkbenchDocument document)
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            Entity entity = _entities[i];
            ref GraphWorkbenchRuntimeIdentity identity = ref _world.Get<GraphWorkbenchRuntimeIdentity>(entity);
            _world.Get<GraphWorkbenchRuntimeCursor>(entity) = CreateCursor(document, in identity);
        }
    }

    private GraphWorkbenchRuntimeCursor CreateCursor(
        GraphWorkbenchDocument document,
        in GraphWorkbenchRuntimeIdentity identity)
    {
        string fsmNodeId = string.Empty;
        string fsmLabel = string.Empty;
        string btNodeId = string.Empty;
        string btLabel = string.Empty;
        string graphId = identity.GraphId;

        if (!string.IsNullOrWhiteSpace(identity.StateMachineId) &&
            TryFindStateMachine(document, identity.StateMachineId, out GraphWorkbenchStateMachineDocument? fsm) &&
            fsm != null)
        {
            GraphWorkbenchNodeDocument node = PickNode(fsm.Nodes, _tick / 3);
            fsmNodeId = node.Id;
            fsmLabel = node.Label;
            if (!string.IsNullOrWhiteSpace(node.ImplementationGraphId))
            {
                graphId = node.ImplementationGraphId;
            }
        }

        if (!string.IsNullOrWhiteSpace(identity.BehaviorTreeId) &&
            TryFindBehaviorTree(document, identity.BehaviorTreeId, out GraphWorkbenchBehaviorTreeDocument? bt) &&
            bt != null)
        {
            GraphWorkbenchNodeDocument node = PickNode(bt.Nodes, _tick / 2);
            btNodeId = node.Id;
            btLabel = node.Label;
            if (!string.IsNullOrWhiteSpace(node.ImplementationGraphId))
            {
                graphId = node.ImplementationGraphId;
            }
        }

        string graphNodeId = string.Empty;
        string graphLabel = string.Empty;
        if (TryFindGraph(document, graphId, out GraphWorkbenchGraphDocument? graph) &&
            graph != null)
        {
            GraphWorkbenchNodeDocument node = PickNode(graph.Nodes, _tick);
            graphNodeId = node.Id;
            graphLabel = node.Label;
        }

        return new GraphWorkbenchRuntimeCursor(
            graphId,
            graphNodeId,
            graphLabel,
            identity.StateMachineId,
            fsmNodeId,
            fsmLabel,
            identity.BehaviorTreeId,
            btNodeId,
            btLabel);
    }

    private static GraphWorkbenchAggregate[] CountByDomain(GraphWorkbenchEntityDebug[] rows)
    {
        return rows
            .GroupBy(static row => row.Domain, StringComparer.Ordinal)
            .Select(static group => new GraphWorkbenchAggregate(group.Key, group.Count()))
            .OrderBy(static row => row.Domain, StringComparer.Ordinal)
            .ToArray();
    }

    private static GraphWorkbenchNodeDocument PickNode(List<GraphWorkbenchNodeDocument> nodes, int cursor)
    {
        if (nodes.Count == 0)
        {
            return new GraphWorkbenchNodeDocument();
        }

        int index = Math.Abs(cursor) % nodes.Count;
        return nodes[index];
    }

    private static bool TryFindGraph(GraphWorkbenchDocument document, string graphId, out GraphWorkbenchGraphDocument? graph)
    {
        graph = document.Graphs.FirstOrDefault(item => string.Equals(item.Id, graphId, StringComparison.Ordinal));
        return graph != null;
    }

    private static bool TryFindStateMachine(
        GraphWorkbenchDocument document,
        string stateMachineId,
        out GraphWorkbenchStateMachineDocument? stateMachine)
    {
        stateMachine = document.StateMachines.FirstOrDefault(item => string.Equals(item.Id, stateMachineId, StringComparison.Ordinal));
        return stateMachine != null;
    }

    private static bool TryFindBehaviorTree(
        GraphWorkbenchDocument document,
        string behaviorTreeId,
        out GraphWorkbenchBehaviorTreeDocument? behaviorTree)
    {
        behaviorTree = document.BehaviorTrees.FirstOrDefault(item => string.Equals(item.Id, behaviorTreeId, StringComparison.Ordinal));
        return behaviorTree != null;
    }
}

internal struct GraphWorkbenchRuntimeIdentity
{
    public GraphWorkbenchRuntimeIdentity(
        string id,
        string label,
        string domain,
        string graphId,
        string stateMachineId,
        string behaviorTreeId,
        int x,
        int y)
    {
        Id = id;
        Label = label;
        Domain = domain;
        GraphId = graphId;
        StateMachineId = stateMachineId;
        BehaviorTreeId = behaviorTreeId;
        X = x;
        Y = y;
    }

    public string Id;
    public string Label;
    public string Domain;
    public string GraphId;
    public string StateMachineId;
    public string BehaviorTreeId;
    public int X;
    public int Y;
}

internal struct GraphWorkbenchRuntimeCursor
{
    public static readonly GraphWorkbenchRuntimeCursor Empty = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    public GraphWorkbenchRuntimeCursor(
        string currentGraphId,
        string currentGraphNodeId,
        string currentGraphLabel,
        string currentStateMachineId,
        string currentStateNodeId,
        string currentStateLabel,
        string currentBehaviorTreeId,
        string currentBehaviorNodeId,
        string currentBehaviorLabel)
    {
        CurrentGraphId = currentGraphId;
        CurrentGraphNodeId = currentGraphNodeId;
        CurrentGraphLabel = currentGraphLabel;
        CurrentStateMachineId = currentStateMachineId;
        CurrentStateNodeId = currentStateNodeId;
        CurrentStateLabel = currentStateLabel;
        CurrentBehaviorTreeId = currentBehaviorTreeId;
        CurrentBehaviorNodeId = currentBehaviorNodeId;
        CurrentBehaviorLabel = currentBehaviorLabel;
    }

    public string CurrentGraphId;
    public string CurrentGraphNodeId;
    public string CurrentGraphLabel;
    public string CurrentStateMachineId;
    public string CurrentStateNodeId;
    public string CurrentStateLabel;
    public string CurrentBehaviorTreeId;
    public string CurrentBehaviorNodeId;
    public string CurrentBehaviorLabel;
}
