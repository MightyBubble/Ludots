using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Client;
using Ludots.Core.Physics3DNet.Input;
using Ludots.Core.Scripting;

namespace Physics3DMod;

internal sealed class Physics3DRuntime : IDisposable
{
    private GameEngine? _engine;
    private Physics3DWorld? _world;
    private Physics3DSimulationSystem? _system;
    private NetworkProcessRole _networkRole;
    private Physics3DNetworkPlayerLifecycle? _players;
    private Physics3DNetworkBodyRegistry? _bodyRegistry;
    private Physics3DNetworkBodyRegistrySystem? _bodyRegistrySystem;
    private Physics3DAuthoritativeReplicationSeatRuntimeFactory? _seatFactory;
    private Physics3DNetworkAoiInterestPort? _interest;
    private INetworkRuntimeObserver? _observer;
    private Physics3DAuthoritativeFixedInputSystem? _fixedInputSystem;
    private Physics3DReplicatedClientConvergence? _clientConvergence;

    public Task EnsureInstalledAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("Physics3DMod requires a live GameEngine.");
        if (_engine != null && !ReferenceEquals(_engine, engine))
        {
            throw new InvalidOperationException("Physics3DMod cannot attach one runtime to multiple GameEngine instances.");
        }

        if (_world != null || _system != null)
        {
            if (_world == null || _system == null)
            {
                throw new InvalidOperationException("Physics3DMod runtime is only partially installed.");
            }

            return Task.CompletedTask;
        }

        RequireServiceAbsent(engine, Physics3DServiceKeys.World);
        RequireServiceAbsent(engine, Physics3DServiceKeys.SimulationSystem);

        Physics3DWorldConfig worldConfig = new Physics3DWorldConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        int sourceFixedStepHz = FixedHzFromDeltaTime(Time.FixedDeltaTime);
        NetworkRuntimeConfig? network = engine.MergedConfig.Networking;
        NetworkProcessRole role = NetworkProcessRole.Standalone;
        Physics3DNetworkRuntimeConfig? physicsNetwork = null;
        if (network != null)
        {
            network.Validate();
            role = engine.GetService(CoreServiceKeys.NetworkProcessRole);
            if (role == NetworkProcessRole.Standalone)
            {
                throw new InvalidOperationException(
                    "Physics3D network composition requires the host to select an authoritative or replicated-client role before map load.");
            }

            physicsNetwork = new Physics3DNetworkRuntimeConfigLoader(engine.ConfigPipeline).Load(
                engine.ConfigCatalog,
                engine.ConfigConflictReport,
                network);
            if (network.SimulationTickRateHz != sourceFixedStepHz || worldConfig.FixedStepHz != sourceFixedStepHz)
            {
                throw new InvalidOperationException(
                    $"Physics3D, networking, and engine fixed-step rates must agree; got physics {worldConfig.FixedStepHz}Hz, networking {network.SimulationTickRateHz}Hz, engine {sourceFixedStepHz}Hz.");
            }

            if (worldConfig.MobileBodyCapacity < network.PlayerCapacity)
            {
                throw new InvalidOperationException(
                    $"Physics3D mobile body capacity {worldConfig.MobileBodyCapacity} is below player capacity {network.PlayerCapacity}.");
            }

            ValidateRoleServicesAbsent(engine, role);
        }

        var world = new Physics3DWorld(worldConfig);
        var system = new Physics3DSimulationSystem(
            engine.World,
            world,
            sourceFixedStepHz,
            worldConfig.MaximumPhysicsStepsPerSourceTick);
        Physics3DNetworkPlayerLifecycle? players = null;
        Physics3DNetworkBodyRegistry? bodyRegistry = null;
        Physics3DNetworkBodyRegistrySystem? bodyRegistrySystem = null;
        Physics3DAuthoritativeReplicationSeatRuntimeFactory? seatFactory = null;
        Physics3DNetworkAoiInterestPort? interest = null;
        INetworkRuntimeObserver? observer = null;
        Physics3DAuthoritativeFixedInputSystem? fixedInputSystem = null;
        ReplicationSchemaProjectorRegistry? projectors = null;
        ClientReplicationSchemaApplierRegistry? appliers = null;
        IReplicationSchemaProjector? projector = null;
        IClientReplicationSchemaApplier? applier = null;
        Physics3DReplicatedClientConvergence? clientConvergence = null;

