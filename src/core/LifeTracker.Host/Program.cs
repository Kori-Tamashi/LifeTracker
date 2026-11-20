using LifeTracker.Host;
using LifeTracker.Infrastructure.Shared;

try
{
    if (args.Length > 0)
    {
        switch (args)
        {
            case ["backup", var destination]:
                var source = Environment.GetEnvironmentVariable("LIFETRACKER_RUNTIME_ROOT")
                    ?? throw new InvalidOperationException("Runtime root is required.");
                await OfflineProfileBackup.CreateAsync(source, destination);
                break;
            case ["backup-daily", var catalog]:
                var dailySource = Environment.GetEnvironmentVariable("LIFETRACKER_RUNTIME_ROOT")
                    ?? throw new InvalidOperationException("Runtime root is required.");
                await new ManagedDailyBackup().RunAsync(dailySource, catalog);
                break;
            case ["verify-backup", var backup]:
                await OfflineProfileBackup.VerifyAsync(backup);
                break;
            case ["restore", var backup, var destination]:
                await OfflineProfileBackup.RestoreAsync(backup, destination);
                break;
            default:
                throw new ArgumentException("Unsupported maintenance command.");
        }
        Console.WriteLine("LifeTracker offline maintenance completed.");
        return 0;
    }
    var root = Environment.GetEnvironmentVariable("LIFETRACKER_RUNTIME_ROOT")
        ?? throw new InvalidOperationException("Runtime root is required.");
    var port = int.Parse(Environment.GetEnvironmentVariable("LIFETRACKER_PORT") ?? "17841", System.Globalization.CultureInfo.InvariantCulture);
    await using var host = await LocalApiHost.CreateAsync(new(root, port));
    await host.StartAsync();
    Console.WriteLine("LifeTracker API: " + host.Address);
    await host.WaitForShutdownAsync();
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine("LifeTracker operation failed; verify configuration, stop all profile writers and check local recovery state.");
    return 1;
}
