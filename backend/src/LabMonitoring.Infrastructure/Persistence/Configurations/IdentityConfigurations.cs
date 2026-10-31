using LabMonitoring.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabMonitoring.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.Property(x => x.Phone).HasMaxLength(20);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.Username).IsUnique();
        builder.HasIndex(x => x.KeycloakUserId).IsUnique().HasFilter("[KeycloakUserId] IS NOT NULL");
        builder.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IsSystem).HasDefaultValue(false);
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class FunctionScreenConfiguration : IEntityTypeConfiguration<FunctionScreen>
{
    public void Configure(EntityTypeBuilder<FunctionScreen> builder)
    {
        builder.ToTable("FunctionScreens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Route).HasMaxLength(200);
        builder.Property(x => x.Icon).HasMaxLength(50);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoleScreenPermissionConfiguration : IEntityTypeConfiguration<RoleScreenPermission>
{
    public void Configure(EntityTypeBuilder<RoleScreenPermission> builder)
    {
        builder.ToTable("RoleScreenPermissions");
        builder.HasKey(x => new { x.RoleId, x.ScreenId });

        builder.HasOne(x => x.Role).WithMany(x => x.ScreenPermissions)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Screen).WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.ScreenId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserScreenPermissionConfiguration : IEntityTypeConfiguration<UserScreenPermission>
{
    public void Configure(EntityTypeBuilder<UserScreenPermission> builder)
    {
        builder.ToTable("UserScreenPermissions");
        builder.HasKey(x => new { x.UserId, x.ScreenId });

        builder.HasOne(x => x.User).WithMany(x => x.ScreenPermissions)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Screen).WithMany(x => x.UserPermissions)
            .HasForeignKey(x => x.ScreenId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(x => new { x.UserId, x.RoleId });

        builder.HasOne(x => x.User).WithMany(x => x.UserRoles)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Role).WithMany(x => x.UserRoles)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserRoomScopeConfiguration : IEntityTypeConfiguration<UserRoomScope>
{
    public void Configure(EntityTypeBuilder<UserRoomScope> builder)
    {
        builder.ToTable("UserRoomScopes");
        builder.HasKey(x => new { x.UserId, x.RoomId });

        builder.HasOne(x => x.User).WithMany(x => x.RoomScopes)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Room).WithMany(x => x.UserScopes)
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
    }
}
