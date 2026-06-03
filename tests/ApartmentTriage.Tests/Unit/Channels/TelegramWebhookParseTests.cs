using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Infrastructure.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Channels;

/// <summary>
/// Exercises the webhook path's core: deserialize a raw Telegram Update (exactly as the wire
/// delivers it, via JsonBotAPI.Options) → TryEnqueue → drain via ReadMessagesAsync. This is the
/// same Update→IncomingMessage transformation the webhook endpoint relies on, minus HTTP plumbing.
/// </summary>
public class TelegramWebhookParseTests
{
    // A real client is required by the adapter ctor; for the text/skip paths it is never invoked
    // (no file download, no reply), so a dummy token with no network access is sufficient.
    private static TelegramAdapter NewAdapter() =>
        new(new TelegramBotClient("123456:dummytoken"), NullLogger<TelegramAdapter>.Instance);

    private static async Task<List<IncomingMessage>> DrainAsync(TelegramAdapter adapter, int expected)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var collected = new List<IncomingMessage>();
        await foreach (var msg in adapter.ReadMessagesAsync(cts.Token))
        {
            collected.Add(msg);
            if (collected.Count >= expected)
                break;
        }
        return collected;
    }

    [Fact]
    public async Task TextUpdate_DeserializedFromWireJson_DrainsToIncomingMessage()
    {
        const string json = """
        {
          "update_id": 123456,
          "message": {
            "message_id": 42,
            "from": { "id": 8013067042, "is_bot": false, "first_name": "Ayşe", "last_name": "Yılmaz", "language_code": "tr" },
            "chat": { "id": 8013067042, "type": "private" },
            "date": 1735000000,
            "text": "Asansör bozuldu"
          }
        }
        """;

        var update = JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)!;
        var adapter = NewAdapter();

        adapter.TryEnqueue(update).Should().BeTrue();

        var drained = await DrainAsync(adapter, expected: 1);

        drained.Should().ContainSingle();
        var msg = drained[0];
        msg.ExternalId.Should().Be("42");
        msg.SenderId.Should().Be("8013067042");
        msg.Text.Should().Be("Asansör bozuldu");
        msg.LanguageCode.Should().Be("tr");
        msg.SenderName.Should().Be("Ayşe Yılmaz");
        msg.ReceivedAt.UtcDateTime.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1735000000).UtcDateTime);
        msg.ImageData.Should().BeNull();
    }

    [Fact]
    public async Task NonMessageUpdate_IsSkipped_OnlyTextUpdateDrains()
    {
        // An update carrying no Message (e.g. an edited-channel-post-only payload) must be skipped,
        // not yielded. We enqueue a skip-update first, then a valid text update, and assert the
        // single drained item is the text one — proving the skip branch advances rather than blocks.
        const string skipJson = """
        { "update_id": 1, "edited_channel_post": { "message_id": 9, "chat": { "id": 5, "type": "channel" }, "date": 1735000000, "text": "edit" } }
        """;
        const string textJson = """
        {
          "update_id": 2,
          "message": {
            "message_id": 7,
            "from": { "id": 555, "is_bot": false, "first_name": "Mehmet" },
            "chat": { "id": 555, "type": "private" },
            "date": 1735000100,
            "text": "Su kaçağı var"
          }
        }
        """;

        var adapter = NewAdapter();
        adapter.TryEnqueue(JsonSerializer.Deserialize<Update>(skipJson, JsonBotAPI.Options)!).Should().BeTrue();
        adapter.TryEnqueue(JsonSerializer.Deserialize<Update>(textJson, JsonBotAPI.Options)!).Should().BeTrue();

        var drained = await DrainAsync(adapter, expected: 1);

        drained.Should().ContainSingle();
        drained[0].ExternalId.Should().Be("7");
        drained[0].Text.Should().Be("Su kaçağı var");
        drained[0].SenderName.Should().Be("Mehmet");
    }
}
