using System.Text.Json;
using Moq;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Domain.DTOs;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Tests;

public class ChatToolsTests
{
    [Fact]
    public void GetTools_WhenInspected_ReturnsNineUniqueFunctionNames()
    {
        var tools = CreateTools(out _, out _, out _).GetTools();

        var names = tools.Select(t => t.Name).ToList();

        Assert.Equal(9, names.Count);
        Assert.Equal(9, names.Distinct().Count());
        Assert.Contains("ListStudents", names);
        Assert.Contains("GetStudentById", names);
        Assert.Contains("GetStudentsEnrolledInMoreThan", names);
        Assert.Contains("ListCourses", names);
        Assert.Contains("GetCourseById", names);
        Assert.Contains("GetStudentCountPerCourse", names);
        Assert.Contains("ListEnrollments", names);
        Assert.Contains("GetStudentsInCourse", names);
        Assert.Contains("GetCoursesForStudent", names);
    }

    [Fact]
    public async Task ListStudentsAsync_TwoStudents_ReturnsJsonWithFormattedEnrollmentDates()
    {
        var tools = CreateTools(out var students, out _, out _);
        students
            .Setup(s => s.GetStudentsAsync())
            .ReturnsAsync(
            [
                new Student
                {
                    Id = 1,
                    Name = "Ada",
                    Email = "ada@example.com",
                    Phone = "555-0100",
                    Address = "1 Test Street",
                    DateOfEnroll = new DateTime(2024, 9, 1)
                },
                new Student { Id = 2, Name = "Grace", Email = "grace@example.com" }
            ]);

        var json = ChatJson.Parse(await tools.ListStudentsAsync());

        Assert.Equal(2, json.GetArrayLength());
        Assert.Equal(1, json[0].GetProperty("Id").GetInt32());
        Assert.Equal("Ada", json[0].GetProperty("Name").GetString());
        Assert.Equal("ada@example.com", json[0].GetProperty("Email").GetString());
        Assert.Equal("2024-09-01", json[0].GetProperty("DateOfEnroll").GetString());
        Assert.Equal(JsonValueKind.Null, json[1].GetProperty("DateOfEnroll").ValueKind);
    }

