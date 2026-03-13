using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public readonly struct OpenGenericEntry
{
    public Type ImplType { get; }
    public ServiceLifetime Lifetime { get; }
    public Type? DecoratorImplType { get; }

    public OpenGenericEntry(Type implType, ServiceLifetime lifetime, Type? decoratorImplType = null)
    {
        ImplType = implType;
        Lifetime = lifetime;
        DecoratorImplType = decoratorImplType;
    }
}
