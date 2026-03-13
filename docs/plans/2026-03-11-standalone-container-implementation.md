# Standalone Container Mode Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a standalone container mode that eliminates MS DI fallback overhead for scenarios where all services are ZeroInject-attributed.

**Architecture:** Two new base classes (`ZeroInjectStandaloneProvider`, `ZeroInjectStandaloneScope`) in `ZeroInject.Container`, plus generator changes to emit a second standalone provider class alongside the existing hybrid one. The standalone provider has no fallback — unknown types return null, scope creation is direct.

**Tech Stack:** C# source generator (Roslyn `IIncrementalGenerator`, `netstandard2.0`), runtime classes targeting `net8.0;net10.0`, xUnit tests, BenchmarkDotNet.

---

### Task 1: Create `ZeroInjectStandaloneProvider` base class

**Files:**
- Create: `src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs`
- Test: `tests/ZeroInject.Tests/ContainerTests/StandaloneProviderBaseTests.cs`

**Step 1: Write the failing tests**

Create `tests/ZeroInject.Tests/ContainerTests/StandaloneProviderBaseTests.cs`:

```csharp
namespace ZeroInject.Tests.ContainerTests;

public class StandaloneProviderBaseTests
{
    // Minimal concrete subclass for testing the abstract base
    private sealed class TestProvider : ZeroInject.Container.ZeroInjectStandaloneProvider
    {
        protected override object? ResolveKnown(Type serviceType)
        {
            if (serviceType == typeof(string))
                return "hello";
            return null;
        }

        protected override ZeroInject.Container.ZeroInjectStandaloneScope CreateScopeCore()
        {
            return new TestScope(this);
        }
    }

    private sealed class TestScope : ZeroInject.Container.ZeroInjectStandaloneScope
    {
        public TestScope(ZeroInject.Container.ZeroInjectStandaloneProvider root) : base(root) { }

        protected override object? ResolveScopedKnown(Type serviceType)
        {
            if (serviceType == typeof(string))
                return "scoped-hello";
            return null;
        }
    }

    [Fact]
    public void GetService_KnownType_ReturnsInstance()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(string));
        Assert.Equal("hello", result);
    }

    [Fact]
    public void GetService_UnknownType_ReturnsNull()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(int));
        Assert.Null(result);
    }

    [Fact]
    public void GetService_IServiceProvider_ReturnsSelf()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(IServiceProvider));
        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_IServiceScopeFactory_ReturnsSelf()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory));
        Assert.Same(provider, result);
    }

    [Fact]
    public void CreateScope_ReturnsWorkingScope()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        var result = scope.ServiceProvider.GetService(typeof(string));
        Assert.Equal("scoped-hello", result);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var provider = new TestProvider();
        provider.Dispose();
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var provider = new TestProvider();
        await provider.DisposeAsync();
        await provider.DisposeAsync(); // Should not throw
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~StandaloneProviderBaseTests" --no-build`
Expected: FAIL — `ZeroInjectStandaloneProvider` does not exist yet.

**Step 3: Write minimal implementation**

Create `src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectStandaloneProvider : IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    private int _disposed;

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

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Exchange(ref _disposed, 1);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // No fallback to dispose — just mark as disposed
        }

        GC.SuppressFinalize(this);
        return default;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~StandaloneProviderBaseTests"`
Expected: PASS (7/7)

**Step 5: Commit**

```bash
git add src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs tests/ZeroInject.Tests/ContainerTests/StandaloneProviderBaseTests.cs
git commit -m "feat: add ZeroInjectStandaloneProvider base class"
```

---

### Task 2: Create `ZeroInjectStandaloneScope` base class

**Files:**
- Create: `src/ZeroInject.Container/ZeroInjectStandaloneScope.cs`
- Test: `tests/ZeroInject.Tests/ContainerTests/StandaloneScopeTests.cs`

**Step 1: Write the failing tests**

Create `tests/ZeroInject.Tests/ContainerTests/StandaloneScopeTests.cs`:

