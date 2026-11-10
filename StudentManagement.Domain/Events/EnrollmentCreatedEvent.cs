namespace StudentManagement.Domain.Events;

// Raised by EnrollmentService after a NEW enrollment row is saved.
// EnrollmentId may be 0 when the persistence layer has not assigned an identity yet.
public sealed record EnrollmentCreatedEvent(int EnrollmentId, int CourseId, int StudentId) : IDomainEvent;
