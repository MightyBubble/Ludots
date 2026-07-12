using Arch.Core;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.MassNavigation.Runtime;

public interface IMassNavigationDomainRelationshipProjection
{
    uint Revision { get; }
    bool IsCooperative(int sourceDomainId, int targetDomainId);
}

internal sealed class MassNavigationIdentityDomainRelationshipProjection : IMassNavigationDomainRelationshipProjection
{
    public static readonly MassNavigationIdentityDomainRelationshipProjection Instance = new();

    public uint Revision => 0;

    public bool IsCooperative(int sourceDomainId, int targetDomainId) => sourceDomainId == targetDomainId;
}

internal sealed class MassNavigationDomainStanceProjection : IMassNavigationDomainRelationshipProjection
{
    private readonly DomainStanceQuery _stances;
    private readonly Dictionary<int, Entity> _domainById;
    private readonly int _cooperativeStanceId;
    private readonly int _capacity;

    public MassNavigationDomainStanceProjection(
        DomainStanceQuery stances,
        int capacity,
        string cooperativeStance)
    {
        _stances = stances ?? throw new ArgumentNullException(nameof(stances));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (string.IsNullOrWhiteSpace(cooperativeStance) ||
            !stances.TryResolveStanceId(cooperativeStance, out _cooperativeStanceId))
        {
            throw new InvalidOperationException(
                $"MassNavigation relationship policy cooperative stance '{cooperativeStance}' is not registered in DomainStanceQuery.");
        }

        _capacity = capacity;
        _domainById = new Dictionary<int, Entity>(capacity);
    }

    public uint Revision => _stances.Revision;

    public void ResetDomains(ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        _domainById.Clear();
        AppendDomains(seeds);
    }

    public void AppendDomains(ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        for (int i = 0; i < seeds.Length; i++)
        {
            Entity domain = seeds[i].DomainRep;
            if (domain == Entity.Null)
            {
                continue;
            }

            if (_domainById.TryGetValue(domain.Id, out Entity existing))
            {
                if (existing != domain)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation domain id {domain.Id} resolved to more than one entity generation.");
                }

                continue;
            }

            if (_domainById.Count >= _capacity)
            {
                throw new InvalidOperationException(
                    $"MassNavigation relationship projection requires more than configured relationshipDomainCapacity {_capacity} domains.");
            }

            _domainById.Add(domain.Id, domain);
        }
    }

    public bool IsCooperative(int sourceDomainId, int targetDomainId)
    {
        Entity source = RequireDomain(sourceDomainId);
        Entity target = RequireDomain(targetDomainId);
        return _stances.GetStance(source, target) == _cooperativeStanceId;
    }

    private Entity RequireDomain(int domainId)
    {
        if (!_domainById.TryGetValue(domainId, out Entity domain))
        {
            throw new InvalidOperationException(
                $"MassNavigation relationship projection has no bound representative for domain id {domainId}.");
        }

        return domain;
    }
}
