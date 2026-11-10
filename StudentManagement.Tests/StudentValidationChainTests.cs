using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Services;
using StudentManagement.Application.Validation;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

// WP8 AC7 — Chain of Responsibility: RequiredFieldsRule -> EmailFormatRule ->
// DuplicateEmailRule, run by StudentService on create AND update. A failing link
// short-circuits the chain and raises ValidationException before the repository is
// touched. Existing API status-code behavior is unchanged (legacy construction with
// no chain keeps saving; guarded by the untouched StudentApiControllerTests suite).
public class StudentValidationChainTests
{
    private sealed class SpyHandler : IValidationHandler
    {
        private readonly string _name;
        private readonly List<string> _callLog;
        private readonly string? _error;
        private IValidationHandler? _next;

        public SpyHandler(string name, List<string> callLog, string? error = null)
        {
            _name = name;
            _callLog = callLog;
            _error = error;
        }

        public IValidationHandler SetNext(IValidationHandler next)
        {
            _next = next;
            return next;
        }

        public Task<string?> ValidateAsync(Student student)
        {
            _callLog.Add(_name);
            if (_error is not null)
                return Task.FromResult<string?>(_error);

            return _next is null ? Task.FromResult<string?>(null) : _next.ValidateAsync(student);
        }
    }

    private static Student ValidStudent() =>
        new() { Name = "Ada Lovelace", Email = "ada@example.com" };

    private static (StudentService Service, Mock<IStudentRepository> StudentRepository) CreateService(
        IValidationHandler? chain = null,
        IEnumerable<Student>? existingStudents = null)
    {
        var studentRepository = new Mock<IStudentRepository>();
        studentRepository.Setup(r => r.GetAllAsyncReadOnly())
                         .ReturnsAsync(existingStudents ?? new List<Student>());
        studentRepository.Setup(r => r.AddAsync(It.IsAny<Student>())).Returns(Task.CompletedTask);
        studentRepository.Setup(r => r.SaveAsync()).Returns(Task.CompletedTask);

        var service = new StudentService(
            studentRepository.Object,
            new Mock<IEnrollmentRepository>().Object,
            new Mock<IDistributedCache>().Object,
            NullLogger<StudentService>.Instance,
            chain);

        return (service, studentRepository);
    }

    // Links the spies head-first in registration order and returns the HEAD.
    private static IValidationHandler BuildChain(List<string> log, params (string Name, string? Error)[] links)    {
        SpyHandler? head = null;
        SpyHandler? previous = null;
        foreach (var (name, error) in links)
        {
            var spy = new SpyHandler(name, log, error);
            if (head is null)
                head = spy;
            else
                previous!.SetNext(spy);
            previous = spy;
        }

        return head!;
    }

    // ---------- each rule in isolation ----------

    [Fact]
    public async Task RequiredFieldsRule_ValidateAsync_CompleteStudent_ReturnsNoErrorAndForwards()
    {
        // Arrange
        var log = new List<string>();
        var next = new SpyHandler("next", log);
        var rule = new RequiredFieldsRule();
        rule.SetNext(next);

        // Act
        var error = await rule.ValidateAsync(ValidStudent());

        // Assert
        Assert.True(string.IsNullOrEmpty(error));
        Assert.Equal(new[] { "next" }, log);
    }

    [Theory]
    [InlineData(null, "a@example.com", "Name")]   // missing name
    [InlineData("   ", "a@example.com", "Name")]  // blank name
    public async Task RequiredFieldsRule_ValidateAsync_MissingName_ReturnsNameErrorAndShortCircuits(string? name, string email, string expectedFragment)
    {
        // Arrange
        var log = new List<string>();
        var next = new SpyHandler("next", log);
        var rule = new RequiredFieldsRule();
        rule.SetNext(next);

        // Act
        var error = await rule.ValidateAsync(new Student { Name = name, Email = email });

        // Assert — failing link short-circuits: downstream never runs
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
        Assert.Empty(log);
    }

