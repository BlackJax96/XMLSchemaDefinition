using System;
using System.Linq;

namespace XMLSchemaDefinition
{
    public abstract class BaseElementString : IParsable
    {
        public abstract void ReadFromString(string str);
        public abstract string WriteToString();
    }

    /// <summary>
    /// Use for all primitive types such as <see cref="Enum"/>, <see cref="int"/>, <see cref="bool"/>, etc. Also supports <see cref="IParsable"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class StringPrimitive<T> : BaseElementString where T : struct
    {
        public T Value { get; set; }
        public override void ReadFromString(string str)
            => Value = str.ParseAs<T>();
        public override string WriteToString()
            => Value.ToString();
    }

    public class StringParsable<T> : BaseElementString where T : IParsable
    {
        //This has to be a field so that ReadFromString works properly
        private T _value = default;

        public T Value { get => _value; set => _value = value; }

        public override void ReadFromString(string str)
        {
            _value = Activator.CreateInstance<T>();
            _value.ReadFromString(str);
        }
        public override string WriteToString()
            => _value.WriteToString();
    }

    public class ElementHex : BaseElementString
    {
        private const string Valid = "0123456789ABCDEFabcdef";

        public ElementHex() { }
        public ElementHex(byte[] values) => Values = values;

        public byte[] Values { get; set; }

        public override void ReadFromString(string str)
            => Values = str.
            Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).
            Where(x => Valid.Contains(x)).
            Select(x => byte.Parse(x)).
            ToArray();

        public override string WriteToString()
            => string.Join(" ", Values);

        public static implicit operator ElementHex(byte[] values)
            => new ElementHex(values);
        public static implicit operator byte[] (ElementHex value)
            => value.Values;
    }

    public class ElementString : BaseElementString
    {
        public ElementString() { }
        public ElementString(string value) => Value = value;

        public string Value { get; set; }
        public override void ReadFromString(string str)
            => Value = str;
        public override string WriteToString()
            => Value;

        public static implicit operator ElementString(string value)
            => new ElementString(value);
        public static implicit operator string(ElementString value)
            => value.Value;
    }
}