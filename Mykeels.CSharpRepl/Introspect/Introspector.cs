using System.Reflection;
using System.Collections.Generic;

namespace Mykeels.CSharpRepl;

public static class Introspector
{
    private static readonly Dictionary<Type, string> TypeKeywordMap = new()
    {
        { typeof(string), "string" },
        { typeof(int), "int" },
        { typeof(bool), "bool" },
        { typeof(double), "double" },
        { typeof(float), "float" },
        { typeof(decimal), "decimal" },
        { typeof(object), "object" },
        { typeof(void), "void" },
        { typeof(char), "char" },
        { typeof(byte), "byte" },
        { typeof(sbyte), "sbyte" },
        { typeof(short), "short" },
        { typeof(ushort), "ushort" },
        { typeof(uint), "uint" },
        { typeof(long), "long" },
        { typeof(ulong), "ulong" }
    };

    private static string GetTypeName(Type type)
    {
        if (TypeKeywordMap.TryGetValue(type, out var keyword))
            return keyword;

        if (type.IsGenericType)
        {
            var genericTypeName = type.GetGenericTypeDefinition().Name;
            // Remove the `1, `2, etc. from the name
            var unmangledName = genericTypeName.Contains('`')
                ? genericTypeName.Substring(0, genericTypeName.IndexOf('`'))
                : genericTypeName;
            var genericArgs = type.GetGenericArguments().Select(GetTypeName);
            return $"{type.Namespace}.{unmangledName}<{string.Join(", ", genericArgs)}>";
        }

        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Returns a list of type signatures for static methods and properties of the given type.
    /// Each type signature is a string in the format "methodName(Type1 parameterType1, Type2 parameterType2, ...)" or "Type propertyName".
    /// </summary>
    /// <param name="type">The type to introspect.</param>
    /// <returns>A list of type signatures for static methods and properties of the given type.</returns>
    public static List<string> ListComponents(Type type)
    {
        var components = new List<string>();
        
        var methods = type.GetMethods().Where(m => !m.IsSpecialName);
        var properties = type.GetProperties();
        
        return components
            .Concat(methods.Select(GetMethodSignature))
            .Concat(properties.Select(GetPropertySignature))
            .ToList();
    }

    private static string GetMethodSignature(MethodInfo method)
    {
        string returnType = GetTypeName(method.ReturnType);
        var parameters = method.GetParameters().Select(p => $"{GetTypeName(p.ParameterType)} {p.Name}").ToList();
        bool isProperty = method.Name.StartsWith("get_") && !parameters.Any();

        // Detect async methods
        bool isAsync = method.ReturnType == typeof(Task)
            || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            || method.ReturnType == typeof(ValueTask)
            || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>));

        string prefix = isAsync ? "async " : "";

        return isProperty
            ? $"{returnType} {method.Name}"
            : $"{prefix}{returnType} {method.Name}({string.Join(", ", parameters)})";
    }

    private static string GetPropertySignature(PropertyInfo property)
    {
        return $"{GetTypeName(property.PropertyType)} {property.Name}";
    }
}