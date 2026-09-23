using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Commands;

namespace Ludots.Core.Scripting
{
    public enum TriggerOverrideMode
    {
        Replace = 0,
        Wrap = 1,
    }

    /// <summary>
    /// Explicit C# trigger override authored by a mod. Replace swaps the base trigger
    /// for the factory's replacement; Wrap runs the base between Pre and Post commands
    /// (Cancel suppresses the base). Every override is attributed to a mod and must be
    /// unique per target: two mods overriding the same trigger type fail unless one is
    /// downstream of the other (dependency order decides, downstream wins).
    /// </summary>
    public sealed class TriggerOverrideSpec
    {
        public string Id { get; init; } = string.Empty;
        public Type TargetType { get; init; } = null!;
        public TriggerOverrideMode Mode { get; init; } = TriggerOverrideMode.Replace;
        public Func<Trigger, Trigger>? Replacement { get; init; }
        public IReadOnlyList<GameCommand>? Pre { get; init; }
        public IReadOnlyList<GameCommand>? Post { get; init; }
        public bool Cancel { get; init; }
    }

    /// <summary>
    /// Registration surface for C# trigger overrides. Mods register at OnLoad; the
    /// registry arbitrates by mod dependency order (downstream wins) and fails closed
    /// on a diamond (two independent mods overriding the same trigger type) or an
    /// unresolvable target type.
    /// </summary>
    public sealed class TriggerOverrideRegistry
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EmptyClosure
            = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        private readonly List<RegisteredOverride> _overrides = new();
        private IReadOnlyDictionary<string, IReadOnlySet<string>> _dependencyClosure = EmptyClosure;

        public void SetDependencyClosure(IReadOnlyDictionary<string, IReadOnlySet<string>> dependencyClosure)
        {
            _dependencyClosure = dependencyClosure ?? EmptyClosure;
        }

        public void Clear()
        {
            _overrides.Clear();
            _dependencyClosure = EmptyClosure;
        }

        public void Register(TriggerOverrideSpec spec, string modId)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Override attribution mod id is required.", nameof(modId));
            }

            if (string.IsNullOrWhiteSpace(spec.Id))
            {
                throw new ArgumentException("Override id is required.", nameof(spec));
            }

            if (spec.TargetType == null || !typeof(Trigger).IsAssignableFrom(spec.TargetType))
            {
                throw new InvalidOperationException(
                    $"Mod '{modId}' override '{spec.Id}' targets '{spec.TargetType?.FullName ?? "<null>"}' which is not a trigger type; "
                    + "overriding a non-existent trigger fails closed.");
            }

            if (!Enum.IsDefined(typeof(TriggerOverrideMode), spec.Mode))
            {
                throw new ArgumentOutOfRangeException(nameof(spec), "Undefined trigger override mode.");
            }

            if (spec.Mode == TriggerOverrideMode.Replace && spec.Replacement == null)
            {
                throw new InvalidOperationException(
                    $"Mod '{modId}' override '{spec.Id}' declares Replace without a Replacement factory.");
            }

            if (spec.Mode == TriggerOverrideMode.Wrap
                && (spec.Pre == null || spec.Pre.Count == 0)
                && (spec.Post == null || spec.Post.Count == 0)
                && !spec.Cancel)
            {
                throw new InvalidOperationException(
                    $"Mod '{modId}' override '{spec.Id}' declares Wrap with no Pre, Post, or Cancel; the wrap is a no-op.");
            }

            int incumbentIndex = -1;
            for (int i = 0; i < _overrides.Count; i++)
            {
                if (_overrides[i].Spec.TargetType == spec.TargetType)
                {
                    incumbentIndex = i;
                    break;
                }
            }

            if (incumbentIndex < 0)
            {
                _overrides.Add(new RegisteredOverride(spec, modId));
                return;
            }

            RegisteredOverride incumbent = _overrides[incumbentIndex];
            if (string.Equals(incumbent.ModId, modId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Mod '{modId}' declares override '{spec.Id}' for trigger '{spec.TargetType.Name}' "
                    + $"which is already overridden by the same mod ('{incumbent.Spec.Id}').");
            }

            if (IsDownstream(modId, incumbent.ModId))
            {
                _overrides[incumbentIndex] = new RegisteredOverride(spec, modId);
                return;
            }

            if (IsDownstream(incumbent.ModId, modId))
            {
                throw new InvalidOperationException(
                    $"Mod '{modId}' override '{spec.Id}' for trigger '{spec.TargetType.Name}' is upstream of mod "
                    + $"'{incumbent.ModId}' override '{incumbent.Spec.Id}' and cannot override it.");
            }

            throw new InvalidOperationException(
                $"Mods '{incumbent.ModId}' and '{modId}' both override trigger '{spec.TargetType.Name}' with no "
                + "dependency order between them; the override is ambiguous and fails closed.");
        }

        public bool TryGetApplicable(Type triggerType, out RegisteredOverride registered)
        {
            for (int i = _overrides.Count - 1; i >= 0; i--)
            {
                if (_overrides[i].Spec.TargetType == triggerType)
                {
                    registered = _overrides[i];
                    return true;
                }
            }

            registered = default;
            return false;
        }

        private bool IsDownstream(string modId, string upstreamId)
        {
            if (_dependencyClosure.TryGetValue(modId, out IReadOnlySet<string>? closure))
            {
                return closure.Contains(upstreamId);
            }

            return false;
        }

        public readonly struct RegisteredOverride
        {
            public RegisteredOverride(TriggerOverrideSpec spec, string modId)
            {
                Spec = spec;
                ModId = modId;
            }

            public TriggerOverrideSpec Spec { get; }
            public string ModId { get; }
        }
    }

    /// <summary>
    /// Runs a wrapped C# trigger: Pre commands, then the base (unless Cancel), then Post
    /// commands. Dispatch position (EventKey/Priority/arbitration keys) mirrors the base.
    /// </summary>
    public sealed class WrappedTrigger : Trigger
    {
        private readonly Trigger _base;
        private readonly string _overrideId;
        private readonly IReadOnlyList<GameCommand> _pre;
        private readonly IReadOnlyList<GameCommand> _post;
        private readonly bool _cancel;

        public WrappedTrigger(Trigger baseTrigger, TriggerOverrideSpec spec)
        {
            _base = baseTrigger ?? throw new ArgumentNullException(nameof(baseTrigger));
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            _overrideId = spec.Id;
            _pre = spec.Pre ?? Array.Empty<GameCommand>();
            _post = spec.Post ?? Array.Empty<GameCommand>();
            _cancel = spec.Cancel;
            EventKey = baseTrigger.EventKey;
            Priority = baseTrigger.Priority;
            ModRank = baseTrigger.ModRank;
            DeclarationIndex = baseTrigger.DeclarationIndex;
        }

        public override string Name => $"Wrap:{_overrideId}:{_base.Name}";

        public override bool CheckConditions(ScriptContext context) => _base.CheckConditions(context);

        public override async Task ExecuteAsync(ScriptContext context)
        {
            if (!CheckConditions(context))
            {
                return;
            }

            for (int i = 0; i < _pre.Count; i++)
            {
                await _pre[i].ExecuteAsync(context);
            }

            if (!_cancel)
            {
                await _base.ExecuteAsync(context);
            }

            for (int i = 0; i < _post.Count; i++)
            {
                await _post[i].ExecuteAsync(context);
            }
        }
    }
}
