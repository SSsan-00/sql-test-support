using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    public sealed class SqlAstFingerprinter
    {
        private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal)
        {
            nameof(TSqlFragment.StartLine),
            nameof(TSqlFragment.StartColumn),
            nameof(TSqlFragment.StartOffset),
            nameof(TSqlFragment.FragmentLength),
            nameof(TSqlFragment.FirstTokenIndex),
            nameof(TSqlFragment.LastTokenIndex),
            nameof(TSqlFragment.ScriptTokenStream)
        };

        public string CreateFingerprint(TSqlFragment fragment)
        {
            ArgumentNullException.ThrowIfNull(fragment);

            var builder = new StringBuilder();
            WriteValue(builder, fragment, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(bytes);
        }

        private static void WriteValue(
            StringBuilder builder,
            object? value,
            ISet<object> visited,
            int depth)
        {
            if (value is null)
            {
                builder.Append("<null>");
                return;
            }

            if (depth > 128)
            {
                builder.Append("<max-depth>");
                return;
            }

            switch (value)
            {
                case string text:
                    builder.Append('"').Append(text).Append('"');
                    return;
                case char character:
                    builder.Append('\'').Append(character).Append('\'');
                    return;
                case bool boolean:
                    builder.Append(boolean ? "true" : "false");
                    return;
                case Enum enumValue:
                    builder.Append(value.GetType().FullName).Append('.').Append(enumValue);
                    return;
                case IFormattable formattable when IsScalar(value.GetType()):
                    builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                    return;
            }

            if (value is TSqlParserToken)
            {
                builder.Append("<token>");
                return;
            }

            if (!value.GetType().IsValueType && !visited.Add(value))
            {
                builder.Append("<cycle>");
                return;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                builder.Append('[');
                var index = 0;
                foreach (var item in enumerable)
                {
                    if (index++ > 0)
                    {
                        builder.Append(',');
                    }

                    WriteValue(builder, item, visited, depth + 1);
                }

                builder.Append(']');
                return;
            }

            var type = value.GetType();
            builder.Append(type.FullName).Append('{');

            foreach (var property in GetFingerprintProperties(type))
            {
                builder.Append(property.Name).Append('=');
                WriteValue(builder, property.GetValue(value), visited, depth + 1);
                builder.Append(';');
            }

            builder.Append('}');
        }

        private static IReadOnlyList<PropertyInfo> GetFingerprintProperties(Type type)
            => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.GetMethod is not null &&
                    property.GetIndexParameters().Length == 0 &&
                    !ExcludedProperties.Contains(property.Name))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();

        private static bool IsScalar(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsPrimitive ||
                   underlying == typeof(decimal) ||
                   underlying == typeof(DateTime) ||
                   underlying == typeof(DateTimeOffset) ||
                   underlying == typeof(Guid);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object? x, object? y)
                => ReferenceEquals(x, y);

            public int GetHashCode(object obj)
                => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
