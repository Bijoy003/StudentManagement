namespace StudentManagement.Domain.Entities;

// Grading behaviour mode on a Course. Letter is 0 so an uninitialized/stale row
// falls back to Letter semantics.
public enum GradeMode
{
    Letter = 0,
    PassFail = 1,
    Gpa = 2
}
