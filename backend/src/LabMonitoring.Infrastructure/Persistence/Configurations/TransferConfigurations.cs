using LabMonitoring.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabMonitoring.Infrastructure.Persistence.Configurations;

internal sealed class TransferFolderConfiguration : IEntityTypeConfiguration<TransferFolder>
{
    public void Configure(EntityTypeBuilder<TransferFolder> builder)
    {
        builder.ToTable("TransferFolders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Path).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.HasIndex(x => new { x.RoomId, x.Direction });

        builder.HasOne(x => x.Room).WithMany()
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FileTransferConfiguration : IEntityTypeConfiguration<FileTransfer>
{
    public void Configure(EntityTypeBuilder<FileTransfer> builder)
    {
        builder.ToTable("FileTransfers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.FolderPath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.Filter).HasMaxLength(200);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.CommandId);
        builder.HasIndex(x => x.RoomId);

        builder.HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CollectedFileConfiguration : IEntityTypeConfiguration<CollectedFile>
{
    public void Configure(EntityTypeBuilder<CollectedFile> builder)
    {
        builder.ToTable("CollectedFiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.FileTransferId, x.ComputerId }).IsUnique();

        builder.HasOne(x => x.FileTransfer).WithMany(x => x.Collected)
            .HasForeignKey(x => x.FileTransferId).OnDelete(DeleteBehavior.Cascade);
        // Cascade from Computers so the bulk "delete machines" script and the Computers DELETE endpoint
        // don't get blocked by collected zips (the zip files on disk are cleaned by the retention job).
        builder.HasOne(x => x.Computer).WithMany()
            .HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Cascade);
    }
}
