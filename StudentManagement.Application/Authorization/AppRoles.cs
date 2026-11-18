namespace StudentManagement.Application.Authorization;

// Single source of truth for the Identity role names seeded by DbInitializer.
// Order matters: the layout badge shows the first role the caller holds.
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
    public const string User = "User";

    public static readonly IReadOnlyList<string> All = [Admin, Teacher, Student, User];

    // Staff = Admin or Teacher: the roles allowed to manage students, courses and
    // enrollments and to reach the full chat tool set. Everyone else (Student, User,
    // unknown or no roles) gets the course catalog only.
    public static bool IsStaff(IEnumerable<string>? roles) =>
        roles is not null && roles.Any(role => role == Admin || role == Teacher);
}
