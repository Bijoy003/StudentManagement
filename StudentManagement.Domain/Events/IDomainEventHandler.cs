namespace StudentManagement.Domain.Events;

// Handler contract for a specific domain event. Implementations are discovered by
// assembly scanning (see Application's AddDomainEvents) and invoked by
// IDomainEventDispatcher when a matching event is dispatched.
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
