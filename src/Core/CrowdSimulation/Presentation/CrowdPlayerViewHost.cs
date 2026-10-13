using System;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 玩家视图：键鼠只提交指令或写选中集合，绘制走路线通道和屏幕覆盖层。
/// 拳击上下文的点按图在触发阶段卸下；同一逻辑拍里图还没挂上时，这里在仍激活时再卸一次。
/// </summary>
internal sealed class CrowdPlayerViewHost
{
    private const int FormationLineId = 900;
    private const int RippleMarkerId = 901;
    private const int GoalCrossHId = 902;
    private const int GoalCrossVId = 903;
    private const int RouteEndId = 904;
    private const int EnemyMarkIdBase = 6000;
    private const int DashIdBase = 910;
    private const int StructureIdBase = 2000;
    private const int MaxPlayers = 8;

    private readonly GameEngine? _engine;
    private readonly CrowdPlayerViewConfig _config;
    private readonly Vector4 _plan;
    private readonly Vector4 _ink;
    private readonly Vector4 _paper;
    private readonly Vector4 _goal;
    private readonly Vector4 _ripple;
    private readonly Vector4 _card;
    private readonly Vector4 _text;
    private readonly Vector4 _enemyMark;
    private readonly StringBuilder _sb = new(512);
    private readonly float _dashVisual;
    private readonly float _gapVisual;
    private readonly float _routeThicknessVisual;
    private readonly float _rippleRadiusVisual;
    private readonly float _markerRadiusVisual;
    private readonly float _enemyMarkRadiusVisual;
    private readonly float _crossArmVisual;

    private CrowdSimSession? _boundSession;
    private bool[] _layers = Array.Empty<bool>();
    private int[] _agentIndex = Array.Empty<int>();
    private string[] _shapeIds = Array.Empty<string>();
    private string[] _shapeLabels = Array.Empty<string>();
    private uint[] _handles = Array.Empty<uint>();
    private Entity[] _entities = Array.Empty<Entity>();
    private Vector2[][] _dashes = Array.Empty<Vector2[]>();
    private Vector2[][] _structures = Array.Empty<Vector2[]>();
    private readonly Vector2[] _formLine = new Vector2[2];
    private readonly Vector2[] _crossH = new Vector2[2];
    private readonly Vector2[] _crossV = new Vector2[2];
    private readonly int[] _counts = new int[MaxPlayers + 1];
    private readonly int[] _sigCounts = new int[MaxPlayers + 1];
    private readonly Vector4[] _teamColors = new Vector4[MaxPlayers + 1];
    private readonly bool[] _teamKnown = new bool[MaxPlayers + 1];

    private int _playerCount;
    private int _radiusLast;
    private int _player = 1;
    private int _focus;
    private int _deployCount;
    private int _radiusMin;
    private int _radiusMax;
    private int _clickCount;
    private int _clickUnitType;
    private int _clickRadius;
    private bool _fogHide;
    private bool _route;
    private bool _lineOfSight;
    private int _sceneKind;
    private bool _formDown;
    private float _formX0;
    private float _formY0;
    private float _formX1;
    private float _formY1;
    private bool _boxDown;
    private float _boxX0;
    private float _boxY0;
    private float _boxX1;
    private float _boxY1;
    private bool _boxingReady;
    private int _boxingProfileId;
    private int _boxingContextKeyId;
    private CrowdFogCheckScene? _scene;
    private int _markPlayer = -1;
    private int _markXCm;
    private int _markYCm;
    private int _markCatchCm;
    private bool _logArmed;
    private int _seenLog;
    private long _rippleStart = long.MinValue;
    private int _rippleX;
    private int _rippleY;
    private bool _textReady;
    private int _sigPlayer = int.MinValue;
    private int _sigOrder = int.MinValue;
    private int _sigMode = int.MinValue;
    private int _sigShape = int.MinValue;
    private int _sigPlanning = int.MinValue;
    private int _sigMoving = int.MinValue;
    private int _sigArrived = int.MinValue;
    private int _sigOther = int.MinValue;
    private int _sigFlags = int.MinValue;
    private int _sigDeploy = int.MinValue;
    private int _sigRadius = int.MinValue;
    private int _sigClick = int.MinValue;
    private int _sigScene = int.MinValue;
    private int _sigFocus = int.MinValue;
    private string _teamBar = "";
    private string _statusLine = "";
    private string _modeLine = "";
    private string _clickLine = "";
    private string _deployLine = "";

    public CrowdPlayerViewHost(GameEngine? engine, CrowdPlayerViewConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _engine = engine;
        _config = config;
        _plan = config.Colors.Plan.ToVector4();
        _ink = config.Colors.Ink.ToVector4();
        _paper = config.Colors.Paper.ToVector4();
        _goal = config.Colors.Goal.ToVector4();
        _ripple = config.Colors.Ripple.ToVector4();
        _card = config.Colors.Card.ToVector4();
        _text = config.Colors.Text.ToVector4();
        _enemyMark = config.Colors.EnemyMark.ToVector4();
        _dashVisual = WorldUnits.CmToM(config.DashCm);
        _gapVisual = WorldUnits.CmToM(config.GapCm);
        _routeThicknessVisual = WorldUnits.CmToM(config.RouteThicknessCm);
        _rippleRadiusVisual = WorldUnits.CmToM(config.RippleRadiusCm);
        _markerRadiusVisual = WorldUnits.CmToM(config.MarkerRadiusCm);
        _enemyMarkRadiusVisual = WorldUnits.CmToM(config.EnemyMarkRadiusCm);
        _crossArmVisual = WorldUnits.CmToM(config.CrossArmCm);
        Prefs = new CrowdPlayerOrderPrefs
        {
            ShapeId = config.DefaultShapeId,
            MagicBox = config.MagicBox,
        };
        _fogHide = config.FogHide;
        _route = config.PlayerRoute;
        _lineOfSight = config.LineOfSight;
        _deployCount = config.DeployCount;
        _radiusMin = config.RadiusMinIndex;
        _radiusMax = config.RadiusMaxIndex;
        _clickCount = config.ClickCount;
        _clickUnitType = config.ClickUnitType;
        _clickRadius = config.ClickRadiusClass;
    }

