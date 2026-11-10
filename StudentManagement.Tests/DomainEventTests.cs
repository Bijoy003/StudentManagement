using System.Reflection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application;
using StudentManagement.Application.EventHandlers;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Events;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

// WP7 AC5 — Observer: in-process domain events (IDomainEvent / IDomainEventHandler<T>
// / dispatcher registered by assembly scanning, NO MediatR). EnrollmentService raises
// EnrollmentCreated/EnrollmentDeleted; a handler invalidates the course-counts cache key
// exactly like today's manual invalidation (which the existing EnrollmentServiceTests
// pin and which must remain valid).
public class DomainEventTests
{
    private static Assembly TestAssembly => typeof(DomainEventTests).Assembly;
    private static Assembly ApplicationAssembly => typeof(DomainEventDispatcher).Assembly;

    // Static queues: assembly scanning may register handlers as transients, so the
    // dispatcher can invoke a DIFFERENT instance than one resolved via GetServices.
    // Tests in a class never run in parallel (xUnit) and each test clears before acting.
    private sealed class RecordingCreatedHandler : IDomainEventHandler<EnrollmentCreatedEvent>
    {
        public static readonly System.Collections.Concurrent.ConcurrentQueue<EnrollmentCreatedEvent> Handled = new();

        public Task HandleAsync(EnrollmentCreatedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Handled.Enqueue(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDeletedHandler : IDomainEventHandler<EnrollmentDeletedEvent>
    {
        public static readonly System.Collections.Concurrent.ConcurrentQueue<EnrollmentDeletedEvent> Handled = new();

        public Task HandleAsync(EnrollmentDeletedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Handled.Enqueue(domainEvent);
            return Task.CompletedTask;
        }
    }

    private static IServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        services.AddDomainEvents(TestAssembly, ApplicationAssembly);
        return services.BuildServiceProvider();
    }

    private static EnrollmentService CreateService(
        Mock<IEnrollmentRepository> repository,
        Mock<IDomainEventDispatcher>? dispatcher = null,
        Mock<IDistributedCache>? distributedCache = null)
    {
        return new EnrollmentService(
            repository.Object,
            new MemoryCache(new MemoryCacheOptions()),
            (distributedCache ?? new Mock<IDistributedCache>()).Object,
            NullLogger<EnrollmentService>.Instance,
            dispatcher?.Object);
    }

    // ---------- registration + dispatch ----------

    [Fact]
    public void AddDomainEvents_ScannedAssemblies_ReturnsResolvableDispatcher()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDomainEvents(TestAssembly, ApplicationAssembly);
        var dispatcher = services.BuildServiceProvider().GetService<IDomainEventDispatcher>();

        // Assert
        Assert.NotNull(dispatcher);
    }

    [Fact]
    public async Task DomainEventDispatcher_EnrollmentCreatedEvent_ReturnsMatchingHandlerInvoked()
    {
        // Arrange — recording handlers live in this assembly and are picked up by the scan
        RecordingCreatedHandler.Handled.Clear();
        var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        var @event = new EnrollmentCreatedEvent(11, 3, 9);

        // Act
        await dispatcher.DispatchAsync(@event);

        // Assert — RED until the dispatcher invokes scan-registered handlers
        var handled = RecordingCreatedHandler.Handled.ToArray();
        var single = Assert.Single(handled);
        Assert.Equal(@event, single);
    }

    [Fact]
    public async Task DomainEventDispatcher_EnrollmentDeletedEvent_ReturnsCreatedHandlerNotInvoked()
    {
        // Arrange
        RecordingCreatedHandler.Handled.Clear();
        RecordingDeletedHandler.Handled.Clear();
        var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();

        // Act
        await dispatcher.DispatchAsync(new EnrollmentDeletedEvent(11, 3, 9));

        // Assert — only handlers for the runtime event type run
        Assert.Empty(RecordingCreatedHandler.Handled);

        var handled = RecordingDeletedHandler.Handled.ToArray();
        var single = Assert.Single(handled);
        Assert.Equal(11, single.EnrollmentId);
        Assert.Equal(3, single.CourseId);
        Assert.Equal(9, single.StudentId);
    }

    // ---------- EnrollmentService raises events ----------

