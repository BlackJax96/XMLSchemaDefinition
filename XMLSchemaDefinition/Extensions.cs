using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace XMLSchemaDefinition
{
    public static class Extensions
    {
        private static readonly Dictionary<Type, string> DefaultDictionary = new Dictionary<Type, string>
        {
            { typeof(void),     "void"      },
            { typeof(char),     "char"      },
            { typeof(bool),     "bool"      },
            { typeof(byte),     "byte"      },
            { typeof(sbyte),    "sbyte"     },
            { typeof(short),    "short"     },
            { typeof(ushort),   "ushort"    },
            { typeof(int),      "int"       },
            { typeof(uint),     "uint"      },
            { typeof(long),     "long"      },
            { typeof(ulong),    "ulong"     },
            { typeof(float),    "float"     },
            { typeof(double),   "double"    },
            { typeof(decimal),  "decimal"   },
            { typeof(string),   "string"    },
            { typeof(object),   "object"    },
        };

        /// <summary>
        /// Returns the type as a string in the form that it is written in code.
        /// </summary>
        public static string GetFriendlyName(this Type type, string openBracket = "<", string closeBracket = ">")
            => type.GetFriendlyName(DefaultDictionary, openBracket, closeBracket);
        /// <summary>
        /// Returns the type as a string in the form that it is written in code.
        /// </summary>
        public static string GetFriendlyName(this Type type, Dictionary<Type, string> translations, string openBracket = "<", string closeBracket = ">")
        {
            if (type == null)
                return "null";
            
            if (translations.ContainsKey(type))
                return translations[type];
            
            if (type.IsArray)
                return GetFriendlyName(type.GetElementType(), translations, openBracket, closeBracket) + "[]";
            
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                return type.GetGenericArguments()[0].GetFriendlyName() + "?";
            
            if (type.IsGenericType)
                return type.Name.Split('`')[0] + openBracket + string.Join(", ", type.GetGenericArguments().Select(x => GetFriendlyName(x))) + closeBracket;

            return type.Name;
        }
        public static object GetDefaultValue(this Type t)
            => t == null ? null : (t.IsValueType && Nullable.GetUnderlyingType(t) == null) ? Activator.CreateInstance(t) : null;
        public static T GetCustomAttributeExt<T>(this Type type) where T : Attribute
        {
            T[] types = type.GetCustomAttributesExt<T>();
            if (types.Length > 0)
                return types[0];
            
            return null;
        }
        public static T[] GetCustomAttributesExt<T>(this Type type) where T : Attribute
        {
            List<T> list = new List<T>();
            while (type != null)
            {
                list.AddRange(type.GetCustomAttributes<T>());
                type = type.BaseType;
            }
            return list.ToArray();
        }
        public static T2 ParseAs<T2>(this string value)
           => (T2)ParseAs(value, typeof(T2));
        public static object ParseAs(this string value, Type t)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (string.IsNullOrWhiteSpace(value))
                    return null;
                else
                    return ParseAs(value, t.GetGenericArguments()[0]);
            }

            if (t.GetInterface(nameof(IParsable)) != null)
            {
                IParsable o = (IParsable)Activator.CreateInstance(t);
                o.ReadFromString(value);
                return o;
            }

            if (string.Equals(t.BaseType.Name, nameof(Enum), StringComparison.InvariantCulture))
                return Enum.Parse(t, value);
            
            switch (t.Name)
            {
                case nameof(Boolean): return bool.Parse(value);
                case nameof(SByte): return sbyte.Parse(value);
                case nameof(Byte): return byte.Parse(value);
                case nameof(Char): return char.Parse(value);
                case nameof(Int16): return short.Parse(value);
                case nameof(UInt16): return ushort.Parse(value);
                case nameof(Int32): return int.Parse(value);
                case nameof(UInt32): return uint.Parse(value);
                case nameof(Int64): return long.Parse(value);
                case nameof(UInt64): return ulong.Parse(value);
                case nameof(Single): return float.Parse(value);
                case nameof(Double): return double.Parse(value);
                case nameof(Decimal): return decimal.Parse(value);
                case nameof(String): return value;
            }
            throw new InvalidOperationException($"{t.GetFriendlyName()} is not parsable");
        }
    }
}