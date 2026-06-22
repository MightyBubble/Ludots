using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Engine.TimeFlow
{
    public sealed class TimeFlowService
    {
        public const int DefaultScalePermille = 1000;
        public const int MaxScalePermille = 8000;

        private readonly StringIntRegistry _domainIds =
            new(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);

        private readonly List<DomainState> _domains = new() { new DomainState() };
        private readonly List<TokenState> _tokens = new() { new TokenState() };

        public TimeFlowService()
        {
            EnsureDomain(TimeFlowDomainIds.Simulation, parentName: null, baseScalePermille: DefaultScalePermille);
            EnsureDomain(TimeFlowDomainIds.Gas, TimeFlowDomainIds.Simulation, DefaultScalePermille);
            EnsureDomain(TimeFlowDomainIds.Physics2D, TimeFlowDomainIds.Simulation, DefaultScalePermille);
        }

        public int EnsureDomain(string name, string? parentName = null, int baseScalePermille = DefaultScalePermille)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Domain name must not be empty.", nameof(name));
            }

            bool hasExplicitParent = !string.IsNullOrWhiteSpace(parentName);
            int parentId = 0;
            if (hasExplicitParent)
            {
                parentId = EnsureDomain(parentName!, parentName: null, baseScalePermille: DefaultScalePermille);
            }

            if (_domainIds.TryGetId(name, out int existingId))
            {
                DomainState existing = _domains[existingId];
                if (hasExplicitParent && existing.ParentDomainId != parentId)
                {
                    throw new InvalidOperationException(
                        $"Time domain '{name}' is already registered under parent '{GetDomainName(existing.ParentDomainId)}'.");
                }

                return existingId;
            }

            int id = _domainIds.Register(name);
            while (_domains.Count <= id)
            {
                _domains.Add(default);
            }

            _domains[id] = new DomainState
            {
                Name = name,
                ParentDomainId = parentId,
                BaseScalePermille = ClampScalePermille(baseScalePermille),
                EffectiveScalePermille = ClampScalePermille(baseScalePermille),
                Paused = false,
                ModifierCount = 0
            };

            RecalculateAllDomains();
            return id;
        }

        public string GetDomainName(int domainId)
        {
            if (domainId <= 0 || domainId >= _domains.Count)
            {
                return string.Empty;
            }

            return _domains[domainId].Name ?? string.Empty;
        }

        public TimeFlowToken AcquireScaleToken(string domainName, int scalePermille, string owner, string reason = "")
        {
            int domainId = EnsureDomain(domainName);
            int tokenId = _tokens.Count;
            _tokens.Add(new TokenState
            {
                DomainId = domainId,
                Kind = TokenKind.Scale,
                ScalePermille = ClampScalePermille(scalePermille),
                Owner = owner ?? string.Empty,
                Reason = reason ?? string.Empty,
                Active = true
            });

            RecalculateAllDomains();
            return new TimeFlowToken(tokenId);
        }

        public TimeFlowToken AcquirePauseToken(string domainName, string owner, string reason = "")
        {
            int domainId = EnsureDomain(domainName);
            int tokenId = _tokens.Count;
            _tokens.Add(new TokenState
            {
                DomainId = domainId,
                Kind = TokenKind.Pause,
                ScalePermille = 0,
                Owner = owner ?? string.Empty,
                Reason = reason ?? string.Empty,
                Active = true
            });

            RecalculateAllDomains();
            return new TimeFlowToken(tokenId);
        }

        public void ReleaseToken(TimeFlowToken token)
        {
            if (!TryGetActiveToken(token, out int tokenIndex))
            {
                return;
            }

            TokenState state = _tokens[tokenIndex];
            state.Active = false;
            RecalculateAllDomains();
        }

        public int GetEffectiveScalePermille(string domainName)
        {
            int domainId = EnsureDomain(domainName);
            return _domains[domainId].EffectiveScalePermille;
        }

        public bool IsPaused(string domainName)
        {
            int domainId = EnsureDomain(domainName);
            return _domains[domainId].Paused;
        }

        private bool TryGetActiveToken(TimeFlowToken token, out int tokenIndex)
        {
            tokenIndex = token.Value;
            if (!token.IsValid || tokenIndex <= 0 || tokenIndex >= _tokens.Count)
            {
                return false;
            }

            return _tokens[tokenIndex].Active;
        }

        private void RecalculateAllDomains()
        {
            for (int domainId = 1; domainId < _domains.Count; domainId++)
            {
                RecalculateDomain(domainId);
            }
        }

        private void RecalculateDomain(int domainId)
        {
            DomainState domain = _domains[domainId];
            long localScalePermille = domain.BaseScalePermille;
            bool localPaused = false;
            int modifierCount = 0;

            for (int tokenId = 1; tokenId < _tokens.Count; tokenId++)
            {
                TokenState token = _tokens[tokenId];
                if (!token.Active || token.DomainId != domainId)
                {
                    continue;
                }

                modifierCount++;
                if (token.Kind == TokenKind.Pause)
                {
                    localPaused = true;
                    continue;
                }

                localScalePermille = (localScalePermille * token.ScalePermille) / DefaultScalePermille;
                localScalePermille = ClampScalePermille(localScalePermille);
            }

            if (domain.ParentDomainId > 0)
            {
                DomainState parent = _domains[domain.ParentDomainId];
                localPaused |= parent.Paused;
                localScalePermille = (localScalePermille * parent.EffectiveScalePermille) / DefaultScalePermille;
            }

            domain.ModifierCount = modifierCount;
            domain.Paused = localPaused || localScalePermille <= 0;
            domain.EffectiveScalePermille = domain.Paused
                ? 0
                : ClampScalePermille(localScalePermille);
        }

        private static int ClampScalePermille(long scalePermille)
        {
            if (scalePermille <= 0)
            {
                return 0;
            }

            if (scalePermille > MaxScalePermille)
            {
                return MaxScalePermille;
            }

            return (int)scalePermille;
        }

        private enum TokenKind : byte
        {
            Scale = 0,
            Pause = 1
        }

        private sealed class DomainState
        {
            public string? Name;
            public int ParentDomainId;
            public int BaseScalePermille;
            public int EffectiveScalePermille;
            public bool Paused;
            public int ModifierCount;
        }

        private sealed class TokenState
        {
            public int DomainId;
            public TokenKind Kind;
            public int ScalePermille;
            public string Owner = string.Empty;
            public string Reason = string.Empty;
            public bool Active;
        }
    }
}
