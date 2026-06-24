using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetMemory.Collector;

public sealed class CollectorService : BackgroundService
{
    private readonly GameLogCollector _collector;
    private readonly ILogger<CollectorService> _logger;
    private readonly TimeSpan _interval;

    public CollectorService(
        GameLogCollector collector,
        ILogger<CollectorService> logger,
        TimeSpan? interval = null)
    {
        _collector = collector;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Collector service started, polling every {Interval}s", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = _collector.Tick();
                if (count > 0)
                    _logger.LogInformation("Processed {Count} inventory events", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during collector tick");
            }

            await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
