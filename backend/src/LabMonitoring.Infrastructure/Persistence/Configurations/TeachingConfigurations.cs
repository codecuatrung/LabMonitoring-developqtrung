using LabMonitoring.Domain.Teaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabMonitoring.Infrastructure.Persistence.Configurations;

internal sealed class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.ToTable("Classes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Semester).HasMaxLength(20);
        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasOne(x => x.Teacher).WithMany()
            .HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("Students");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StudentCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.HasIndex(x => x.StudentCode).IsUnique();
    }
}

internal sealed class ClassStudentConfiguration : IEntityTypeConfiguration<ClassStudent>
{
    public void Configure(EntityTypeBuilder<ClassStudent> builder)
    {
        builder.ToTable("ClassStudents");
        builder.HasKey(x => new { x.ClassId, x.StudentId });

        builder.HasOne(x => x.Class).WithMany(x => x.ClassStudents)
            .HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Student).WithMany(x => x.ClassStudents)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScheduleConfiguration : IEntityTypeConfiguration<Schedule>
{
    public void Configure(EntityTypeBuilder<Schedule> builder)
    {
        builder.ToTable("Schedules");
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Class).WithMany(x => x.Schedules)
            .HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Room).WithMany(x => x.Schedules)
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.RoomId, x.DayOfWeek });
    }
}
