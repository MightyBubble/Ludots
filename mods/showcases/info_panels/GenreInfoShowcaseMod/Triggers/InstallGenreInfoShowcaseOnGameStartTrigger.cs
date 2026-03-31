using System.Threading.Tasks;
using GenreInfoShowcaseMod.Runtime;
using GenreInfoShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GenreInfoShowcaseMod.Triggers
{
    internal sealed class InstallGenreInfoShowcaseOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "GenreInfoShowcaseMod.Installed";
        private readonly IModContext _context;
        private readonly GenreInfoShowcaseRuntime _runtime;

        public InstallGenreInfoShowcaseOnGameStartTrigger(IModContext context, GenreInfoShowcaseRuntime runtime)
        {
            _context = context;
            _runtime = runtime;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[InstalledKey] = true;
            engine.RegisterPresentationSystem(new GenreInfoShowcasePanelPresentationSystem(engine, _runtime));
            _context.Log("[GenreInfoShowcaseMod] Presentation system registered.");
            return Task.CompletedTask;
        }
    }
}
