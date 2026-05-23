using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Web.Jobs;

public sealed class TelegramConsumerHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TelegramConsumerHostedService> _logger;

    public TelegramConsumerHostedService(
        IServiceProvider serviceProvider,
        ILogger<TelegramConsumerHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram consumer background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Telegram consumer loop beginning.");
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<ChannelConsumerJob>();
                await job.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram consumer background service failed.");
            }

            _logger.LogInformation("Telegram consumer loop completed; sleeping 10s.");
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
