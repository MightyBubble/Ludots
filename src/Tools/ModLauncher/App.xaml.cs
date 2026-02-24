using System;
using System.Windows;
using Ludots.ModLauncher.Cli;

namespace Ludots.ModLauncher
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            if (e.Args != null && e.Args.Length > 0 && string.Equals(e.Args[0], "cli", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = 1;
                try
                {
                    exitCode = await CliRunner.Run(e.Args);
                }
                catch
                {
                    exitCode = 1;
                }

                Environment.ExitCode = exitCode;
                Shutdown(exitCode);
                return;
            }

            base.OnStartup(e);
        }
    }
}