```csharp
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
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~StandaloneScopeTests" --no-build`
Expected: FAIL — `ZeroInjectStandaloneScope` does not exist yet.

**Step 3: Write minimal implementation**

Create `src/ZeroInject.Container/ZeroInjectStandaloneScope.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectStandaloneScope : IServiceScope, IServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly ZeroInjectStandaloneProvider _root;
    private readonly object _trackLock = new object();
    private List<object>? _disposables;
    private int _disposed;

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
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~StandaloneScopeTests"`
Expected: PASS (8/8)

**Step 5: Commit**

```bash
git add src/ZeroInject.Container/ZeroInjectStandaloneScope.cs tests/ZeroInject.Tests/ContainerTests/StandaloneScopeTests.cs
git commit -m "feat: add ZeroInjectStandaloneScope base class"
```

---

### Task 3: Generator — emit standalone provider class

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Test: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

This is the largest task. The generator currently calls `GenerateServiceProviderClass()` once. We need it to also emit a standalone variant.

**Step 1: Write the failing generator tests**

Add to `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`:

```csharp
// --- Standalone provider generation ---

[Fact]
public void WhenContainerReferenced_GeneratesStandaloneProvider()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("TestAssemblyStandaloneServiceProvider", output);
    Assert.Contains("ZeroInjectStandaloneProvider", output);
}

[Fact]
public void Standalone_HasParameterlessConstructor()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
    Assert.Contains("public TestAssemblyStandaloneServiceProvider() { }", standaloneSection);
}

[Fact]
public void Standalone_ScopeInheritsFromStandaloneScope()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
    Assert.Contains("ZeroInjectStandaloneScope", standaloneSection);
}

[Fact]
public void Standalone_CreateScopeCore_NoFallbackParameter()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
    Assert.Contains("protected override global::ZeroInject.Container.ZeroInjectStandaloneScope CreateScopeCore()", standaloneSection);
    Assert.DoesNotContain("IServiceScope fallbackScope", standaloneSection);
}

[Fact]
public void Standalone_ResolveKnown_SameAsHybrid()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        [Singleton]
        public class Bar { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
    // Same transient and singleton resolution
    Assert.Contains("return new global::TestApp.Foo();", standaloneSection);
    Assert.Contains("Interlocked.CompareExchange", standaloneSection);
}

[Fact]
public void Standalone_ScopeConstructor_NoFallbackScope()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IRepo { }
        [Scoped]
        public class Repo : IRepo { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
    // Scope constructor takes only root, no fallbackScope
    Assert.Contains("public Scope(TestAssemblyStandaloneServiceProvider root) : base(root) { }", standaloneSection);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~ContainerGeneratorTests.Standalone" --no-build`
Expected: FAIL — standalone provider not emitted yet.

**Step 3: Implement generator changes**

Modify `src/ZeroInject.Generator/ZeroInjectGenerator.cs`:

1. In the `RegisterSourceOutput` callback (around line 100), after the existing `GenerateServiceProviderClass` call, add a call to a new `GenerateStandaloneServiceProviderClass` method:

```csharp
// After existing: var providerCode = GenerateServiceProviderClass(allServices, asmName);
var standaloneCode = GenerateStandaloneServiceProviderClass(allServices, asmName);
spc.AddSource(asmName + ".StandaloneServiceProvider.g.cs", standaloneCode);
```

2. Create a new method `GenerateStandaloneServiceProviderClass` that mirrors `GenerateServiceProviderClass` but:
   - Class name: `cleanName + "StandaloneServiceProvider"`
   - Base class: `global::ZeroInject.Container.ZeroInjectStandaloneProvider`
   - Constructor: parameterless (`public ClassName() { }`)
   - `CreateScopeCore`: no `IServiceScope fallbackScope` parameter, returns `new Scope(this)`
   - Nested `Scope` class inherits from `global::ZeroInject.Container.ZeroInjectStandaloneScope`
   - Scope constructor: `public Scope(ClassName root) : base(root) { }`
   - All `ResolveKnown` and `ResolveScopedKnown` bodies are identical to the hybrid version
   - No `BuildZeroInjectServiceProvider` extension or `ServiceProviderFactory` for standalone (user instantiates directly)

