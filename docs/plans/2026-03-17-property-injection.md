# Property Injection Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `[Inject]` attribute support so properties on registered service classes can be injected via `GetRequiredService<T>()` (default) or `GetService<T>()` (when `Required = false`).

**Architecture:** New `InjectAttribute` in the attributes project; generator scans decorated properties during `GetServiceInfo`, stores them in `ServiceRegistrationInfo.PropertyInjections`, and switches from an expression lambda to a block lambda when property injections are present.

**Tech Stack:** C# source generator (Roslyn / `IIncrementalGenerator`), xUnit, `Microsoft.Extensions.DependencyInjection`

---

### Task 1: Add `InjectAttribute`

**Files:**
- Create: `src/ZeroAlloc.Inject/InjectAttribute.cs`

**Step 1: Write the attribute**

```csharp
namespace ZeroAlloc.Inject;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    public bool Required { get; set; } = true;
}
```

**Step 2: Verify it builds**

```
dotnet build src/ZeroAlloc.Inject/ZeroAlloc.Inject.csproj
```
Expected: Build succeeded, 0 errors.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Inject/InjectAttribute.cs
git commit -m "feat: add [Inject] attribute for property injection"
```

---

### Task 2: Add `PropertyInjectionInfo` model

**Files:**
- Create: `src/ZeroAlloc.Inject.Generator/PropertyInjectionInfo.cs`

**Step 1: Write the model**

```csharp
#nullable enable
using System;

namespace ZeroAlloc.Inject.Generator
{
    internal sealed class PropertyInjectionInfo : IEquatable<PropertyInjectionInfo>
    {
        public string FullyQualifiedTypeName { get; }
        public string PropertyName { get; }
        public bool IsRequired { get; }

        public PropertyInjectionInfo(string fullyQualifiedTypeName, string propertyName, bool isRequired)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            PropertyName = propertyName;
            IsRequired = isRequired;
        }

        public bool Equals(PropertyInjectionInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedTypeName == other.FullyQualifiedTypeName
                && PropertyName == other.PropertyName
                && IsRequired == other.IsRequired;
        }

        public override bool Equals(object? obj) => Equals(obj as PropertyInjectionInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedTypeName.GetHashCode();
                hash = hash * 31 + PropertyName.GetHashCode();
                hash = hash * 31 + IsRequired.GetHashCode();
                return hash;
            }
        }
    }
}
```

**Step 2: Verify it builds**

```
dotnet build src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj
```
Expected: Build succeeded, 0 errors.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Inject.Generator/PropertyInjectionInfo.cs
git commit -m "feat: add PropertyInjectionInfo model"
```

---

### Task 3: Extend `ServiceRegistrationInfo` with property injections

**Files:**
- Modify: `src/ZeroAlloc.Inject.Generator/ServiceRegistrationInfo.cs`

**Step 1: Add `PropertyInjections` field**

In the class body, after `ConstructorParameters`, add:
```csharp
public List<PropertyInjectionInfo> PropertyInjections { get; }
```

**Step 2: Add parameter to constructor**

After the `implementationMetadataName` parameter, add:
```csharp
List<PropertyInjectionInfo>? propertyInjections = null
```

In the constructor body, add:
```csharp
PropertyInjections = propertyInjections ?? new List<PropertyInjectionInfo>();
```

**Step 3: Update `Equals` and `GetHashCode`**

In `Equals`, add after `ConstructorParameters.Count != other.ConstructorParameters.Count`:
```csharp
|| PropertyInjections.Count != other.PropertyInjections.Count
```

Add a loop after the `ConstructorParameters` loop:
```csharp
for (int i = 0; i < PropertyInjections.Count; i++)
{
    if (!PropertyInjections[i].Equals(other.PropertyInjections[i]))
        return false;
}
```

In `GetHashCode`, add:
```csharp
hash = hash * 31 + PropertyInjections.Count.GetHashCode();
```

**Step 4: Verify it builds**