    [Fact]
    public async Task ListStudentsAsync_EmptyCollection_ReturnsEmptyJsonArray()
    {
        var tools = CreateTools(out var students, out _, out _);
        students.Setup(s => s.GetStudentsAsync()).ReturnsAsync([]);

        var json = ChatJson.Parse(await tools.ListStudentsAsync());

        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public async Task GetStudentByIdAsync_Found_ReturnsStudentJson()
    {
        var tools = CreateTools(out var students, out _, out _);
        students
            .Setup(s => s.GetStudentByIdAsync(7))
            .ReturnsAsync(new Student
            {
                Id = 7,
                Name = "Ada",
                Email = "ada@example.com",
                Phone = "555-0100",
                Address = "1 Test Street",
                DateOfEnroll = new DateTime(2024, 1, 2)
            });

        var json = ChatJson.Parse(await tools.GetStudentByIdAsync(7));

        Assert.Equal(7, json.GetProperty("Id").GetInt32());
        Assert.Equal("Ada", json.GetProperty("Name").GetString());
        Assert.Equal("2024-01-02", json.GetProperty("DateOfEnroll").GetString());
    }

    [Fact]
    public async Task GetStudentByIdAsync_NotFound_ReturnsErrorJson()
    {
        var tools = CreateTools(out var students, out _, out _);
        students.Setup(s => s.GetStudentByIdAsync(42)).ReturnsAsync((Student?)null);

        var json = ChatJson.Parse(await tools.GetStudentByIdAsync(42));

        Assert.Equal("No student found with id 42.", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetStudentsEnrolledInMoreThanAsync_WithMatches_ReturnsIdNameEmailAndThreshold()
    {
        var tools = CreateTools(out var students, out _, out _);
        students
            .Setup(s => s.GetStudentsEnrolledInMoreThan(2))
            .ReturnsAsync([new Student { Id = 3, Name = "Ada", Email = "ada@example.com" }]);

        var json = ChatJson.Parse(await tools.GetStudentsEnrolledInMoreThanAsync(2));

        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal(3, json[0].GetProperty("Id").GetInt32());
        Assert.Equal("Ada", json[0].GetProperty("Name").GetString());
        Assert.Equal("ada@example.com", json[0].GetProperty("Email").GetString());
        students.Verify(s => s.GetStudentsEnrolledInMoreThan(2), Times.Once);
    }

    [Fact]
    public async Task ListCoursesAsync_TwoCourses_ReturnsIdNameCredits()
    {
        var tools = CreateTools(out _, out var courses, out _);
        courses
            .Setup(s => s.GetAllCourses())
            .ReturnsAsync(
            [
                new Course { Id = 1, Name = "Algebra", Credits = 3 },
                new Course { Id = 2, Name = "Physics", Credits = 4 }
            ]);

        var json = ChatJson.Parse(await tools.ListCoursesAsync());

        Assert.Equal(2, json.GetArrayLength());
        Assert.Equal("Algebra", json[0].GetProperty("Name").GetString());
        Assert.Equal(3, json[0].GetProperty("Credits").GetInt32());
        Assert.Equal(4, json[1].GetProperty("Credits").GetInt32());
    }

    [Fact]
    public async Task ListCoursesAsync_EmptyCollection_ReturnsEmptyJsonArray()
    {
        var tools = CreateTools(out _, out var courses, out _);
        courses.Setup(s => s.GetAllCourses()).ReturnsAsync([]);

        var json = ChatJson.Parse(await tools.ListCoursesAsync());

        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public async Task GetCourseByIdAsync_Found_ReturnsCourseJson()
    {
        var tools = CreateTools(out _, out var courses, out _);
        courses.Setup(s => s.GetCourseById(5)).ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3 });

        var json = ChatJson.Parse(await tools.GetCourseByIdAsync(5));

        Assert.Equal(5, json.GetProperty("Id").GetInt32());
        Assert.Equal("Algebra", json.GetProperty("Name").GetString());
        Assert.Equal(3, json.GetProperty("Credits").GetInt32());
    }

    [Fact]
    public async Task GetCourseByIdAsync_NotFound_ReturnsErrorJson()
    {
        var tools = CreateTools(out _, out var courses, out _);
        courses.Setup(s => s.GetCourseById(99)).ReturnsAsync((Course?)null);

        var json = ChatJson.Parse(await tools.GetCourseByIdAsync(99));

        Assert.Equal("No course found with id 99.", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetStudentCountPerCourseAsync_WithData_ReturnsCourseNameAndCount()
    {
        var tools = CreateTools(out _, out var courses, out _);
        courses
            .Setup(s => s.GetStudentCountPerCourse())
            .ReturnsAsync(
            [
                new CourseStudentCountDto { CourseName = "Algebra", StudentCount = 4 },
                new CourseStudentCountDto { CourseName = "Physics", StudentCount = 0 }
            ]);

        var json = ChatJson.Parse(await tools.GetStudentCountPerCourseAsync());

        Assert.Equal(2, json.GetArrayLength());
        Assert.Equal("Algebra", json[0].GetProperty("CourseName").GetString());
        Assert.Equal(4, json[0].GetProperty("StudentCount").GetInt32());
    }

    [Fact]
    public async Task ListEnrollmentsAsync_WithNavigations_ReturnsStudentCourseNamesAndGrade()
    {
        var tools = CreateTools(out _, out _, out var enrollments);
        enrollments
            .Setup(s => s.GetEnrollments())
            .ReturnsAsync(
            [
                new Enrollment
                {
                    Id = 1,
                    StudentId = 10,
                    CourseId = 20,
                    Grade = 3.5m,
                    Student = new Student { Id = 10, Name = "Ada" },
                    Course = new Course { Id = 20, Name = "Algebra" }
                }
            ]);

        var json = ChatJson.Parse(await tools.ListEnrollmentsAsync());

        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal("Ada", json[0].GetProperty("StudentName").GetString());
        Assert.Equal("Algebra", json[0].GetProperty("CourseName").GetString());
        Assert.Equal(3.5m, json[0].GetProperty("Grade").GetDecimal());
    }

    [Fact]
    public async Task ListEnrollmentsAsync_NullNavigations_ReturnsNullNames()
    {
        var tools = CreateTools(out _, out _, out var enrollments);
        enrollments
            .Setup(s => s.GetEnrollments())
            .ReturnsAsync(
            [
                new Enrollment { Id = 1, StudentId = 10, CourseId = 20, Student = null!, Course = null! }
            ]);

        var json = ChatJson.Parse(await tools.ListEnrollmentsAsync());

        Assert.Equal(JsonValueKind.Null, json[0].GetProperty("StudentName").ValueKind);
        Assert.Equal(JsonValueKind.Null, json[0].GetProperty("CourseName").ValueKind);
    }

    [Fact]
    public async Task GetStudentsInCourseAsync_WithStudents_ReturnsStudentIdsAndNames()
    {
        var tools = CreateTools(out _, out _, out var enrollments);
        enrollments
            .Setup(s => s.GetStudentsInCourse(20))
            .ReturnsAsync([new Student { Id = 10, Name = "Ada", Email = "ada@example.com" }]);

        var json = ChatJson.Parse(await tools.GetStudentsInCourseAsync(20));

        Assert.Equal(10, json[0].GetProperty("Id").GetInt32());
        Assert.Equal("Ada", json[0].GetProperty("Name").GetString());
        enrollments.Verify(s => s.GetStudentsInCourse(20), Times.Once);
    }

    [Fact]
    public async Task GetCoursesForStudentAsync_WithCourses_ReturnsCourseIdsAndNames()
    {
        var tools = CreateTools(out _, out _, out var enrollments);
        enrollments
            .Setup(s => s.GetCoursesForStudent(10))
            .ReturnsAsync([new Course { Id = 20, Name = "Algebra", Credits = 3 }]);

        var json = ChatJson.Parse(await tools.GetCoursesForStudentAsync(10));

        Assert.Equal(20, json[0].GetProperty("Id").GetInt32());
        Assert.Equal("Algebra", json[0].GetProperty("Name").GetString());
        Assert.Equal(3, json[0].GetProperty("Credits").GetInt32());
        enrollments.Verify(s => s.GetCoursesForStudent(10), Times.Once);
    }

    [Fact]
    public async Task ListEnrollmentsAsync_EmptyCollection_ReturnsEmptyJsonArray()
    {
        var tools = CreateTools(out _, out _, out var enrollments);
        enrollments.Setup(s => s.GetEnrollments()).ReturnsAsync([]);

        var json = ChatJson.Parse(await tools.ListEnrollmentsAsync());

        Assert.Equal(0, json.GetArrayLength());
    }

    private static ChatTools CreateTools(
        out Mock<IStudentService> students,
        out Mock<ICourseService> courses,
        out Mock<IEnrollmentService> enrollments)
    {
        students = new Mock<IStudentService>();
        courses = new Mock<ICourseService>();
        enrollments = new Mock<IEnrollmentService>();

        return new ChatTools(students.Object, courses.Object, enrollments.Object);
    }
}
