using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

// WP7 AC2 — Strategy pattern: IGradingStrategy + Letter/PassFail/Gpa strategies and
// EnrollmentService resolving the strategy from a course's GradeMode
// (unknown/default falls back to Letter; out-of-range scores are "Invalid").
public class GradingStrategyTests
{
    private static EnrollmentService CreateService(IEnumerable<IGradingStrategy>? strategies = null)
    {
        return new EnrollmentService(
            new Mock<IEnrollmentRepository>().Object,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<IDistributedCache>().Object,
            NullLogger<EnrollmentService>.Instance,
            domainEventDispatcher: null,
            gradingStrategies: strategies);
    }

    // ---------- strategy identity ----------

    [Fact]
    public void LetterGradeStrategy_Mode_ReturnsLetter()
    {
        // Arrange
        var strategy = new LetterGradeStrategy();

        // Act / Assert
        Assert.Equal(GradeMode.Letter, strategy.Mode);
    }

    [Fact]
    public void PassFailStrategy_Mode_ReturnsPassFail()
    {
        // Arrange
        var strategy = new PassFailStrategy();

        // Act / Assert
        Assert.Equal(GradeMode.PassFail, strategy.Mode);
    }

    [Fact]
    public void GpaStrategy_Mode_ReturnsGpa()
    {
        // Arrange
        var strategy = new GpaStrategy();

        // Act / Assert
        Assert.Equal(GradeMode.Gpa, strategy.Mode);
    }

    // ---------- Letter ----------

    [Theory]
    [InlineData(100.0, "A")]
    [InlineData(95.0, "A")]
    [InlineData(90.0, "A")]
    [InlineData(89.0, "B")]
    [InlineData(80.0, "B")]
    [InlineData(79.0, "C")]
    [InlineData(70.0, "C")]
    [InlineData(69.0, "D")]
    [InlineData(60.0, "D")]
    [InlineData(59.9, "F")]
    [InlineData(0.0, "F")]
    public void LetterGradeStrategy_ComputeGrade_ReturnsBandLetter(double score, string expected)
    {
        // Arrange
        var strategy = new LetterGradeStrategy();

        // Act / Assert
        Assert.Equal(expected, strategy.ComputeGrade((decimal)score));
    }

    [Fact]
    public void LetterGradeStrategy_ComputeGrade_NullScore_ReturnsEmptyString()
    {
        // Arrange
        var strategy = new LetterGradeStrategy();

        // Act / Assert
        Assert.Equal(string.Empty, strategy.ComputeGrade(null));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(101)]
    public void LetterGradeStrategy_ComputeGrade_OutOfRangeScore_ReturnsInvalid(double score)
    {
        // Arrange
        var strategy = new LetterGradeStrategy();

        // Act / Assert — explicit tested behavior: never clamp, never throw
        Assert.Equal("Invalid", strategy.ComputeGrade((decimal)score));
    }

    // ---------- Pass/Fail ----------

    [Theory]
    [InlineData(100.0, "Pass")]
    [InlineData(75.0, "Pass")]
    [InlineData(50.0, "Pass")]
    [InlineData(49.99, "Fail")]
    [InlineData(0.0, "Fail")]
    public void PassFailStrategy_ComputeGrade_ReturnsPassOrFail(double score, string expected)
    {
        // Arrange
        var strategy = new PassFailStrategy();

        // Act / Assert
        Assert.Equal(expected, strategy.ComputeGrade((decimal)score));
    }

