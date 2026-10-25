using System.Net.Http.Json;
using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karnama.Infrastructure.Sms;

public sealed class SmsOptions
{
    public const string Section = "Sms";
    public string Provider { get; set; } = "Console";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Kavenegar: template name. sms.ir: numeric template id.</summary>
    public string OtpTemplate { get; set; } = string.Empty;
}

/// <summary>Development only: writes the code to the console log. Refuses to run in Production.</summary>
internal sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger, IHostEnvironment env) : ISmsSender
{
    public Task SendOtpAsync(string phone, string code, CancellationToken ct)
    {
        if (env.IsProduction()) throw new InvalidOperationException("Console SMS provider is not allowed in Production.");
        logger.LogWarning("[DEV SMS] OTP for {Phone}: {Code}", PhoneNumber.Mask(phone), code);
        return Task.CompletedTask;
    }
}

/// <summary>Kavenegar Verify Lookup. The API key sits in the URL path, so HttpClient logging must stay off for this client.</summary>
internal sealed class KavenegarSmsSender(HttpClient http, IOptions<SmsOptions> options) : ISmsSender
{
    public async Task SendOtpAsync(string phone, string code, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"v1/{Uri.EscapeDataString(o.ApiKey)}/verify/lookup.json?receptor={phone}&token={code}&template={Uri.EscapeDataString(o.OtpTemplate)}";
        using var response = await http.PostAsync(url, null, ct);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed class SmsIrSmsSender(HttpClient http, IOptions<SmsOptions> options) : ISmsSender
{
    public async Task SendOtpAsync(string phone, string code, CancellationToken ct)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/send/verify")
        {
            Content = JsonContent.Create(new
            {
                mobile = phone,
                templateId = int.Parse(o.OtpTemplate, System.Globalization.CultureInfo.InvariantCulture),
                parameters = new[] { new { name = "Code", value = code } },
            }),
        };
        request.Headers.Add("x-api-key", o.ApiKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
