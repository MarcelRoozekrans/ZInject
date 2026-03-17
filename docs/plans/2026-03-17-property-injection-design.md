# Property Injection Design

**Date:** 2026-03-17
**Status:** Approved

## Overview

Add property injection support to ZeroAlloc.Inject via a new `[Inject]` attribute. Mirrors constructor injection behavior: required by default, opt-out via `Required = false`.

## Attribute

New attribute in `ZeroAlloc.Inject`:

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    public bool Required { get; set; } = true;
}
```

Usage:

```csharp
[Transient]
public class MyService : IMyService
{
    [Inject]                        // GetRequiredService<T>()
    public IOtherDep OtherDep { get; set; } = null!;

    [Inject(Required = false)]      // GetService<T>()
    public IOptionalDep? OptDep { get; set; }
}
```

## Generator Changes

### New model: `PropertyInjectionInfo`

```csharp
internal sealed class PropertyInjectionInfo
{
    public string FullyQualifiedTypeName { get; }
    public string PropertyName { get; }
    public bool IsRequired { get; }
}
```

### `ServiceRegistrationInfo`

Add `List<PropertyInjectionInfo> PropertyInjections`.

### Scanning (`GetServiceInfo`)

After processing constructor parameters, scan the type's public settable properties for `[Inject]`. Emit diagnostic **ZAI0XX** and skip any property without a public setter.

### `BuildFactoryLambdaCore`

When `PropertyInjections` is non-empty, generate a block lambda instead of an expression lambda:

```csharp
// Expression lambda (no property injections):
sp => new MyService(sp.GetRequiredService<IDep>())

// Block lambda (with property injections):
sp => {
    var instance = new MyService(sp.GetRequiredService<IDep>());
    instance.OtherDep = sp.GetRequiredService<IOtherDep>();
    instance.OptDep = sp.GetService<IOptionalDep>();
    return instance;
}
```

## Diagnostics

| Code | Severity | Message |
|------|----------|---------|
| ZAI0XX | Error | `[Inject] on property '{PropertyName}' in '{TypeName}' has no public setter` |

## Tests

- Property with `[Inject]` resolves and is populated correctly
- Property with `[Inject(Required = false)]` resolves when registered, is null when not
- `[Inject]` on a get-only property emits the diagnostic

## Out of Scope

- Constructor parameter injection (use existing `[OptionalDependency]`)
- Static properties
- Inherited properties (only declared properties on the registered type)
