using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Pure logical vision registry (Epic #896 / #902).
    /// Participant-owned views and optional client-present view; presentation binds via <see cref="PresentBinding"/>.
    /// </summary>
    public sealed class LogicViewRegistry
    {
        public const string ClientPresentViewId = "logicview.client.present";

        private readonly Dictionary<string, LogicViewEntry> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<Entity, string> _defaultViewByOwner = new();

        public int Count => _byId.Count;

        public string EnsureDefaultView(
            Entity ownerRep,
            string? logicViewId = null,
            CameraManager? camera = null)
        {
            if (ownerRep == Entity.Null)
            {
                throw new ArgumentException("LogicView owner rep is required.", nameof(ownerRep));
            }

            if (_defaultViewByOwner.TryGetValue(ownerRep, out string? existing))
            {
                return existing;
            }

            string id = string.IsNullOrWhiteSpace(logicViewId)
                ? $"logicview.participant.{ownerRep.Id}"
                : logicViewId.Trim();
            if (_byId.ContainsKey(id))
            {
                throw new InvalidOperationException($"LogicView id '{id}' is already registered.");
            }

            var entry = new LogicViewEntry(id, ownerRep, camera ?? new CameraManager());
            _byId.Add(id, entry);
            _defaultViewByOwner.Add(ownerRep, id);
            return id;
        }

        /// <summary>
        /// Seatless / bootstrap present eye. Not participant-owned; PresentBinding may still target it.
        /// </summary>
        public string EnsureClientPresentView(CameraManager? camera = null)
        {
            if (_byId.TryGetValue(ClientPresentViewId, out LogicViewEntry? existing))
            {
                return existing.Id;
            }

            var entry = new LogicViewEntry(ClientPresentViewId, Entity.Null, camera ?? new CameraManager());
            _byId.Add(ClientPresentViewId, entry);
            return ClientPresentViewId;
        }

        public bool TryGetClientPresentCamera(out CameraManager camera)
        {
            camera = null!;
            if (!_byId.TryGetValue(ClientPresentViewId, out LogicViewEntry? entry))
            {
                return false;
            }

            camera = entry.Camera;
            return true;
        }

        public void CopyCameras(List<CameraManager> destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            destination.Clear();
            foreach (LogicViewEntry entry in _byId.Values)
            {
                destination.Add(entry.Camera);
            }
        }

        public bool TryGet(string logicViewId, out LogicViewEntry entry)
        {
            entry = default!;
            if (string.IsNullOrWhiteSpace(logicViewId))
            {
                return false;
            }

            return _byId.TryGetValue(logicViewId.Trim(), out entry!);
        }

        public LogicViewEntry Require(string logicViewId)
        {
            if (!TryGet(logicViewId, out LogicViewEntry entry))
            {
                throw new InvalidOperationException($"LogicView '{logicViewId}' is not registered.");
            }

            return entry;
        }

        public bool TryGetDefaultViewId(Entity ownerRep, out string logicViewId) =>
            _defaultViewByOwner.TryGetValue(ownerRep, out logicViewId!);

        public CameraManager RequireCamera(string logicViewId) => Require(logicViewId).Camera;

        public void ResetAllVirtualCameras()
        {
            foreach (LogicViewEntry entry in _byId.Values)
            {
                entry.Camera.ResetVirtualCameras();
            }
        }

        public void Clear()
        {
            _byId.Clear();
            _defaultViewByOwner.Clear();
        }

        public void RemoveOwner(Entity ownerRep)
        {
            if (!_defaultViewByOwner.Remove(ownerRep, out string? id))
            {
                return;
            }

            _byId.Remove(id);
        }
    }

    public sealed class LogicViewEntry
    {
        public LogicViewEntry(string id, Entity ownerRep, CameraManager camera)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("LogicView id is required.", nameof(id));
            }

            Id = id.Trim();
            OwnerRep = ownerRep;
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            LogicalAspect = 16f / 9f;
        }

        public string Id { get; }
        public Entity OwnerRep { get; }
        public CameraManager Camera { get; }

        public bool IsClientPresent => OwnerRep == Entity.Null;

        /// <summary>Authoring/logical aspect for cast projection; not host window aspect.</summary>
        public float LogicalAspect { get; set; }
    }
}
