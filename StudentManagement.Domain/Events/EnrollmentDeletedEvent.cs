namespace StudentManagement.Domain.Events;

// Raised by EnrollmentService after an EXISTING enrollment row is deleted.
// Not raised when the row did not exist (nothing changed to observe).
public sealed record EnrollmentDeletedEvent(int EnrollmentId, int CourseId, int StudentId) : IDomainEvent;
