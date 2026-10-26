namespace Karnama.Infrastructure.Qa;

/// <summary>QA job "outbox": one pass of the Worker's outbox dispatcher, run inside the API on demand.</summary>
internal sealed class QaOutbox(Notify.OutboxDispatcher dispatcher) : Karnama.Application.Abstractions.IQaOutbox
{
    public Task<int> DispatchOnceAsync(CancellationToken ct) => dispatcher.DispatchOnceAsync(ct);
}
