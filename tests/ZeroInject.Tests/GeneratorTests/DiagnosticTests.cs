using Microsoft.CodeAnalysis;

namespace ZeroInject.Tests.GeneratorTests;

public class DiagnosticTests
{
    [Fact]
    public void NoPublicConstructor_ProducesZI006()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class NoCtorService
            {
                private NoCtorService() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "ZI006");
    }

    [Fact]
    public void NoInterfaces_ProducesZI007()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "ZI007");
    }

    [Fact]
    public void WithInterfaces_DoesNotProduceZI007()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "ZI007");
    }

    [Fact]
    public void WithPublicConstructor_DoesNotProduceZI006()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class GoodService
            {
                public GoodService() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "ZI006");
    }
}
