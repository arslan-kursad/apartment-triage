using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Infrastructure.Persistence.Repositories;

internal sealed class MessageRepository(ApartmentTriageDbContext db) : IMessageRepository
{
    public Task AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        db.Messages.Add(message);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string externalMessageId, ChannelType channelType, CancellationToken cancellationToken = default)
        => db.Messages.AnyAsync(
            m => m.ExternalMessageId == externalMessageId && m.ChannelType == channelType,
            cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
