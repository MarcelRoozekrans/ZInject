using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

public class PropertyInjectionIntegrationTests
{
    // ---------------------------------------------------------------
    // 1. Required [Inject] property is populated at resolution
    // ---------------------------------------------------------------
    [Fact]
    public void InjectProperty_Required_IsPopulatedAtResolution()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IDep { string Value { get; } }
            [Singleton]
            public class Dep : IDep { public string Value => "injected"; }
            public interface IMyService { }
            [Transient]
            public class MyService : IMyService
            {
                [Inject]
                public IDep Dep { get; set; } = null!;
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var myServiceType = assembly.GetType("TestApp.IMyService")!;

        var instance = provider.GetService(myServiceType);

        Assert.NotNull(instance);
        var depProp = instance!.GetType().GetProperty("Dep")!;
        var depValue = depProp.GetValue(instance);
        Assert.NotNull(depValue);
        var valueProp = depValue!.GetType().GetProperty("Value")!;
        var value = (string)valueProp.GetValue(depValue)!;
        Assert.Equal("injected", value);
    }

    // ---------------------------------------------------------------
    // 2. Optional [Inject] property is null when service not registered
    // ---------------------------------------------------------------
    [Fact]
    public void InjectProperty_OptionalNotRegistered_IsNull()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IOptDep { }
            public interface IMyService { }
            [Transient]
            public class MyService : IMyService
            {
                [Inject(Required = false)]
                public IOptDep? Opt { get; set; }
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var myServiceType = assembly.GetType("TestApp.IMyService")!;

        var instance = provider.GetService(myServiceType);

        Assert.NotNull(instance);
        var optProp = instance!.GetType().GetProperty("Opt")!;
        var optValue = optProp.GetValue(instance);
        Assert.Null(optValue);
    }

    // ---------------------------------------------------------------
    // Helpers (copied from IntegrationTests)
    // ---------------------------------------------------------------

    private static (Assembly assembly, IServiceProvider provider) BuildAndCreateProvider(
        string source,
        Action<IServiceCollection>? configureServices = null)
    {
        var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);

        var genErrors = new List<Diagnostic>();
        foreach (var d in diagnostics) { if (d.Severity == DiagnosticSeverity.Error) genErrors.Add(d); }
        if (genErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Generator produced errors:\n" + string.Join("\n", genErrors.Select(e => e.ToString())));
        }

        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = new List<string>();
            foreach (var d in emitResult.Diagnostics)
                { if (d.Severity == DiagnosticSeverity.Error) errors.Add(d.ToString()); }
            throw new InvalidOperationException(
                "Compilation failed:\n" + string.Join("\n", errors));
        }

        ms.Seek(0, SeekOrigin.Begin);

        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        var extensionClass = assembly.GetTypes()
            .First(static t => string.Equals(t.Name, "ZeroAllocInjectServiceCollectionExtensions", StringComparison.Ordinal));
        var buildMethod = extensionClass.GetMethod(
            "BuildZeroAllocInjectServiceProvider",
            BindingFlags.Public | BindingFlags.Static);

        var services = new ServiceCollection();
        configureServices?.Invoke(services);

        var provider = (IServiceProvider)buildMethod!.Invoke(null, [services])!;
        return (assembly, provider);
    }

    private static (Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics) RunGeneratorAndGetCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var extraAssemblies = new[]
        {
            typeof(TransientAttribute).Assembly,
            typeof(ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase).Assembly,
            typeof(ServiceCollectionContainerBuilderExtensions).Assembly,
            typeof(ServiceCollection).Assembly,
        };
        var existingLocations = new HashSet<string>(
            references.Select(static r => r.Display ?? ""), StringComparer.Ordinal);
        foreach (var asm in extraAssemblies)
        {
            if (existingLocations.Add(asm.Location))
            {
                references.Add(MetadataReference.CreateFromFile(asm.Location));
            }
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZeroAllocInjectGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation, diagnostics);
    }
}
