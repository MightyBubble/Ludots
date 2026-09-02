using System;
using System.Collections;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Live index of TriggerGraph mounts that bind a semantic input action directly.
    /// Action-bound mounts do not join the event bus; the binding system looks them up
    /// here and dispatches when the action fires.
    /// </summary>
    public sealed class TriggerGraphActionBindingIndex
    {
        private readonly Dictionary<string, List<TriggerGraphMountTrigger>> _byAction =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _knownActionIds = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> ActionIds => _knownActionIds;

        /// <summary>Action ids that currently have at least one live mount.</summary>
        public IEnumerable<string> MountedActionIds => _byAction.Keys;

        public void RememberActionId(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("Action id is required.", nameof(actionId));
            }

            _knownActionIds.Add(actionId.Trim());
        }

        public void Add(TriggerGraphMountTrigger mount)
        {
            ArgumentNullException.ThrowIfNull(mount);
            if (string.IsNullOrWhiteSpace(mount.ActionId))
            {
                throw new ArgumentException(
                    $"TriggerGraph mount '{mount.Name}' is not action-bound.",
                    nameof(mount));
            }

            string actionId = mount.ActionId;
            _knownActionIds.Add(actionId);
            if (!_byAction.TryGetValue(actionId, out List<TriggerGraphMountTrigger>? list))
            {
                list = new List<TriggerGraphMountTrigger>();
                _byAction[actionId] = list;
            }

            list.Add(mount);
        }

        public void Remove(TriggerGraphMountTrigger mount)
        {
            if (mount == null || string.IsNullOrWhiteSpace(mount.ActionId))
            {
                return;
            }

            if (!_byAction.TryGetValue(mount.ActionId, out List<TriggerGraphMountTrigger>? list))
            {
                return;
            }

            list.Remove(mount);
            if (list.Count == 0)
            {
                _byAction.Remove(mount.ActionId);
            }
        }

        public bool TryGetMounts(string actionId, out IReadOnlyList<TriggerGraphMountTrigger> mounts)
        {
            if (!string.IsNullOrWhiteSpace(actionId) &&
                _byAction.TryGetValue(actionId, out List<TriggerGraphMountTrigger>? list) &&
                list.Count > 0)
            {
                mounts = list;
                return true;
            }

            mounts = Array.Empty<TriggerGraphMountTrigger>();
            return false;
        }
    }
}
