using System.Runtime.CompilerServices;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Domain.Enums;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ApartmentTriage.Infrastructure.Channels;

public sealed class TelegramAdapter : IMessageChannel
{
    private readonly ITelegramBotClient _bot;
    private int _offset;

    public TelegramAdapter(ITelegramBotClient bot) => _bot = bot;

    public ChannelType ChannelType => ChannelType.Telegram;

    public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var updates = await _bot.GetUpdates(
                offset: _offset,
                limit: 100,
                timeout: 30,
                allowedUpdates: [UpdateType.Message],
                cancellationToken: cancellationToken);

            foreach (var upd in updates)
            {
                _offset = upd.Id + 1;
                if (upd.Message?.Text is not { } text)
                    continue;

                var senderId = upd.Message.From?.Id ?? upd.Message.Chat.Id;

                yield return new IncomingMessage(
                    ExternalId: upd.Message.MessageId.ToString(),
                    SenderId: senderId.ToString(),
                    Text: text,
                    ReceivedAt: DateTime.SpecifyKind(upd.Message.Date, DateTimeKind.Utc));
            }
        }
    }

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
        => _bot.SendMessage(long.Parse(recipientId), text, cancellationToken: cancellationToken);
}