```
dotnet build src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj
```
Expected: Build succeeded, 0 errors.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Inject.Generator/ServiceRegistrationInfo.cs
git commit -m "feat: add PropertyInjections to ServiceRegistrationInfo"
```

---

### Task 4: Add ZAI019 diagnostic descriptor

**Files:**
- Modify: `src/ZeroAlloc.Inject.Generator/DiagnosticDescriptors.cs`

**Step 1: Add after `NoDetectedClosedUsages` (ZAI018)**

```csharp
public static readonly DiagnosticDescriptor InjectOnNonSettableProperty = new DiagnosticDescriptor(
    "ZAI019",
    "[Inject] on non-settable property",
    "Property '{0}' of class '{1}' is marked [Inject] but has no public setter; add a public setter or remove [Inject]",
    "ZeroAlloc.Inject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 2: Verify it builds**

```
dotnet build src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj
```
Expected: Build succeeded, 0 errors.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Inject.Generator/DiagnosticDescriptors.cs
git commit -m "feat: add ZAI019 diagnostic for [Inject] on non-settable property"
```

---

### Task 5: Generator — scan properties for `[Inject]`

**Files:**
- Modify: `src/ZeroAlloc.Inject.Generator/ZeroAllocInjectGenerator.cs`

The scanning is in `GetServiceInfo`. Find where `constructorParameters` is built (around line 510) and the `return new ServiceRegistrationInfo(...)` at the end of the method (around line 625).

**Step 1: Add property scanning after the constructor analysis block**

Insert after the `if (chosenCtor != null) { ... }` block (after line ~612), before `string? implementationMetadataName = null;`:

```csharp
// Property injection scanning
var propertyInjections = new List<PropertyInjectionInfo>();
foreach (var member in typeSymbol.GetMembers())
{
    if (member is not IPropertySymbol propSymbol) continue;
    var injectAttr = propSymbol.GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Inject.InjectAttribute");
    if (injectAttr == null) continue;

    // Validate: must have a public setter
    bool hasPublicSetter = propSymbol.SetMethod != null
        && propSymbol.SetMethod.DeclaredAccessibility == Accessibility.Public;
    if (!hasPublicSetter)
    {
        spc_placeholder_reportdiag = true; // see note below
        continue;
    }

    bool isRequired = true;
    foreach (var namedArg in injectAttr.NamedArguments)
    {
        if (namedArg.Key == "Required" && namedArg.Value.Value is bool reqVal)
        {
            isRequired = reqVal;
            break;
        }
    }

    var propTypeFqn = propSymbol.Type.ToDisplayString(FullyQualifiedFormat);
    propertyInjections.Add(new PropertyInjectionInfo(propTypeFqn, propSymbol.Name, isRequired));
}
```

> **Note on diagnostics:** `GetServiceInfo` is a transform function and cannot directly call `spc.ReportDiagnostic`. The diagnostics loop in the `RegisterSourceOutput` callback (around line 148) is where you report diagnostics from the `ServiceRegistrationInfo`. Store the offending property names on the model instead.

**Step 2: Store non-settable property names for diagnostic reporting**

In `ServiceRegistrationInfo`, add:
```csharp
public List<string> NonSettableInjectProperties { get; }
```
Add parameter `List<string>? nonSettableInjectProperties = null` to the constructor and assign:
```csharp
NonSettableInjectProperties = nonSettableInjectProperties ?? new List<string>();
```

**Step 3: Pass the lists through in `GetServiceInfo`**

Change the `return new ServiceRegistrationInfo(...)` call to pass:
```csharp
propertyInjections: propertyInjections,
nonSettableInjectProperties: nonSettableInjectPropNames  // collected in the scan loop
```

Update the scan loop above to use `nonSettableInjectPropNames` instead of `spc_placeholder_reportdiag`.

**Step 4: Emit ZAI019 in the diagnostics loop**

In `RegisterSourceOutput`, in the `foreach (var svc in allServices)` loop (around line 148), after the existing diagnostic checks, add:

```csharp
foreach (var propName in svc.NonSettableInjectProperties)
{
    spc.ReportDiagnostic(Diagnostic.Create(
        DiagnosticDescriptors.InjectOnNonSettableProperty,
        Location.None,
        propName,
        svc.TypeName));
}
```

**Step 5: Verify it builds**

```
dotnet build src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj
```
Expected: Build succeeded, 0 errors.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Inject.Generator/ZeroAllocInjectGenerator.cs
git add src/ZeroAlloc.Inject.Generator/ServiceRegistrationInfo.cs
git commit -m "feat: scan [Inject] properties in GetServiceInfo, emit ZAI019"
```

---

### Task 6: Generator — emit block lambda for property injection

**Files:**
- Modify: `src/ZeroAlloc.Inject.Generator/ZeroAllocInjectGenerator.cs`

The method to change is `BuildFactoryLambdaCore` (around line 732). It currently takes `implType` and `parameters`. It needs a third parameter: the property injections list.

**Step 1: Update signatures**

Change:
```csharp
private static string BuildFactoryLambda(string implType, List<ConstructorParameterInfo> parameters)
private static string BuildKeyedFactoryLambda(string implType, List<ConstructorParameterInfo> parameters)
private static string BuildFactoryLambdaCore(string implType, List<ConstructorParameterInfo> parameters, bool keyed)
```
To:
```csharp
private static string BuildFactoryLambda(string implType, List<ConstructorParameterInfo> parameters, List<PropertyInjectionInfo> propertyInjections)
private static string BuildKeyedFactoryLambda(string implType, List<ConstructorParameterInfo> parameters, List<PropertyInjectionInfo> propertyInjections)
private static string BuildFactoryLambdaCore(string implType, List<ConstructorParameterInfo> parameters, bool keyed, List<PropertyInjectionInfo> propertyInjections)
```

**Step 2: Update all call sites**

Grep for `BuildFactoryLambda(` and `BuildKeyedFactoryLambda(` throughout the file and pass `svc.PropertyInjections` as the new argument.

**Step 3: Update `BuildFactoryLambdaCore` body**

Replace the entire method body with:

```csharp
private static string BuildFactoryLambdaCore(string implType, List<ConstructorParameterInfo> parameters, bool keyed, List<PropertyInjectionInfo> propertyInjections)
{
    var spPrefix = keyed ? "(sp, _)" : "sp";
    bool hasProps = propertyInjections.Count > 0;

    // Build the constructor call expression
    var ctorSb = new StringBuilder();
    ctorSb.Append("new ").Append(implType).Append("(");
    if (parameters.Count > 0)
    {
        ctorSb.Append("\n");
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            var method = param.IsOptional ? "GetService" : "GetRequiredService";
            ctorSb.Append("                sp.");
            ctorSb.Append(method).Append("<").Append(param.FullyQualifiedTypeName).Append(">()");
            if (i < parameters.Count - 1) ctorSb.Append(",");
            ctorSb.Append("\n");
        }
        ctorSb.Append("            )");
    }
    else
    {
        ctorSb.Append(")");
    }

    if (!hasProps)
    {
        // Expression lambda
        return spPrefix + " => " + ctorSb;
    }

    // Block lambda
    var sb = new StringBuilder();
    sb.Append(spPrefix).Append(" =>\n            {\n");
    sb.Append("                var instance = ").Append(ctorSb).Append(";\n");
    foreach (var prop in propertyInjections)
    {
        var method = prop.IsRequired ? "GetRequiredService" : "GetService";
        sb.Append("                instance.").Append(prop.PropertyName)
          .Append(" = sp.").Append(method)
          .Append("<").Append(prop.FullyQualifiedTypeName).Append(">();\n");
    }
    sb.Append("                return instance;\n");
    sb.Append("            }");
    return sb.ToString();
}
```

**Step 4: Verify it builds**

```
dotnet build src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj
```
Expected: Build succeeded, 0 errors.

**Step 5: Run existing tests to confirm no regressions**

```
dotnet test tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj
```
Expected: All tests pass.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Inject.Generator/ZeroAllocInjectGenerator.cs
git commit -m "feat: emit block lambda when property injections are present"
```

---

### Task 7: Generator tests for property injection

**Files:**
- Create: `tests/ZeroAlloc.Inject.Tests/GeneratorTests/PropertyInjectionGeneratorTests.cs`

**Step 1: Write the tests**

```csharp
using System;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class PropertyInjectionGeneratorTests
{
    [Fact]
    public void InjectProperty_Required_GeneratesBlockLambdaWithGetRequiredService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                [Inject]
                public IDep Dep { get; set; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("var instance = new global::TestApp.MyService()", output);
        Assert.Contains("instance.Dep = sp.GetRequiredService<global::TestApp.IDep>()", output);
        Assert.Contains("return instance;", output);
    }

    [Fact]
    public void InjectProperty_RequiredFalse_GeneratesBlockLambdaWithGetService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IOptDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                [Inject(Required = false)]
                public IOptDep? OptDep { get; set; }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("instance.OptDep = sp.GetService<global::TestApp.IOptDep>()", output);
    }

    [Fact]
    public void InjectProperty_NoSetter_ProducesZAI019()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                [Inject]
                public IDep Dep { get; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI019", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectProperty_WithConstructorDeps_GeneratesBlockLambdaWithBoth()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICtorDep { }
            public interface IPropDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(ICtorDep ctorDep) { }

                [Inject]
                public IPropDep PropDep { get; set; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ICtorDep>()", output);
        Assert.Contains("instance.PropDep = sp.GetRequiredService<global::TestApp.IPropDep>()", output);
    }

    [Fact]
    public void NoInjectProperty_StillGeneratesExpressionLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // Expression lambda — no block
        Assert.Contains("sp => new global::TestApp.MyService()", output);
        Assert.DoesNotContain("var instance =", output);
    }
}
```

**Step 2: Run the new tests (expect failures for now)**

```
dotnet test tests/ZeroAlloc.Inject.Tests/ --filter "FullyQualifiedName~PropertyInjectionGeneratorTests"
```
Expected: Tests that reference implementation fail; shape tests pass.

**Step 3: Run all tests**

```
dotnet test tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj
```
Expected: All tests pass.

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Inject.Tests/GeneratorTests/PropertyInjectionGeneratorTests.cs
git commit -m "test: add generator tests for [Inject] property injection"
```