    [Fact]
    public void PassFailStrategy_ComputeGrade_NullScore_ReturnsEmptyString()
    {
        // Arrange
        var strategy = new PassFailStrategy();

        // Act / Assert
        Assert.Equal(string.Empty, strategy.ComputeGrade(null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void PassFailStrategy_ComputeGrade_OutOfRangeScore_ReturnsInvalid(double score)
    {
        // Arrange
        var strategy = new PassFailStrategy();

        // Act / Assert
        Assert.Equal("Invalid", strategy.ComputeGrade((decimal)score));
    }

    // ---------- GPA ----------

    [Theory]
    [InlineData(95.0, "4.0")]
    [InlineData(90.0, "4.0")]
    [InlineData(85.0, "3.0")]
    [InlineData(75.0, "2.0")]
    [InlineData(65.0, "1.0")]
    [InlineData(55.0, "0.0")]
    [InlineData(0.0, "0.0")]
    public void GpaStrategy_ComputeGrade_ReturnsGpaBand(double score, string expected)
    {
        // Arrange
        var strategy = new GpaStrategy();

        // Act / Assert
        Assert.Equal(expected, strategy.ComputeGrade((decimal)score));
    }

    [Fact]
    public void GpaStrategy_ComputeGrade_NullScore_ReturnsEmptyString()
    {
        // Arrange
        var strategy = new GpaStrategy();

        // Act / Assert
        Assert.Equal(string.Empty, strategy.ComputeGrade(null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void GpaStrategy_ComputeGrade_OutOfRangeScore_ReturnsInvalid(double score)
    {
        // Arrange
        var strategy = new GpaStrategy();

        // Act / Assert
        Assert.Equal("Invalid", strategy.ComputeGrade((decimal)score));
    }

    // ---------- EnrollmentService strategy resolution ----------

    private static IEnumerable<IGradingStrategy> AllStrategies() => new IGradingStrategy[]
    {
        new LetterGradeStrategy(),
        new PassFailStrategy(),
        new GpaStrategy()
    };

    [Fact]
    public void EnrollmentService_GetGradingStrategy_LetterMode_ReturnsLetterStrategy()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act
        var strategy = service.GetGradingStrategy(GradeMode.Letter);

        // Assert
        Assert.IsType<LetterGradeStrategy>(strategy);
    }

    [Fact]
    public void EnrollmentService_GetGradingStrategy_PassFailMode_ReturnsPassFailStrategy()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act
        var strategy = service.GetGradingStrategy(GradeMode.PassFail);

        // Assert
        Assert.IsType<PassFailStrategy>(strategy);
    }

    [Fact]
    public void EnrollmentService_GetGradingStrategy_GpaMode_ReturnsGpaStrategy()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act
        var strategy = service.GetGradingStrategy(GradeMode.Gpa);

        // Assert
        Assert.IsType<GpaStrategy>(strategy);
    }

    [Fact]
    public void EnrollmentService_GetGradingStrategy_UnknownMode_ReturnsLetterStrategyFallback()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act
        var strategy = service.GetGradingStrategy((GradeMode)42);

        // Assert — unknown mode falls back to Letter
        Assert.IsType<LetterGradeStrategy>(strategy);
    }

    [Fact]
    public void EnrollmentService_GetGradingStrategy_NoStrategiesInjected_ReturnsLetterStrategyFallback()
    {
        // Arrange — legacy 4-arg construction
        var service = CreateService();

        // Act
        var strategy = service.GetGradingStrategy(GradeMode.Gpa);

        // Assert — nothing registered => safe Letter fallback, never null/throw
        Assert.IsType<LetterGradeStrategy>(strategy);
    }

    // ---------- EnrollmentService display composition ----------

    [Fact]
    public void EnrollmentService_ComputeGradeDisplay_LetterMode95_ReturnsA()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act / Assert
        Assert.Equal("A", service.ComputeGradeDisplay(95m, GradeMode.Letter));
    }

    [Fact]
    public void EnrollmentService_ComputeGradeDisplay_PassFailMode85_ReturnsPass()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act / Assert
        Assert.Equal("Pass", service.ComputeGradeDisplay(85m, GradeMode.PassFail));
    }

    [Fact]
    public void EnrollmentService_ComputeGradeDisplay_GpaMode85_ReturnsThreePointZero()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act / Assert
        Assert.Equal("3.0", service.ComputeGradeDisplay(85m, GradeMode.Gpa));
    }

    [Fact]
    public void EnrollmentService_ComputeGradeDisplay_OutOfRangeScore_ReturnsInvalid()
    {
        // Arrange
        var service = CreateService(AllStrategies());

        // Act / Assert — out-of-range surfaces the shared "Invalid" verdict
        Assert.Equal("Invalid", service.ComputeGradeDisplay(101m, GradeMode.PassFail));
    }
}
