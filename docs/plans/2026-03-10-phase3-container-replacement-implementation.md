# Phase 3: Generated Container Replacement — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Generate a complete `IServiceProvider` implementation with direct `new` calls and type-check resolution, replacing MS DI's runtime resolution engine for ZeroInject-attributed services.

**Architecture:** A new `ZeroInject.Container` package provides runtime base classes (`ZeroInjectServiceProviderBase`, `ZeroInjectScope`). The existing generator is extended to detect the package reference and emit a generated `IServiceProvider` subclass alongside the existing `IServiceCollection` extension method. Unknown services fall back to an inner MS DI provider.

**Tech Stack:** C# / .NET 8+10, Roslyn `IIncrementalGenerator`, `Microsoft.Extensions.DependencyInjection.Abstractions`

**Important constraints:**
- Generator targets `netstandard2.0` — no modern C# features (no raw string literals, file-scoped namespaces, `is not`, `??=`, records)
- Runtime and test projects target `net8.0;net10.0`
- All existing Phase 2 tests must continue passing unchanged

---

### Task 1: Create `ZeroInject.Container` project and add to solution

**Files:**
- Create: `src/ZeroInject.Container/ZeroInject.Container.csproj`
- Modify: `ZeroInject.slnx`

**Step 1: Create project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <RootNamespace>ZeroInject.Container</RootNamespace>
    <PackageId>ZeroInject.Container</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
  </ItemGroup>