        if (network != null && physicsNetwork != null)
        {
            if (role == NetworkProcessRole.AuthoritativeServer)
            {
                NetworkEntityTable entities = RequireService(engine, CoreServiceKeys.NetworkEntityTable);
                KnowledgeProjectionStore knowledge = RequireService(engine, CoreServiceKeys.KnowledgeProjectionStore);
                knowledge.ReserveRecords(physicsNetwork.KnowledgeRecordCapacity);
                projectors = RequireService(engine, CoreServiceKeys.ReplicationSchemaProjectors);
                RequireMutableSchemaSlot(projectors, physicsNetwork.ReplicationSchemaId);
                bodyRegistry = new Physics3DNetworkBodyRegistry(
                    engine.World,
                    world,
                    entities,
                    engine.GameSession.SimulationTicks,
                    physicsNetwork.ReplicationSchemaId,
                    physicsNetwork.BodyRegistryCommandCapacity);
                bodyRegistrySystem = new Physics3DNetworkBodyRegistrySystem(engine.World, bodyRegistry);
                players = new Physics3DNetworkPlayerLifecycle(
                    engine.World,
                    world,
                    entities,
                    knowledge,
                    network.PlayerCapacity,
                    physicsNetwork.ReplicationSchemaId,
                    physicsNetwork.PlayerBody,
                    physicsNetwork.PlayerSpawn);
                seatFactory = new Physics3DAuthoritativeReplicationSeatRuntimeFactory(
                    engine.World,
                    entities,
                    knowledge,
                    projectors,
                    network.PlayerCapacity,
                    network.ReplicationEntityCapacityPerSeat,
                    network.BaselineCapacity,
                    network.DisclosureChangeLogCapacity);
                interest = new Physics3DNetworkAoiInterestPort(
                    engine.World,
                    world,
                    entities,
                    players,
                    knowledge,
                    network.ReplicationEntityCapacityPerSeat,
                    physicsNetwork.Aoi);
                observer = new Physics3DNetworkPlayerLifecycleObserver(players);
                var inputSource = new Physics3DLazyAuthoritativeFixedInputSource(
                    network.PlayerCapacity,
                    checked((ushort)network.FixedInputSchemaId),
                    network.FixedInputFramePayloadBytes,
                    () => engine.TryGetService(
                        CoreServiceKeys.AuthoritativeFixedInputIngress,
                        out AuthoritativeFixedInputIngress ingress)
                            ? ingress
                            : null);
                var inputConsumer = new Physics3DAuthoritativeFixedInputConsumer(
                    inputSource,
                    players,
                    world,
                    physicsNetwork.Movement);
                fixedInputSystem = new Physics3DAuthoritativeFixedInputSystem(
                    engine.GameSession.SimulationTicks,
                    inputConsumer);
                projector = new Physics3DBodyReplicationProjector(
                    world,
                    physicsNetwork.ReplicationSchemaId,
                    physicsNetwork.Quantization);
            }
            else
            {
                appliers = RequireService(engine, CoreServiceKeys.ClientReplicationSchemaAppliers);
                RequireMutableSchemaSlot(appliers, physicsNetwork.ReplicationSchemaId);
                IPhysics3DClientInputSource clientInput = RequireService(
                    engine,
                    Physics3DNetworkServiceKeys.ClientInputSource);
                IPhysics3DLocalPredictionDriver predictionDriver = RequireService(
                    engine,
                    Physics3DNetworkServiceKeys.LocalPredictionDriver);
                clientConvergence = new Physics3DReplicatedClientConvergence(
                    engine.World,
                    world,
                    physicsNetwork.ClientConvergence,
                    network.GlobalNetworkEntityCapacity,
                    network.ReplicationEntityCapacityPerSeat,
                    clientInput,
                    predictionDriver);
                observer = new Physics3DClientNetworkRuntimeObserver(clientConvergence);
                applier = new Physics3DClientBodyReplicationApplier(
                    world,
                    physicsNetwork.ReplicationSchemaId,
                    physicsNetwork.Quantization,
                    physicsNetwork.PlayerBody,
                    clientConvergence);
            }
        }

