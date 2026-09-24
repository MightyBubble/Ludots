using Ludots.Core.Hosting;

namespace Ludots.Launcher.Backend;

public static class LauncherProcessGroupValidation
{
    public static void Validate(
        LauncherProcessGroupDefinition processGroup,
        Func<string, bool> isSupportedAdapter)
    {
        ArgumentNullException.ThrowIfNull(processGroup);
        ArgumentNullException.ThrowIfNull(isSupportedAdapter);

        if (string.IsNullOrWhiteSpace(processGroup.Host))
        {
            throw new InvalidOperationException("Process group host is required.");
        }

        if ((uint)(processGroup.Port - 1) >= ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Process group port must be between 1 and {ushort.MaxValue}; got {processGroup.Port}.");
        }

        if (processGroup.FaultProfile != NetworkHostBootstrapConfig.NormalFaultProfile &&
            processGroup.FaultProfile != NetworkHostBootstrapConfig.UnstableFaultProfile)
        {
            throw new InvalidOperationException(
                $"Process group faultProfile must be '{NetworkHostBootstrapConfig.NormalFaultProfile}' or '{NetworkHostBootstrapConfig.UnstableFaultProfile}'.");
        }

        if (processGroup.Processes == null || processGroup.Processes.Count == 0)
        {
            throw new InvalidOperationException("Process group must declare at least one process.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clientInstanceIds = new HashSet<int>();
        var serverCount = 0;
        var clientCount = 0;

        foreach (var process in processGroup.Processes)
        {
            if (process == null)
            {
                throw new InvalidOperationException("Process group contains a null process entry.");
            }

            if (string.IsNullOrWhiteSpace(process.Name))
            {
                throw new InvalidOperationException("Every process group member must declare a non-empty name.");
            }

            if (!names.Add(process.Name.Trim()))
            {
                throw new InvalidOperationException($"Process group member name '{process.Name}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(process.AdapterId))
            {
                throw new InvalidOperationException($"Process '{process.Name}' must declare an adapterId.");
            }

            var adapterId = process.AdapterId.Trim().ToLowerInvariant();
            if (!isSupportedAdapter(adapterId))
            {
                throw new InvalidOperationException(
                    $"Process '{process.Name}' declares unsupported adapter '{process.AdapterId}'.");
            }

            if (process.FaultSeed <= 0)
            {
                throw new InvalidOperationException($"Process '{process.Name}' faultSeed must be positive.");
            }

            var role = process.ProcessRole?.Trim() ?? string.Empty;
            if (role == "authoritativeServer")
            {
                serverCount++;
                if (process.ClientInstanceId != 0)
                {
                    throw new InvalidOperationException(
                        $"Authoritative server process '{process.Name}' must use clientInstanceId 0.");
                }

                continue;
            }

            if (role == "replicatedClient")
            {
                clientCount++;
                if (process.ClientInstanceId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Replicated client process '{process.Name}' must declare a positive clientInstanceId.");
                }

                if (!clientInstanceIds.Add(process.ClientInstanceId))
                {
                    throw new InvalidOperationException(
                        $"Process group clientInstanceId '{process.ClientInstanceId}' is duplicated.");
                }

                continue;
            }

            throw new InvalidOperationException(
                $"Process '{process.Name}' has unknown processRole '{process.ProcessRole}'. " +
                "Expected authoritativeServer or replicatedClient.");
        }

        if (serverCount != 1)
        {
            throw new InvalidOperationException(
                $"Process group must declare exactly one authoritativeServer; observed {serverCount}.");
        }

        if (clientCount < 1)
        {
            throw new InvalidOperationException(
                "Process group must declare at least one replicatedClient.");
        }
    }

    public static LauncherProcessGroupDefinition Clone(LauncherProcessGroupDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new LauncherProcessGroupDefinition
        {
            Host = source.Host,
            Port = source.Port,
            FaultProfile = source.FaultProfile,
            Processes = source.Processes
                .Select(process => new LauncherProcessGroupMemberDefinition
                {
                    Name = process.Name,
                    AdapterId = process.AdapterId,
                    ProcessRole = process.ProcessRole,
                    ClientInstanceId = process.ClientInstanceId,
                    FaultSeed = process.FaultSeed
                })
                .ToList()
        };
    }
}