    public CrowdPlayerOrderPrefs Prefs { get; }

    public void Bind(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        int players = session.Config.PairRules.PlayerCount;
        if (players < 1 || players > MaxPlayers)
        {
            throw new InvalidOperationException($"玩家视图只接受 1..{MaxPlayers} 个玩家，当前 {players}。");
        }

        int types = session.Config.UnitTypes.Count;
        int radii = session.Config.RadiusClasses.Count;
        int capacity = session.Units.Capacity;
        if (types < 1 || radii < 1 || capacity < 1)
        {
            throw new InvalidOperationException("玩家视图需要单位类型、半径级和单位容量。");
        }

        int shape = -1;
        var formations = session.Config.Formations;
        _shapeIds = new string[formations.Count];
        _shapeLabels = new string[formations.Count];
        for (int i = 0; i < formations.Count; i++)
        {
            _shapeIds[i] = formations[i].Id;
            _shapeLabels[i] = formations[i].Label;
            if (string.Equals(formations[i].Id, _config.DefaultShapeId, StringComparison.Ordinal))
            {
                shape = i;
            }
        }

        if (shape < 0)
        {
            throw new InvalidOperationException($"玩家视图默认阵型 {_config.DefaultShapeId} 不在阵型表里。");
        }

        if (_config.ClickUnitType >= types || _config.ClickRadiusClass >= radii || _config.RadiusMinIndex >= radii)
        {
            throw new InvalidOperationException("玩家视图的点选模板、点选半径或半径窗超出活表。");
        }

        _radiusLast = radii - 1;
        _radiusMin = _config.RadiusMinIndex;
        _radiusMax = _config.RadiusMaxIndex < 0 ? _radiusLast : _config.RadiusMaxIndex;
        if (_radiusMax > _radiusLast || _radiusMax < _radiusMin)
        {
            throw new InvalidOperationException("玩家视图半径窗超出活表。");
        }

        _layers = new bool[types];
        _agentIndex = new int[types];
        for (int i = 0; i < types; i++)
        {
            _layers[i] = _config.LayersStartEnabled;
            _agentIndex[i] = session.Config.UnitTypes[i].AgentTypeIndex;
        }

        _handles = new uint[capacity];
        _entities = new Entity[capacity];
        _dashes = new Vector2[_config.MaxDashes][];
        for (int i = 0; i < _dashes.Length; i++)
        {
            _dashes[i] = new Vector2[2];
        }

        _structures = new Vector2[_config.MaxBeliefMarkers][];
        for (int i = 0; i < _structures.Length; i++)
        {
            _structures[i] = new Vector2[5];
        }

        _playerCount = players;
        _focus = 0;
        _deployCount = _config.DeployCount;
        _clickCount = _config.ClickCount;
        _clickUnitType = _config.ClickUnitType;
        _clickRadius = _config.ClickRadiusClass;
        Prefs.ShapeId = _config.DefaultShapeId;
        Prefs.MagicBox = _config.MagicBox;
        CacheTeamColors(session);
        _boundSession = session;
        _textReady = false;
        _logArmed = false;
    }

    public void ApplyInput(
        CrowdSimSession? session,
        FrozenInputActionReader actions,
        bool picked,
        double xCm,
        double yCm,
        S4DeployDemoRuntime demo)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(demo);
        if (session != null && !ReferenceEquals(session, _boundSession))
        {
            Bind(session);
        }

