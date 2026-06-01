using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain;
using ApartmentTriage.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Infrastructure.Persistence.Repositories;

internal sealed class ResidentRepository(ApartmentTriageDbContext db) : IResidentRepository
{
    public Task<Resident?> FindByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
        => db.Residents.FirstOrDefaultAsync(r => r.TelegramId == telegramId, cancellationToken);

    public Task<Resident?> FindByWhatsAppNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        var normalized = PhoneNumberNormalizer.Normalize(number);
        if (normalized is null)
            return Task.FromResult<Resident?>(null);

        return db.Residents.FirstOrDefaultAsync(r => r.WhatsAppNumber == normalized, cancellationToken);
    }

    public Task<Resident?> FindByContactPhoneAsync(string number, CancellationToken cancellationToken = default)
    {
        var normalized = PhoneNumberNormalizer.Normalize(number);
        if (normalized is null)
            return Task.FromResult<Resident?>(null);

        return db.Residents.FirstOrDefaultAsync(r => r.ContactPhone == normalized, cancellationToken);
    }

    public Task AddAsync(Resident resident, CancellationToken cancellationToken = default)
    {
        db.Residents.Add(resident);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