</Project>
```

**Step 2: Add to solution**

Add to `ZeroInject.slnx` under `/src/`:
```xml
<Project Path="src/ZeroInject.Container/ZeroInject.Container.csproj" />
```

**Step 3: Verify it builds**

Run: `dotnet build src/ZeroInject.Container/ZeroInject.Container.csproj`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```bash
git add src/ZeroInject.Container/ZeroInject.Container.csproj ZeroInject.slnx
git commit -m "feat: create ZeroInject.Container project"
```

---

### Task 2: Implement `ZeroInjectServiceProviderBase`

**Files:**
- Create: `src/ZeroInject.Container/ZeroInjectServiceProviderBase.cs`

**Step 1: Write the base class**

```csharp
using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectServiceProviderBase : IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _fallback;
    private int _disposed;

    protected ZeroInjectServiceProviderBase(IServiceProvider fallback)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    protected IServiceProvider Fallback => _fallback;

    /// <summary>
    /// Override in generated subclass. Return the resolved service or null if not known.
    /// </summary>
    protected abstract object? ResolveKnown(Type serviceType);

    /// <summary>
    /// Override in generated subclass. Creates a new scope with generated scoped resolution.
    /// </summary>
    protected abstract ZeroInjectScope CreateScopeCore(IServiceScope fallbackScope);

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
            return this;
        if (serviceType == typeof(IServiceScopeFactory))
            return this;

        var result = ResolveKnown(serviceType);
        if (result != null)
            return result;

        return _fallback.GetService(serviceType);
    }

    public IServiceScope CreateScope()
    {
        var fallbackScope = (_fallback as IServiceScopeFactory)?.CreateScope()
            ?? throw new InvalidOperationException("Fallback provider does not support scoping.");
        return CreateScopeCore(fallbackScope);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        (_fallback as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        if (_fallback is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            (_fallback as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/ZeroInject.Container/ZeroInject.Container.csproj`
Expected: Build succeeded, 0 errors

**Step 3: Commit**

```bash
git add src/ZeroInject.Container/ZeroInjectServiceProviderBase.cs
git commit -m "feat: add ZeroInjectServiceProviderBase"
```

---

### Task 3: Implement `ZeroInjectScope`

**Files:**
- Create: `src/ZeroInject.Container/ZeroInjectScope.cs`

**Step 1: Write the scope class**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectScope : IServiceScope, IServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly ZeroInjectServiceProviderBase _root;
    private readonly IServiceScope _fallbackScope;
    private List<object>? _disposables;
    private int _disposed;

    protected ZeroInjectScope(ZeroInjectServiceProviderBase root, IServiceScope fallbackScope)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _fallbackScope = fallbackScope ?? throw new ArgumentNullException(nameof(fallbackScope));
    }

    protected ZeroInjectServiceProviderBase Root => _root;

    public IServiceProvider ServiceProvider => this;

    /// <summary>
    /// Override in generated subclass. Resolves scoped/transient/singleton services.
    /// Return null for unknown types.
    /// </summary>
    protected abstract object? ResolveScopedKnown(Type serviceType);

    /// <summary>
    /// Track an IDisposable or IAsyncDisposable transient for disposal when scope ends.
    /// </summary>
    protected T TrackDisposable<T>(T instance)
    {
        if (instance is IDisposable || instance is IAsyncDisposable)
        {
            _disposables ??= new List<object>();
            _disposables.Add(instance);
        }
        return instance;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
            return this;
        if (serviceType == typeof(IServiceScopeFactory))
            return _root;

        var result = ResolveScopedKnown(serviceType);
        if (result != null)
            return result;

        return _fallbackScope.ServiceProvider.GetService(serviceType);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_disposables != null)
        {
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                if (_disposables[i] is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        _fallbackScope.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_disposables != null)
        {
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                if (_disposables[i] is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (_disposables[i] is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        if (_fallbackScope is IAsyncDisposable asyncScope)
            await asyncScope.DisposeAsync().ConfigureAwait(false);
        else
            _fallbackScope.Dispose();

        GC.SuppressFinalize(this);
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/ZeroInject.Container/ZeroInject.Container.csproj`
Expected: Build succeeded, 0 errors

**Step 3: Commit**

```bash
git add src/ZeroInject.Container/ZeroInjectScope.cs
git commit -m "feat: add ZeroInjectScope base class"
```

---

### Task 4: Add `ZeroInject.Container` reference to test project and write base class unit tests

**Files:**
- Modify: `tests/ZeroInject.Tests/ZeroInject.Tests.csproj`
- Create: `tests/ZeroInject.Tests/ContainerTests/ServiceProviderBaseTests.cs`
- Create: `tests/ZeroInject.Tests/ContainerTests/ScopeTests.cs`

**Step 1: Add project reference to test project**

Add to `tests/ZeroInject.Tests/ZeroInject.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\ZeroInject.Container\ZeroInject.Container.csproj" />
```

**Step 2: Write ServiceProviderBase tests**

Create concrete test subclasses that simulate what the generator would emit, then test:

- Resolving a known transient service returns new instance each time
- Resolving a known singleton returns same instance (thread-safe)
- Resolving unknown type falls through to fallback provider
- `IServiceProvider` resolves to self
- `IServiceScopeFactory` resolves to self
- `CreateScope()` returns a scope
- `Dispose()` disposes fallback provider
- `Dispose()` is idempotent

**Step 3: Write Scope tests**

- Resolving scoped service returns same instance within scope
- Resolving transient returns new instance each time
- Resolving singleton delegates to root
- Unknown type falls through to fallback scope
- `Dispose()` disposes tracked disposables in reverse order
- `Dispose()` disposes fallback scope
- `DisposeAsync()` calls `IAsyncDisposable` on tracked services
- `TrackDisposable` tracks `IDisposable` instances

**Step 4: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "FullyQualifiedName~ContainerTests"`
Expected: All tests pass

**Step 5: Commit**

```bash
git add tests/ZeroInject.Tests/ZeroInject.Tests.csproj tests/ZeroInject.Tests/ContainerTests/
git commit -m "test: add unit tests for container base classes"
```

---

### Task 5: Extend generator to detect `ZeroInject.Container` reference

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:77-84` (the `combined` pipeline)
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:86-150` (the `RegisterSourceOutput` callback)

**Step 1: Write a failing test**

Create `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`:

```csharp
[Fact]
public void WhenContainerReferenced_GeneratesServiceProvider()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }

        [Transient]
        public class MyService : IMyService { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

    Assert.Contains("class ZeroInjectTestAssemblyServiceProvider", output);
    Assert.Contains("ResolveKnown", output);
}
```

This requires a new `RunGeneratorWithContainer` helper method that adds a reference to `ZeroInject.Container` assembly.

**Step 2: Add `RunGeneratorWithContainer` to `GeneratorTestHelper`**

Modify `tests/ZeroInject.Tests/GeneratorTests/GeneratorTestHelper.cs` to add a second method that includes the `ZeroInject.Container` assembly reference. The generator can then detect it via `compilation.ReferencedAssemblyNames` looking for `"ZeroInject.Container"`.

**Step 3: Add detection logic to generator**

In `ZeroInjectGenerator.Initialize`, add a `CompilationProvider.Select` that checks if `ZeroInject.Container` is among referenced assemblies:

```csharp
var hasContainer = context.CompilationProvider.Select(
    static (compilation, _) =>
    {
        foreach (var asm in compilation.ReferencedAssemblyNames)
        {
            if (asm.Name == "ZeroInject.Container")
                return true;
        }
        return false;
    });
```

Combine this into the existing pipeline and pass it to `RegisterSourceOutput`. When `true`, call a new `GenerateServiceProviderClass` method alongside the existing `GenerateExtensionClass`.

**Step 4: Add stub `GenerateServiceProviderClass` method**

Add a private method that emits a minimal class:

```csharp
private static string GenerateServiceProviderClass(
    List<ServiceRegistrationInfo> services,
    string assemblyName)
{
    var className = "ZeroInject" + CleanAssemblyName(assemblyName) + "ServiceProvider";
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    // ... emit class inheriting ZeroInjectServiceProviderBase
    return sb.ToString();
}
```

**Step 5: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj`
Expected: All tests pass (existing + new)

**Step 6: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs tests/ZeroInject.Tests/GeneratorTests/
git commit -m "feat: detect ZeroInject.Container reference and emit service provider stub"
```

---

### Task 6: Generate `ResolveKnown` — transient resolution

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs` (in `GenerateServiceProviderClass`)
- Create: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs` (add tests)

**Step 1: Write failing tests**

```csharp
[Fact]
public void TransientService_GeneratesNewInResolveKnown()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("if (serviceType == typeof(global::TestApp.IFoo))", output);
    Assert.Contains("new global::TestApp.Foo()", output);
}

[Fact]
public void TransientWithDep_GeneratesGetServiceCall()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        [Transient]
        public class Foo : IFoo { }
        public interface IBar { }
        [Transient]
        public class Bar : IBar
        {
            public Bar(IFoo foo) { }
        }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("new global::TestApp.Bar(\n                (global::TestApp.IFoo)GetService(typeof(global::TestApp.IFoo))!", output);
}
```

**Step 2: Implement `ResolveKnown` emission for transients**

In `GenerateServiceProviderClass`, emit `if (serviceType == typeof(T)) return new T(args);` for each transient service. Constructor arguments use `(ParamType)GetService(typeof(ParamType))!` for required and `(ParamType?)GetService(typeof(ParamType))` for optional parameters.

**Step 3: Run tests, verify pass, commit**

---

### Task 7: Generate singleton resolution with `Interlocked.CompareExchange`

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write failing test**

```csharp
[Fact]
public void SingletonService_GeneratesInterlockedField()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface ICache { }
        [Singleton]
        public class Cache : ICache { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("private global::TestApp.Cache? _singleton_", output);
    Assert.Contains("Interlocked.CompareExchange", output);
}
```

**Step 2: Implement**

For each singleton service:
- Emit a `private TypeName? _singleton_N;` field on the root provider class
- In `ResolveKnown`, emit:
```csharp
if (serviceType == typeof(ICache))
{
    if (_singleton_0 != null) return _singleton_0;
    var instance = new Cache();
    return Interlocked.CompareExchange(ref _singleton_0, instance, null) ?? _singleton_0;
}
```

**Step 3: Run tests, verify pass, commit**

---

### Task 8: Generate scope class with scoped resolution

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write failing test**

```csharp
[Fact]
public void ScopedService_GeneratesScopeField()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        [Scoped]
        public class Worker { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("private global::TestApp.Worker? _scoped_", output);
    Assert.Contains("sealed class", output); // scope is a nested sealed class
    Assert.Contains("ResolveScopedKnown", output);
}
```

**Step 2: Implement**

Generate a nested sealed scope class that:
- Inherits `ZeroInjectScope`
- Has `private TypeName? _scoped_N;` fields for each scoped service
- Overrides `ResolveScopedKnown`:
  - Transient: `new T(args)` (fresh each call)
  - Singleton: `Root.GetService(serviceType)` (delegate to root)
  - Scoped: `_scoped_N ??= new T(args)` (cached per scope)

Also generate `CreateScopeCore()` override on the root provider.

**Step 3: Run tests, verify pass, commit**

---

### Task 9: Generate `BuildZeroInjectServiceProvider` extension method

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write failing test**

```csharp
[Fact]
public void GeneratesBuildExtensionMethod()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        [Transient]
        public class Svc { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("BuildZeroInjectServiceProvider", output);
    Assert.Contains("this IServiceCollection services", output);
    Assert.Contains("BuildServiceProvider()", output);
}
```

**Step 2: Implement**

Generate a static extension method:
```csharp
public static IServiceProvider BuildZeroInjectServiceProvider(this IServiceCollection services)
{
    var fallback = services.BuildServiceProvider();
    return new ZeroInjectAppServiceProvider(fallback);
}
```

**Step 3: Run tests, verify pass, commit**

---

### Task 10: Generate `ZeroInjectServiceProviderFactory` for ASP.NET Core

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write failing test**

```csharp
[Fact]
public void GeneratesServiceProviderFactory()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        [Transient]
        public class Svc { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("class ZeroInjectServiceProviderFactory", output);
    Assert.Contains("IServiceProviderFactory<IServiceCollection>", output);
    Assert.Contains("CreateBuilder", output);
    Assert.Contains("CreateServiceProvider", output);
}
```

**Step 2: Implement**

Generate a class implementing `IServiceProviderFactory<IServiceCollection>`:
```csharp
public sealed class ZeroInjectServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;
    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        var fallback = containerBuilder.BuildServiceProvider();
        return new ZeroInjectAppServiceProvider(fallback);
    }
}
```

**Step 3: Run tests, verify pass, commit**

---

### Task 11: Handle keyed services in generated container

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write failing test**

```csharp
[Fact]
public void KeyedService_GeneratesKeyedResolution()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface ICache { }
        [Singleton(Key = "redis")]
        public class RedisCache : ICache { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("IKeyedServiceProvider", output);
    Assert.Contains("\"redis\"", output);
}
```

**Step 2: Implement**

When any keyed services exist, make the generated provider also implement `IKeyedServiceProvider`. Add a `GetKeyedService(Type serviceType, object? serviceKey)` method with type+key checks.

**Step 3: Run tests, verify pass, commit**

---

### Task 12: Handle `As` property and `AllowMultiple` in container

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write tests**

```csharp
[Fact]
public void AsProperty_OnlyRegistersSpecifiedType()
{
    var source = """
        using ZeroInject;
        namespace TestApp;
        public interface IFoo { }
        public interface IBar { }
        [Transient(As = typeof(IFoo))]
        public class Svc : IFoo, IBar { }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("typeof(global::TestApp.IFoo)", output);
    Assert.DoesNotContain("typeof(global::TestApp.IBar)", output);
}
```

**Step 2: Implement**

When `AsType` is set, only emit a type-check for that type (not all interfaces). Concrete type registration is also skipped (same as Phase 2 behavior).

**Step 3: Run tests, verify pass, commit**

---

### Task 13: Handle `IDisposable` tracking for transients

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write test**

```csharp
[Fact]
public void DisposableTransient_GeneratesTrackDisposable()
{
    var source = """
        using ZeroInject;
        using System;
        namespace TestApp;
        public interface IFoo : IDisposable { }
        [Transient]
        public class Foo : IFoo { public void Dispose() { } }
        """;
    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("TrackDisposable", output);
}
```

**Step 2: Implement**

When the generator detects a service type implements `IDisposable` or `IAsyncDisposable`, wrap the transient creation in `TrackDisposable(new Foo())` in the scope's `ResolveScopedKnown`. The base class's `TrackDisposable` adds it to the disposal list.

**Step 3: Run tests, verify pass, commit**

---

### Task 14: Ensure existing Phase 2 tests still pass

**Files:**
- No modifications — verification only

**Step 1: Run full test suite**

Run: `dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj`
Expected: All 61+ existing tests pass, plus all new container tests

**Step 2: Run full solution build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors, 0 warnings (except ZI007 from sample)

---

### Task 15: Integration test — build and resolve services end-to-end

**Files:**
- Create: `tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs`

**Step 1: Write integration tests**

These tests actually build the generated container and resolve services. They require a different test approach — compile the generated code and run it. Use the test helper to get the generated source, then compile it with the runtime dependencies and execute.

Alternatively, create a simple test project that references all three packages and verify resolution works at runtime.

Key scenarios to test:
- Resolve transient service (no deps)
- Resolve transient service (with deps)
- Resolve singleton returns same instance
- Resolve scoped returns same instance within scope, different across scopes
- Unknown service falls through to fallback
- `IServiceScopeFactory` resolves to provider
- Scope disposal disposes tracked transients

**Step 2: Run tests, verify pass, commit**

---

### Task 16: Update benchmarks to include Phase 3 container

**Files:**
- Modify: `benchmarks/ZeroInject.Benchmarks/ZeroInject.Benchmarks.csproj`
- Modify: `benchmarks/ZeroInject.Benchmarks/RegistrationBenchmarks.cs`
- Modify: `benchmarks/ZeroInject.Benchmarks/ResolutionBenchmarks.cs`

**Step 1: Add `ZeroInject.Container` reference to benchmark project**

**Step 2: Add Phase 3 benchmarks**

Add a third benchmark method to both classes:

Registration:
```csharp
[Benchmark(Description = "ZeroInject (container)")]
public IServiceProvider ZeroInject_Container()
{
    var services = new ServiceCollection();
    return services.BuildZeroInjectServiceProvider();
}
```

Resolution: Build the generated container in `GlobalSetup` and add resolution benchmarks using it.

**Step 3: Run benchmarks, verify results show improvement**

**Step 4: Commit**

```bash
git add benchmarks/
git commit -m "bench: add Phase 3 generated container benchmarks"
```

---

### Task 17: Update README with Phase 3 usage

**Files:**
- Modify: `README.md`

**Step 1: Add container replacement section**

Document:
- How to install `ZeroInject.Container`
- Console app usage (`BuildZeroInjectServiceProvider()`)
- ASP.NET Core usage (`UseServiceProviderFactory`)
- Performance comparison table
- v1 limitations (IEnumerable, open generics)

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add Phase 3 container replacement usage guide"
```
