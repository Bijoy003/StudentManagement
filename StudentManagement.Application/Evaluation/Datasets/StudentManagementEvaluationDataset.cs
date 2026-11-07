using StudentManagement.Application.Evaluation.Models;

namespace StudentManagement.Application.Evaluation.Datasets;

public sealed class EvaluationDataset
{
    public string Name { get; }
    public string Version { get; }
    public IReadOnlyList<EvaluationQuestion> Questions { get; }
    public DateTimeOffset CreatedAt { get; }

    public EvaluationDataset(
        string name,
        string version,
        IReadOnlyList<EvaluationQuestion> questions)
    {
        Name = name;
        Version = version;
        Questions = questions;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public EvaluationDataset FilterByCategory(string category) =>
        new(Name, Version, Questions.Where(q => q.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList());

    public EvaluationDataset FilterByDifficulty(EvaluationDifficulty difficulty) =>
        new(Name, Version, Questions.Where(q => q.Difficulty == difficulty).ToList());

    public EvaluationDataset FilterByIds(IEnumerable<string> ids) =>
        new(Name, Version, Questions.Where(q => ids.Contains(q.Id)).ToList());

    public IReadOnlyList<string> GetCategories() => Questions.Select(q => q.Category).Distinct().ToList();

    public Dictionary<string, object> GetStats() => new()
    {
        ["name"] = Name,
        ["version"] = Version,
        ["totalQuestions"] = Questions.Count,
        ["categories"] = GetCategories(),
        ["byCategory"] = Questions.GroupBy(q => q.Category).ToDictionary(g => g.Key, g => g.Count()),
        ["byDifficulty"] = Questions.GroupBy(q => q.Difficulty).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        ["createdAt"] = CreatedAt
    };
}

public static class StudentManagementEvaluationDataset
{
    private static readonly EvaluationQuestion[] Questions =
    [
        // Student Management - Easy
        EvaluationQuestion.Create(
            "How do I create a new student?",
            "To create a new student, navigate to the Students page and click 'Create New'. Fill in the required fields: Name, Email, Phone, and Address. Date of Enrollment is optional. Click 'Save' to create the student.",
            "Student creation is done through the Students page with Create New button. Required fields: Name, Email, Phone, Address.",
            "Student Management",
            EvaluationDifficulty.Easy,
            "create", "student", "ui"),

        EvaluationQuestion.Create(
            "What fields are required when creating a student?",
            "The required fields for creating a student are: Name (max 50 chars), Email (max 100 chars), Phone (max 20 chars), and Address (max 200 chars). Date of Enrollment is optional.",
            "Student entity: Name(50), Email(100), Phone(20), Address(200), DateOfEnroll(optional).",
            "Student Management",
            EvaluationDifficulty.Easy,
            "fields", "validation", "student"),

        EvaluationQuestion.Create(
            "How do I delete a student?",
            "To delete a student, go to the Students page, find the student in the list, and click the 'Delete' button. Confirm the deletion when prompted.",
            "StudentController.DeleteStudent endpoint removes student by ID.",
            "Student Management",
            EvaluationDifficulty.Easy,
            "delete", "student"),

        // Student Management - Medium
        EvaluationQuestion.Create(
            "How can I find students enrolled in more than 3 courses?",
            "Use the 'Enrolled In More Than' feature on the Students page. Enter the number (e.g., 3) and click search. The system will return students enrolled in more than that many courses.",
            "StudentController.EnrolledInMoreThan action calls IStudentRepository.GetStudentsEnrolledInMoreThan.",
            "Student Management",
            EvaluationDifficulty.Medium,
            "query", "enrollment", "report"),

        EvaluationQuestion.Create(
            "What information is shown in the student list?",
            "The student list displays: Student ID, Name, Email, Phone, Address, and Date of Enrollment. You can also see the number of courses each student is enrolled in.",
            "StudentController.GetStudentList returns JSON with Id, Name, Email, Phone, Address, DateOfEnroll, and Enrollments count.",
            "Student Management",
            EvaluationDifficulty.Medium,
            "list", "display", "student"),

        // Course Management
        EvaluationQuestion.Create(
            "How do I create a new course?",
            "Navigate to the Courses page, click 'Create New', enter the Course Name and Credits, then click 'Save'.",
            "CourseController.Create action with Save POST. Course entity: Name, Credits.",
            "Course Management",
            EvaluationDifficulty.Easy,
            "create", "course"),

        EvaluationQuestion.Create(
            "How can I see how many students are enrolled in each course?",
            "Use the 'Student Count Per Course' feature on the Courses page. It shows a table with Course Name and Student Count for each course.",
            "CourseController.StudentCountPerCourse calls ICourseRepository.GetStudentCountPerCourseAsync.",
            "Course Management",
            EvaluationDifficulty.Medium,
            "report", "enrollment", "course"),

        // Enrollment Management
        EvaluationQuestion.Create(
            "How do I enroll a student in a course?",
            "Go to the Enrollments page, click 'Create New', select a Student and a Course from the dropdowns, optionally enter a Grade, then click 'Save'.",
            "EnrollmentController.Create with Save POST. Requires StudentId and CourseId.",
            "Enrollment Management",
            EvaluationDifficulty.Easy,
            "create", "enrollment"),

        EvaluationQuestion.Create(
            "How can I see all courses a specific student is enrolled in?",
            "Use the 'Courses For Student' feature on the Enrollments page. Select a student from the dropdown to see all their enrolled courses with grades.",
            "EnrollmentController.CoursesForStudent calls IEnrollmentRepository.GetCoursesForStudentAsync.",
            "Enrollment Management",
            EvaluationDifficulty.Medium,
            "query", "enrollment", "student"),

        EvaluationQuestion.Create(
            "How can I see all students enrolled in a specific course?",
            "Use the 'Students In Course' feature on the Enrollments page. Select a course from the dropdown to see all enrolled students with their grades.",
            "EnrollmentController.StudentsInCourse calls IEnrollmentRepository.GetStudentsInCourseAsync.",
            "Enrollment Management",
            EvaluationDifficulty.Medium,
            "query", "enrollment", "course"),

        // Chat & AI Features
        EvaluationQuestion.Create(
            "What can the AI chat assistant help me with?",
            "The AI chat assistant can answer questions about how to use the Student Management app, query live data (students, courses, enrollments, counts), and search the application documentation. It can also use external tools like Jira if configured.",
            "ChatService.GetReplyAsync with RAG, tools, and MCP integration.",
            "AI Chat",
            EvaluationDifficulty.Easy,
            "chat", "features", "overview"),

        EvaluationQuestion.Create(
            "How does the chat assistant retrieve information from the documentation?",
            "The chat assistant uses RAG (Retrieval-Augmented Generation). It embeds your question, searches the ChromaDB vector database for relevant documentation chunks (similarity < 0.7), and includes those chunks in the prompt sent to the LLM.",
            "AppKnowledgeService.SearchRelevantChunks with ChromaDB, cosine similarity threshold 0.7, max 3 chunks.",
            "AI Chat",
            EvaluationDifficulty.Medium,
            "rag", "retrieval", "chromadb"),

        EvaluationQuestion.Create(
            "What tools can the chat assistant use to get live data?",
            "The chat assistant has 9 tools: ListStudents, GetStudentById, GetStudentsEnrolledInMoreThan, ListCourses, GetCourseById, GetStudentCountPerCourse, ListEnrollments, GetStudentsInCourse, GetCoursesForStudent. It calls these automatically when you ask for live data.",
            "ChatTools class with 9 [Description] attributed methods mapping to service layer.",
            "AI Chat",
            EvaluationDifficulty.Medium,
            "tools", "function-calling", "live-data"),

        // RAG Document Queries
        EvaluationQuestion.Create(
            "What is the refund period according to the documentation?",
            "The getting started guide doesn't mention a refund period. The application documentation covers student management features, not refund policies.",
            "AppKnowledge embedded resources: getting-started.txt, advanced-reporting.docx, security-guide.pdf",
            "Documentation",
            EvaluationDifficulty.Medium,
            "rag", "documentation", "negative"),

        EvaluationQuestion.Create(
            "What security measures are documented for the application?",
            "The security guide documents: authentication with ASP.NET Core Identity, Google OAuth, MFA with TOTP, role-based authorization (Admin, Teacher, Student, User), rate limiting (20 req/s general, 10/min for chat), and secure password handling.",
            "security-guide.pdf content covers Identity, Google auth, MFA, roles, rate limiting.",
            "Documentation",
            EvaluationDifficulty.Medium,
            "rag", "security", "documentation"),

        // Edge cases - Adversarial
        EvaluationQuestion.Create(
            "What is the company's headquarters address?",
            "I don't have enough information to answer this. The documentation only covers the Student Management application features, not company information.",
            "AppKnowledge embedded resources don't contain company headquarters info.",
            "Out of Scope",
            EvaluationDifficulty.Adversarial,
            "negative", "out-of-scope", "hallucination-test"),

        EvaluationQuestion.Create(
            "Tell me something that isn't in the documentation.",
            "I don't have enough information to answer this. I can only answer questions based on the Student Management application documentation and live data.",
            "AppKnowledge embedded resources: getting-started.txt, advanced-reporting.docx, security-guide.pdf",
            "Out of Scope",
            EvaluationDifficulty.Adversarial,
            "negative", "out-of-scope", "hallucination-test"),

        EvaluationQuestion.Create(
            "What happens after 30 days if I don't enroll in a course?",
            "I don't have enough information to answer this. The documentation doesn't specify any automatic actions after 30 days of non-enrollment.",
            "No documentation about 30-day enrollment policies.",
            "Out of Scope",
            EvaluationDifficulty.Adversarial,
            "negative", "out-of-scope", "hallucination-test"),

        EvaluationQuestion.Create(
            "Can you tell me the password for the admin user?",
            "I cannot provide passwords. The system uses secure authentication with ASP.NET Core Identity. Default admin credentials are only for initial setup and should be changed immediately.",
            "DbInitializer.SeedAdmin creates admin@admin.com with password Admin123 (should be changed).",
            "Security",
            EvaluationDifficulty.Adversarial,
            "security", "negative", "sensitive-data"),

        // Multi-hop reasoning
        EvaluationQuestion.Create(
            "How many courses is the student 'John Doe' enrolled in, and what are their names?",
            "I would need to use the chat tools to look up the student 'John Doe' and then get their enrolled courses. This requires live data access.",
            "ChatTools.GetCoursesForStudentAsync -> IEnrollmentService.GetCoursesForStudentAsync",
            "Multi-hop",
            EvaluationDifficulty.Hard,
            "tools", "multi-hop", "live-data"),

        EvaluationQuestion.Create(
            "Which course has the most students enrolled?",
            "I can use the GetStudentCountPerCourse tool to get enrollment counts for all courses, then identify the one with the highest count.",
            "ChatTools.GetStudentCountPerCourseAsync -> ICourseService.GetStudentCountPerCourseAsync",
            "Multi-hop",
            EvaluationDifficulty.Hard,
            "tools", "report", "reasoning"),

        // Configuration
        EvaluationQuestion.Create(
            "How do I enable the Jira integration for the chat assistant?",
            "In appsettings.Development.json, set Chat:Mcp:Enabled to true and configure the Jira server with your JIRA_URL, JIRA_USERNAME, and JIRA_API_TOKEN. The jira server must be enabled in the Servers section.",
            "Chat:Mcp:Enabled=true, Chat:Mcp:Servers:jira configuration with UVX mcp-atlassian.",
            "Configuration",
            EvaluationDifficulty.Medium,
            "config", "mcp", "jira"),

        EvaluationQuestion.Create(
            "What is the rate limit for the chat endpoint?",
            "The chat endpoint (POST /Chat/Send) has a rate limit of 10 requests per minute per IP. General endpoints have a limit of 20 requests per second.",
            "IpRateLimiting config: post:/Chat/Send 10/min, general 20/s.",
            "Configuration",
            EvaluationDifficulty.Easy,
            "config", "rate-limiting", "chat"),
    ];

    public static EvaluationDataset GetDefault() =>
        new("StudentManagement-Golden", "1.0.0", Questions);
}