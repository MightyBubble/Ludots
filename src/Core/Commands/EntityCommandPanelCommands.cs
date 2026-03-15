using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Diagnostics;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace Ludots.Core.Commands
{
    public sealed class OpenEntityCommandPanelCommand : GameCommand
    {
        private readonly string _alias;
        private readonly string _targetEntityContextKey;
        private readonly string _sourceId;
        private readonly string _instanceKey;
        private readonly EntityCommandPanelAnchor _anchor;
        private readonly EntityCommandPanelSize _size;
        private readonly int _initialGroupIndex;
        private readonly bool _startVisible;

        public OpenEntityCommandPanelCommand(
            string alias,
            string targetEntityContextKey,
            string sourceId,
            string instanceKey,
            EntityCommandPanelAnchor anchor,
            EntityCommandPanelSize size,
            int initialGroupIndex = 0,
            bool startVisible = true)
        {
            _alias = alias ?? string.Empty;
            _targetEntityContextKey = targetEntityContextKey ?? string.Empty;
            _sourceId = sourceId ?? string.Empty;
            _instanceKey = instanceKey ?? string.Empty;
            _anchor = anchor;
            _size = size;
            _initialGroupIndex = initialGroupIndex;
            _startVisible = startVisible;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var service = context.Get(CoreServiceKeys.EntityCommandPanelService);
            var handles = context.Get(CoreServiceKeys.EntityCommandPanelHandleStore);
            if (service == null || handles == null)
            {
                Log.Warn(in LogChannels.Presentation, "[EntityCommandPanel] Service or handle store missing during open command.");
                return Task.CompletedTask;
            }

            Entity target = ResolveTarget(context, _targetEntityContextKey);
            if (target == Entity.Null)
            {
                Log.Warn(in LogChannels.Presentation, $"[EntityCommandPanel] Open command could not resolve target key '{_targetEntityContextKey}'.");
                return Task.CompletedTask;
            }

            var request = new EntityCommandPanelOpenRequest
            {
                TargetEntity = target,
                SourceId = _sourceId,
                InstanceKey = _instanceKey,
                Anchor = _anchor,
                Size = _size,
                InitialGroupIndex = _initialGroupIndex,
                StartVisible = _startVisible
            };

            EntityCommandPanelHandle handle = service.Open(in request);
            if (handle.IsValid && !string.IsNullOrWhiteSpace(_alias))
            {
                handles.TryBind(_alias, handle);
            }

            return Task.CompletedTask;
        }

        internal static Entity ResolveTarget(ScriptContext context, string contextKey)
        {
            if (string.IsNullOrWhiteSpace(contextKey))
            {
                return context.Get(CoreServiceKeys.SelectedEntity);
            }

            if (string.Equals(contextKey, CoreServiceKeys.SelectedEntity.Name, StringComparison.Ordinal))
            {
                return context.Get(CoreServiceKeys.SelectedEntity);
            }

            if (string.Equals(contextKey, CoreServiceKeys.LocalPlayerEntity.Name, StringComparison.Ordinal))
            {
                return context.Get(CoreServiceKeys.LocalPlayerEntity);
            }

            return context.Get<Entity>(contextKey);
        }
    }

    public sealed class CloseEntityCommandPanelCommand : GameCommand
    {
        private readonly string _alias;

        public CloseEntityCommandPanelCommand(string alias)
        {
            _alias = alias ?? string.Empty;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var service = context.Get(CoreServiceKeys.EntityCommandPanelService);
            var handles = context.Get(CoreServiceKeys.EntityCommandPanelHandleStore);
            if (service == null || handles == null || string.IsNullOrWhiteSpace(_alias))
            {
                return Task.CompletedTask;
            }

            if (handles.TryGet(_alias, out EntityCommandPanelHandle handle))
            {
                service.Close(handle);
                handles.Remove(_alias);
            }

            return Task.CompletedTask;
        }
    }

    public sealed class SetEntityCommandPanelVisibilityCommand : GameCommand
    {
        private readonly string _alias;
        private readonly bool _visible;

        public SetEntityCommandPanelVisibilityCommand(string alias, bool visible)
        {
            _alias = alias ?? string.Empty;
            _visible = visible;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var service = context.Get(CoreServiceKeys.EntityCommandPanelService);
            var handles = context.Get(CoreServiceKeys.EntityCommandPanelHandleStore);
            if (service == null || handles == null || string.IsNullOrWhiteSpace(_alias))
            {
                return Task.CompletedTask;
            }

            if (handles.TryGet(_alias, out EntityCommandPanelHandle handle))
            {
                service.SetVisible(handle, _visible);
            }

            return Task.CompletedTask;
        }
    }

    public sealed class SetEntityCommandPanelGroupCommand : GameCommand
    {
        private readonly string _alias;
        private readonly int _groupIndex;
        private readonly int _delta;
        private readonly bool _useDelta;

        public SetEntityCommandPanelGroupCommand(string alias, int groupIndex)
        {
            _alias = alias ?? string.Empty;
            _groupIndex = groupIndex;
            _delta = 0;
            _useDelta = false;
        }

        private SetEntityCommandPanelGroupCommand(string alias, int groupIndex, int delta, bool useDelta)
        {
            _alias = alias ?? string.Empty;
            _groupIndex = groupIndex;
            _delta = delta;
            _useDelta = useDelta;
        }

        public static SetEntityCommandPanelGroupCommand Cycle(string alias, int delta)
        {
            return new SetEntityCommandPanelGroupCommand(alias, 0, delta, useDelta: true);
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var service = context.Get(CoreServiceKeys.EntityCommandPanelService);
            var handles = context.Get(CoreServiceKeys.EntityCommandPanelHandleStore);
            if (service == null || handles == null || string.IsNullOrWhiteSpace(_alias))
            {
                return Task.CompletedTask;
            }

            if (handles.TryGet(_alias, out EntityCommandPanelHandle handle))
            {
                if (_useDelta)
                {
                    service.CycleGroup(handle, _delta);
                }
                else
                {
                    service.SetGroupIndex(handle, _groupIndex);
                }
            }

            return Task.CompletedTask;
        }
    }

    public sealed class RebindEntityCommandPanelTargetCommand : GameCommand
    {
        private readonly string _alias;
        private readonly string _targetEntityContextKey;

        public RebindEntityCommandPanelTargetCommand(string alias, string targetEntityContextKey)
        {
            _alias = alias ?? string.Empty;
            _targetEntityContextKey = targetEntityContextKey ?? string.Empty;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var service = context.Get(CoreServiceKeys.EntityCommandPanelService);
            var handles = context.Get(CoreServiceKeys.EntityCommandPanelHandleStore);
            if (service == null || handles == null || string.IsNullOrWhiteSpace(_alias))
            {
                return Task.CompletedTask;
            }

            if (!handles.TryGet(_alias, out EntityCommandPanelHandle handle))
            {
                return Task.CompletedTask;
            }

            Entity target = OpenEntityCommandPanelCommand.ResolveTarget(context, _targetEntityContextKey);
            if (target != Entity.Null)
            {
                service.RebindTarget(handle, target);
            }

            return Task.CompletedTask;
        }
    }

    public sealed class SetEntityCommandPanelAnchorCommand : GameCommand
    {
        private readonly string _alias;
        private readonly EntityCommandPanelAnchor _anchor;

        public SetEntityCommandPanelAnchorCommand(string alias, EntityCommandPanelAnchor anchor)
        {
            _alias = alias ?? string.Empty;
            _anchor = anchor;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var service = context.Get(CoreServiceKeys.EntityCommandPanelService);
            var handles = context.Get(CoreServiceKeys.EntityCommandPanelHandleStore);
            if (service == null || handles == null || string.IsNullOrWhiteSpace(_alias))
            {
                return Task.CompletedTask;
            }

            if (handles.TryGet(_alias, out EntityCommandPanelHandle handle))
            {
                service.SetAnchor(handle, in _anchor);
            }

            return Task.CompletedTask;
        }
    }

    public sealed class SetEntityCommandPanelSizeCommand : GameCommand
    {
        private readonly string _alias;
        private readonly EntityCommandPanelSize _size;

        public SetEntityCommandPanelSizeCommand(string alias, EntityCommandPanelSize size)
        {
            _alias = alias ?? string.Empty;
            _size = size;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var service = context.Get(CoreServiceKeys.EntityCommandPanelService);
            var handles = context.Get(CoreServiceKeys.EntityCommandPanelHandleStore);
            if (service == null || handles == null || string.IsNullOrWhiteSpace(_alias))
            {
                return Task.CompletedTask;
            }

            if (handles.TryGet(_alias, out EntityCommandPanelHandle handle))
            {
                service.SetSize(handle, in _size);
            }

            return Task.CompletedTask;
        }
    }
}
