using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Modding
{
    public class ModContext : IModContext
    {
        public string ModId { get; }
        public IVirtualFileSystem VFS { get; }
        public FunctionRegistry FunctionRegistry { get; }
        public SystemFactoryRegistry SystemFactoryRegistry { get; }
        public TriggerDecoratorRegistry TriggerDecorators { get; }

        private readonly TriggerManager _triggerManager;
        private readonly LogChannel _logChannel;
        public LogChannel LogChannel => _logChannel;

        public ModContext(
            string modId,
            IVirtualFileSystem vfs,
            FunctionRegistry fr,
            TriggerManager tm,
            SystemFactoryRegistry sfr,
            TriggerDecoratorRegistry tdr)
        {
            ModId = modId;
            VFS = vfs;
            FunctionRegistry = fr;
            _triggerManager = tm;
            SystemFactoryRegistry = sfr;
            TriggerDecorators = tdr;
            _logChannel = Diagnostics.Log.GetOrCreateModChannel(modId);
        }

        public void OnEvent(EventKey eventKey, Func<ScriptContext, Task> handler)
        {
            _triggerManager.RegisterEventHandler(eventKey, handler);
        }

        public void Log(string message)
        {
            Diagnostics.Log.Info(in _logChannel, message);
        }

        public void Log(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Trace:
                    Diagnostics.Log.Trace(in _logChannel, message);
                    break;
                case LogLevel.Debug:
                    Diagnostics.Log.Dbg(in _logChannel, message);
                    break;
                case LogLevel.Info:
                    Diagnostics.Log.Info(in _logChannel, message);
                    break;
                case LogLevel.Warning:
                    Diagnostics.Log.Warn(in _logChannel, message);
                    break;
                case LogLevel.Error:
                    Diagnostics.Log.Error(in _logChannel, message);
                    break;
            }
        }

        public Stream GetResource(string uri)
        {
            return VFS.GetStream(uri);
        }
    }
}
