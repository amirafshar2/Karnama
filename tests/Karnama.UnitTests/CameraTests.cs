using FluentAssertions;
using Karnama.Domain.Cctv;
using Xunit;

namespace Karnama.UnitTests;

public sealed class CameraTests
{
    [Theory]
    [InlineData("rtsp://admin:Secret1@192.168.1.64:554/Streaming/Channels/101", "rtsp://admin:••••@192.168.1.64:554/Streaming/Channels/101")]
    [InlineData("rtsp://192.168.1.9/live?user=admin&password=Secret1&ch=1", "rtsp://192.168.1.9/live?user=admin&password=••••&ch=1")]
    [InlineData("rtsp://10.0.0.5/cam?pwd=Secret1", "rtsp://10.0.0.5/cam?pwd=••••")]
    public void Passwords_never_show(string url, string masked)
    {
        var u = Camera.ValidateSource(url);
        u.IsSuccess.Should().BeTrue();
        Camera.Mask(u.Value!).Should().Be(masked).And.NotContain("Secret1");
    }

    [Theory]
    [InlineData("rtsp://8.8.8.8/x")]
    [InlineData("rtsp://camera.example.com/x")]
    [InlineData("rtsp://[::ffff:8.8.8.8]/x")]
    [InlineData("http://192.168.1.5/x")]
    [InlineData("rtsp://127.0.0.1/x")]
    [InlineData("rtsp://169.254.169.254/x")]
    public void Only_office_cameras(string url) => Camera.ValidateSource(url).IsFailure.Should().BeTrue();
}
