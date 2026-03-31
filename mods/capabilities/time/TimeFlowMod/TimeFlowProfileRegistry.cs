namespace TimeFlowMod;

public sealed class TimeFlowProfileRegistry
{
    private readonly Dictionary<string, TimeFlowProfile> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _profiles.Count;

    public static TimeFlowProfileRegistry FromConfig(TimeFlowConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        TimeFlowProfileRegistry registry = new();
        foreach ((string id, TimeFlowProfileConfig profileConfig) in config.Profiles)
        {
            Validate(id, profileConfig);
            registry._profiles[id] = new TimeFlowProfile
            {
                Id = id,
                Description = profileConfig.Description ?? string.Empty,
                GlobalTimeScale = profileConfig.GlobalTimeScale,
                SimulationScalePermille = profileConfig.SimulationScalePermille,
                GasScalePermille = profileConfig.GasScalePermille,
                Physics2DScalePermille = profileConfig.Physics2DScalePermille,
                Navigation2DScalePermille = profileConfig.Navigation2DScalePermille,
                TasksScalePermille = profileConfig.TasksScalePermille,
                LoopMode = profileConfig.LoopMode,
                GasMode = profileConfig.GasMode,
                GasStepEveryFixedTicks = profileConfig.GasStepEveryFixedTicks,
                PhysicsTargetHz = profileConfig.PhysicsTargetHz,
                PhysicsMaxStepsPerFixedTick = profileConfig.PhysicsMaxStepsPerFixedTick,
                NavigationTargetHz = profileConfig.NavigationTargetHz,
                NavigationMaxStepsPerFixedTick = profileConfig.NavigationMaxStepsPerFixedTick
            };
        }

        return registry;
    }

    public bool TryGet(string id, out TimeFlowProfile profile)
    {
        return _profiles.TryGetValue(id, out profile!);
    }

    public IReadOnlyCollection<TimeFlowProfile> GetAll()
    {
        return _profiles.Values;
    }

    private static void Validate(string id, TimeFlowProfileConfig config)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("TimeFlow profile id must not be empty.");
        }

        if (config.GlobalTimeScale.HasValue && config.GlobalTimeScale.Value < 0f)
        {
            throw new InvalidOperationException($"TimeFlow profile '{id}' has negative GlobalTimeScale.");
        }

        ValidateScalePermille(id, nameof(config.SimulationScalePermille), config.SimulationScalePermille);
        ValidateScalePermille(id, nameof(config.GasScalePermille), config.GasScalePermille);
        ValidateScalePermille(id, nameof(config.Physics2DScalePermille), config.Physics2DScalePermille);
        ValidateScalePermille(id, nameof(config.Navigation2DScalePermille), config.Navigation2DScalePermille);
        ValidateScalePermille(id, nameof(config.TasksScalePermille), config.TasksScalePermille);

        if (config.GasStepEveryFixedTicks.HasValue && config.GasStepEveryFixedTicks.Value < 1)
        {
            throw new InvalidOperationException($"TimeFlow profile '{id}' has invalid GasStepEveryFixedTicks.");
        }

        if (config.PhysicsTargetHz.HasValue && config.PhysicsTargetHz.Value < 0)
        {
            throw new InvalidOperationException($"TimeFlow profile '{id}' has invalid PhysicsTargetHz.");
        }

        if (config.NavigationTargetHz.HasValue && config.NavigationTargetHz.Value < 0)
        {
            throw new InvalidOperationException($"TimeFlow profile '{id}' has invalid NavigationTargetHz.");
        }

        if (config.PhysicsMaxStepsPerFixedTick.HasValue && config.PhysicsMaxStepsPerFixedTick.Value < 1)
        {
            throw new InvalidOperationException($"TimeFlow profile '{id}' has invalid PhysicsMaxStepsPerFixedTick.");
        }

        if (config.NavigationMaxStepsPerFixedTick.HasValue && config.NavigationMaxStepsPerFixedTick.Value < 1)
        {
            throw new InvalidOperationException($"TimeFlow profile '{id}' has invalid NavigationMaxStepsPerFixedTick.");
        }
    }

    private static void ValidateScalePermille(string profileId, string fieldName, int? value)
    {
        if (value.HasValue && value.Value < 0)
        {
            throw new InvalidOperationException($"TimeFlow profile '{profileId}' has invalid {fieldName}.");
        }
    }
}
