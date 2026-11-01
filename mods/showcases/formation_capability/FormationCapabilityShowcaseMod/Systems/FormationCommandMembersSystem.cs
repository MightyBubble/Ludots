using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.EntityCollections;
using Ludots.Core.Scripting;

namespace FormationCapabilityShowcaseMod.Systems;

/// <summary>
/// Players select formation anchors, but orders go to soldiers: this keeps the local player's
/// members collection equal to the soldiers of whatever the command source currently holds, so
/// the command graph orders that collection.
/// </summary>
internal sealed class FormationCommandMembersSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly FormationMemberResolver _members;
    private readonly string _commandSourceKey;
    private readonly string _membersKey;
    private readonly Entity[] _selected;
    private readonly Entity[] _resolved;
    private readonly Entity[] _published;
    private int _publishedCount = -1;
    private Entity _publishedOwner = Entity.Null;

    public FormationCommandMembersSystem(
        World world,
        Dictionary<string, object> globals,
        string commandSourceKey,
        string membersKey,
        int maxMembersPerFormation,
        int maxMemberCount)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        _commandSourceKey = string.IsNullOrWhiteSpace(commandSourceKey)
            ? throw new ArgumentException("Command source collection key is required.", nameof(commandSourceKey))
            : commandSourceKey;
        _membersKey = string.IsNullOrWhiteSpace(membersKey)
            ? throw new ArgumentException("Command members collection key is required.", nameof(membersKey))
            : membersKey;
        if (maxMemberCount < maxMembersPerFormation)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMemberCount));
        }

        _members = new FormationMemberResolver(world, maxMembersPerFormation);
        _selected = new Entity[maxMemberCount];
        _resolved = new Entity[maxMemberCount];
        _published = new Entity[maxMemberCount];
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity owner) || !_world.IsAlive(owner))
        {
            return;
        }

        EntityCollectionStore collections = _globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) &&
            storeObj is EntityCollectionStore store
                ? store
                : throw new InvalidOperationException("Formation command members require EntityCollectionStore.");

        int selectedCount = collections.CopyEntities(owner, _commandSourceKey, _selected);
        int count = 0;
        for (int i = 0; i < selectedCount; i++)
        {
            Span<Entity> remaining = _resolved.AsSpan(count);
            if (remaining.Length < _members.MaxMembersPerFormation)
            {
                throw new InvalidOperationException(
                    $"Formation command members exceed capacity {_resolved.Length} while resolving '{_commandSourceKey}'.");
            }

            count += _members.Resolve(_selected[i], remaining);
        }

        if (owner == _publishedOwner && count == _publishedCount &&
            _resolved.AsSpan(0, count).SequenceEqual(_published.AsSpan(0, count)))
        {
            return;
        }

        _resolved.AsSpan(0, count).CopyTo(_published);
        _publishedCount = count;
        _publishedOwner = owner;
        var descriptor = EntityCollectionDescriptor.Create(
            _membersKey,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.Display,
            owner,
            count > 0 ? _resolved[0] : Entity.Null,
            "Formation command members",
            "Soldiers of the selected formations.");
        collections.Replace(owner, descriptor, _resolved.AsSpan(0, count), owner);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }
}
