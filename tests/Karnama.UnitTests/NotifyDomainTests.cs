using FluentAssertions;
using Karnama.Application.Attendance;
using Karnama.Domain.Catalog;
using Karnama.Domain.Notify;
using Karnama.Domain.Publishing;
using Karnama.Infrastructure.Notify;

namespace Karnama.UnitTests;

public sealed class NotifyDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Quiet_hours_delay_non_important_messages_until_morning()
    {
        var p = new NotificationPrefs(Guid.NewGuid());   // default quiet 22:00–08:00 Tehran
        var night = Iran.At(new DateOnly(2026, 10, 25), new TimeOnly(23, 30));
        var at = p.SendAt(night, Iran.Local, Iran.At);
        Iran.Local(at).TimeOfDay.Should().Be(TimeSpan.FromHours(8));
        at.Should().BeAfter(night);
        var day = Iran.At(new DateOnly(2026, 10, 25), new TimeOnly(11, 0));
        p.SendAt(day, Iran.Local, Iran.At).Should().Be(day);
    }

    [Fact]
    public void Outbox_backs_off_and_gives_up()
    {
        var m = new OutboxMessage(Channel.Bale, Guid.NewGuid(), "x", null, "k", Now, Now);
        m.Fail("HttpRequestException", Now);
        m.Status.Should().Be(OutboxStatus.Pending);
        m.DueAt.Should().BeAfter(Now);
        for (var i = 0; i < 7; i++) m.Fail("HttpRequestException", Now);
        m.Status.Should().Be(OutboxStatus.Failed);
    }

    [Fact]
    public void Publish_job_flow_and_client_rules()
    {
        var at = Now.AddHours(1);
        var j = PublishJob.Create(Guid.NewGuid(), null, Guid.NewGuid(), "cafe", PageManagement.AgencyManaged, at, "کپشن", ["#a", "a", "b c", ""], [], Now).Value!;
        j.Hashtags.Should().Equal("a");
        PublishJob.Create(Guid.NewGuid(), null, Guid.NewGuid(), "cafe", PageManagement.AgencyManaged, Now.AddHours(-1), "x", [], [], Now).IsFailure.Should().BeTrue();
        j.MarkDue(Now).Should().BeFalse();
        j.MarkDue(at).Should().BeTrue();
        j.MarkDue(at).Should().BeFalse();   // once
        j.MarkOverdue(at.AddHours(1), TimeSpan.FromHours(2)).Should().BeFalse();
        j.MarkOverdue(at.AddHours(2), TimeSpan.FromHours(2)).Should().BeTrue();
        j.MarkPublished(Guid.NewGuid(), true, null, at).IsFailure.Should().BeTrue();          // agency page: the client can't confirm
        j.MarkPublished(Guid.NewGuid(), false, "http://x.com/p", at).IsFailure.Should().BeTrue();  // https only
        j.MarkPublished(Guid.NewGuid(), false, "https://instagram.com/p/1", at).IsSuccess.Should().BeTrue();
        j.Cancel().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Gateway_signature_rejects_tampering_and_replay()
    {
        const string secret = "s3cret-0123456789";
        var ts = Now.ToUnixTimeSeconds();
        var sig = GatewaySignature.Compute(secret, ts, "{\"a\":1}");
        GatewaySignature.Verify(secret, ts.ToString(), sig, "{\"a\":1}", Now).Should().BeTrue();
        GatewaySignature.Verify(secret, ts.ToString(), sig, "{\"a\":2}", Now).Should().BeFalse();
        GatewaySignature.Verify(secret, ts.ToString(), sig, "{\"a\":1}", Now.AddMinutes(6)).Should().BeFalse();
        GatewaySignature.Verify("", ts.ToString(), sig, "{\"a\":1}", Now).Should().BeFalse();
    }
}
