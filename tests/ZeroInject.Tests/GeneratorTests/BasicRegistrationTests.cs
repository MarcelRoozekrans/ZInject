namespace ZeroInject.Tests.GeneratorTests;

public class BasicRegistrationTests
{
    [Fact]
    public void NoAttributedClasses_GeneratesNothing()
    {
        var source = """
            namespace TestApp;
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("Error", diagnostics.Select(d => d.Severity.ToString()));
    }

    [Fact]
    public void TransientAttribute_GeneratesTryAddTransient()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("Error", diagnostics.Select(d => d.Severity.ToString()));
        Assert.Contains("TryAddTransient<global::TestApp.IMyService, global::TestApp.MyService>", output);
        Assert.Contains("TryAddTransient<global::TestApp.MyService>", output);
    }

    [Fact]
    public void ScopedAttribute_GeneratesTryAddScoped()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddScoped<global::TestApp.IRepo, global::TestApp.Repo>", output);
        Assert.Contains("TryAddScoped<global::TestApp.Repo>", output);
    }

    [Fact]
    public void SingletonAttribute_GeneratesTryAddSingleton()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddSingleton<global::TestApp.ICache, global::TestApp.Cache>", output);
        Assert.Contains("TryAddSingleton<global::TestApp.Cache>", output);
    }

    [Fact]
    public void ConcreteOnly_RegistersConcreteType()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.PlainService>", output);
    }

    [Fact]
    public void FilteredInterfaces_AreExcluded()
    {
        var source = """
            using ZeroInject;
            using System;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService, IDisposable
            {
                public void Dispose() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IMyService, global::TestApp.MyService>", output);
        Assert.DoesNotContain("IDisposable", output);
    }

    [Fact]
    public void GeneratedMethod_ReturnsIServiceCollection()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("public static IServiceCollection", output);
        Assert.Contains("return services;", output);
    }
}
