namespace ZInject;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ZInjectAttribute : Attribute
{
    public string MethodName { get; }

    public ZInjectAttribute(string methodName)
    {
        MethodName = methodName;
    }
}
