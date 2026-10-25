using System.Net;
using FluentAssertions;
using Karnama.Application.Media;
using Karnama.Domain.Media;

namespace Karnama.UnitTests;

public class BotParseTests
{
    [Fact]
    public void Photo_keeps_the_largest_size_and_the_smallest_as_preview()
    {
        var u = ParsedUpdate.Parse("""
            {"update_id":1,"message":{"message_id":9,"date":1700000000,"chat":{"id":5,"type":"private"},"caption":"c",
             "photo":[{"file_id":"small","file_unique_id":"s","file_size":900},{"file_id":"big","file_unique_id":"b","file_size":90000}]}}
            """)!;
        u.Media!.Kind.Should().Be(MediaKind.Photo);
        u.Media.FileId.Should().Be("big");
        u.Media.ThumbFileId.Should().Be("small");
        u.Caption.Should().Be("c");
        u.Date.ToUnixTimeSeconds().Should().Be(1700000000);
    }

    [Fact]
    public void Groups_and_garbage_are_ignored_and_buttons_are_read()
    {
        ParsedUpdate.Parse("""{"update_id":2,"message":{"message_id":1,"chat":{"id":-100,"type":"supergroup"},"text":"hi"}}""").Should().BeNull();
        ParsedUpdate.Parse("not json").Should().BeNull();
        ParsedUpdate.Parse("""{"message":{}}""").Should().BeNull();
        var cb = ParsedUpdate.Parse("""{"update_id":3,"callback_query":{"id":"q1","from":{"id":44},"data":"l|x"}}""")!;
        cb.ChatId.Should().Be(44);
        cb.Callback!.Data.Should().Be("l|x");
    }

    [Fact]
    public void Short_ids_fit_telegram_buttons_and_round_trip()
    {
        var id = Guid.CreateVersion7();
        var s = MediaService.ShortId(id);
        s.Length.Should().Be(22);
        $"g|{s}|{s}".Length.Should().BeLessThanOrEqualTo(64, "callback_data is limited to 64 bytes");
    }

    [Theory]
    [InlineData("""{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}""", false, true)]
    [InlineData("""{"ok":false,"error_code":429,"description":"Too Many Requests"}""", false, false)]
    [InlineData("""{"ok":true,"result":{"message_id":5}}""", true, false)]
    public void Bot_api_answers_decide_retry_or_stop(string json, bool ok, bool permanent)
    {
        var r = Karnama.Infrastructure.Notify.BotGateway.Interpret(HttpStatusCode.OK, json);
        r.Ok.Should().Be(ok);
        r.Permanent.Should().Be(permanent);
    }
}
