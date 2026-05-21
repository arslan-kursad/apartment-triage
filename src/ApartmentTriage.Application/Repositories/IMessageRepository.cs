using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Repositories;

public interface IMessageRepository
{
    Task AddAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a message with the given external ID and channel already exists (idempotency guard).</summary>
    Task<bool> ExistsAsync(string externalMessageId, ChannelType channelType, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
