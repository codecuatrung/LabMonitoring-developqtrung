using LabMonitoring.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabMonitoring.Infrastructure.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.Schedule).WithMany()
            .HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Room).WithMany()
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Class).WithMany()
            .HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Teacher).WithMany()
            .HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.RoomId, x.Status });
        builder.HasIndex(x => x.StartedAt);
    }
}

internal sealed class SessionComputerConfiguration : IEntityTypeConfiguration<SessionComputer>
{
    public void Configure(EntityTypeBuilder<SessionComputer> builder)
    {
        builder.ToTable("SessionComputers");
        builder.HasKey(x => new { x.SessionId, x.ComputerId });

        builder.HasOne(x => x.Session).WithMany(x => x.Computers)
            .HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Computer).WithMany()
            .HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Student).WithMany()
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.SetNull);
    }
}
