using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure.Services;

/// <summary>
/// Chicken-and-egg solution for the first Manager: on startup, promotes the resident whose
/// TelegramId matches <c>Auth:BootstrapManagerIdentifier</c> to <see cref="ResidentRole.Manager"/>.
/// When <c>Auth:BootstrapManagerPhone</c> is set, links that WhatsApp/contact record to the
/// bootstrap Telegram ID (fixes split rows after phone normalization) and deactivates the ghost.
/// Idempotent and safe to run every boot.
/// </summary>
public sealed class ManagerBootstrapper(
    IResidentRepository residents,
    IConfiguration configuration,
    ILogger<ManagerBootstrapper> logger)
{
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetBootstrapTelegramId(out var telegramId))
            return Task.CompletedTask;

        return LinkAndPromoteAsync(telegramId, cancellationToken);
    }

    internal async Task LinkAndPromoteAsync(long telegramId, CancellationToken cancellationToken)
    {
        var bootstrapPhone = PhoneNumberNormalizer.Normalize(configuration["Auth:BootstrapManagerPhone"]);

        var telegramResident = await residents.FindByTelegramIdAsync(telegramId, cancellationToken);
        var phoneResident = bootstrapPhone is null
            ? null
            : await residents.FindByWhatsAppNumberAsync(bootstrapPhone, cancellationToken)
              ?? await residents.FindByContactPhoneAsync(bootstrapPhone, cancellationToken);

        if (phoneResident is not null)
        {
            await MergePhoneManagerAsync(telegramId, telegramResident, phoneResident, cancellationToken);
            return;
        }

        if (telegramResident is not null)
        {
            await PromoteIfNeededAsync(telegramResident, telegramId, cancellationToken);
            return;
        }

        logger.LogWarning(
            "Bootstrap manager Telegram ID {TelegramId} not found — message the bot or set " +
            "Auth:BootstrapManagerPhone to the manager WhatsApp row; will retry on next startup",
            telegramId);
    }

    private async Task MergePhoneManagerAsync(
        long telegramId,
        Resident? telegramResident,
        Resident phoneResident,
        CancellationToken cancellationToken)
    {
        if (telegramResident is not null && telegramResident.Id != phoneResident.Id)
        {
            logger.LogInformation(
                "Bootstrap: merging Telegram resident {GhostId} into phone manager {KeeperId} (Telegram {TelegramId})",
                telegramResident.Id, phoneResident.Id, telegramId);

            telegramResident.ClearTelegramId();
            telegramResident.Deactivate();
            await residents.SaveChangesAsync(cancellationToken);
        }

        if (phoneResident.TelegramId != telegramId)
            phoneResident.UpdateContactInfo(telegramId: telegramId);

        await PromoteIfNeededAsync(phoneResident, telegramId, cancellationToken);
    }

    private async Task PromoteIfNeededAsync(Resident resident, long telegramId, CancellationToken cancellationToken)
    {
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

    private bool TryGetBootstrapTelegramId(out long telegramId)
    {
        telegramId = 0;
        var identifier = configuration["Auth:BootstrapManagerIdentifier"]?.Trim();
        if (string.IsNullOrEmpty(identifier))
        {
            logger.LogInformation(
                "Bootstrap manager not configured (Auth:BootstrapManagerIdentifier empty) — skipping");
            return false;
        }

        if (!long.TryParse(identifier, out telegramId))
        {
            logger.LogWarning(
                "Auth:BootstrapManagerIdentifier '{Identifier}' is not a valid Telegram ID — skipping",
                identifier);
            return false;
        }

        return true;
    }
}
