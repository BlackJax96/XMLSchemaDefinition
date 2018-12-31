using System;
using System.Collections.Generic;

namespace XMLSchemaDefinition
{
    /// <summary>
    /// Indicates that this class or struct can be written to a string and read back.
    /// </summary>
    public interface IParsable
    {
        string WriteToString();
        void ReadFromString(string str);
    }

    /// <summary>
    /// Used to set the version for an element class.
    /// If the XML document's version does not fit the criteria, the element definition is ignored.
    /// </summary>
    public interface IVersion
    {
        /// <summary>
        /// The version of the element.
        /// Version must match exactly, but * can be used as a wildcard to ignore matching a specific char.
        /// </summary>
        string Version { get; set; }
    }

    /// <summary>
    /// Specifies that this element contains a string between its open and close tags.
    /// </summary>
    public interface IStringElement : IElement
    {
        BaseElementString GenericStringContent { get; set; }
        Type GenericStringType { get; }
    }

    /// <summary>
    /// Base interface for all elements.
    /// Use to access common elements despite the element's defined parent in its class name's generic parameters.
    /// </summary>
    public interface IElement
    {
        ulong TypeFlag { get; }
        string ElementName { get; set; }
        Type ParentType { get; }
        bool WantsManualRead { get; }
        object UserData { get; set; }
        IElement Parent { get; set; }
        IRootElement RootElement { get; }
        Dictionary<Type, List<IElement>> ChildElements { get; }
        int ElementIndex { get; set; }
        string Tree { get; set; }

        T2 GetChild<T2>() where T2 : IElement;
        T2[] GetChildren<T2>() where T2 : IElement;
        void PreRead();
        void PostRead();
        void OnAttributesRead();
        void ManualReadAttribute(string name, string value);
        IElement ManualReadChildElement(string name, string version);
        /// <summary>
        /// Returns child elements in the same order they appear in the file.
        /// </summary>
        IEnumerable<IElement> ChildElementsInOrder();
    }

    /// <summary>
    /// The root XML element.
    /// </summary>
    public interface IRootElement : IElement
    {

    }
}