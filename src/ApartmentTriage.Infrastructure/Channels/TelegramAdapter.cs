using System.Runtime.CompilerServices;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ApartmentTriage.Infrastructure.Channels;

public sealed class TelegramAdapter : IMessageChannel
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramAdapter> _logger;
    private int _offset;

    // Telegram compresses photos to JPEG; reject anything larger than 10 MB after download.
    private const int MaxImageBytes = 10 * 1024 * 1024;

    public TelegramAdapter(ITelegramBotClient bot, ILogger<TelegramAdapter> logger)
    {
        _bot = bot;
        _logger = logger;
    }

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

                if (upd.Message is not { } msg)
                    continue;

                var senderId = msg.From?.Id ?? msg.Chat.Id;
                var languageCode = msg.From?.LanguageCode;
                var lang = languageCode == "tr" ? "tr" : "en";

                // /start — send welcome and stop
                if (msg.Text == "/start")
                {
                    await _bot.SendMessage(senderId, lang == "tr" ? TrWelcome : EnWelcome,
                        cancellationToken: cancellationToken);
                    continue;
                }

                // Photo message
                if (msg.Photo is { Length: > 0 } photos)
                {
                    var result = await TryDownloadPhotoAsync(photos, senderId, lang, cancellationToken);
                    if (result is null)
                        continue; // validation failed, user already notified

                    var (imageData, imageMimeType) = result.Value;
                    var caption = msg.Caption ?? (lang == "tr" ? "[Görsel mesaj]" : "[Image message]");

                    yield return new IncomingMessage(
                        ExternalId: msg.MessageId.ToString(),
                        SenderId: senderId.ToString(),
                        Text: caption,
                        ReceivedAt: DateTime.SpecifyKind(msg.Date, DateTimeKind.Utc),
                        LanguageCode: languageCode,
                        ImageData: imageData,
                        ImageMimeType: imageMimeType);

                    continue;
                }

                // Plain text message
                if (msg.Text is not { } text)
                    continue;

                yield return new IncomingMessage(
                    ExternalId: msg.MessageId.ToString(),
                    SenderId: senderId.ToString(),
                    Text: text,
                    ReceivedAt: DateTime.SpecifyKind(msg.Date, DateTimeKind.Utc),
                    LanguageCode: languageCode);
            }
        }
    }

    /// <summary>
    /// Downloads the highest-resolution photo from Telegram CDN.
    /// Returns null (and notifies the user) if the file exceeds MaxImageBytes.
    /// </summary>
    private async Task<(byte[] Data, string MimeType)?> TryDownloadPhotoAsync(
        Telegram.Bot.Types.PhotoSize[] photos,
        long senderId,
        string lang,
        CancellationToken ct)
    {
        var largest = photos[^1]; // last = highest resolution

        try
        {
            var file = await _bot.GetFile(largest.FileId, ct);

            if (file.FileSize is > MaxImageBytes)
            {
                _logger.LogWarning(
                    "Image too large ({Size} bytes) from {SenderId} — rejecting",
                    file.FileSize, senderId);

                await _bot.SendMessage(senderId,
                    lang == "tr"
                        ? "⚠️ Görsel çok büyük. Lütfen 10 MB'tan küçük bir fotoğraf gönderin."
                        : "⚠️ Image too large. Please send a photo under 10 MB.",
                    cancellationToken: ct);
                return null;
            }

            using var ms = new MemoryStream();
            await _bot.DownloadFile(file.FilePath!, ms, ct);
            return (ms.ToArray(), "image/jpeg");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download photo from {SenderId}", senderId);
            await _bot.SendMessage(senderId,
                lang == "tr"
                    ? "⚠️ Görsel indirilemedi. Lütfen tekrar deneyin."
                    : "⚠️ Could not download your image. Please try again.",
                cancellationToken: ct);
            return null;
        }
    }

    private const string TrWelcome = """
        👋 Merhaba! Ben Hanwas AI.
        Apartmanınızdaki arıza ve bakım taleplerinizi buraya yazmanız yeterli — sistemimiz talebinizi otomatik olarak değerlendirip yöneticinize iletecek.

        📌 Bildirebilecekleriniz:
        · Su kaçağı, elektrik arızası, asansör
        · Ortak alan sorunları
        · Acil durumlar
        · 📷 Fotoğraf göndererek sorunu daha hızlı değerlendirmemize yardımcı olabilirsiniz (mesaj başına 1 görsel).

        ⚠️ Sınırlar: Mesaj başına 1 fotoğraf · Maksimum dosya boyutu ~10 MB

        🔒 Mesajlarınız yalnızca bakım yönetimi amacıyla işlenmektedir. (KVKK md. 6698)

        Talebinizi yazabilirsiniz 👇
        """;

    private const string EnWelcome = """
        👋 Hello! I'm Hanwas AI.
        Just describe your maintenance issue — our system will assess and route it to your building manager automatically.

        📌 You can report:
        · Water leaks, electrical faults, elevator issues
        · Common area problems
        · Emergencies
        · 📷 Send a photo (1 image per message) to help us assess the issue faster.

        ⚠️ Limits: 1 photo per message · Max file size ~10 MB

        🔒 Messages are processed solely for maintenance management. (KVKK §6698)

        Please describe your issue 👇
        """;

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
        => _bot.SendMessage(long.Parse(recipientId), text, cancellationToken: cancellationToken);
}
