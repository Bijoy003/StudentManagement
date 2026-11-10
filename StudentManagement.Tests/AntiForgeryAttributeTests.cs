using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Web.Controllers;

namespace StudentManagement.Tests;

// WP8 AC10 — [ValidateAntiForgeryToken] re-enabled on StudentController.SaveStudent
// (the commented-out attribute is the known gap). Reflection-style contract test; the
// JS side (sending the token) is e2e-covered. CourseController.Save already has no token
// either but is out of this work package's scope.
public class AntiForgeryAttributeTests
{
    private static MethodInfo? SaveStudentAction =>
        typeof(StudentController).GetMethod("SaveStudent");

    [Fact]
    public void SaveStudent_WhenInspected_ReturnsValidateAntiForgeryTokenAttribute()
    {
        // Arrange
        var action = SaveStudentAction;
        Assert.NotNull(action);

        // Act
        var attribute = action!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();

        // Assert — RED today: the attribute is commented out in the controller
        Assert.NotNull(attribute);
    }

    [Fact]
    public void SaveStudent_WhenInspected_ReturnsHttpPostActionWithBodyBindingIntact()
    {
        // Arrange
        var action = SaveStudentAction;
        Assert.NotNull(action);

        // Act / Assert — re-adding antiforgery must not change the HTTP verb or binding
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        var parameter = action.GetParameters().Single();
        Assert.Same(typeof(Domain.Entities.Student).Name, parameter.ParameterType.Name);
        Assert.NotNull(parameter.GetCustomAttribute<FromBodyAttribute>());
    }

    [Fact]
    public void AccountController_PostActionsWithAntiForgeryToken_ReturnsCoverageUnchanged()
    {
        // Arrange — baseline of the flows that already carry a token today
        var expectedProtected = new[]
        {
            "ForgotPassword", "ResetPassword", "Profile", "Enable2FA",
            "ConfirmAuthenticator", "Disable2FA", "ChangePassword"
        };

        // Act
        var protectedPosts = typeof(AccountController)
            .GetTypeInfo()
            .DeclaredMethods
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() is not null
                        && m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();

        // Assert — regression guard: WP8 changes must not strip existing protection
        foreach (var name in expectedProtected)
        {
            Assert.Contains(name, protectedPosts);
        }
    }
}
