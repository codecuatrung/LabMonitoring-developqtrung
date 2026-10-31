using LabMonitoring.Domain.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabMonitoring.Infrastructure.Persistence.Configurations;

internal sealed class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("Buildings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.MapImageUrl).HasMaxLength(300);
        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasOne(x => x.Building).WithMany(x => x.Rooms)
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ComputerConfiguration : IEntityTypeConfiguration<Computer>
{
    public void Configure(EntityTypeBuilder<Computer> builder)
    {
        builder.ToTable("Computers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AgentSecretHash).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AgentTokenHash).HasMaxLength(255).IsRequired();
        builder.Property(x => x.IsApproved).HasDefaultValue(false);
        builder.Property(x => x.MacAddress).HasMaxLength(17);
        builder.Property(x => x.AgentVersion).HasMaxLength(20);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(x => x.MachineId).IsUnique();
        builder.HasIndex(x => x.AgentTokenHash).IsUnique().HasFilter("[AgentTokenHash] <> ''");

        builder.HasOne(x => x.Room).WithMany(x => x.Computers)
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ComputerSpecConfiguration : IEntityTypeConfiguration<ComputerSpec>
{
    public void Configure(EntityTypeBuilder<ComputerSpec> builder)
    {
        builder.ToTable("ComputerSpecs");
        builder.HasKey(x => x.ComputerId);
        builder.Property(x => x.CpuModel).HasMaxLength(150);
        builder.Property(x => x.OsVersion).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Computer).WithOne(x => x.Spec)
            .HasForeignKey<ComputerSpec>(x => x.ComputerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ComputerStatusConfiguration : IEntityTypeConfiguration<ComputerStatus>
{
    public void Configure(EntityTypeBuilder<ComputerStatus> builder)
    {
        builder.ToTable("ComputerStatuses");
        builder.HasKey(x => x.ComputerId);
        builder.Property(x => x.CurrentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CurrentWindowsUser).HasMaxLength(100);
        builder.Property(x => x.LastIpAddress).HasMaxLength(45);
        builder.HasIndex(x => x.LastHeartbeatAt);

        builder.HasOne(x => x.Computer).WithOne(x => x.Status)
            .HasForeignKey<ComputerStatus>(x => x.ComputerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ComputerMetricConfiguration : IEntityTypeConfiguration<ComputerMetric>
{
    public void Configure(EntityTypeBuilder<ComputerMetric> builder)
    {
        builder.ToTable("ComputerMetrics");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CpuUsagePercent).HasPrecision(5, 2);
        builder.Property(x => x.RamUsagePercent).HasPrecision(5, 2);
        builder.Property(x => x.DiskUsagePercent).HasPrecision(5, 2);
        builder.Property(x => x.TemperatureC).HasPrecision(5, 2);

        builder.HasIndex(x => new { x.ComputerId, x.RecordedAt });
        builder.HasIndex(x => x.RecordedAt);

        builder.HasOne(x => x.Computer).WithMany(x => x.Metrics)
            .HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AgentPackageConfiguration : IEntityTypeConfiguration<AgentPackage>
{
    public void Configure(EntityTypeBuilder<AgentPackage> builder)
    {
        builder.ToTable("AgentPackages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.Version).IsUnique();
        builder.HasIndex(x => x.UploadedAt);

        builder.HasOne(x => x.UploadedBy).WithMany()
            .HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.SetNull);
    }
}
