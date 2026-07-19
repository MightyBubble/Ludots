using System;
using Arch.Core;

namespace Ludots.Core.Input.Interaction;

/// <summary>
/// Optional Command Router extension that expands one routed cluster actor into executable actors.
/// Implementations own their domain membership lookup; the router owns validation and batch submission.
/// </summary>
public interface ICommandActorExpander
{
    int MaxExpandedActorsPerSource { get; }
    int MaxExpandedActorCount { get; }

    int Expand(Entity source, Span<Entity> destination);
}
