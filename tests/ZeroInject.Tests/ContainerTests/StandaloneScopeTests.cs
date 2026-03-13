namespace ZeroInject.Tests.ContainerTests;

public class StandaloneScopeTests
{
    private sealed class TestProvider : ZeroInject.Container.ZeroInjectStandaloneProvider
    {
        protected override object? ResolveKnown(Type serviceType) => null;
        protected override ZeroInject.Container.ZeroInjectStandaloneScope CreateScopeCore()
            => new TestScope(this);
    }

    private sealed class TestScope : ZeroInject.Container.ZeroInjectStandaloneScope
    {
        public TestScope(ZeroInject.Container.ZeroInjectStandaloneProvider root) : base(root) { }
        protected override object? ResolveScopedKnown(Type serviceType)
        {
            if (serviceType == typeof(string))
                return "scoped";
            return null;
        }
    }

    private sealed class DisposableService : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() { IsDisposed = true; }
    }

    private sealed class AsyncDisposableService : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }
        public ValueTask DisposeAsync() { IsDisposed = true; return default; }
    }

    [Fact]
    public void GetService_KnownType_ReturnsInstance()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Equal("scoped", scope.ServiceProvider.GetService(typeof(string)));
    }

    [Fact]
    public void GetService_UnknownType_ReturnsNull()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Null(scope.ServiceProvider.GetService(typeof(int)));
    }

    [Fact]
    public void GetService_IServiceProvider_ReturnsSelf()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Same(scope.ServiceProvider, scope.ServiceProvider.GetService(typeof(IServiceProvider)));
    }

    [Fact]
    public void GetService_IServiceScopeFactory_ReturnsRoot()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Same(provider, scope.ServiceProvider.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)));
    }

    [Fact]
    public void ServiceProvider_Property_ReturnsSelf()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Same(scope.ServiceProvider, scope.ServiceProvider);
    }

    [Fact]
    public void Dispose_DisposesTrackedServices()
    {
        using var provider = new TestProvider();
        var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        var svc = new DisposableService();
        // Access TrackDisposable via reflection since it's protected
        var trackMethod = typeof(ZeroInject.Container.ZeroInjectStandaloneScope)
            .GetMethod("TrackDisposable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(typeof(DisposableService));
        trackMethod.Invoke(scope.ServiceProvider, [svc]);

        Assert.False(svc.IsDisposed);
        scope.Dispose();
        Assert.True(svc.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTrackedAsyncServices()
    {
        using var provider = new TestProvider();
        var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        var svc = new AsyncDisposableService();
        var trackMethod = typeof(ZeroInject.Container.ZeroInjectStandaloneScope)
            .GetMethod("TrackDisposable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(typeof(AsyncDisposableService));
        trackMethod.Invoke(scope.ServiceProvider, [svc]);

        Assert.False(svc.IsDisposed);
        await ((IAsyncDisposable)scope).DisposeAsync();
        Assert.True(svc.IsDisposed);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        using var provider = new TestProvider();
        var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        scope.Dispose();
        scope.Dispose(); // Should not throw
    }

    // Open generic scope test helpers (reuse interfaces from StandaloneProviderBaseTests)
    private class TestOpenGenericScopeProvider : ZeroInject.Container.ZeroInjectStandaloneProvider
    {
        private static readonly Dictionary<Type, ZeroInject.Container.OpenGenericEntry> _map = new()
        {
            { typeof(StandaloneProviderBaseTests.IGenericService<>),   new ZeroInject.Container.OpenGenericEntry(typeof(StandaloneProviderBaseTests.GenericService<>),         Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient) },
            { typeof(StandaloneProviderBaseTests.ISingletonGeneric<>), new ZeroInject.Container.OpenGenericEntry(typeof(StandaloneProviderBaseTests.SingletonGenericService<>), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton) },
            { typeof(StandaloneProviderBaseTests.IScopedGeneric<>),    new ZeroInject.Container.OpenGenericEntry(typeof(StandaloneProviderBaseTests.ScopedGenericService<>),   Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped) },
        };
        protected override IReadOnlyDictionary<Type, ZeroInject.Container.OpenGenericEntry>? OpenGenericMap => _map;
        protected override object? ResolveKnown(Type serviceType) => ResolveOpenGenericRoot(serviceType);
        protected override ZeroInject.Container.ZeroInjectStandaloneScope CreateScopeCore() => new TestOpenGenericScope(this);

        private class TestOpenGenericScope : ZeroInject.Container.ZeroInjectStandaloneScope
        {
            private static readonly Dictionary<Type, ZeroInject.Container.OpenGenericEntry> _map = new()
            {
                { typeof(StandaloneProviderBaseTests.IGenericService<>),   new ZeroInject.Container.OpenGenericEntry(typeof(StandaloneProviderBaseTests.GenericService<>),         Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient) },
                { typeof(StandaloneProviderBaseTests.ISingletonGeneric<>), new ZeroInject.Container.OpenGenericEntry(typeof(StandaloneProviderBaseTests.SingletonGenericService<>), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton) },
                { typeof(StandaloneProviderBaseTests.IScopedGeneric<>),    new ZeroInject.Container.OpenGenericEntry(typeof(StandaloneProviderBaseTests.ScopedGenericService<>),   Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped) },
            };
            protected override IReadOnlyDictionary<Type, ZeroInject.Container.OpenGenericEntry>? OpenGenericMap => _map;
            public TestOpenGenericScope(ZeroInject.Container.ZeroInjectStandaloneProvider root) : base(root) { }
            protected override object? ResolveScopedKnown(Type serviceType) => ResolveOpenGenericScoped(serviceType);
        }
    }

    [Fact]
    public void ResolveOpenGenericScoped_Transient_ReturnsNewInstances()
    {
        var provider = new TestOpenGenericScopeProvider();
        using var scope = (ZeroInject.Container.ZeroInjectStandaloneScope)provider.CreateScope();
        var a = scope.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IGenericService<string>));
        var b = scope.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IGenericService<string>));
        Assert.NotNull(a);
        Assert.NotSame(a, b); // transient: new each time
    }

    [Fact]
    public void ResolveOpenGenericScoped_Scoped_ReturnsSameInstanceWithinScope()
    {
        var provider = new TestOpenGenericScopeProvider();
        using var scope = (ZeroInject.Container.ZeroInjectStandaloneScope)provider.CreateScope();
        var a = scope.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IScopedGeneric<int>));
        var b = scope.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IScopedGeneric<int>));
        Assert.Same(a, b);
    }

    [Fact]
    public void ResolveOpenGenericScoped_Singleton_DelegatesToRoot()
    {
        var provider = new TestOpenGenericScopeProvider();
        var fromRoot = provider.GetService(typeof(StandaloneProviderBaseTests.ISingletonGeneric<double>));
        using var scope = (ZeroInject.Container.ZeroInjectStandaloneScope)provider.CreateScope();
        var fromScope = scope.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.ISingletonGeneric<double>));
        Assert.Same(fromRoot, fromScope);
    }
}