        bool simulationRegistered = false;
        bool bodyRegistrySystemRegistered = false;
        bool fixedInputRegistered = false;
        bool worldServiceSet = false;
        bool simulationSystemServiceSet = false;
        bool controllerServiceSet = false;
        bool seatFactoryServiceSet = false;
        bool interestServiceSet = false;
        bool observerServiceSet = false;
        bool bodyRegistryServiceSet = false;
        bool clientConvergenceRegistered = false;
        bool clientConvergenceServiceSet = false;
        bool fixedInputPayloadSourceServiceSet = false;
        try
        {
            engine.RegisterSystem(system, SystemGroup.InputCollection);
            simulationRegistered = true;
            if (bodyRegistrySystem != null)
            {
                engine.RegisterSystem(bodyRegistrySystem, SystemGroup.RuntimeEntityBinding);
                bodyRegistrySystemRegistered = true;
            }

            if (fixedInputSystem != null)
            {
                engine.InsertSystemBeforeRequired<Physics3DSimulationSystem>(
                    fixedInputSystem,
                    SystemGroup.InputCollection);
                fixedInputRegistered = true;
            }

            if (clientConvergence != null)
            {
                engine.RegisterPresentationSystem(clientConvergence);
                clientConvergenceRegistered = true;
            }

            engine.SetService(Physics3DServiceKeys.World, (IPhysics3DWorld)world);
            worldServiceSet = true;
            engine.SetService(Physics3DServiceKeys.SimulationSystem, system);
            simulationSystemServiceSet = true;

            if (role == NetworkProcessRole.AuthoritativeServer)
            {
                engine.SetService(Physics3DNetworkServiceKeys.BodyRegistry, bodyRegistry!);
                bodyRegistryServiceSet = true;
                engine.SetService(
                    CoreServiceKeys.AuthoritativeSeatControllerResolver,
                    (IAuthoritativeSeatControllerResolver)players!);
                controllerServiceSet = true;
                engine.SetService(
                    CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory,
                    (IAuthoritativeReplicationSeatRuntimeFactory)seatFactory!);
                seatFactoryServiceSet = true;
                engine.SetService(
                    CoreServiceKeys.AuthoritativeReplicationInterest,
                    (IAuthoritativeReplicationInterestPort)interest!);
                interestServiceSet = true;
            }
            else if (role == NetworkProcessRole.ReplicatedClient)
            {
                engine.SetService(Physics3DNetworkServiceKeys.ClientConvergence, clientConvergence!);
                clientConvergenceServiceSet = true;
                engine.SetService(CoreServiceKeys.FixedInputPayloadSource, (IFixedInputPayloadSource)clientConvergence!);
                fixedInputPayloadSourceServiceSet = true;
            }

            if (observer != null)
            {
                engine.SetService(CoreServiceKeys.NetworkRuntimeObserverBridge, observer);
                observerServiceSet = true;
            }

            if (projectors != null &&
                projectors.Register(physicsNetwork!.ReplicationSchemaId, projector!) != ReplicationSchemaRegistrationResult.Success)
            {
                throw new InvalidOperationException(
                    $"Physics3D replication projector schema {physicsNetwork.ReplicationSchemaId} could not be registered.");
            }

            if (appliers != null &&
                appliers.Register(physicsNetwork!.ReplicationSchemaId, applier!) != ReplicationSchemaRegistrationResult.Success)
            {
                throw new InvalidOperationException(
                    $"Physics3D client applier schema {physicsNetwork.ReplicationSchemaId} could not be registered.");
            }
        }
        catch
        {
            if (fixedInputPayloadSourceServiceSet) engine.RemoveService(CoreServiceKeys.FixedInputPayloadSource);
            if (clientConvergenceServiceSet) engine.RemoveService(Physics3DNetworkServiceKeys.ClientConvergence);
            if (observerServiceSet) engine.RemoveService(CoreServiceKeys.NetworkRuntimeObserverBridge);
            if (interestServiceSet) engine.RemoveService(CoreServiceKeys.AuthoritativeReplicationInterest);
            if (seatFactoryServiceSet) engine.RemoveService(CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory);
            if (controllerServiceSet) engine.RemoveService(CoreServiceKeys.AuthoritativeSeatControllerResolver);
            if (bodyRegistryServiceSet) engine.RemoveService(Physics3DNetworkServiceKeys.BodyRegistry);
            if (simulationSystemServiceSet) engine.RemoveService(Physics3DServiceKeys.SimulationSystem);
            if (worldServiceSet) engine.RemoveService(Physics3DServiceKeys.World);
            if (fixedInputRegistered) engine.UnregisterSystem(fixedInputSystem!, SystemGroup.InputCollection);
            if (clientConvergenceRegistered) engine.UnregisterPresentationSystem(clientConvergence!);
            if (bodyRegistrySystemRegistered) engine.UnregisterSystem(bodyRegistrySystem!, SystemGroup.RuntimeEntityBinding);
            if (simulationRegistered) engine.UnregisterSystem(system, SystemGroup.InputCollection);
            interest?.Dispose();
            bodyRegistry?.Dispose();
            players?.Dispose();
            clientConvergence?.Dispose();
            world.Dispose();
            throw;
        }

