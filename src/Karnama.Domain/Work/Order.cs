using Karnama.Domain.Common;

namespace Karnama.Domain.Work;

public enum OrderStatus { Submitted, InProgress, Completed, Rejected, Cancelled }

public enum OrderSource { Admin, Client }

public enum OrderPriority { Normal, High, Urgent }

/// <summary>
/// One order of one service for one client. The form answers are stored with the form version they were made with.
/// Accepting it creates one <see cref="WorkItem"/> per workflow stage.
/// </summary>
public sealed class Order : AuditableEntity
{
    public const int MaxTitle = 150;
    public const int MaxNote = 4000;

    private Order() { ServiceName = new LocalizedText(); FormDataJson = "{}"; Title = string.Empty; }

    /// <summary>Human number shown everywhere ("Auftrag 124"), from a database sequence.</summary>
    public long Number { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public LocalizedText ServiceName { get; private set; }
    public int FormVersion { get; private set; }
    public string FormDataJson { get; private set; }
    public string Title { get; private set; }
    public string? Note { get; private set; }
    public OrderSource Source { get; private set; }
    public OrderStatus Status { get; private set; }
    public OrderPriority Priority { get; private set; }
    public DateTimeOffset? Deadline { get; private set; }

    /// <summary>The client package whose quota this order used; null when outside a package.</summary>
    public Guid? ClientPackageId { get; private set; }
    /// <summary>True when the service uses quota but none was left: billed separately at <see cref="Price"/>.</summary>
    public bool OutsidePackage { get; private set; }
    /// <summary>Snapshot of the price in rials when outside a package.</summary>
    public long? Price { get; private set; }

    public string? RejectReason { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public uint Version { get; private set; }

    public static Result<Order> Create(long number, Guid clientId, Guid serviceTypeId, LocalizedText serviceName, int formVersion, string formDataJson,
        string? title, string? note, OrderSource source, OrderPriority priority, DateTimeOffset? deadline, DateTimeOffset now)
    {
        title = title?.Trim();
        if (string.IsNullOrEmpty(title)) title = serviceName.Get("de");
        if (title.Length > MaxTitle) return Error.Validation("orders.title", "Der Titel darf höchstens 150 Zeichen haben.");
        if (note is { Length: > MaxNote }) return Error.Validation("orders.note", "Die Beschreibung darf höchstens 4000 Zeichen haben.");
        if (deadline is { } d && d < now.AddMinutes(-5)) return Error.Validation("orders.deadlinePast", "Die Deadline darf nicht in der Vergangenheit liegen.");
        return new Order
        {
            Number = number,
            ClientId = clientId,
            ServiceTypeId = serviceTypeId,
            ServiceName = serviceName,
            FormVersion = formVersion,
            FormDataJson = formDataJson,
            Title = title,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Source = source,
            Status = OrderStatus.Submitted,
            Priority = priority,
            Deadline = deadline,
        };
    }

    public Result Accept(Guid? clientPackageId, bool outsidePackage, long? price, DateTimeOffset now)
    {
        if (Status != OrderStatus.Submitted) return Error.Conflict("orders.notSubmitted", "Dieser Auftrag wurde bereits geprüft.");
        Status = OrderStatus.InProgress;
        ClientPackageId = clientPackageId;
        OutsidePackage = outsidePackage;
        Price = outsidePackage ? price : null;
        AcceptedAt = now;
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status != OrderStatus.Submitted) return Error.Conflict("orders.notSubmitted", "Nur ein Auftrag, der auf Prüfung wartet, kann abgelehnt werden.");
        if (string.IsNullOrWhiteSpace(reason)) return Error.Validation("orders.rejectReason", "Geben Sie den Ablehnungsgrund an; der Kunde sieht ihn.");
        Status = OrderStatus.Rejected;
        RejectReason = reason.Trim()[..Math.Min(reason.Trim().Length, 1000)];
        return Result.Success();
    }

    /// <summary>
    /// The client withdraws its own order from the portal. Only while the agency hasn't accepted it yet:
    /// after that, work (and package quota) is already planned, so cancelling goes through the agency.
    /// </summary>
    public Result CancelByClient()
    {
        if (Status is OrderStatus.Completed or OrderStatus.Rejected or OrderStatus.Cancelled)
            return Error.Conflict("orders.closed", "Dieser Auftrag ist geschlossen.");
        if (Status != OrderStatus.Submitted)
            return Error.Conflict("orders.alreadyAccepted", "Die Agentur hat diesen Auftrag angenommen und die Arbeit hat begonnen; schreiben Sie der Agentur, wenn Sie stornieren möchten.");
        Status = OrderStatus.Cancelled;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is OrderStatus.Completed or OrderStatus.Rejected or OrderStatus.Cancelled)
            return Error.Conflict("orders.closed", "Dieser Auftrag ist geschlossen.");
        Status = OrderStatus.Cancelled;
        return Result.Success();
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != OrderStatus.InProgress) return;
        Status = OrderStatus.Completed;
        CompletedAt = now;
    }

    /// <summary>Back to work after a finished order got a change request.</summary>
    public void Reopen()
    {
        if (Status == OrderStatus.Completed) { Status = OrderStatus.InProgress; CompletedAt = null; }
    }

    public Result UpdateDetails(string title, string? note, OrderPriority priority, DateTimeOffset? deadline)
    {
        title = title.Trim();
        if (title.Length is 0 or > MaxTitle) return Error.Validation("orders.title", "Der Titel muss zwischen 1 und 150 Zeichen haben.");
        if (note is { Length: > MaxNote }) return Error.Validation("orders.note", "Die Beschreibung darf höchstens 4000 Zeichen haben.");
        Title = title;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Priority = priority;
        Deadline = deadline;
        return Result.Success();
    }

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
}