---

### Task 8: Integration tests for property injection

**Files:**
- Create: `tests/ZeroAlloc.Inject.Tests/ContainerTests/PropertyInjectionIntegrationTests.cs`

Look at `tests/ZeroAlloc.Inject.Tests/ContainerTests/IntegrationTests.cs` for the `BuildAndCreateProvider` helper — copy its using statements and helper call pattern.

**Step 1: Write the integration tests**

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

public class PropertyInjectionIntegrationTests
{
    [Fact]
    public void InjectProperty_Required_IsPopulatedAtResolution()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IDep { string Value { get; } }
            public interface IMyService { IDep Dep { get; } }

            [Transient]
            public class Dep : IDep
            {
                public string Value => "injected";
            }

            [Transient]
            public class MyService : IMyService
            {
                [Inject]
                public IDep Dep { get; set; } = null!;

                IDep IMyService.Dep => Dep;
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var serviceType = assembly.GetType("TestApp.IMyService")!;
        var service = provider.GetRequiredService(serviceType);

        var depProp = serviceType.GetProperty("Dep")!;
        var dep = depProp.GetValue(service)!;
        var valueProp = dep.GetType().GetProperty("Value")!;
        Assert.Equal("injected", valueProp.GetValue(dep));
    }

    [Fact]
    public void InjectProperty_OptionalNotRegistered_IsNull()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IOptDep { }
            public interface IMyService { IOptDep? Opt { get; } }

