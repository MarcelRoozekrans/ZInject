using ZeroInject;

namespace ZeroInject.Sample;

public interface IGreetingService
{
    string Greet(string name);
}

public interface ICache
{
    string Get(string key);
}

[Transient]
public class GreetingService : IGreetingService
{
    public string Greet(string name) => $"Hello, {name}!";
}

[Singleton(Key = "memory")]
public class MemoryCache : ICache
{
    public string Get(string key) => $"cached:{key}";
}

[Scoped]
public class ScopedWorker
{
    public string DoWork() => "Working...";
}
