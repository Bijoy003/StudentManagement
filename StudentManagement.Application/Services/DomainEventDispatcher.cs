using StudentManagement.Domain.Events;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Services;

// In-process dispatcher: resolves every IDomainEventHandler<TEvent> registered for the
// RUNTIME type of the event (closed-generic IEnumerable from the container, populated by
// AddDomainEvents assembly scanning) and invokes HandleAsync on each sequentially.
// Event types without handlers dispatch to nothing.
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public DomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var handlerInterface = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        if (_serviceProvider.GetService(typeof(IEnumerable<>).MakeGenericType(handlerInterface))
            is not System.Collections.IEnumerable handlers)
        {
            return;
        }

        var handleMethod = handlerInterface.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            await (Task)handleMethod.Invoke(handler, new object[] { domainEvent, cancellationToken })!;
        }
    }
}