The key differences in the emitted code:
- Line `sb.AppendLine("        public " + className + "(IServiceProvider fallback) : base(fallback) { }");`
  becomes `sb.AppendLine("        public " + className + "() { }");`
- Line `sb.AppendLine("        protected override global::ZeroInject.Container.ZeroInjectScope CreateScopeCore(IServiceScope fallbackScope)");`
  becomes `sb.AppendLine("        protected override global::ZeroInject.Container.ZeroInjectStandaloneScope CreateScopeCore()");`
- Scope base: `global::ZeroInject.Container.ZeroInjectStandaloneScope` instead of `global::ZeroInject.Container.ZeroInjectScope`
- Scope constructor: `public Scope(ClassName root) : base(root) { }` instead of `public Scope(ClassName root, IServiceScope fallbackScope) : base(root, fallbackScope) { }`

**Implementation approach:** Extract the shared body-generation logic into a helper, or duplicate `GenerateServiceProviderClass` and modify the 4-5 differing lines. Given the generator targets `netstandard2.0` (no modern C#), duplication with clear naming is simpler and avoids accidental regressions in the existing hybrid path.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~ContainerGeneratorTests"`
Expected: ALL PASS (existing + 6 new standalone tests)

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs
git commit -m "feat: generator emits standalone provider class alongside hybrid"
```

---

### Task 4: Integration tests for standalone container

**Files:**
- Modify: `tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs`

**Step 1: Write the failing integration tests**

Add a `BuildAndCreateStandaloneProvider` helper and standalone tests to `IntegrationTests.cs`:

```csharp
/// <summary>
/// Builds and creates a standalone provider (no fallback).
/// Finds the generated *StandaloneServiceProvider class and instantiates it.
/// </summary>
private static (Assembly assembly, IServiceProvider provider) BuildAndCreateStandaloneProvider(string source)
{
    var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);

    var genErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    if (genErrors.Count > 0)
    {
        throw new InvalidOperationException(
            "Generator produced errors:\n" + string.Join("\n", genErrors));
    }

    using var ms = new MemoryStream();
    var emitResult = outputCompilation.Emit(ms);
    if (!emitResult.Success)
    {
        var errors = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());
        throw new InvalidOperationException(
            "Compilation failed:\n" + string.Join("\n", errors));
    }

    ms.Seek(0, SeekOrigin.Begin);
    var loadContext = new AssemblyLoadContext(null, isCollectible: true);
    var assembly = loadContext.LoadFromStream(ms);

    // Find the standalone provider class (ends with "StandaloneServiceProvider")
    var providerType = assembly.GetTypes()
        .First(t => t.Name.EndsWith("StandaloneServiceProvider"));
    var provider = (IServiceProvider)Activator.CreateInstance(providerType)!;
    return (assembly, provider);
}

// --- Standalone integration tests ---

[Fact]
public void Standalone_Transient_ReturnsNonNull_DifferentInstances()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var fooType = assembly.GetType("TestApp.IFoo")!;

    var a = provider.GetService(fooType);
    var b = provider.GetService(fooType);

    Assert.NotNull(a);
    Assert.NotNull(b);
    Assert.NotSame(a, b);
}

[Fact]
public void Standalone_Singleton_ReturnsSameInstance()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface ICache { }
        [Singleton]
        public class Cache : ICache { }
        """;
    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var cacheType = assembly.GetType("TestApp.ICache")!;

    var a = provider.GetService(cacheType);
    var b = provider.GetService(cacheType);

    Assert.NotNull(a);
    Assert.Same(a, b);
}

[Fact]
public void Standalone_Scoped_SameWithinScope_DifferentAcrossScopes()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IRepo { }
        [Scoped]
        public class Repo : IRepo { }
        """;
    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var repoType = assembly.GetType("TestApp.IRepo")!;
    var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

    using var scope1 = scopeFactory.CreateScope();
    var a1 = scope1.ServiceProvider.GetService(repoType);
    var a2 = scope1.ServiceProvider.GetService(repoType);

    using var scope2 = scopeFactory.CreateScope();
    var b1 = scope2.ServiceProvider.GetService(repoType);

    Assert.NotNull(a1);
    Assert.Same(a1, a2);
    Assert.NotSame(a1, b1);
}

