using Karnama.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Karnama.Infrastructure.Persistence.Configurations;

internal sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.ToTable("work_schedules");
        b.Property(x => x.Days).AsJson();
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.UserId).IsUnique().AreNullsDistinct(false);  // one default (null) + one per person
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("holidays");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Date).IsUnique();
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.ToTable("shifts");
        b.Property(x => x.Methods).AsJson();
        b.Property(x => x.ReviewReason).HasMaxLength(1000);
        b.Property(x => x.EarlyLeaveReason).HasMaxLength(500);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.UserId, x.Day });
        b.HasIndex(x => x.Day);
        // One open shift per person, guaranteed by the database (double taps, two devices).
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"Status\" = 0").HasDatabaseName("IX_shifts_one_open");
    }
}

internal sealed class AttendanceEventConfiguration : IEntityTypeConfiguration<AttendanceEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceEvent> b)
    {
        b.ToTable("attendance_events");
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.MethodsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => x.ShiftId);
    }
}

internal sealed class WorkTimerConfiguration : IEntityTypeConfiguration<WorkTimer>
{
    public void Configure(EntityTypeBuilder<WorkTimer> b)
    {
        b.ToTable("work_timers");
        b.HasIndex(x => new { x.WorkItemId, x.StartedAt });
        // Only one running timer per person.
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"StoppedAt\" IS NULL").HasDatabaseName("IX_work_timers_one_running");
        b.HasIndex(x => new { x.UserId, x.StartedAt });
    }
}

internal sealed class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> b)
    {
        b.ToTable("attendance_corrections");
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class AttendancePolicyConfiguration : IEntityTypeConfiguration<AttendancePolicy>
{
    public void Configure(EntityTypeBuilder<AttendancePolicy> b)
    {
        b.ToTable("attendance_policies");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Rule).HasMaxLength(300).IsRequired();
        b.Property(x => x.RoleIds).AsJson();
        b.Property(x => x.UserIds).AsJson();
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class AttendancePlaceConfiguration : IEntityTypeConfiguration<AttendancePlace>
{
    public void Configure(EntityTypeBuilder<AttendancePlace> b)
    {
        b.ToTable("attendance_places");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
    }
}

internal sealed class KioskCodeUseConfiguration : IEntityTypeConfiguration<KioskCodeUse>
{
    public void Configure(EntityTypeBuilder<KioskCodeUse> b)
    {
        b.ToTable("kiosk_code_uses");
        b.HasKey(x => new { x.Step, x.N });
    }
}

internal sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.ToTable("leave_types");
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Color).HasMaxLength(7).IsRequired();
        b.Property(x => x.AnnualDays).HasPrecision(6, 2);
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.ToTable("leave_requests");
        b.Property(x => x.Reason).HasMaxLength(LeaveRequest.MaxReason).IsRequired();
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.UserId, x.From, x.To });
        b.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

internal sealed class AbsenceConfiguration : IEntityTypeConfiguration<Absence>
{
    public void Configure(EntityTypeBuilder<Absence> b)
    {
        b.ToTable("absences");
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.UserId, x.Day }).IsUnique();   // one per person per day, even if two workers run the job
        b.HasIndex(x => new { x.Status, x.ReasonDeadline });
    }
}