    [Theory]
    [InlineData("Ada", null)]
    [InlineData("Ada", "   ")]
    public async Task RequiredFieldsRule_ValidateAsync_MissingEmail_ReturnsEmailErrorAndShortCircuits(string name, string? email)
    {
        // Arrange
        var log = new List<string>();
        var next = new SpyHandler("next", log);
        var rule = new RequiredFieldsRule();
        rule.SetNext(next);

        // Act
        var error = await rule.ValidateAsync(new Student { Name = name, Email = email });

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Email", error);
        Assert.Empty(log);
    }

    [Fact]
    public async Task EmailFormatRule_ValidateAsync_MalformedEmail_ReturnsEmailFormatError()
    {
        // Arrange
        var rule = new EmailFormatRule();

        // Act
        var error = await rule.ValidateAsync(new Student { Name = "Ada", Email = "not-an-email" });

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Email format", error);
    }

    [Fact]
    public async Task EmailFormatRule_ValidateAsync_ValidEmail_ReturnsNoErrorAndForwards()
    {
        // Arrange
        var log = new List<string>();
        var next = new SpyHandler("next", log);
        var rule = new EmailFormatRule();
        rule.SetNext(next);

        // Act
        var error = await rule.ValidateAsync(ValidStudent());

        // Assert
        Assert.True(string.IsNullOrEmpty(error));
        Assert.Equal(new[] { "next" }, log);
    }

    [Fact]
    public async Task EmailFormatRule_ValidateAsync_BlankEmail_ReturnsNoErrorBecausePresenceIsNotItsConcern()
    {
        // Arrange
        var rule = new EmailFormatRule();

        // Act — RequiredFieldsRule owns the "missing email" verdict
        var error = await rule.ValidateAsync(new Student { Name = "Ada", Email = null });

        // Assert
        Assert.True(string.IsNullOrEmpty(error));
    }

    [Fact]
    public async Task DuplicateEmailRule_ValidateAsync_EmailTakenByOtherStudent_ReturnsDuplicateError()
    {
        // Arrange
        var repository = new Mock<IStudentRepository>();
        repository.Setup(r => r.GetAllAsyncReadOnly())
                  .ReturnsAsync(new List<Student> { new() { Id = 1, Name = "Other", Email = "ada@example.com" } });
        var rule = new DuplicateEmailRule(repository.Object);

        // Act — new student (Id 0) colliding with an existing row
        var error = await rule.ValidateAsync(new Student { Name = "Ada", Email = "ada@example.com" });

        // Assert
        Assert.NotNull(error);
        Assert.Contains("already", error);
    }

    [Fact]
    public async Task DuplicateEmailRule_ValidateAsync_UniqueEmail_ReturnsNoErrorAndForwards()
    {
        // Arrange
        var log = new List<string>();
        var next = new SpyHandler("next", log);
        var repository = new Mock<IStudentRepository>();
        repository.Setup(r => r.GetAllAsyncReadOnly())
                  .ReturnsAsync(new List<Student> { new() { Id = 1, Name = "Other", Email = "other@example.com" } });
        var rule = new DuplicateEmailRule(repository.Object);
        rule.SetNext(next);

        // Act
        var error = await rule.ValidateAsync(ValidStudent());

        // Assert
        Assert.True(string.IsNullOrEmpty(error));
        Assert.Equal(new[] { "next" }, log);
    }

    [Fact]
    public async Task DuplicateEmailRule_ValidateAsync_UpdatingSameStudent_ReturnsNoError()
    {
        // Arrange — a student keeping its own email is not a duplicate of itself
        var repository = new Mock<IStudentRepository>();
        repository.Setup(r => r.GetAllAsyncReadOnly())
                  .ReturnsAsync(new List<Student> { new() { Id = 1, Name = "Ada", Email = "ada@example.com" } });
        var rule = new DuplicateEmailRule(repository.Object);

        // Act
        var error = await rule.ValidateAsync(new Student { Id = 1, Name = "Ada", Email = "ada@example.com" });

        // Assert
        Assert.True(string.IsNullOrEmpty(error));
    }

    // ---------- chain through StudentService: ordering + short-circuit ----------

