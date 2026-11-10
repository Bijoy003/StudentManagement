using Microsoft.Extensions.Caching.Distributed;
using StudentManagement.Domain.Events;

namespace StudentManagement.Application.EventHandlers;

// Observer for enrollment lifecycle events: evicts the distributed course-counts entry —
// the same eviction EnrollmentService performs manually (kept as belt-and-braces; both
// must stay equivalent).
public sealed class CourseStudentCountsCacheInvalidationHandler
    : IDomainEventHandler<EnrollmentCreatedEvent>,
      IDomainEventHandler<EnrollmentDeletedEvent>
{
    private readonly IDistributedCache _cache;

    public CourseStudentCountsCacheInvalidationHandler(IDistributedCache cache)
    {
        _cache = cache;
    }

    public Task HandleAsync(EnrollmentCreatedEvent domainEvent, CancellationToken cancellationToken = default)
        => _cache.RemoveAsync(CacheKeys.CourseStudentCounts, cancellationToken);

    public Task HandleAsync(EnrollmentDeletedEvent domainEvent, CancellationToken cancellationToken = default)
        => _cache.RemoveAsync(CacheKeys.CourseStudentCounts, cancellationToken);
}
