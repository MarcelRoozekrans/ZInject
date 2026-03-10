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
}
