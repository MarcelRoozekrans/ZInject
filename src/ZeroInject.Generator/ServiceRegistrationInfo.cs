#nullable enable
using System;
using System.Collections.Generic;

namespace ZeroInject.Generator
{
    internal sealed class ServiceRegistrationInfo : IEquatable<ServiceRegistrationInfo>
    {
        public string Namespace { get; }
        public string TypeName { get; }
        public string FullyQualifiedName { get; }
        public string Lifetime { get; }
        public List<string> Interfaces { get; }
        public string? AsType { get; }
        public string? Key { get; }
        public bool AllowMultiple { get; }
        public bool IsOpenGeneric { get; }
        public string? OpenGenericArity { get; }

        public ServiceRegistrationInfo(
            string ns,
            string typeName,
            string fullyQualifiedName,
            string lifetime,
            List<string> interfaces,
            string? asType,
            string? key,
            bool allowMultiple,
            bool isOpenGeneric,
            string? openGenericArity)
        {
            Namespace = ns;
            TypeName = typeName;
            FullyQualifiedName = fullyQualifiedName;
            Lifetime = lifetime;
            Interfaces = interfaces;
            AsType = asType;
            Key = key;
            AllowMultiple = allowMultiple;
            IsOpenGeneric = isOpenGeneric;
            OpenGenericArity = openGenericArity;
        }

        public bool Equals(ServiceRegistrationInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedName == other.FullyQualifiedName
                && Lifetime == other.Lifetime
                && AsType == other.AsType
                && Key == other.Key
                && AllowMultiple == other.AllowMultiple;
        }

        public override bool Equals(object? obj) => Equals(obj as ServiceRegistrationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedName.GetHashCode();
                hash = hash * 31 + Lifetime.GetHashCode();
                return hash;
            }
        }
    }
}
