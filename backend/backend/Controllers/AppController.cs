using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AppController : ControllerBase
    {
        public const int PORT = 7032;

        public static readonly string URL = "http://127.0.0.1:" + PORT;

        private static int TabCount = 0;

        public static int Tabs => Volatile.Read(ref TabCount);

        private readonly IHostApplicationLifetime Lifetime;

        public AppController(IHostApplicationLifetime lifetime)
        {
            Lifetime = lifetime;
        }

        [HttpGet("live")]
        public async Task Live()
        {
            Interlocked.Increment(ref TabCount);

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";

            await Response.WriteAsync("retry: 1000\n\n");
            await Response.Body.FlushAsync();

            TaskCompletionSource closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using (CancellationTokenRegistration tab = HttpContext.RequestAborted.Register(() => closed.TrySetResult()))
            using (CancellationTokenRegistration app = Lifetime.ApplicationStopping.Register(() => closed.TrySetResult()))
            {
                await closed.Task;
            }

            Interlocked.Decrement(ref TabCount);
        }

        [HttpGet("open")]
        public IActionResult Open()
        {
            if (Tabs == 0)
            {
                OpenTab();
            }

            return StatusCode(200);
        }

        public static void OpenTab()
        {
            Process.Start(new ProcessStartInfo(URL) { UseShellExecute = true })?.Dispose();
        }
    }
}