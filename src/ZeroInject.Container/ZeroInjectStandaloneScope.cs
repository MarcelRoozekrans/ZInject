using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectStandaloneScope : IServiceScope, IServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly ZeroInjectStandaloneProvider _root;
    private readonly object _trackLock = new object();
    private List<object>? _disposables;
    private int _disposed;
    private System.Collections.Generic.Dictionary<Type, object>? _openGenericScoped;

    protected virtual System.Collections.Generic.IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => null;

    protected ZeroInjectStandaloneScope(ZeroInjectStandaloneProvider root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    protected ZeroInjectStandaloneProvider Root => _root;

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (serviceType == typeof(IServiceScopeFactory))
        {
            return _root;
        }

        return ResolveScopedKnown(serviceType);
    }

    protected abstract object? ResolveScopedKnown(Type serviceType);

    protected T TrackDisposable<T>(T instance)
        where T : notnull
    {
        if (instance is IDisposable or IAsyncDisposable)
        {
            lock (_trackLock)
            {
                _disposables ??= [];
                _disposables.Add(instance);
            }
        }

        return instance;
    }

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

    protected object? ResolveOpenGenericScoped(Type serviceType)
    {
        if (OpenGenericMap == null || !serviceType.IsGenericType) return null;
        var openDef = serviceType.GetGenericTypeDefinition();
        if (!OpenGenericMap.TryGetValue(openDef, out var entry)) return null;

        var typeArgs = serviceType.GenericTypeArguments;
        var closedImpl = entry.ImplType.MakeGenericType(typeArgs);

        switch (entry.Lifetime)
        {
            case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton:
                return Root.ResolveOpenGenericRoot(serviceType);

            case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped:
                lock (_trackLock)
                {
                    _openGenericScoped ??= new System.Collections.Generic.Dictionary<Type, object>();
                    if (_openGenericScoped.TryGetValue(serviceType, out var existing)) return existing;
                    var inner = CreateInstance(closedImpl);
                    object instance;
                    if (entry.DecoratorImplType != null)
                    {
                        var closedDecorator = entry.DecoratorImplType.MakeGenericType(typeArgs);
                        instance = CreateInstance(closedDecorator, inner);
                    }
                    else
                    {
                        instance = inner;
                    }
                    _openGenericScoped[serviceType] = instance;
                    TrackDisposable(instance);
                    return instance;
                }

            default: // Transient
                var transientInner = CreateInstance(closedImpl);
                object transient;
                if (entry.DecoratorImplType != null)
                {
                    var closedDecorator = entry.DecoratorImplType.MakeGenericType(typeArgs);
                    transient = CreateInstance(closedDecorator, transientInner);
                }
                else
                {
                    transient = transientInner;
                }
                TrackDisposable(transient);
                return transient;
        }
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
            List<object>? snapshot;
            lock (_trackLock)
            {
                snapshot = _disposables;
                _disposables = null;
            }

            if (snapshot is not null)
            {
                for (var i = snapshot.Count - 1; i >= 0; i--)
                {
                    if (snapshot[i] is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            List<object>? snapshot;
            lock (_trackLock)
            {
                snapshot = _disposables;
                _disposables = null;
            }

            if (snapshot is not null)
            {
                for (var i = snapshot.Count - 1; i >= 0; i--)
                {
                    if (snapshot[i] is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else if (snapshot[i] is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }

        GC.SuppressFinalize(this);
    }
}
