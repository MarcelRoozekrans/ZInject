using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectStandaloneProvider : IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    private int _disposed;
    private System.Collections.Concurrent.ConcurrentDictionary<Type, object>? _openGenericSingletons;

    protected virtual System.Collections.Generic.IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => null;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (serviceType == typeof(IServiceScopeFactory))
        {
            return this;
        }

        return ResolveKnown(serviceType);
    }

    protected abstract object? ResolveKnown(Type serviceType);

    public IServiceScope CreateScope()
    {
        return CreateScopeCore();
    }

    protected abstract ZeroInjectStandaloneScope CreateScopeCore();

    internal object CreateInstance(Type type, object? innerArg = null)
    {
        var ctor = type.GetConstructors()[0];
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (innerArg != null && parameters[i].ParameterType.IsAssignableFrom(innerArg.GetType()))
                args[i] = innerArg;
            else
                args[i] = GetService(parameters[i].ParameterType);
        }
        return ctor.Invoke(args);
    }

    internal protected object? ResolveOpenGenericRoot(Type serviceType)
    {
        if (OpenGenericMap == null || !serviceType.IsGenericType) return null;
        var openDef = serviceType.GetGenericTypeDefinition();
        if (!OpenGenericMap.TryGetValue(openDef, out var entry)) return null;
        if (entry.Lifetime == Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped) return null; // scoped only from scope

        var typeArgs = serviceType.GenericTypeArguments;
        var closedImpl = entry.ImplType.MakeGenericType(typeArgs);

        object BuildInstance()
        {
            var inner = CreateInstance(closedImpl);
            if (entry.DecoratorImplType != null)
            {
                var closedDecorator = entry.DecoratorImplType.MakeGenericType(typeArgs);
                return CreateInstance(closedDecorator, inner);
            }
            return inner;
        }

        if (entry.Lifetime == Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)
        {
            _openGenericSingletons ??= new System.Collections.Concurrent.ConcurrentDictionary<Type, object>();
            return _openGenericSingletons.GetOrAdd(serviceType, _ => BuildInstance());
        }

        return BuildInstance(); // Transient
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // No resources to dispose in standalone base — subclasses override
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // No fallback to dispose — just mark as disposed
        }

        GC.SuppressFinalize(this);
        return default;
    }
}
