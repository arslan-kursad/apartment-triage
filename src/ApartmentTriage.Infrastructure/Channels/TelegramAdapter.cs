using System.Runtime.CompilerServices;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Services;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ApartmentTriage.Infrastructure.Channels;

public sealed class TelegramAdapter : IMessageChannel
{
    private readonly ITelegramBotClient _bot;
    private readonly ITranscriptionService _transcription;
    private readonly ILogger<TelegramAdapter> _logger;
    private int _offset;

    // Telegram compresses photos to JPEG; reject anything larger than 10 MB after download.
    private const int MaxImageBytes = 10 * 1024 * 1024;

    // Voice duration limit — reject messages longer than this.
    private const int MaxVoiceSeconds = 60;

    public TelegramAdapter(
        ITelegramBotClient bot,
        ITranscriptionService transcription,
        ILogger<TelegramAdapter> logger)
    {
        _bot = bot;
        _transcription = transcription;
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
                    var imageResult = await TryDownloadPhotoAsync(photos, senderId, lang, cancellationToken);
                    if (imageResult is null)
                        continue; // validation failed, user already notified

                    var (imageData, imageMimeType) = imageResult.Value;
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

                // Voice message
                if (msg.Voice is { } voice)
                {
                    var voiceResult = await TryTranscribeVoiceAsync(voice, senderId, lang, cancellationToken);
                    if (voiceResult is null)
                        continue; // validation failed or transcription error, user already notified

                    yield return new IncomingMessage(
                        ExternalId: msg.MessageId.ToString(),
                        SenderId: senderId.ToString(),
                        Text: voiceResult,
                        ReceivedAt: DateTime.SpecifyKind(msg.Date, DateTimeKind.Utc),
                        LanguageCode: languageCode);

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

    /// <summary>
    /// Downloads and transcribes a voice message.
    /// Returns null (and notifies the user) if duration exceeds MaxVoiceSeconds or transcription fails.
    /// </summary>
    private async Task<string?> TryTranscribeVoiceAsync(
        Telegram.Bot.Types.Voice voice,
        long senderId,
        string lang,
        CancellationToken ct)
    {
        if (voice.Duration > MaxVoiceSeconds)
        {
            _logger.LogWarning(
                "Voice message too long ({Duration}s) from {SenderId} — rejecting",
                voice.Duration, senderId);

            await _bot.SendMessage(senderId,
                lang == "tr"
                    ? $"⚠️ Ses mesajı en fazla {MaxVoiceSeconds} saniye olabilir."
                    : $"⚠️ Voice messages must be under {MaxVoiceSeconds} seconds.",
                cancellationToken: ct);
            return null;
        }

        try
        {
            var file = await _bot.GetFile(voice.FileId, ct);

            using var ms = new MemoryStream();
            await _bot.DownloadFile(file.FilePath!, ms, ct);
            ms.Position = 0;

            var transcript = await _transcription.TranscribeAsync(ms, lang, ct);

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _logger.LogWarning("Empty transcript from {SenderId}", senderId);
                await _bot.SendMessage(senderId,
                    lang == "tr"
                        ? "⚠️ Ses mesajında konuşma algılanamadı. Lütfen tekrar deneyin."
                        : "⚠️ No speech detected in your voice message. Please try again.",
                    cancellationToken: ct);
                return null;
            }

            _logger.LogInformation(
                "Voice transcribed for {SenderId} ({Duration}s → {Chars} chars)",
                senderId, voice.Duration, transcript.Length);

            return $"[Ses] {transcript}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to transcribe voice from {SenderId}", senderId);
            await _bot.SendMessage(senderId,
                lang == "tr"
                    ? "⚠️ Ses mesajı işlenemedi. Lütfen tekrar deneyin."
                    : "⚠️ Could not process your voice message. Please try again.",
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
        · 📷 Fotoğraf: mesaj başına 1 görsel, maks. ~10 MB
        · 🎙️ Ses kaydı: maks. 60 saniye

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
        · 📷 Photo: 1 image per message, max ~10 MB
        · 🎙️ Voice message: max 60 seconds

        🔒 Messages are processed solely for maintenance management. (KVKK §6698)

        Please describe your issue 👇
        """;

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
        => _bot.SendMessage(long.Parse(recipientId), text, cancellationToken: cancellationToken);
}
