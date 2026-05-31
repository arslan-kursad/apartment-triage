using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure.Services;

/// <summary>
/// Chicken-and-egg solution for the first Manager: on startup, promotes the resident whose
/// TelegramId matches <c>Auth:BootstrapManagerIdentifier</c> to <see cref="ResidentRole.Manager"/>.
/// Idempotent and safe to run every boot — no-op when unconfigured, when the resident has not
/// messaged the bot yet, or when the role is already set.
/// </summary>
public sealed class ManagerBootstrapper(
    IResidentRepository residents,
    IConfiguration configuration,
    ILogger<ManagerBootstrapper> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var identifier = configuration["Auth:BootstrapManagerIdentifier"]?.Trim();
        if (string.IsNullOrEmpty(identifier))
        {
            logger.LogInformation(
                "Bootstrap manager not configured (Auth:BootstrapManagerIdentifier empty) — skipping");
            return;
        }

        if (!long.TryParse(identifier, out var telegramId))
        {
            logger.LogWarning(
                "Auth:BootstrapManagerIdentifier '{Identifier}' is not a valid Telegram ID — skipping",
                identifier);
            return;
        }

        var resident = await residents.FindByTelegramIdAsync(telegramId, cancellationToken);
        if (resident is null)
        {
            logger.LogWarning(
                "Bootstrap manager Telegram ID {TelegramId} not found — the resident must message " +
                "the bot first to be created; will retry on next startup",
                telegramId);
            return;
        }

        if (resident.Role == ResidentRole.Manager)
        {
            logger.LogInformation(
                "Bootstrap manager {ResidentId} already has the Manager role — no change", resident.Id);
            return;
        }

        resident.SetRole(ResidentRole.Manager);
        await residents.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bootstrap: resident {ResidentId} (Telegram {TelegramId}) promoted to Manager",
            resident.Id, telegramId);
    }
}