    [Fact]
    public async Task EnrollmentService_SaveEnrollment_NewEnrollment_ReturnsEnrollmentCreatedEventDispatched()
    {
        // Arrange
        var dispatched = new List<IDomainEvent>();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        dispatcher
            .Setup(d => d.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IDomainEvent, CancellationToken>((e, _) => dispatched.Add(e))
            .Returns(Task.CompletedTask);

        var repository = new Mock<IEnrollmentRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = CreateService(repository, dispatcher);

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 0, CourseId = 3, StudentId = 9 });

        // Assert — RED until SaveEnrollment publishes the created event
        var created = Assert.Single(dispatched.OfType<EnrollmentCreatedEvent>());
        Assert.Equal(3, created.CourseId);
        Assert.Equal(9, created.StudentId);
    }

    [Fact]
    public async Task EnrollmentService_DeleteEnrollment_ExistingEnrollment_ReturnsEnrollmentDeletedEventDispatched()
    {
        // Arrange
        var dispatched = new List<IDomainEvent>();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        dispatcher
            .Setup(d => d.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IDomainEvent, CancellationToken>((e, _) => dispatched.Add(e))
            .Returns(Task.CompletedTask);

        var repository = new Mock<IEnrollmentRepository>();
        repository.Setup(r => r.GetByIdAsync(5))
                  .ReturnsAsync(new Enrollment { Id = 5, CourseId = 3, StudentId = 9 });
        repository.Setup(r => r.DeleteAsync(5)).Returns(Task.CompletedTask);
        repository.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = CreateService(repository, dispatcher);

        // Act
        await service.DeleteEnrollment(5);

        // Assert — RED until DeleteEnrollment publishes the deleted event
        var deleted = Assert.Single(dispatched.OfType<EnrollmentDeletedEvent>());
        Assert.Equal(5, deleted.EnrollmentId);
        Assert.Equal(3, deleted.CourseId);
        Assert.Equal(9, deleted.StudentId);
    }

    [Fact]
    public async Task EnrollmentService_SaveEnrollment_ExistingEnrollment_ReturnsNoEnrollmentCreatedEventDispatched()
    {
        // Arrange — update (Id != 0) is not a creation; guard against over-publishing.
        // NOTE: intentionally green during RED phase (nothing publishes yet).
        var dispatcher = new Mock<IDomainEventDispatcher>();
        var repository = new Mock<IEnrollmentRepository>();
        repository.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = CreateService(repository, dispatcher);

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 5, CourseId = 3, StudentId = 9 });

        // Assert
        dispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<EnrollmentCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------- handler invalidates course counts cache ----------

    private static void WireCacheRemoval(Mock<IDistributedCache> cache)
    {
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task CourseStudentCountsCacheInvalidationHandler_EnrollmentCreatedEvent_ReturnsCourseCountsKeyEvicted()
    {
        // Arrange
        var cache = new Mock<IDistributedCache>();
        WireCacheRemoval(cache);
        var handler = new CourseStudentCountsCacheInvalidationHandler(cache.Object);

        // Act
        await handler.HandleAsync(new EnrollmentCreatedEvent(1, 3, 9));

        // Assert — same key the service invalidates manually today
        cache.Verify(c => c.RemoveAsync(CacheKeys.CourseStudentCounts, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CourseStudentCountsCacheInvalidationHandler_EnrollmentDeletedEvent_ReturnsCourseCountsKeyEvicted()
    {
        // Arrange
        var cache = new Mock<IDistributedCache>();
        WireCacheRemoval(cache);
        var handler = new CourseStudentCountsCacheInvalidationHandler(cache.Object);

        // Act
        await handler.HandleAsync(new EnrollmentDeletedEvent(1, 3, 9));

        // Assert
        cache.Verify(c => c.RemoveAsync(CacheKeys.CourseStudentCounts, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddDomainEvents_DispatchEnrollmentCreated_ReturnsCourseCountsCacheEvictedEndToEnd()
    {
        // Arrange — production wiring: scan registers the real invalidation handler
        var cache = new Mock<IDistributedCache>();
        WireCacheRemoval(cache);
        var provider = BuildProvider(services => services.AddSingleton<IDistributedCache>(cache.Object));
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();

        // Act
        await dispatcher.DispatchAsync(new EnrollmentCreatedEvent(1, 3, 9));

        // Assert — RED until scanning + dispatch + handler all work
        cache.Verify(c => c.RemoveAsync(CacheKeys.CourseStudentCounts, It.IsAny<CancellationToken>()), Times.Once);
    }
}
