using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ludots.WebUI.DataPlane;

namespace CityRallyWebUiShowcaseMod.Runtime;

internal sealed class CityRallyCommandHandler : IWebUiCommandHandler
{
    private readonly CityRallyTopicProducer _producer;

    public CityRallyCommandHandler(CityRallyTopicProducer producer)
    {
        _producer = producer;
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_producer.ApplyCommand(request));
    }
}

internal sealed class CityRallyGenerationResolver : IWebUiEntityGenerationResolver
{
    public CityRallyGenerationResolver()
    {
    }

    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        return entityRef.StableId <= 0 && entityRef.Generation <= 0;
    }
}

internal sealed class CityRallyPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "selectEntity",
        "activateAbilitySlot",
        "switchParticipantView",
        "cancelPlanting",
        "rightClick",
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (AllowedCommands.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in CityRallyWebUiShowcaseMod.";
        return false;
    }
}