            [Transient]
            public class MyService : IMyService
            {
                [Inject(Required = false)]
                public IOptDep? Opt { get; set; }

                IOptDep? IMyService.Opt => Opt;
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var serviceType = assembly.GetType("TestApp.IMyService")!;
        var service = provider.GetRequiredService(serviceType);

        var optProp = serviceType.GetProperty("Opt")!;
        Assert.Null(optProp.GetValue(service));
    }

    // Copy BuildAndCreateProvider from IntegrationTests — or extract to a shared base class if you prefer
    private static (Assembly assembly, IServiceProvider provider) BuildAndCreateProvider(string source)
        => IntegrationTestHelper.BuildAndCreateProvider(source);
}
```

> **Note:** Either copy `BuildAndCreateProvider` directly here, or extract it to a shared `IntegrationTestHelper` static class. Check whether `IntegrationTests.cs` already exposes it as `internal static` — if not, extract it to avoid duplication.

**Step 2: Run the integration tests**

```
dotnet test tests/ZeroAlloc.Inject.Tests/ --filter "FullyQualifiedName~PropertyInjectionIntegrationTests"
```
Expected: All pass.

**Step 3: Run the full test suite**

```
dotnet test tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj
```
Expected: All tests pass.

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Inject.Tests/ContainerTests/PropertyInjectionIntegrationTests.cs
git commit -m "test: add integration tests for [Inject] property injection"
```
