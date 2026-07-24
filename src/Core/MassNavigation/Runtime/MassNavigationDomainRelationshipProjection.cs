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

    public void ValidateResetDomains(ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        ValidateDomains(seeds, includeExistingDomains: false);
    }

    public void ValidateAppendDomains(ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        ValidateDomains(seeds, includeExistingDomains: true);
    }

    public void ResetDomains(ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        ValidateResetDomains(seeds);
        _domainById.Clear();
        AppendDomainsWithoutValidation(seeds);
    }

    public void AppendDomains(ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        ValidateAppendDomains(seeds);
        AppendDomainsWithoutValidation(seeds);
    }

    public bool IsCooperative(int sourceDomainId, int targetDomainId)
    {
        Entity source = RequireDomain(sourceDomainId);
        Entity target = RequireDomain(targetDomainId);
        return _stances.GetStance(source, target) == _cooperativeStanceId;
    }

    private void ValidateDomains(ReadOnlySpan<MassNavigationAgentSeed> seeds, bool includeExistingDomains)
    {
        int newDomainCount = 0;
        for (int i = 0; i < seeds.Length; i++)
        {
            Entity domain = seeds[i].DomainRep;
            if (domain == Entity.Null)
            {
                continue;
            }

            if (includeExistingDomains && _domainById.TryGetValue(domain.Id, out Entity existing))
            {
                if (existing != domain)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation domain id {domain.Id} resolved to more than one entity generation.");
                }

                continue;
            }

            if (HasEarlierDomain(seeds, i, domain))
            {
                continue;
            }

            newDomainCount++;
            int requiredDomainCount = includeExistingDomains ? _domainById.Count + newDomainCount : newDomainCount;
            if (requiredDomainCount > _capacity)
            {
                throw new InvalidOperationException(
                    $"MassNavigation relationship projection requires more than configured relationshipDomainCapacity {_capacity} domains.");
            }
        }
    }

    private static bool HasEarlierDomain(ReadOnlySpan<MassNavigationAgentSeed> seeds, int currentIndex, Entity domain)
    {
        for (int i = 0; i < currentIndex; i++)
        {
            Entity previous = seeds[i].DomainRep;
            if (previous == Entity.Null || previous.Id != domain.Id)
            {
                continue;
            }

            if (previous != domain)
            {
                throw new InvalidOperationException(
                    $"MassNavigation domain id {domain.Id} resolved to more than one entity generation.");
            }

            return true;
        }

        return false;
    }

    private void AppendDomainsWithoutValidation(ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        for (int i = 0; i < seeds.Length; i++)
        {
            Entity domain = seeds[i].DomainRep;
            if (domain == Entity.Null)
            {
                continue;
            }

            if (_domainById.TryGetValue(domain.Id, out _))
            {
                continue;
            }

            _domainById.Add(domain.Id, domain);
        }
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
