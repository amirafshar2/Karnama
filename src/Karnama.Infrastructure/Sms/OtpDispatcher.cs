using System.Threading.Channels;
using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Karnama.Infrastructure.Sms;

/// <summary>
/// In-memory queue for OTP SMS. Keeps the provider's latency and errors out of the HTTP response
/// (no account enumeration by timing). Codes live only seconds and never touch disk.
/// M6 moves all notifications to the transactional outbox; OTPs stay here because they expire in 2 minutes.
/// </summary>
internal sealed class OtpDispatcher(ILogger<OtpDispatcher> logger) : IOtpDispatcher
{
    private readonly Channel<(string Phone, string Code)> _queue =
        Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<(string Phone, string Code)> Reader => _queue.Reader;

    public void Enqueue(string phone, string code)
    {
        if (!_queue.Writer.TryWrite((phone, code)))
            logger.LogWarning("OTP queue full; dropped SMS for {Phone}", PhoneNumber.Mask(phone));
    }
}

internal sealed class OtpDispatchService(OtpDispatcher dispatcher, IServiceScopeFactory scopes, ILogger<OtpDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (phone, code) in dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ISmsSender>().SendOtpAsync(phone, code, stoppingToken);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "OTP SMS to {Phone} failed (attempt {Attempt})", PhoneNumber.Mask(phone), attempt);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), stoppingToken);
                }
            }
        }
    }
}
