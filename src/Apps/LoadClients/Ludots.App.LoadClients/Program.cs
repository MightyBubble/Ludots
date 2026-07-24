using Ludots.App.LoadClients;

int exitCode = 0;
try
{
    LoadClientHostConfig config = LoadClientHostConfig.ParseCommandLine(args);
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };

    var host = new LoadClientHost(config, baseDirectory: AppContext.BaseDirectory);
    LoadClientRunEvidence evidence = host.Run(shutdown.Token);
    Console.Out.WriteLine(evidence.ToMachineReadableLine());
    exitCode = evidence.Outcome switch
    {
        LoadClientRunOutcome.Passed => 0,
        LoadClientRunOutcome.Cancelled => 130,
        _ => 1,
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    exitCode = 1;
}

return exitCode;
