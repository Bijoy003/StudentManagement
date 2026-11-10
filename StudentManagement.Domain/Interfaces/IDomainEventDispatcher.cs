using StudentManagement.Domain.Events;

namespace StudentManagement.Domain.Interfaces;

// Resolves and invokes every registered IDomainEventHandler<TEvent> for the runtime
// type of the event. Implementation lives in Application (Domain stays dependency-free).
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
