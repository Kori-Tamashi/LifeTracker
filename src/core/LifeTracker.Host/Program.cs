using LifeTracker.Host;

try
{
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
    Console.Error.WriteLine("LifeTracker API startup failed; verify runtime configuration and local recovery state.");
    return 1;
}
