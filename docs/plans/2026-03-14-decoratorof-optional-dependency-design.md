# Design: `[DecoratorOf]` and `[OptionalDependency]`

**Date:** 2026-03-14
**Status:** Approved

## Summary

Add two new attributes to ZInject:

- `[DecoratorOf(typeof(IInterface), Order = N, WhenRegistered = typeof(T))]` — an explicit, composable decorator attribute with ordering and conditional application
- `[OptionalDependency]` — marks a constructor parameter as optional, generating `GetService<T>()` instead of `GetRequiredService<T>()`

The existing `[Decorator]` attribute is unchanged (no breaking changes).

## Attributes

### `DecoratorOfAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DecoratorOfAttribute : Attribute
{
    public DecoratorOfAttribute(Type decoratedInterface) { }
    public Type DecoratedInterface { get; }
    public int Order { get; set; }           // ascending: 1 = innermost, default 0
    public Type? WhenRegistered { get; set; } // runtime check in AddXxxServices()
}
```

- `AllowMultiple = true` — a class can decorate multiple interfaces
- `Order` ascending: lower = innermost (closer to real implementation), higher = outermost (first to intercept)
- `WhenRegistered` — runtime check: decorator is only wired if `services.Any(d => d.ServiceType == typeof(T))` at the time `AddXxxServices()` is called

### `OptionalDependencyAttribute`

```csharp
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class OptionalDependencyAttribute : Attribute { }
```

- Applied to constructor parameters only
- Parameter must be nullable — ZI015 error if not
- Generator emits `sp.GetService<T>()` instead of `sp.GetRequiredService<T>()`

### Example

```csharp
[DecoratorOf(typeof(IRetriever), Order = 1, WhenRegistered = typeof(SomeOptions))]
public class LoggingRetriever : IRetriever
{
    public LoggingRetriever(IRetriever inner, [OptionalDependency] ILogger? logger) { }
}

[DecoratorOf(typeof(IRetriever), Order = 2)]
public class TracingRetriever : IRetriever
{
    public TracingRetriever(IRetriever inner) { }
}
```

Resulting chain (when `SomeOptions` is registered):

```
caller → TracingRetriever (Order=2) → LoggingRetriever (Order=1) → real IRetriever impl
```

## Ordering

Decorators sorted ascending by `Order` before chain generation. Lower = registered first = innermost.

| Order | Role |
|-------|------|
| 1 | Innermost — wraps the real implementation |
| 2 | Wraps Order=1 |
| N | Outermost — first to intercept incoming calls |

Default `Order` is `0`. Two decorators with the same `Order` on the same interface → ZI017 error.

## `WhenRegistered` — Runtime Check

The check is emitted in `AddXxxServices()` and wraps the decorator registration:

```csharp
// Without WhenRegistered — always applied
services.Remove(existing);
services.AddTransient<IRetriever>(sp =>
    new TracingRetriever(sp.GetRequiredService<IRetriever>()));

// With WhenRegistered = typeof(SomeOptions)
if (services.Any(d => d.ServiceType == typeof(SomeOptions)))
{
    services.Remove(existing);
    services.AddTransient<IRetriever>(sp =>
        new LoggingRetriever(
            sp.GetRequiredService<IRetriever>(),
            sp.GetService<ILogger>()));
}
```

Performance: one O(n) `IServiceCollection` scan per `WhenRegistered` at startup only. No impact on resolution.

Also applies to the hybrid and standalone generated containers.

## Generator Changes

### New pipeline

Add a second `ForAttributeWithMetadataName` pipeline for `ZInject.DecoratorOfAttribute`, producing `DecoratorRegistrationInfo` records extended with `Order` and `WhenRegistered` (nullable FQN string).

### `DecoratorRegistrationInfo` additions

- `int Order` (default 0)
- `string? WhenRegisteredFqn` (null = unconditional)

### Validation (before code generation)

- ZI015 — `[OptionalDependency]` on non-nullable parameter
- ZI016 — `[DecoratorOf]` interface not implemented by the class
- ZI017 — two decorators for the same interface share the same `Order`

### Code generation

1. Sort `validDecorators` by `Order` ascending per interface
2. For each decorator in order, emit wrapping registration
3. Wrap in `if (services.Any(...))` when `WhenRegisteredFqn` is set
4. Emit `GetService<T>()` for parameters annotated with `[OptionalDependency]`

## What Does NOT Change

- `[Decorator]` attribute and its existing behaviour
- `ServiceRegistrationInfo`, `ServiceAttribute`, lifetime attributes
- Diagnostics ZI001–ZI014
- All existing tests continue to pass

## New Tests

- `[DecoratorOf]` basic wrapping
- `Order` produces correct chain (innermost → outermost)
- `WhenRegistered` emits conditional block; unconditional when absent
- `[OptionalDependency]` emits `GetService` vs `GetRequiredService`
- ZI015: error on non-nullable optional parameter
- ZI016: error when interface not implemented
- ZI017: error on duplicate `Order` for same interface