[Fact]
public void Standalone_UnknownType_ReturnsNull()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (_, provider) = BuildAndCreateStandaloneProvider(source);

    // Unknown type — no fallback, should return null
    var result = provider.GetService(typeof(IntegrationFallbackMarker));
    Assert.Null(result);
}

[Fact]
public void Standalone_IServiceProvider_And_IServiceScopeFactory_ResolveToSelf()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (_, provider) = BuildAndCreateStandaloneProvider(source);

    Assert.Same(provider, provider.GetService(typeof(IServiceProvider)));
    Assert.Same(provider, provider.GetService(typeof(IServiceScopeFactory)));
}

[Fact]
public void Standalone_ScopeDisposal_DisposesTrackedTransients()
{
    const string source = """
        using ZeroInject;
        using System;
        namespace TestApp;
        public interface IHandle { }
        [Transient]
        public class Handle : IHandle, IDisposable
        {
            public bool IsDisposed { get; private set; }
            public void Dispose() { IsDisposed = true; }
        }
        """;
    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var handleType = assembly.GetType("TestApp.IHandle")!;
    var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
    var scope = scopeFactory.CreateScope();

    var instance = scope.ServiceProvider.GetService(handleType)!;
    var isDisposedProp = instance.GetType().GetProperty("IsDisposed")!;
    Assert.False((bool)isDisposedProp.GetValue(instance)!);

    scope.Dispose();

    Assert.True((bool)isDisposedProp.GetValue(instance)!);
}

[Fact]
public void Standalone_IEnumerable_ReturnsAllRegistrations()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IHandler { }
        [Transient(AllowMultiple = true)]
        public class HandlerA : IHandler { }
        [Transient(AllowMultiple = true)]
        public class HandlerB : IHandler { }
        """;
    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var enumerableType = typeof(IEnumerable<>).MakeGenericType(assembly.GetType("TestApp.IHandler")!);

    var result = provider.GetService(enumerableType);
    Assert.NotNull(result);
    var array = ((System.Collections.IEnumerable)result!).Cast<object>().ToArray();
    Assert.Equal(2, array.Length);
}

[Fact]
public void Standalone_Singleton_ConsistentBetweenRootAndScope()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface ICache { }
        [Singleton]
        public class Cache : ICache { }
        """;
    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var cacheType = assembly.GetType("TestApp.ICache")!;
    var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

    var rootInstance = provider.GetService(cacheType);
    using var scope = scopeFactory.CreateScope();
    var scopeInstance = scope.ServiceProvider.GetService(cacheType);

    Assert.NotNull(rootInstance);
    Assert.Same(rootInstance, scopeInstance);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~Standalone_" --no-build`
Expected: FAIL — standalone provider class not yet emitted (depends on Task 3).

**Step 3: Run tests after Task 3 is complete**

Run: `dotnet test tests/ZeroInject.Tests --filter "FullyQualifiedName~Standalone_"`
Expected: PASS (9/9)

**Step 4: Commit**

```bash
git add tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs
git commit -m "test: add standalone container integration tests"
```

---

### Task 5: Add standalone benchmarks

**Files:**
- Modify: `benchmarks/ZeroInject.Benchmarks/ResolutionBenchmarks.cs`

**Step 1: Add standalone provider to benchmarks**

Add a `_standaloneProvider` field and initialize it in `Setup()`:

```csharp
private IServiceProvider _standaloneProvider = null!;

// In Setup():
_standaloneProvider = new ZeroInject.Generated.ZeroInjectBenchmarksStandaloneServiceProvider();

// In Cleanup():
(_standaloneProvider as IDisposable)?.Dispose();
```

Add standalone benchmark methods for each category:

