using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Tests;

// WP7 AC1 — GradeMode enum on Course (default Letter; Letter/PassFail/Gpa), verified at
// entity + EF-model + SQLite persistence level only (migration internals are out of scope).
public class CourseGradeModeTests
{
    private static PropertyInfo? GradeModeProperty => typeof(Course).GetProperty("GradeMode");

    [Fact]
    public void GradeMode_Values_ReturnsLetterPassFailGpa()
    {
        // Arrange / Act
        var names = Enum.GetNames<GradeMode>();

        // Assert
        Assert.Equal(new[] { "Letter", "PassFail", "Gpa" }, names);
        Assert.Equal(0, (int)GradeMode.Letter); // Letter must be the 0/default member
        Assert.Equal(1, (int)GradeMode.PassFail);
        Assert.Equal(2, (int)GradeMode.Gpa);
    }

    [Fact]
    public void CourseEntity_GradeModeProperty_ReturnsGradeModeTypedProperty()
    {
        // Arrange / Act
        var property = GradeModeProperty;

        // Assert — RED until Course gains the GradeMode property (WP7 AC1 + migration)
        Assert.NotNull(property);
        Assert.Same(typeof(GradeMode), property!.PropertyType);
    }

    [Fact]
    public void CourseEntity_NewCourse_ReturnsLetterDefault()
    {
        // Arrange
        var property = GradeModeProperty;
        Assert.NotNull(property);

        // Act
        var value = (GradeMode)property!.GetValue(new Course())!;

        // Assert
        Assert.Equal(GradeMode.Letter, value);
    }

    [Fact]
    public async Task ApplicationDbContext_CourseGradeModeProperty_ReturnsIntBackedMappedProperty()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new ApplicationDbContext(options);

        // Act
        var entityType = context.Model.FindEntityType(typeof(Course));
        Assert.NotNull(entityType);
        var property = entityType!.FindProperty("GradeMode");

        // Assert — mapped as the enum's int-backed store type, not text
        Assert.NotNull(property);
        var mapping = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping>(
            property!.GetTypeMapping());
        Assert.Equal("INTEGER", mapping.StoreType);

        await connection.CloseAsync();
    }

    [Fact]
    public async Task ApplicationDbContext_SaveAndReloadCourseWithGpaMode_ReturnsPersistedGpaMode()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var property = GradeModeProperty;
        Assert.NotNull(property);

        using (var context = new ApplicationDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            var course = new Course { Name = "Gpa Course", Credits = 3 };
            property!.SetValue(course, GradeMode.Gpa);
            context.Courses.Add(course);
            await context.SaveChangesAsync();
        }

        // Act
        GradeMode reloaded;
        using (var context = new ApplicationDbContext(options))
        {
            var course = await context.Courses.SingleAsync(c => c.Name == "Gpa Course");
            reloaded = (GradeMode)property!.GetValue(course)!;
        }

        // Assert
        Assert.Equal(GradeMode.Gpa, reloaded);

        await connection.CloseAsync();
    }
}
