using ApartmentTriage.Domain.Entities;

namespace ApartmentTriage.Application.Repositories;

public interface IResidentRepository
{
    Task<Resident?> FindByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default);
    Task<Resident?> FindByWhatsAppNumberAsync(string number, CancellationToken cancellationToken = default);
    Task<Resident?> FindByContactPhoneAsync(string number, CancellationToken cancellationToken = default);
    Task AddAsync(Resident resident, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
