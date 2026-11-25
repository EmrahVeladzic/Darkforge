using backend.Controllers;

public class TabWatchService : BackgroundService
{
    private const long RECONNECT_WAIT_MS = 2500;

    private const long NO_TAB_QUIT_MS = 10000;

    private const long FIRST_TAB_QUIT_MS = 60000;

    private readonly IHostApplicationLifetime Lifetime;

    public TabWatchService(IHostApplicationLifetime lifetime)
    {
        Lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using (CancellationTokenRegistration s = Lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (CancellationTokenRegistration t = stoppingToken.Register(() => started.TrySetResult()))
        {
            await started.Task;
        }

        await Task.Delay((int)RECONNECT_WAIT_MS);

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (AppController.Tabs == 0)
        {
            AppController.OpenTab();
        }

        bool had_tab = false;
        long no_tab_since = Environment.TickCount64;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000);

            if (AppController.Tabs > 0)
            {
                had_tab = true;
                no_tab_since = Environment.TickCount64;
            }
            else if (Environment.TickCount64 - no_tab_since > (had_tab ? NO_TAB_QUIT_MS : FIRST_TAB_QUIT_MS))
            {
                Lifetime.StopApplication();
                return;
            }
        }
    }
}