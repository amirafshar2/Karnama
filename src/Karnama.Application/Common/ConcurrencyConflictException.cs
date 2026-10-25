namespace Karnama.Application.Common;

/// <summary>Thrown by the data layer when an optimistic concurrency check fails; mapped to HTTP 409.</summary>
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("این مورد هم‌زمان توسط کس دیگری تغییر کرده است. صفحه را تازه کنید و دوباره امتحان کنید.", inner);
