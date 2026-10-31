using LabMonitoring.Domain.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabMonitoring.Infrastructure.Persistence.Configurations;

internal sealed class CommandConfiguration : IEntityTypeConfiguration<Command>
{
    public void Configure(EntityTypeBuilder<Command> builder)
    {
        builder.ToTable("Commands");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CommandType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Signature).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.Nonce).IsUnique();
        builder.HasIndex(x => x.IssuedAt);

        builder.HasOne(x => x.Computer).WithMany()
            .HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Room).WithMany()
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Session).WithMany()
            .HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.IssuedBy).WithMany()
            .HasForeignKey(x => x.IssuedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CommandResultConfiguration : IEntityTypeConfiguration<CommandResult>
{
    public void Configure(EntityTypeBuilder<CommandResult> builder)
    {
        builder.ToTable("CommandResults");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);

        builder.HasOne(x => x.Command).WithMany(x => x.Results)
            .HasForeignKey(x => x.CommandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Computer).WithMany()
            .HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CommandId, x.ComputerId }).IsUnique();
    }
}
