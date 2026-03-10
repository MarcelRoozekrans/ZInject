namespace ZeroInject.Tests.GeneratorTests;

public class FactoryRegistrationTests
{
    [Fact]
    public void ParameterlessConstructor_GeneratesFactoryLambda()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp => new global::TestApp.MyService()", output);
    }

    [Fact]
    public void SingleParameter_GeneratesGetRequiredService()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(ILogger logger) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp => new global::TestApp.MyService(", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
    }

    [Fact]
    public void OptionalParameter_GeneratesGetService()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(ILogger? logger = null) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp.GetService<global::TestApp.ILogger>()", output);
        Assert.DoesNotContain("GetRequiredService", output);
    }

    [Fact]
    public void MultipleConstructors_WithoutAttribute_ProducesZI009()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService() { }
                public MyService(int x) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "ZI009");
    }

    [Fact]
    public void PrimitiveParameter_String_ProducesZI010()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(string name) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "ZI010");
    }

    [Fact]
    public void PrimitiveParameter_Int_ProducesZI010()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(int count) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "ZI010");
    }

    [Fact]
    public void ConcreteOnly_GeneratesFactoryLambda()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp => new global::TestApp.PlainService()", output);
    }

    [Fact]
    public void OpenGeneric_StillUsesServiceDescriptor()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IRepository<T> { }

            [Scoped]
            public class Repository<T> : IRepository<T> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("ServiceDescriptor.Scoped(typeof(global::TestApp.IRepository<>), typeof(global::TestApp.Repository<>))", output);
        Assert.DoesNotContain("sp =>", output);
    }

    [Fact]
    public void MultipleParameters_GeneratesAllGetRequiredService()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }
            public interface IRepo { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(IRepo repo, ILogger logger) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
    }

    [Fact]
    public void MixedRequiredAndOptionalParameters()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }
            public interface IRepo { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(IRepo repo, ILogger? logger = null) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
        Assert.Contains("sp.GetService<global::TestApp.ILogger>()", output);
    }

    [Fact]
    public void KeyedService_GeneratesKeyedFactoryLambda()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("(sp, _) => new global::TestApp.RedisCache()", output);
        Assert.Contains("\"redis\"", output);
    }

    [Fact]
    public void KeyedService_WithParameters_GeneratesKeyedFactoryLambda()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface ICache { }
            public interface ISerializer { }

            [Singleton(Key = "redis")]
            public class RedisCache : ICache
            {
                public RedisCache(ISerializer serializer) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("(sp, _) => new global::TestApp.RedisCache(", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ISerializer>()", output);
    }
}