    [Fact]
    public async Task StudentService_SaveStudentAsync_ChainConfigured_ReturnsRulesInvokedInRegistrationOrder()
    {
        // Arrange — Required -> EmailFormat -> DuplicateEmail, wired in order
        var log = new List<string>();
        var chain = BuildChain(log, ("required", null), ("email-format", null), ("duplicate", null));
        var (service, repository) = CreateService(chain);

        // Act
        await service.SaveStudentAsync(ValidStudent());

        // Assert — RED until StudentService runs the chain head-first
        Assert.Equal(new[] { "required", "email-format", "duplicate" }, log);
        repository.Verify(r => r.AddAsync(It.IsAny<Student>()), Times.Once);
    }

    [Fact]
    public async Task StudentService_SaveStudentAsync_FirstRuleFails_ReturnsValidationExceptionAndNoRepositoryWrite()
    {
        // Arrange
        var log = new List<string>();
        var chain = BuildChain(log,
            ("required", "Name is required"),
            ("email-format", null),
            ("duplicate", null));
        var (service, repository) = CreateService(chain);

        // Act + Assert — validation happens BEFORE persistence, error surfaces as message
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => service.SaveStudentAsync(new Student { Name = null, Email = "ada@example.com" }));
        Assert.Contains("Name is required", exception.Message);
        Assert.Equal(new[] { "required" }, log); // short-circuit: downstream links never run
        repository.Verify(r => r.AddAsync(It.IsAny<Student>()), Times.Never);
        repository.Verify(r => r.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task StudentService_SaveStudentAsync_SecondRuleFails_ReturnsShortCircuitWithoutThirdRule()
    {
        // Arrange
        var log = new List<string>();
        var chain = BuildChain(log,
            ("required", null),
            ("email-format", "Invalid email format"),
            ("duplicate", null));
        var (service, _) = CreateService(chain);

        // Act + Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => service.SaveStudentAsync(new Student { Name = "Ada", Email = "nope" }));
        Assert.Contains("Invalid email format", exception.Message);
        Assert.Equal(new[] { "required", "email-format" }, log);
    }

    [Fact]
    public async Task StudentService_SaveStudentAsync_UpdateExistingWithInvalidEmail_ReturnsValidationExceptionBeforeUpdate()
    {
        // Arrange — update path (Id != 0) must run the same chain
        var log = new List<string>();
        var chain = BuildChain(log, ("required", "Invalid email format"));
        var (service, repository) = CreateService(chain);

        // Act + Assert
        await Assert.ThrowsAsync<ValidationException>(
            () => service.SaveStudentAsync(new Student { Id = 7, Name = "Ada", Email = "nope" }));
        repository.Verify(r => r.Update(It.IsAny<Student>()), Times.Never);
        repository.Verify(r => r.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task StudentService_SaveStudentAsync_WithRealRuleChainAndMissingName_ReturnsRequiredFieldsErrorFirst()
    {
        // Arrange — real RequiredFieldsRule in front of a recording link
        var log = new List<string>();
        var downstream = new SpyHandler("downstream", log);
        var chain = new RequiredFieldsRule();
        chain.SetNext(downstream);
        var (service, _) = CreateService(chain);

        // Act + Assert — RED via RequiredFieldsRule stub; GREEN proves rule text + short-circuit
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => service.SaveStudentAsync(new Student { Name = "  ", Email = "ada@example.com" }));
        Assert.Contains("Name", exception.Message);
        Assert.Empty(log);
    }

    [Fact]
    public async Task StudentService_SaveStudentAsync_NoChainInjected_ReturnsStudentSavedUnchanged()
    {
        // Arrange — legacy 4-arg construction (all existing tests + controllers).
        // NOTE: intentionally green during RED phase: pins "existing behavior unchanged".
        var (service, repository) = CreateService();

        // Act
        await service.SaveStudentAsync(ValidStudent());

        // Assert
        repository.Verify(r => r.AddAsync(It.IsAny<Student>()), Times.Once);
        repository.Verify(r => r.SaveAsync(), Times.Once);
    }
}