        _engine = engine;
        _world = world;
        _system = system;
        _networkRole = role;
        _players = players;
        _bodyRegistry = bodyRegistry;
        _bodyRegistrySystem = bodyRegistrySystem;
        _seatFactory = seatFactory;
        _interest = interest;
        _observer = observer;
        _fixedInputSystem = fixedInputSystem;
        _clientConvergence = clientConvergence;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_engine == null)
        {
            if (_world != null || _system != null)
            {
                throw new InvalidOperationException("Physics3DMod runtime lost its owning GameEngine.");
            }

            return;
        }

        if (_world == null || _system == null)
        {
            throw new InvalidOperationException("Physics3DMod runtime is only partially installed.");
        }

        RequireOwnedService(_engine, Physics3DServiceKeys.World, (IPhysics3DWorld)_world);
        RequireOwnedService(_engine, Physics3DServiceKeys.SimulationSystem, _system);
        if (_observer != null)
        {
            RequireOwnedService(_engine, CoreServiceKeys.NetworkRuntimeObserverBridge, _observer);
        }

        if (_clientConvergence != null)
        {
            RequireOwnedService(
                _engine,
                Physics3DNetworkServiceKeys.ClientConvergence,
                _clientConvergence);
            RequireOwnedService(
                _engine,
                CoreServiceKeys.FixedInputPayloadSource,
                (IFixedInputPayloadSource)_clientConvergence);
        }

        if (_networkRole == NetworkProcessRole.AuthoritativeServer)
        {
            RequireOwnedService(
                _engine,
                CoreServiceKeys.AuthoritativeSeatControllerResolver,
                (IAuthoritativeSeatControllerResolver)_players!);
            RequireOwnedService(
                _engine,
                Physics3DNetworkServiceKeys.BodyRegistry,
                _bodyRegistry!);
            RequireOwnedService(
                _engine,
                CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory,
                (IAuthoritativeReplicationSeatRuntimeFactory)_seatFactory!);
            RequireOwnedService(
                _engine,
                CoreServiceKeys.AuthoritativeReplicationInterest,
                (IAuthoritativeReplicationInterestPort)_interest!);
        }

        if (_fixedInputSystem != null &&
            !_engine.UnregisterSystem(_fixedInputSystem, SystemGroup.InputCollection))
        {
            throw new InvalidOperationException("Physics3DMod fixed-input system was not registered during unload.");
        }

        if (_bodyRegistrySystem != null &&
            !_engine.UnregisterSystem(_bodyRegistrySystem, SystemGroup.RuntimeEntityBinding))
        {
            throw new InvalidOperationException("Physics3DMod network body registry system was not registered during unload.");
        }

        if (_clientConvergence != null &&
            !_engine.UnregisterPresentationSystem(_clientConvergence))
        {
            throw new InvalidOperationException("Physics3DMod client convergence presentation system was not registered during unload.");
        }

        if (!_engine.UnregisterSystem(_system, SystemGroup.InputCollection))
        {
            throw new InvalidOperationException("Physics3DMod simulation system was not registered during unload.");
        }

        if (_observer != null && !_engine.RemoveService(CoreServiceKeys.NetworkRuntimeObserverBridge))
        {
            throw new InvalidOperationException("Physics3DMod network observer service was not registered during unload.");
        }

        if (_clientConvergence != null &&
            (!_engine.RemoveService(CoreServiceKeys.FixedInputPayloadSource) ||
             !_engine.RemoveService(Physics3DNetworkServiceKeys.ClientConvergence)))
        {
            throw new InvalidOperationException("Physics3DMod client convergence services were not registered during unload.");
        }

        if (_networkRole == NetworkProcessRole.AuthoritativeServer &&
            (!_engine.RemoveService(CoreServiceKeys.AuthoritativeReplicationInterest) ||
             !_engine.RemoveService(CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory) ||
             !_engine.RemoveService(CoreServiceKeys.AuthoritativeSeatControllerResolver) ||
             !_engine.RemoveService(Physics3DNetworkServiceKeys.BodyRegistry)))
        {
            throw new InvalidOperationException("Physics3DMod authoritative network services were not registered during unload.");
        }

        if (!_engine.RemoveService(Physics3DServiceKeys.SimulationSystem) ||
            !_engine.RemoveService(Physics3DServiceKeys.World))
        {
            throw new InvalidOperationException("Physics3DMod services were not registered during unload.");
        }

        _interest?.Dispose();
        _bodyRegistry?.Dispose();
        _players?.Dispose();
        _clientConvergence?.Dispose();
        _world.Dispose();
        _fixedInputSystem = null;
        _clientConvergence = null;
        _observer = null;
        _interest = null;
        _seatFactory = null;
        _bodyRegistrySystem = null;
        _bodyRegistry = null;
        _players = null;
        _system = null;
        _world = null;
        _networkRole = NetworkProcessRole.Standalone;
        _engine = null;
    }

    private static void ValidateRoleServicesAbsent(GameEngine engine, NetworkProcessRole role)
    {
        RequireServiceAbsent(engine, CoreServiceKeys.NetworkRuntimeObserverBridge);
        if (role == NetworkProcessRole.ReplicatedClient)
        {
            RequireServiceAbsent(engine, Physics3DNetworkServiceKeys.ClientConvergence);
            RequireServiceAbsent(engine, CoreServiceKeys.FixedInputPayloadSource);
            return;
        }

        if (role != NetworkProcessRole.AuthoritativeServer)
        {
            return;
        }

        RequireServiceAbsent(engine, CoreServiceKeys.AuthoritativeSeatControllerResolver);
        RequireServiceAbsent(engine, Physics3DNetworkServiceKeys.BodyRegistry);
        RequireServiceAbsent(engine, CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory);
        RequireServiceAbsent(engine, CoreServiceKeys.AuthoritativeReplicationInterest);
    }

    private static void RequireMutableSchemaSlot(ReplicationSchemaProjectorRegistry registry, int schemaId)
    {
        if (registry.IsFrozen || registry.TryGet(schemaId, out _))
        {
            throw new InvalidOperationException(
                $"Physics3D replication projector schema {schemaId} must be unclaimed in a mutable registry.");
        }
    }

    private static void RequireMutableSchemaSlot(ClientReplicationSchemaApplierRegistry registry, int schemaId)
    {
        if (registry.IsFrozen || registry.TryGet(schemaId, out _))
        {
            throw new InvalidOperationException(
                $"Physics3D client applier schema {schemaId} must be unclaimed in a mutable registry.");
        }
    }

    private static T RequireService<T>(GameEngine engine, ServiceKey<T> key)
        where T : class
    {
        return engine.GetService(key) ??
            throw new InvalidOperationException($"Physics3DMod requires service '{key.Name}'.");
    }

    private static void RequireServiceAbsent<T>(GameEngine engine, ServiceKey<T> key)
    {
        if (engine.TryGetService(key, out _))
        {
            throw new InvalidOperationException($"Physics3DMod cannot install because '{key.Name}' is already registered.");
        }
    }

    private static void RequireOwnedService<T>(GameEngine engine, ServiceKey<T> key, T expected)
        where T : class
    {
        if (!engine.TryGetService(key, out T current) || !ReferenceEquals(current, expected))
        {
            throw new InvalidOperationException($"Physics3DMod no longer owns registered service '{key.Name}'.");
        }
    }

    private static int FixedHzFromDeltaTime(float deltaTime)
    {
        if (!(deltaTime > 0f) || !float.IsFinite(deltaTime))
        {
            throw new InvalidOperationException($"Engine fixed delta time '{deltaTime}' is invalid for Physics3D.");
        }

        int fixedHz = (int)MathF.Round(1f / deltaTime);
        if (fixedHz <= 0 || MathF.Abs((1f / fixedHz) - deltaTime) > 1e-5f)
        {
            throw new InvalidOperationException(
                $"Engine fixed delta time '{deltaTime}' is not representable as 1/integer Hz for Physics3D.");
        }

        return fixedHz;
    }
}