```csharp
// Transient (no deps)
[Benchmark(Description = "Standalone: Resolve transient (no deps)")]
[BenchmarkCategory("Transient")]
public ISimpleService Standalone_ResolveTransient()
    => _standaloneProvider.GetRequiredService<ISimpleService>();

// Transient (1 dep)
[Benchmark(Description = "Standalone: Resolve transient (1 dep)")]
[BenchmarkCategory("TransientWithDep")]
public IServiceWithDep Standalone_ResolveWithDep()
    => _standaloneProvider.GetRequiredService<IServiceWithDep>();

// Transient (2 deps)
[Benchmark(Description = "Standalone: Resolve transient (2 deps)")]
[BenchmarkCategory("TransientMultiDep")]
public IServiceWithMultipleDeps Standalone_ResolveMultipleDeps()
    => _standaloneProvider.GetRequiredService<IServiceWithMultipleDeps>();

// Singleton
[Benchmark(Description = "Standalone: Resolve singleton")]
[BenchmarkCategory("Singleton")]
public ISingletonService Standalone_ResolveSingleton()
    => _standaloneProvider.GetRequiredService<ISingletonService>();

// Scoped
private IServiceScope _standaloneScope = null!;
// Add to ScopeSetup IterationSetup Targets array: nameof(Standalone_ResolveScoped)
// In ScopeSetup: _standaloneScope = (_standaloneProvider as IServiceScopeFactory)!.CreateScope();
// In ScopeCleanup: _standaloneScope.Dispose();

[Benchmark(Description = "Standalone: Resolve scoped")]
[BenchmarkCategory("Scoped")]
public IScopedService Standalone_ResolveScoped()
    => _standaloneScope.ServiceProvider.GetRequiredService<IScopedService>();

// IEnumerable
[Benchmark(Description = "Standalone: Resolve IEnumerable<T>")]
[BenchmarkCategory("Enumerable")]
public IMultiService[] Standalone_ResolveEnumerable()
    => _standaloneProvider.GetRequiredService<IEnumerable<IMultiService>>().ToArray();

// Scope creation
[Benchmark(Description = "Standalone: Create scope")]
[BenchmarkCategory("ScopeCreation")]
public IServiceScope Standalone_CreateScope()
{
    var scope = (_standaloneProvider as IServiceScopeFactory)!.CreateScope();
    scope.Dispose();
    return scope;
}
```

**Step 2: Build to verify it compiles**

Run: `dotnet build benchmarks/ZeroInject.Benchmarks -c Release`
Expected: BUILD SUCCEEDED

**Step 3: Run benchmarks**

Run: `dotnet run --project benchmarks/ZeroInject.Benchmarks -c Release`
Expected: 32 benchmarks run (8 categories × 4 providers). Standalone scope creation should be significantly faster than hybrid container.

**Step 4: Commit**

```bash
git add benchmarks/ZeroInject.Benchmarks/ResolutionBenchmarks.cs
git commit -m "bench: add standalone container benchmarks"
```

---

### Task 6: Add standalone to registration benchmarks

**Files:**
- Modify: `benchmarks/ZeroInject.Benchmarks/RegistrationBenchmarks.cs`

**Step 1: Add standalone instantiation benchmark**

```csharp
[Benchmark(Description = "Standalone: Direct instantiation")]
public IServiceProvider Standalone_DirectInstantiation()
{
    return new ZeroInject.Generated.ZeroInjectBenchmarksStandaloneServiceProvider();
}
```

**Step 2: Build and run**

Run: `dotnet build benchmarks/ZeroInject.Benchmarks -c Release && dotnet run --project benchmarks/ZeroInject.Benchmarks -c Release -- --filter "RegistrationBenchmarks"`
Expected: Standalone instantiation should be near-zero (no ServiceCollection, no BuildServiceProvider).

**Step 3: Commit**

```bash
git add benchmarks/ZeroInject.Benchmarks/RegistrationBenchmarks.cs
git commit -m "bench: add standalone instantiation benchmark"
```
