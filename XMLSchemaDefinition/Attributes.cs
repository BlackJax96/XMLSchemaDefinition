using System;

namespace XMLSchemaDefinition
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ElementName : Attribute
    {
        public string Name { get; private set; }
        public string Version { get; private set; }
        public bool CaseSensitive { get; set; }
        public ElementName(string elementName, string version = "1.*.*")
        {
            Name = elementName;
            Version = version;
        }
        /// <summary>
        /// Returns true if the version of this element matches the provided schema version.
        /// Use * for wildcard chars.
        /// </summary>
        public bool VersionMatches(string version)
        {
            if (version == null)
                return true;
            
            string elemVer = Version;
            for (int i = 0; i < elemVer.Length; ++i)
            {
                char thisChar = elemVer[i];
                char thatChar = version[i];
                if (thisChar != thatChar && thisChar != '*' && thatChar != '*')
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Returns true if the provided element name and version match this element.
        /// </summary>
        /// <param name="elementName">The name of the element.</param>
        /// <param name="version">The version of the schema this element might be included in. Use * for wildcard chars.</param>
        public bool Matches(string elementName, string version)
        {
            bool nameMatch = string.Equals(Name, elementName,
                CaseSensitive ?
                StringComparison.InvariantCulture :
                StringComparison.InvariantCultureIgnoreCase);
            bool versionMatch = VersionMatches(version);
            return nameMatch && versionMatch;
        }
    }

    /// <summary>
    /// Defines a property or field as a representation of an XML attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class Attr : Attribute
    {
        public string AttributeName { get; private set; }
        public bool Required { get; private set; }
        public Attr(string attributeName, bool required)
        {
            AttributeName = attributeName;
            Required = required;
        }
    }

    /// <summary>
    /// Declares a child element this element class may contain.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class Child : Attribute
    {
        public Type ChildEntryType { get; private set; }
        public int MinCount { get; private set; }
        public int MaxCount { get; private set; }

        public Child(Type childEntryType, int requiredCount)
        {
            ChildEntryType = childEntryType;
            MaxCount = MinCount = requiredCount;
        }
        public Child(Type childEntryType, int minCount, int maxCount)
        {
            ChildEntryType = childEntryType;
            MinCount = minCount;
            MaxCount = maxCount;
        }
    }

    /// <summary>
    /// Declares a child element that this element class may contain but the scheme definition does not support yet.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class UnsupportedChild : Attribute
    {
        public string ElementName { get; private set; }
        public UnsupportedChild(string elementName)
            => ElementName = elementName;
    }

    public enum EMultiChildType
    {
        /// <summary>
        /// Only one appearance of one of the given types.
        /// </summary>
        OneOfOne,

        /// <summary>
        /// At least one appearance of any of the given types.
        /// Any combinations of types may appear.
        /// </summary>
        AtLeastOneOfAny,

        /// <summary>
        /// Each type must appear at least once.
        /// </summary>
        AtLeastOneOfAll,

        /// <summary>
        /// One or more appearance of only one of the given types.
        /// </summary>
        AtLeastOneOfOne,

        /// <summary>
        /// Zero or more appearance of only one of the given types.
        /// </summary>
        AnyAmountOfOne,
    }

    /// <summary>
    /// Specifies that at least one child element of the specifies types needs to exist.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class MultiChild : Attribute
    {
        public EMultiChildType Selection { get; private set; }
        public Type[] Types { get; private set; }
        public MultiChild(EMultiChildType selection, params Type[] types)
        {
            Types = types;
            Selection = selection;
        }
    }
}