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

    // Max download size for images (Telegram compresses photos to JPEG; ~10 MB upper bound).
    private const int MaxImageBytes = 10 * 1024 * 1024;

    // Voice duration limit — reject messages longer than this.
    private const int MaxVoiceSeconds = 60;

    // Accepted image MIME types (Anthropic vision + common formats).
    private static readonly HashSet<string> AllowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

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

                // Photo message (Telegram-compressed JPEG)
                if (msg.Photo is { Length: > 0 } photos)
                {
                    var largest = photos[^1];
                    var imageResult = await TryDownloadFileAsync(
                        largest.FileId, "image/jpeg", senderId, lang, cancellationToken);
                    if (imageResult is null)
                        continue;

                    var caption = msg.Caption ?? (lang == "tr" ? "[Görsel mesaj]" : "[Image message]");
                    yield return new IncomingMessage(
                        ExternalId: msg.MessageId.ToString(),
                        SenderId: senderId.ToString(),
                        Text: caption,
                        ReceivedAt: DateTime.SpecifyKind(msg.Date, DateTimeKind.Utc),
                        LanguageCode: languageCode,
                        ImageData: imageResult.Value.Data,
                        ImageMimeType: imageResult.Value.MimeType);
                    continue;
                }

                // Document message — images sent "as file", PDFs, DOCX, etc.
                if (msg.Document is { } doc)
                {
                    var mime = doc.MimeType ?? string.Empty;

                    if (AllowedImageMimeTypes.Contains(mime))
                    {
                        // Image document — process like a photo
                        var docResult = await TryDownloadFileAsync(
                            doc.FileId, mime, senderId, lang, cancellationToken);
                        if (docResult is null)
                            continue;

                        var caption = msg.Caption ?? (lang == "tr" ? "[Görsel mesaj]" : "[Image message]");
                        yield return new IncomingMessage(
                            ExternalId: msg.MessageId.ToString(),
                            SenderId: senderId.ToString(),
                            Text: caption,
                            ReceivedAt: DateTime.SpecifyKind(msg.Date, DateTimeKind.Utc),
                            LanguageCode: languageCode,
                            ImageData: docResult.Value.Data,
                            ImageMimeType: docResult.Value.MimeType);
                    }
                    else if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        // Unsupported image format (e.g. image/bmp, image/tiff)
                        _logger.LogWarning(
                            "Unsupported image MIME type '{Mime}' from {SenderId}", mime, senderId);
                        await _bot.SendMessage(senderId,
                            lang == "tr"
                                ? "⚠️ Yalnızca JPEG, PNG, WebP veya GIF görseller kabul edilmektedir."
                                : "⚠️ Only JPEG, PNG, WebP or GIF images are accepted.",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        // Non-image document (PDF, DOCX, etc.)
                        _logger.LogInformation(
                            "Document type '{Mime}' from {SenderId} — rejected (not image)", mime, senderId);
                        await _bot.SendMessage(senderId,
                            lang == "tr"
                                ? "⚠️ Belge göndermek için yöneticinizle iletişime geçin."
                                : "⚠️ For documents, please contact your building manager.",
                            cancellationToken: cancellationToken);
                    }
                    continue;
                }

                // Voice message
                if (msg.Voice is { } voice)
                {
                    var voiceResult = await TryTranscribeVoiceAsync(voice, senderId, lang, cancellationToken);
                    if (voiceResult is null)
                        continue;

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
    /// Downloads a file from the Telegram CDN by fileId.
    /// Returns null (and notifies the user) on size violation or download failure.
    /// </summary>
    private async Task<(byte[] Data, string MimeType)?> TryDownloadFileAsync(
        string fileId,
        string mimeType,
        long senderId,
        string lang,
        CancellationToken ct)
    {
        try
        {
            var file = await _bot.GetFile(fileId, ct);

            if (file.FileSize is > MaxImageBytes)
            {
                _logger.LogWarning(
                    "File too large ({Size} bytes) from {SenderId} — rejecting", file.FileSize, senderId);
                await _bot.SendMessage(senderId,
                    lang == "tr"
                        ? "⚠️ Görsel çok büyük. Lütfen 10 MB'tan küçük bir fotoğraf gönderin."
                        : "⚠️ Image too large. Please send a photo under 10 MB.",
                    cancellationToken: ct);
                return null;
            }

            using var ms = new MemoryStream();
            await _bot.DownloadFile(file.FilePath!, ms, ct);
            return (ms.ToArray(), mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download file from {SenderId}", senderId);
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
                "Voice too long ({Duration}s) from {SenderId} — rejecting", voice.Duration, senderId);
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
        · 📷 Fotoğraf: mesaj başına 1 görsel, maks. ~10 MB (JPEG/PNG/WebP/GIF)
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
        · 📷 Photo: 1 image per message, max ~10 MB (JPEG/PNG/WebP/GIF)
        · 🎙️ Voice message: max 60 seconds

        🔒 Messages are processed solely for maintenance management. (KVKK §6698)

        Please describe your issue 👇
        """;

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
        => _bot.SendMessage(long.Parse(recipientId), text, cancellationToken: cancellationToken);
}