        if (_engine != null &&
            _engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) &&
            seats != null &&
            seats.TryGetSoleSeat(out ClientLocalSeat seat) &&
            seat.PossessedPlayerId > 0)
        {
            _player = seat.PossessedPlayerId;
        }

        var keys = _config.Actions;
        if (session != null && Pressed(actions, keys.CycleLayer) && _layers.Length > 0)
        {
            _focus = (_focus + 1) % _layers.Length;
        }

        if (session != null && Pressed(actions, keys.ToggleLayer) && (uint)_focus < (uint)_layers.Length)
        {
            _layers[_focus] = !_layers[_focus];
        }

        if (session != null)
        {
            ApplyLayers(session);
        }

        if (session != null && Pressed(actions, keys.NextTeam))
        {
            SwitchTeam(session);
        }

        if (session != null && Pressed(actions, keys.SelectAll) && _entities.Length > 0)
        {
            CrowdPlayerSelection.SelectVisible(session, _player, _entities);
        }

        if (session != null && Pressed(actions, keys.ClearSelection))
        {
            CrowdPlayerSelection.Clear(session, _player);
        }

        if (Pressed(actions, keys.ToggleFog))
        {
            _fogHide = !_fogHide;
        }

        if (Pressed(actions, keys.ToggleRoute))
        {
            _route = !_route;
        }

        if (Pressed(actions, keys.ToggleLos))
        {
            _lineOfSight = !_lineOfSight;
            if (session?.Fog != null)
            {
                demo.EnqueueCommand(new CrowdSimCommand { Kind = CrowdSimCommandKind.FogSight, On = _lineOfSight });
            }
        }

        if (session != null && Pressed(actions, keys.SceneOutside))
        {
            LaunchOutside(session, demo);
        }

        if (session != null && Pressed(actions, keys.SceneBehind))
        {
            LaunchBehind(session, demo);
        }

        if (Pressed(actions, keys.DeployCountDown))
        {
            StepCount(ref _deployCount, -1, _config.DeployCountStep, _config.DeployCountMin, _config.DeployCountMax);
        }

        if (Pressed(actions, keys.DeployCountUp))
        {
            StepCount(ref _deployCount, 1, _config.DeployCountStep, _config.DeployCountMin, _config.DeployCountMax);
        }

        if (Pressed(actions, keys.ClickCountDown))
        {
            StepCount(ref _clickCount, -1, _config.ClickCountStep, _config.ClickCountMin, _config.ClickCountMax);
        }

        if (Pressed(actions, keys.ClickCountUp))
        {
            StepCount(ref _clickCount, 1, _config.ClickCountStep, _config.ClickCountMin, _config.ClickCountMax);
        }

        if (Pressed(actions, keys.CycleClickTemplate) && _layers.Length > 0)
        {
            _clickUnitType = (_clickUnitType + 1) % _layers.Length;
        }

        if (Pressed(actions, keys.CycleClickRadius) && _radiusLast >= 0)
        {
            _clickRadius = (_clickRadius + 1) % (_radiusLast + 1);
        }

        if (Pressed(actions, keys.CycleRadiusRange))
        {
            CycleRadius();
        }

        if (Pressed(actions, keys.ToggleMagicBox))
        {
            Prefs.MagicBox = !Prefs.MagicBox;
        }

        if (Pressed(actions, keys.CycleShape) && _shapeIds.Length > 0)
        {
            int index = ShapeIndex(Prefs.ShapeId);
            Prefs.ShapeId = _shapeIds[(index + 1) % _shapeIds.Length];
        }

        demo.ClickSpawnCount = _clickCount;
        demo.ClickSpawnUnitType = _clickUnitType;
        demo.ClickSpawnRadiusClass = _clickRadius;

        Vector2 pointer = actions.ReadAction<Vector2>(ReservedInputActionIds.PointerPos);
        bool boxing = actions.IsDown(keys.BoxSelectBegin);
        if (boxing)
        {
            if (!_boxDown)
            {
                _boxX0 = pointer.X;
                _boxY0 = pointer.Y;
                _boxDown = true;
            }

            _boxX1 = pointer.X;
            _boxY1 = pointer.Y;
        }
        else
        {
            _boxDown = false;
        }

        bool secondary = actions.IsDown(keys.SecondaryHold);
        if (secondary && picked)
        {
            if (!_formDown)
            {
                _formX0 = (float)xCm;
                _formY0 = (float)yCm;
                _formDown = true;
            }

            _formX1 = (float)xCm;
            _formY1 = (float)yCm;
        }

        if (Pressed(actions, keys.FormationCommit))
        {
            if (_formDown && session != null)
            {
                SubmitFormation(session, demo);
            }

            _formDown = false;
        }
        else if (!secondary)
        {
            _formDown = false;
        }

        if (Pressed(actions, keys.PrimaryTap))
        {
            ReleaseBoxing();
            if (demo.Tool == CrowdClickTool.Off && session != null)
            {
                CrowdPlayerSelection.Clear(session, _player);
            }
        }
    }

    public void BuildDeploy(CrowdSimSession session, Action<CrowdSimCommand> submit)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(submit);
        if (!ReferenceEquals(session, _boundSession))
        {
            Bind(session);
        }

        bool full = _radiusMin == 0 && _radiusMax == _radiusLast;
        for (int i = 0; full && i < _layers.Length; i++)
        {
            if (!_layers[i])
            {
                full = false;
            }
        }

        if (full)
        {
            submit(new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = _deployCount });
            return;
        }

        int pairs = 0;
        for (int t = 0; t < _layers.Length; t++)
        {
            if (!_layers[t])
            {
                continue;
            }

            pairs += _radiusMax - _radiusMin + 1;
        }

        if (pairs < 1)
        {
            throw new InvalidOperationException("玩家视图部署的模板层和半径窗没有组合。");
        }

        RuntimeDeployBase? home = null;
        var bases = session.Config.Deploy.Bases;
        for (int i = 0; i < bases.Count; i++)
        {
            if (bases[i].PlayerId == _player)
            {
                home = bases[i];
                break;
            }
        }

        if (home == null)
        {
            throw new InvalidOperationException($"玩家 {_player} 没有出生点。");
        }

        int each = _deployCount / pairs;
        int extra = _deployCount % pairs;
        int index = 0;
        for (int t = 0; t < _layers.Length; t++)
        {
            if (!_layers[t])
            {
                continue;
            }

            for (int r = _radiusMin; r <= _radiusMax; r++)
            {
                int count = each + (index < extra ? 1 : 0);
                index++;
                if (count < 1)
                {
                    continue;
                }

                submit(new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.SpawnAt,
                    Player = _player,
                    XCm = home.XCm,
                    YCm = home.YCm,
                    Count = count,
                    UnitType = t,
                    RIdx = r,
                });
            }
        }
    }

    public void Draw(CrowdSimSession session, RouteVisualBuffer routes, ScreenOverlayBuffer overlay)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(overlay);
        if (!ReferenceEquals(session, _boundSession))
        {
            Bind(session);
        }

        int viewer = ViewerGroup(session);
        CrowdOrder? latest = LatestOrder(session, _player);
        int planning = 0;
        int moving = 0;
        int arrived = 0;
        int other = 0;
        Array.Clear(_counts);
        int units = session.Units.Count;
        for (int i = 0; i < units; i++)
        {
            Entity entity = session.Units.EntityAt(i);
            int owner = session.World.Get<PlayerOwner>(entity).PlayerId;
            if ((uint)owner < (uint)_counts.Length)
            {
                _counts[owner]++;
            }

            bool shown = CrowdPlayerVisibility.UnitShown(session, viewer, i, _fogHide);
            if (!session.World.Has<PresentationHidden>(entity))
            {
                session.World.Add(entity, new PresentationHidden { Hidden = !shown });
            }
            else
            {
                ref PresentationHidden hidden = ref session.World.Get<PresentationHidden>(entity);
                hidden.Hidden = !shown;
            }

            if (shown && _sceneKind != 0 && owner == _markPlayer)
            {
                DrawEnemyMark(session, routes, i);
            }

            if (latest == null)
            {
                continue;
            }

            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            if (!session.Groups.TryGet(state.GroupId, out CrowdNavGroupSet.Group group) || group.OrderId != latest.Id)
            {
                continue;
            }

            byte unitState = state.State;
            if (group.Planning && unitState != (byte)CrowdUnitState.Arrived)
            {
                planning++;
            }
            else if (unitState == (byte)CrowdUnitState.Moving || unitState == (byte)CrowdUnitState.Jump)
            {
                moving++;
            }
            else if (unitState == (byte)CrowdUnitState.Arrived)
            {
                arrived++;
            }
            else
            {
                other++;
            }
        }

        NoteRipple(session);
        DrawStructures(session, routes, viewer);
        if (_boxDown)
        {
            DrawBox(overlay);
        }

        if (_formDown)
        {
            _formLine[0] = new Vector2(WorldUnits.CmToM(_formX0), WorldUnits.CmToM(_formY0));
            _formLine[1] = new Vector2(WorldUnits.CmToM(_formX1), WorldUnits.CmToM(_formY1));
            routes.AddPolylineShared(new RouteVisualId(FormationLineId), _formLine, _routeThicknessVisual, _plan);
        }

        long now = Environment.TickCount64;
        if (_rippleStart != long.MinValue && now - _rippleStart < _config.RippleMs)
        {
            routes.AddMarker(
                new RouteVisualId(RippleMarkerId),
                new Vector2(WorldUnits.CmToM(_rippleX), WorldUnits.CmToM(_rippleY)),
                RouteVisualMarkerShape.Ring,
                _rippleRadiusVisual,
                _routeThicknessVisual,
                _ripple);
        }

        if (latest != null && TryLeader(session, latest, out CrowdLeader leader, out int goal, out bool unfinished))
        {
            DrawOrderFeedback(session, routes, viewer, leader, goal, unfinished);
        }

        var card = CrowdCommandStatusCard.Classify(planning, moving, arrived, other);
        PublishText(latest, card, planning, moving, arrived, other);
        DrawChrome(overlay);
    }

    private void ApplyLayers(CrowdSimSession session)
    {
        int tag = session.Presentation.HiddenTemplateTagId;
        if (tag <= 0)
        {
            throw new InvalidOperationException("玩家视图需要隐藏模板标签。");
        }

        int count = session.Units.Count;
        for (int i = 0; i < count; i++)
        {
            Entity entity = session.Units.EntityAt(i);
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            if (!session.Groups.TryGet(state.GroupId, out CrowdNavGroupSet.Group group))
            {
                continue;
            }

            bool template = false;
            for (int t = 0; t < _layers.Length; t++)
            {
                if (_layers[t] && _agentIndex[t] == group.LayerIdx)
                {
                    template = true;
                    break;
                }
            }

            bool radius = group.RIdx >= _radiusMin && group.RIdx <= _radiusMax;
            CrowdPlayerLayerScope.Apply(session.World, entity, tag, CrowdPlayerLayerScope.Included(template, radius));
        }
    }

    private void SubmitFormation(CrowdSimSession session, S4DeployDemoRuntime demo)
    {
        CrowdFormationDrag.FromLine(_formX0, _formY0, _formX1, _formY1, out float midX, out float midY, out int widthCm, out float faceX, out float faceY);
        if (widthCm < 1)
        {
            return;
        }

        int members = CopyMembers(session);
        if (members < 1)
        {
            return;
        }

        var copy = new uint[members];
        _handles.AsSpan(0, members).CopyTo(copy);
        demo.EnqueueCommand(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = _player,
            XCm = (int)midX,
            YCm = (int)midY,
            WidthCm = widthCm,
            HasFace = true,
            FaceX = Fix64.FromDouble(faceX),
            FaceY = Fix64.FromDouble(faceY),
            HasAuto = true,
            Auto = Prefs.MagicBox,
            ShapeId = Prefs.ShapeId,
            Members = copy,
        });
    }

    private int CopyMembers(CrowdSimSession session)
    {
        int selected = session.SelectedCollectionCount(_player);
        if (selected > 0)
        {
            if (selected > _entities.Length)
            {
                throw new InvalidOperationException("order: 成员缓冲装不下本方单位。");
            }

            Entity[]? reps = session.Presentation.PlayerReps;
            if (reps == null || (uint)_player >= (uint)reps.Length)
            {
                throw new InvalidOperationException("order: 成员集为空。");
            }

            int copied = session.Presentation.Collections.CopyEntities(reps[_player], session.Presentation.SelectedCollectionKeyId, _entities);
            int n = 0;
            for (int i = 0; i < copied; i++)
            {
                if (session.Units.TryHandleOf(_entities[i], out uint handle))
                {
                    _handles[n++] = handle;
                }
            }

            if (n == 0)
            {
                throw new InvalidOperationException("order: 成员集为空。");
            }

            return n;
        }

        int own = session.Units.CopyOwnHandles(_player, _handles);
        return own;
    }

    private void SwitchTeam(CrowdSimSession session)
    {
        int next = _player >= _playerCount ? 1 : _player + 1;
        Entity[]? reps = session.Presentation.PlayerReps;
        if (reps == null || (uint)next >= (uint)reps.Length || reps[next] == Entity.Null)
        {
            throw new InvalidOperationException($"玩家 {next} 没有代表。");
        }

        CrowdPlayerSelection.ClearForSwitch(session, _player, next);
        if (_engine != null)
        {
            ClientLocalSeatBindings.BindSoleSeat(_engine, reps[next], next, _config.SeatId);
        }

        _player = next;
    }

    private void LaunchOutside(CrowdSimSession session, S4DeployDemoRuntime demo)
    {
        CrowdFogCheckScene scene = RequireScene();
        EnqueueKill(demo);
        EnqueueCluster(demo, scene, scene.ViewerPlayer, scene.ViewerXCm, scene.ViewerYCm, scene.ClusterSpacingCm);
        EnqueueCluster(demo, scene, scene.EnemyPlayer, scene.EnemyXCm, scene.EnemyYCm, scene.ClusterSpacingCm);
        demo.EnqueueCommand(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = scene.UnseenTemplate,
            XCm = scene.UnseenXCm,
            YCm = scene.UnseenYCm,
            SizeCm = scene.UnseenSizeCm,
        });
        Occupy(session, scene.ViewerPlayer);
        Frame(scene.OutsideLookXCm, scene.OutsideLookYCm, scene.OutsideDistanceCm, scene.OutsideYaw, scene.OutsidePitch);
        ArmMark(scene.EnemyPlayer, scene.EnemyXCm, scene.EnemyYCm, scene.ClusterSpacingCm, scene.SpawnCount);
        _sceneKind = 1;
        _fogHide = true;
    }

    private void LaunchBehind(CrowdSimSession session, S4DeployDemoRuntime demo)
    {
        CrowdFogCheckScene scene = RequireScene();
        var fog = session.Fog ?? throw new InvalidOperationException("墙后核对需要迷雾。");
        int cellCm = fog.FogCellSizeCm;
        var cell = Fix64.FromInt(cellCm);
        int fx = (int)(Fix64.FromInt(scene.ViewerXCm) / cell).ToLong();
        int fy = (int)(Fix64.FromInt(scene.ViewerYCm) / cell).ToLong();
        int wallCellWest = (fx + scene.WallCellsEast) * cellCm;
        int wallCellEast = wallCellWest + cellCm;
        // 墙贴遮挡格东缘、敌方贴下一格西缘。格心相距一整格，同一机位要么看不见墙，要么看不见人。
        int wallX = wallCellEast - (scene.WallSizeCm / 2);
        int wallY = (fy * cellCm) + (cellCm / 2);
        int navCm = session.Config.NavCellSizeCm;
        // 格心正好落在导航格线上，点名生成会把一排拆进相邻格子，迷雾只亮其中一格。收到格心后整排一起可见或一起消失。
        int behindX = SnapNavCenter(wallCellEast + scene.BehindFromCellWestCm, navCm);
        int clusterY = SnapNavCenter(wallY, navCm);
        if (scene.BehindFromCellWestCm >= cellCm || wallX - (scene.WallSizeCm / 2) < wallCellWest)
        {
            throw new InvalidOperationException("墙和墙后敌方必须分别落在相邻两格迷雾里。");
        }
        int span = (scene.SpawnCount - 1) * scene.BehindClusterSpacingCm;
        if (span >= cellCm || span >= navCm)
        {
            throw new InvalidOperationException("核对单位的间距超出一格，遮挡会把他们拆开。");
        }
        int behindCell = behindX / cellCm;
        int clusterCell = clusterY / cellCm;
        if (behindCell != wallCellEast / cellCm || clusterCell != wallY / cellCm)
        {
            throw new InvalidOperationException("墙后敌方收进导航格心后离开了遮挡东侧那一格迷雾。");
        }

        EnqueueKill(demo);
        EnqueueCluster(demo, scene, scene.ViewerPlayer, scene.ViewerXCm, scene.ViewerYCm, scene.ClusterSpacingCm);
        demo.EnqueueCommand(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = scene.WallTemplate,
            XCm = wallX,
            YCm = wallY,
            SizeCm = scene.WallSizeCm,
        });
        EnqueueCluster(demo, scene, scene.EnemyPlayer, behindX, clusterY, scene.BehindClusterSpacingCm);
        demo.EnqueueCommand(new CrowdSimCommand { Kind = CrowdSimCommandKind.FogSight, On = _lineOfSight });
        Occupy(session, scene.ViewerPlayer);
        Frame(scene.BehindLookXCm, scene.BehindLookYCm, scene.BehindDistanceCm, scene.BehindYaw, scene.BehindPitch);
        ArmMark(scene.EnemyPlayer, behindX, clusterY, scene.BehindClusterSpacingCm, scene.SpawnCount);
        _sceneKind = 2;
        _fogHide = true;
    }

    private static int SnapNavCenter(int cm, int navCm)
    {
        if (navCm < 1)
        {
            throw new InvalidOperationException("导航格尺寸必须 > 0。");
        }

        int index = cm / navCm;
        return (index * navCm) + (navCm / 2);
    }

    private void Occupy(CrowdSimSession session, int playerId)
    {
        Entity[]? reps = session.Presentation.PlayerReps;
        if (reps == null || (uint)playerId >= (uint)reps.Length || reps[playerId] == Entity.Null)
        {
            throw new InvalidOperationException($"玩家 {playerId} 没有代表。");
        }

        CrowdPlayerSelection.ClearForSwitch(session, _player, playerId);
        if (_engine != null)
        {
            ClientLocalSeatBindings.BindSoleSeat(_engine, reps[playerId], playerId, _config.SeatId);
        }

        _player = playerId;
    }

    private void Frame(int xCm, int yCm, float distanceCm, float yaw, float pitch)
    {
        if (_engine == null)
        {
            return;
        }

        // 座位绑定之后呈现眼不再是 bootstrap 的 ClientPresent。取景必须打到正在画的那台相机。
        CameraManager camera = ClientLocalSeatAccess.ResolveAuthorityCamera(_engine);
        camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = _config.CameraId,
            TargetCm = new Vector2(xCm, yCm),
            DistanceCm = distanceCm,
            Yaw = yaw,
            Pitch = pitch,
        });
    }

    private void ArmMark(int player, int xCm, int yCm, int spacingCm, int count)
    {
        _markPlayer = player;
        _markXCm = xCm;
        _markYCm = yCm;
        _markCatchCm = (((count - 1) * spacingCm) / 2) + spacingCm;
    }

    /// <summary>
    /// 核对机位下单位网格只有几像素。环只画仍被迷雾判定为可见的敌方，半径来自玩家视图配置。
    /// </summary>
    private void DrawEnemyMark(CrowdSimSession session, RouteVisualBuffer routes, int dense)
    {
        if (session.Movement == null)
        {
            return;
        }

        Fix64Vec2 pos = session.Movement.Positions[dense];
        long dx = (pos.X - Fix64.FromInt(_markXCm)).ToLong();
        long dy = (pos.Y - Fix64.FromInt(_markYCm)).ToLong();
        if (dx < 0) dx = -dx;
        if (dy < 0) dy = -dy;
        if (dx > _markCatchCm || dy > _markCatchCm)
        {
            return;
        }

        routes.AddMarker(
            new RouteVisualId(EnemyMarkIdBase + dense),
            new Vector2(WorldUnits.CmToM(pos.X.ToFloat()), WorldUnits.CmToM(pos.Y.ToFloat())),
            RouteVisualMarkerShape.Ring,
            _enemyMarkRadiusVisual,
            _routeThicknessVisual,
            _enemyMark);
    }

    private void EnqueueKill(S4DeployDemoRuntime demo)
    {
        demo.EnqueueCommand(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = _config.KillXCm,
            YCm = _config.KillYCm,
            RadiusCm = _config.KillRadiusCm,
            RingScale = Fix64.FromDouble(_config.KillRingScale),
            Impulse = Fix64.FromDouble(_config.KillImpulse),
        });
    }

    private static void EnqueueCluster(S4DeployDemoRuntime demo, CrowdFogCheckScene scene, int player, int xCm, int yCm, int spacingCm)
    {
        int count = scene.SpawnCount;
        int origin = -((count - 1) * spacingCm) / 2;
        for (int i = 0; i < count; i++)
        {
            EnqueueSpawn(demo, player, xCm, yCm + origin + (i * spacingCm), 1, scene.UnitType, scene.RIdx);
        }
    }

    private static void EnqueueSpawn(S4DeployDemoRuntime demo, int player, int xCm, int yCm, int count, int unitType, int rIdx)
    {
        demo.EnqueueCommand(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = player,
            XCm = xCm,
            YCm = yCm,
            Count = count,
            UnitType = unitType,
            RIdx = rIdx,
        });
    }

    private CrowdFogCheckScene RequireScene()
    {
        if (_scene != null)
        {
            return _scene;
        }

        if (_engine == null)
        {
            throw new InvalidOperationException("玩家视图核对场景需要虚拟文件系统。");
        }

        using var stream = _engine.VFS.GetStream(_config.FogSceneUri);
        using var doc = JsonDocument.Parse(stream);
        _scene = CrowdFogCheckScene.Load(doc.RootElement, _config.FogSceneUri);
        return _scene;
    }

    private void ReleaseBoxing()
    {
        if (_engine == null)
        {
            return;
        }

        if (!_boxingReady)
        {
            if (!_engine.TryGetService(CoreServiceKeys.InteractionContextProfileRegistry, out InteractionContextProfileRegistry? profiles) ||
                profiles == null ||
                !profiles.ProfileIdRegistry.TryGetId(_config.BoxingContextId, out _boxingProfileId))
            {
                throw new InvalidOperationException($"玩家视图找不到拳击上下文 {_config.BoxingContextId}。");
            }

            _boxingContextKeyId = ConfigKeyRegistry.GetId(_config.BoxingContextId);
            _boxingReady = true;
        }

        if (!_engine.TryGetService(CoreServiceKeys.InteractionContextInstances, out InteractionContextInstanceRuntime? contexts) ||
            contexts == null ||
            !_engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) ||
            seats == null ||
            !seats.TryGetSoleSeat(out ClientLocalSeat seat) ||
            seat.PossessedRep == Entity.Null)
        {
            return;
        }

        if (contexts.IsActive(seat.PossessedRep, _boxingProfileId))
        {
            contexts.Deactivate(seat.PossessedRep, _boxingContextKeyId);
        }
    }

    private void NoteRipple(CrowdSimSession session)
    {
        int count = session.Commands.Log.Count;
        if (!_logArmed)
        {
            _seenLog = count;
            _logArmed = true;
            return;
        }

        for (int i = _seenLog; i < count; i++)
        {
            CrowdSimCommand cmd = session.Commands.Log[i].Cmd;
            if (cmd.Kind != CrowdSimCommandKind.Order || cmd.Player != _player)
            {
                continue;
            }

            _rippleX = cmd.XCm;
            _rippleY = cmd.YCm;
            _rippleStart = Environment.TickCount64;
        }

        _seenLog = count;
    }

    private void DrawStructures(CrowdSimSession session, RouteVisualBuffer routes, int viewer)
    {
        var fog = session.Fog;
        if (_fogHide && fog != null)
        {
            if ((uint)viewer >= (uint)fog.Belief.Length)
            {
                throw new InvalidOperationException($"视野组 {viewer} 没有认知。");
            }

            var belief = fog.Belief[viewer];
            if (belief.Count > _structures.Length)
            {
                throw new InvalidOperationException("玩家视图认知结构超出 maxBeliefMarkers。");
            }

            int n = 0;
            foreach (var pair in belief)
            {
                WriteFootprint(routes, pair.Value.Item2, n++);
            }

            return;
        }

        CrowdStructuresStore? store = session.Structures;
        if (store == null)
        {
            return;
        }

        var ids = store.EntityIds;
        if (ids.Count > _structures.Length)
        {
            throw new InvalidOperationException("玩家视图结构超出 maxBeliefMarkers。");
        }

        for (int i = 0; i < ids.Count; i++)
        {
            if (store.TryGetFootprint(ids[i], out CrowdStructureFootprint footprint))
            {
                WriteFootprint(routes, footprint, i);
            }
        }
    }

    private void WriteFootprint(RouteVisualBuffer routes, CrowdStructureFootprint footprint, int index)
    {
        var box = footprint.Bbox();
        float minX = WorldUnits.CmToM(box.MinX.ToFloat());
        float minY = WorldUnits.CmToM(box.MinY.ToFloat());
        float maxX = WorldUnits.CmToM(box.MaxX.ToFloat());
        float maxY = WorldUnits.CmToM(box.MaxY.ToFloat());
        Vector2[] pts = _structures[index];
        pts[0] = new Vector2(minX, minY);
        pts[1] = new Vector2(maxX, minY);
        pts[2] = new Vector2(maxX, maxY);
        pts[3] = new Vector2(minX, maxY);
        pts[4] = new Vector2(minX, minY);
        routes.AddPolylineShared(new RouteVisualId(StructureIdBase + index), pts, _routeThicknessVisual, _ink);
    }

    private void DrawBox(ScreenOverlayBuffer overlay)
    {
        int x0 = (int)_boxX0;
        int y0 = (int)_boxY0;
        int x1 = (int)_boxX1;
        int y1 = (int)_boxY1;
        int left = Math.Min(x0, x1);
        int top = Math.Min(y0, y1);
        int width = Math.Abs(x1 - x0);
        int height = Math.Abs(y1 - y0);
        if (width < 1)
        {
            width = 1;
        }

        if (height < 1)
        {
            height = 1;
        }

        overlay.AddRect(left, top, width, height, _paper, _plan);
    }

    private void DrawOrderFeedback(CrowdSimSession session, RouteVisualBuffer routes, int viewer, CrowdLeader leader, int goal, bool unfinished)
    {
        int side = session.Config.NavCellsPerSide;
        int cell = session.Config.NavCellSizeCm;
        if (goal < 0 || side < 1)
        {
            return;
        }

        int gx = ((goal % side) * cell) + (cell / 2);
        int gy = ((goal / side) * cell) + (cell / 2);
        if (!CrowdPlayerVisibility.PointShown(session, viewer, gx, gy, _fogHide))
        {
            return;
        }

        float goalX = WorldUnits.CmToM(gx);
        float goalY = WorldUnits.CmToM(gy);
        if (unfinished)
        {
            float arm = _crossArmVisual;
            _crossH[0] = new Vector2(goalX - arm, goalY);
            _crossH[1] = new Vector2(goalX + arm, goalY);
            _crossV[0] = new Vector2(goalX, goalY - arm);
            _crossV[1] = new Vector2(goalX, goalY + arm);
            routes.AddPolylineShared(new RouteVisualId(GoalCrossHId), _crossH, _routeThicknessVisual, _goal);
            routes.AddPolylineShared(new RouteVisualId(GoalCrossVId), _crossV, _routeThicknessVisual, _goal);
        }

        if (!_route)
        {
            return;
        }

        float leadX = WorldUnits.CmToM(leader.XCm.ToFloat());
        float leadY = WorldUnits.CmToM(leader.YCm.ToFloat());
        float dx = goalX - leadX;
        float dy = goalY - leadY;
        float len = MathF.Sqrt((dx * dx) + (dy * dy));
        float period = _dashVisual + _gapVisual;
        int segments = 0;
        float t = 0f;
        while (len > 0f && t < len)
        {
            if (segments >= _dashes.Length)
            {
                throw new InvalidOperationException("玩家路线虚线超出 maxDashes。");
            }

            float end = MathF.Min(len, t + _dashVisual);
            Vector2[] seg = _dashes[segments];
            seg[0] = new Vector2(leadX + (dx * (t / len)), leadY + (dy * (t / len)));
            seg[1] = new Vector2(leadX + (dx * (end / len)), leadY + (dy * (end / len)));
            routes.AddPolylineShared(new RouteVisualId(DashIdBase + segments), seg, _routeThicknessVisual, _plan);
            segments++;
            t += period;
        }

        routes.AddMarker(
            new RouteVisualId(RouteEndId),
            new Vector2(goalX, goalY),
            RouteVisualMarkerShape.Diamond,
            _markerRadiusVisual,
            _routeThicknessVisual,
            _goal);
    }

    private void PublishText(CrowdOrder? latest, CrowdCommandCardState card, int planning, int moving, int arrived, int other)
    {
        int shape = latest == null ? -1 : ShapeIndex(latest.ShapeId);
        int mode = latest == null ? -1 : (int)latest.Mode;
        int orderId = latest?.Id ?? 0;
        int flags = (_fogHide ? 1 : 0) | (_route ? 2 : 0) | (_lineOfSight ? 4 : 0) | (Prefs.MagicBox ? 8 : 0);
        int radius = (_radiusMin << 16) | _radiusMax;
        int click = (_clickCount << 16) ^ (_clickUnitType << 8) ^ _clickRadius;
        if (_textReady &&
            _sigPlayer == _player &&
            _sigOrder == orderId &&
            _sigMode == mode &&
            _sigShape == shape &&
            _sigPlanning == planning &&
            _sigMoving == moving &&
            _sigArrived == arrived &&
            _sigOther == other &&
            _sigFlags == flags &&
            _sigDeploy == _deployCount &&
            _sigRadius == radius &&
            _sigClick == click &&
            _sigScene == _sceneKind &&
            _sigFocus == _focus &&
            CountsSame())
        {
            return;
        }

        _sigPlayer = _player;
        _sigOrder = orderId;
        _sigMode = mode;
        _sigShape = shape;
        _sigPlanning = planning;
        _sigMoving = moving;
        _sigArrived = arrived;
        _sigOther = other;
        _sigFlags = flags;
        _sigDeploy = _deployCount;
        _sigRadius = radius;
        _sigClick = click;
        _sigScene = _sceneKind;
        _sigFocus = _focus;
        Array.Copy(_counts, _sigCounts, _counts.Length);
        _textReady = true;

        _sb.Clear();
        for (int p = 1; p <= _playerCount; p++)
        {
            if (p > 1)
            {
                _sb.Append(' ');
            }

            _sb.Append('P');
            _sb.Append(p);
            _sb.Append(' ');
            _sb.Append(_counts[p]);
            if (p == _player)
            {
                _sb.Append('*');
            }
        }

        _teamBar = _sb.ToString();
        _sb.Clear();
        _sb.Append(CardLabel(card));
        _sb.Append(' ');
        _sb.Append(planning);
        _sb.Append('/');
        _sb.Append(moving);
        _sb.Append('/');
        _sb.Append(arrived);
        _sb.Append('/');
        _sb.Append(other);
        _statusLine = _sb.ToString();

        _sb.Clear();
        _sb.Append(shape >= 0 ? _shapeLabels[shape] : Prefs.ShapeId);
        _sb.Append(' ');
        _sb.Append(ModeLabel(mode));
        _sb.Append(' ');
        _sb.Append(Prefs.MagicBox ? _config.Labels.MagicOn : _config.Labels.MagicOff);
        _sb.Append(' ');
        _sb.Append(_fogHide ? _config.Labels.FogOn : _config.Labels.FogOff);
        _sb.Append(' ');
        _sb.Append(_route ? _config.Labels.RouteOn : _config.Labels.RouteOff);
        _sb.Append(' ');
        _sb.Append(_lineOfSight ? _config.Labels.LosOn : _config.Labels.LosOff);
        _modeLine = _sb.ToString();

        _sb.Clear();
        _sb.Append(_config.Labels.ClickSpawn);
        _sb.Append(' ');
        _sb.Append(_clickUnitType);
        _sb.Append(' ');
        _sb.Append(_clickRadius);
        _sb.Append(' ');
        _sb.Append(_clickCount);
        _clickLine = _sb.ToString();

        _sb.Clear();
        _sb.Append(_deployCount);
        _sb.Append(' ');
        _sb.Append(_radiusMin);
        _sb.Append('-');
        _sb.Append(_radiusMax);
        _sb.Append(' ');
        _sb.Append(_focus);
        if (_sceneKind == 1)
        {
            _sb.Append(' ');
            _sb.Append(_config.Labels.SceneOutside);
        }
        else if (_sceneKind == 2)
        {
            _sb.Append(' ');
            _sb.Append(_config.Labels.SceneBehind);
        }

        _deployLine = _sb.ToString();
    }

    private bool CountsSame()
    {
        for (int p = 1; p <= _playerCount; p++)
        {
            if (_counts[p] != _sigCounts[p])
            {
                return false;
            }
        }

        return true;
    }

    private void DrawChrome(ScreenOverlayBuffer overlay)
    {
        int swatch = _config.FontSize;
        int x = _config.TeamBarX;
        int y = _config.TeamBarY;
        for (int p = 1; p <= _playerCount; p++)
        {
            if (!_teamKnown[p])
            {
                continue;
            }

            overlay.AddRect(x, y, swatch, swatch, _teamColors[p], _teamColors[p]);
            x += swatch + _config.LinePx;
        }

        overlay.AddText(_config.TeamBarX, _config.TeamBarY + swatch + _config.LinePx, _teamBar, _config.FontSize, _text);
        overlay.AddRect(_config.StatusCardX, _config.StatusCardY, _config.StatusCardW, _config.StatusCardH, _card, _plan);
        int textX = _config.StatusCardX + _config.LinePx;
        int textY = _config.StatusCardY + _config.LinePx;
        int step = _config.FontSize + _config.LinePx;
        overlay.AddText(textX, textY, _statusLine, _config.FontSize, _text);
        overlay.AddText(textX, textY + step, _modeLine, _config.FontSize, _text);
        overlay.AddText(textX, textY + (step * 2), _clickLine, _config.FontSize, _text);
        overlay.AddText(textX, textY + (step * 3), _deployLine, _config.FontSize, _text);
    }

    private string CardLabel(CrowdCommandCardState card) => card switch
    {
        CrowdCommandCardState.Planning => _config.Labels.Planning,
        CrowdCommandCardState.Moving => _config.Labels.Moving,
        CrowdCommandCardState.Arrived => _config.Labels.Arrived,
        CrowdCommandCardState.Partial => _config.Labels.Partial,
        _ => _config.Labels.Idle,
    };

    private string ModeLabel(int mode) => mode switch
    {
        (int)CrowdOrderMode.Formation => _config.Labels.Formation,
        (int)CrowdOrderMode.Converge => _config.Labels.Converge,
        (int)CrowdOrderMode.Preserve => _config.Labels.Preserve,
        _ => _config.Labels.Idle,
    };

    private static bool TryLeader(CrowdSimSession session, CrowdOrder order, out CrowdLeader leader, out int goal, out bool unfinished)
    {
        leader = null!;
        goal = -1;
        unfinished = false;
        bool found = false;
        for (int i = 0; i < order.Groups.Count; i++)
        {
            if (!session.Groups.TryGet(order.Groups[i].GroupId, out CrowdNavGroupSet.Group group) || group.Leader == null)
            {
                continue;
            }

            if (!found || (group.Leader.Done == false && leader.Done))
            {
                leader = group.Leader;
                goal = group.Goal;
                unfinished = !group.Leader.Done;
                found = true;
            }
        }

        return found;
    }

    private static CrowdOrder? LatestOrder(CrowdSimSession session, int playerId)
    {
        CrowdOrder? best = null;
        int bestId = -1;
        var list = session.Orders.List;
        for (int i = 0; i < list.Count; i++)
        {
            CrowdOrder order = list[i];
            if (order.Player != playerId || order.Id <= bestId)
            {
                continue;
            }

            bestId = order.Id;
            best = order;
        }

        return best;
    }

    private int ViewerGroup(CrowdSimSession session)
    {
        var fog = session.Fog;
        if (fog == null)
        {
            return 0;
        }

        var indexBy = session.Config.PairRules.IndexByPlayerId;
        if ((uint)_player >= (uint)indexBy.Length)
        {
            throw new InvalidOperationException($"玩家 {_player} 不在配对表里。");
        }

        int index = indexBy[_player];
        if ((uint)index >= (uint)fog.GroupOf.Length)
        {
            throw new InvalidOperationException($"玩家 {_player} 没有视野组。");
        }

        return fog.GroupOf[index];
    }

    private void CacheTeamColors(CrowdSimSession session)
    {
        Array.Clear(_teamKnown);
        if (_engine == null ||
            !_engine.TryGetService(CoreServiceKeys.TeamColorPalette, out TeamColorPalette? palette) ||
            palette == null)
        {
            return;
        }

        int count = session.Config.PairRules.PlayerCount;
        for (int p = 1; p <= count && p < _teamColors.Length; p++)
        {
            if (palette.TryGet(p, out Vector4 color))
            {
                _teamColors[p] = color;
                _teamKnown[p] = true;
            }
        }
    }

    private int ShapeIndex(string? id)
    {
        for (int i = 0; i < _shapeIds.Length; i++)
        {
            if (string.Equals(_shapeIds[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void CycleRadius()
    {
        if (_radiusLast < 0)
        {
            return;
        }

        if (_radiusMax < _radiusLast)
        {
            _radiusMax++;
            return;
        }

        if (_radiusMin < _radiusLast)
        {
            _radiusMin++;
            _radiusMax = _radiusMin;
            return;
        }

        _radiusMin = 0;
        _radiusMax = _radiusLast;
    }

    private static void StepCount(ref int value, int direction, int step, int min, int max)
    {
        int next = value + (direction * step);
        if (next < min)
        {
            next = min;
        }
        else if (next > max)
        {
            next = max;
        }

        value = next;
    }

    private static bool Pressed(FrozenInputActionReader actions, string action) => actions.PressedThisTick(action);
}
