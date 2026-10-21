using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Application.DTOs.Students;

// JSON Merge Patch (RFC 7386) payload: a property that is omitted (or null) is left unchanged.
public sealed class StudentPatchRequest
{
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 50 characters")]
    public string? Name { get; set; }

    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
    public string? Address { get; set; }

    public DateTime? DateOfEnroll { get; set; }
}
