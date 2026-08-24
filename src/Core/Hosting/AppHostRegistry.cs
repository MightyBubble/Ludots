using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Hosting
{
    /// <summary>
    /// Registry of the App host running in the current process. A process hosts a single App;
    /// CI scenarios that spin multiple engines still see exactly one host per process.
    /// </summary>
    public sealed class AppHostRegistry
    {
        private IAppHost? _current;

        public IAppHost? Current => _current;

        public AppDescriptor? CurrentDescriptor => _current?.Descriptor;

        public void Register(IAppHost host)
        {
            ArgumentNullException.ThrowIfNull(host);

            if (_current != null)
            {
                throw new InvalidOperationException(
                    $"AppHostRegistry already holds app '{_current.Descriptor.AppId}'; a process hosts a single App.");
            }

            _current = host;
        }
    }
}
