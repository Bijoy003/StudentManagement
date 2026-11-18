namespace StudentManagement.Application.Authorization;

// Policy names registered in Program.cs (AddAuthorization). Controllers reference
// these constants instead of magic strings so the gate and its registration cannot drift.
public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string TeacherOnly = "TeacherOnly";
    public const string Staff = "Staff";
    public const string StudentSelf = "StudentSelf";
}
