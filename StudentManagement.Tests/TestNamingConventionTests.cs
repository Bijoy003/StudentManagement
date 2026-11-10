using System.Reflection;
using System.Text.RegularExpressions;

namespace StudentManagement.Tests;

// WP AC14 — naming guard: every [Fact]/[Theory] declared by the new WP7/WP8 test
// classes follows {Method}_{Scenario}_Returns{Expected}. Inherently green at RED time
// (it guards the suite itself, not a production feature) — reported as such.
public class TestNamingConventionTests
{
    private static readonly Type[] NewPatternTestTypes =
    {
        typeof(CourseGradeModeTests),
        typeof(GradingStrategyTests),
        typeof(CourseApiContractTests),
        typeof(DomainEventTests),
        typeof(DocumentParserBaseTests),
        typeof(StudentValidationChainTests),
        typeof(CacheKeysTests),
        typeof(MailgunEmailServiceTests),
        typeof(AntiForgeryAttributeTests)
    };

    private static readonly Regex Convention = new(
        @"^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)+_Returns[A-Za-z0-9]+$",
        RegexOptions.Compiled);

    private static List<string> CollectNewTestFactNames()
    {
        var names = new List<string>();
        foreach (var type in NewPatternTestTypes)
        {
            foreach (var method in type.GetTypeInfo().DeclaredMethods)
            {
                if (method.IsPublic &&
                    (method.GetCustomAttribute<FactAttribute>() is not null ||
                     method.GetCustomAttribute<TheoryAttribute>() is not null))
                {
                    names.Add(method.Name);
                }
            }
        }

        return names;
    }

    public static TheoryData<string> NewTestFactNames
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var name in CollectNewTestFactNames())
                names.Add(name);
            return names;
        }
    }

    [Theory]
    [MemberData(nameof(NewTestFactNames))]
    public void NewPatternTest_MethodName_ReturnsConventionCompliant(string methodName)
    {
        // Arrange / Act / Assert
        Assert.Matches(Convention, methodName);
    }

    [Fact]
    public void NewPatternTest_ScannedClasses_ReturnsNotEmptyCoverage()
    {
        // Arrange / Act
        var names = CollectNewTestFactNames();

        // Assert — the guard itself must actually be watching tests
        Assert.True(names.Count >= 40, $"Expected the WP7/WP8 suite to contribute >= 40 facts, found {names.Count}");
    }
}
