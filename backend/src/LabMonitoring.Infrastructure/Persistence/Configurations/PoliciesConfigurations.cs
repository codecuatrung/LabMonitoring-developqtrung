using LabMonitoring.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabMonitoring.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<ManagedApplication>
{
    public void Configure(EntityTypeBuilder<ManagedApplication> builder)
    {
        builder.ToTable("Applications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ExecutablePath).HasMaxLength(255).IsRequired();
    }
}

internal sealed class ApplicationPolicyConfiguration : IEntityTypeConfiguration<ApplicationPolicy>
{
    public void Configure(EntityTypeBuilder<ApplicationPolicy> builder)
    {
        builder.ToTable("ApplicationPolicies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PolicyType).HasConversion<string>().HasMaxLength(10).IsRequired();

        builder.HasOne(x => x.Room).WithMany()
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Application).WithMany(x => x.Policies)
            .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.RoomId, x.ApplicationId }).IsUnique();
    }
}

internal sealed class WebsitePolicyConfiguration : IEntityTypeConfiguration<WebsitePolicy>
{
    public void Configure(EntityTypeBuilder<WebsitePolicy> builder)
    {
        builder.ToTable("WebsitePolicies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Domain).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PolicyType).HasConversion<string>().HasMaxLength(10).IsRequired();

        builder.HasOne(x => x.Room).WithMany()
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.RoomId, x.Domain }).IsUnique();
    }
}
