# ZeroInject Phase 2 — Source-Generated Factory Lambdas

## Overview

Evolve the generator from emitting type-based registrations to factory lambdas with direct `new()` calls. This eliminates `Activator.CreateInstance` and reflection for all attributed services while keeping Microsoft's container for lifetime/scope management.

## Motivation

Raw performance — eliminate reflection overhead in service construction. Every attributed service gets a compile-time-generated factory lambda with explicit constructor calls.

## What Changes

### Constructor Analysis

The generator's `GetServiceInfo` inspects the constructor of each attributed class:

1. **Single public constructor** — use it directly
2. **Multiple public constructors** — look for `[ActivatorUtilitiesConstructor]`, error if not found (ZI009)
3. **No public constructor** — already handled by ZI006

For each constructor parameter:
- Extract fully qualified type name and parameter name
- Track whether it's optional (has default value)
- Compile-time error (ZI010) if parameter is a primitive/value type

### Data Model

New `ConstructorParameterInfo` added to `ServiceRegistrationInfo`:

```csharp
internal sealed class ConstructorParameterInfo : IEquatable<ConstructorParameterInfo>
{
    public string FullyQualifiedTypeName { get; }
    public string ParameterName { get; }
    public bool IsOptional { get; }  // has default value
}
```

`ServiceRegistrationInfo` gets:
```csharp
public List<ConstructorParameterInfo> ConstructorParameters { get; }
```

### Generated Code

**Before (Phase 1):**
```csharp
services.TryAddTransient<IOrderService, OrderService>();
services.TryAddTransient<OrderService>();
```

**After (Phase 2):**
```csharp
services.TryAddTransient<IOrderService>(sp => new global::TestApp.OrderService(
    sp.GetRequiredService<global::TestApp.IRepository>(),
    sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::TestApp.OrderService>>()));
services.TryAddTransient(sp => new global::TestApp.OrderService(
    sp.GetRequiredService<global::TestApp.IRepository>(),
    sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::TestApp.OrderService>>()));
```

### Rules

- **Parameterless constructor** — `sp => new Foo()` (still a factory, avoids reflection)
- **Optional parameters** — `sp.GetService<T>()` instead of `sp.GetRequiredService<T>()`
- **Open generics** — unchanged, still `ServiceDescriptor` (can't generate factories for open generics)
- **Keyed registrations** — factory lambda for the implementation, keyed for the registration
- **Concrete-type registration** — also uses factory lambda

### New Diagnostics

| ID | Severity | Description |
|---|---|---|
| ZI009 | Error | Multiple public constructors without `[ActivatorUtilitiesConstructor]` |
| ZI010 | Error | Constructor parameter is a primitive/value type — use `IOptions<T>` instead |

**ZI010 triggers for:** `string`, `int`, `bool`, `double`, `decimal`, `float`, `long`, `byte`, `char`, `short`, `Guid`, `DateTime`, `TimeSpan`, `Uri`, `CancellationToken`, and any `enum` or user-defined `struct`.

**ZI010 does NOT trigger for:** interfaces, classes, `IEnumerable<T>`, `Func<T>`, `Lazy<T>`.

## What Stays the Same

- All attributes (`[Transient]`, `[Scoped]`, `[Singleton]`, `As`, `Key`, `AllowMultiple`)
- Interface filtering (IDisposable, IEquatable<T>, etc.)
- Method naming (assembly-level `[ZeroInject]` override)
- Extension class structure and namespace
- `TryAdd` vs `Add` semantics
- Open generic registration via `ServiceDescriptor`
- No custom `IServiceProvider` — Microsoft's container handles lifetime/scope

## Impact on Existing Code

- `ServiceRegistrationInfo` extended with `ConstructorParameters`
- `GetServiceInfo` adds constructor analysis
- `EmitRegistration` / `EmitSingleRegistration` / `EmitConcreteRegistration` switch to factory lambda output
- Existing test assertions need updating (output format changes)
- New test file: `FactoryRegistrationTests.cs` for constructor-specific scenarios
