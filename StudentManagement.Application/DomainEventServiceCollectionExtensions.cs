using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Events;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application;

// Assembly-scanning registration of the in-process domain event pipeline (no MediatR):
// IDomainEventDispatcher plus every concrete IDomainEventHandler<TEvent> implementation
// found in the given assemblies (all loaded assemblies when none are passed), each
// registered against every closed handler interface it implements.
public static class DomainEventServiceCollectionExtensions
{
    public static IServiceCollection AddDomainEvents(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        var targets = assemblies.Length > 0
            ? assemblies.Distinct().AsEnumerable()
            : AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in targets)
        {
            foreach (var handlerType in GetConcreteTypes(assembly))
            {
                foreach (var handlerInterface in handlerType
                             .GetInterfaces()
                             .Where(i => i.IsGenericType &&
                                         i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)))
                {
                    services.AddTransient(handlerInterface, sp => CreateHandlerOrNull(sp, handlerType)!);
                }
            }
        }

        return services;
    }

    // A handler whose dependencies the current host does not register (e.g. a trimmed
    // test container without IDistributedCache) is skipped with a warning instead of
    // failing the activation of every other handler for the same event.
    private static object? CreateHandlerOrNull(IServiceProvider serviceProvider, Type handlerType)
    {
        try
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, handlerType);
        }
        catch (InvalidOperationException)
        {
            (serviceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?
                .CreateLogger(typeof(DomainEventServiceCollectionExtensions))
                .LogWarning(
                    "Domain event handler {HandlerType} could not be activated (unregistered dependencies) and was skipped.",
                    handlerType.Name);
            return null;
        }
    }

    private static IEnumerable<Type> GetConcreteTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false });
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is { IsClass: true, IsAbstract: false })!;
        }
    }
}
