namespace Karnama.Application.Common;

/// <summary>Thrown by the data layer when an optimistic concurrency check fails; mapped to HTTP 409.</summary>
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("Dieser Eintrag wurde gleichzeitig von jemand anderem geändert. Laden Sie die Seite neu und versuchen Sie es erneut.", inner);
