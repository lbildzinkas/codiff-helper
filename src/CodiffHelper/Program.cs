using CodiffHelper;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Let the running step stop and clean up (for example a half-made clone) instead of exiting mid-way.
    e.Cancel = true;
    cancellation.Cancel();
};

return await App.RunAsync(args, AppServices.ForThisProcess(), cancellation.Token);
