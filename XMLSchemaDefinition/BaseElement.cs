using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace XMLSchemaDefinition
{
    public abstract class BaseStringElement<TParent, TString> : BaseElement<TParent>, IStringElement
        where TParent : class, IElement
        where TString : BaseElementString
    {
        public TString StringContent { get; set; }

        [Browsable(false)]
        public BaseElementString GenericStringContent
        {
            get => StringContent;
            set => StringContent = value as TString;
        }

        [Browsable(false)]
        public Type GenericStringType => typeof(TString);
    }

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="TParent">The type of the parent element.</typeparam>
    public abstract class BaseElement<TParent> : IElement where TParent : class, IElement
    {
        [Browsable(false)]
        public virtual ulong TypeFlag => 0;

        [Browsable(false)]
        public int ElementIndex { get; set; } = -1;

        [Browsable(false)]
        public string Tree { get; set; }

        private string _elementName;

        [Browsable(false)]
        public string ElementName
        {
            get
            {
                if (Attribute.GetCustomAttribute(GetType(), typeof(ElementName)) is ElementName name && name.Name != null)
                    return name.Name;

                return _elementName;
            }
            set
            {
                if (Attribute.GetCustomAttribute(GetType(), typeof(ElementName)) is ElementName name && name.Name == null)
                    _elementName = value;
            }
        }

        /// <summary>
        /// User-specified information for this element.
        /// </summary>
        [Browsable(false)]
        public object UserData { get; set; }

        /// <summary>
        /// Reference to the root element.
        /// </summary>
        [Browsable(false)]
        public IRootElement RootElement { get; private set; }

        IElement IElement.Parent
        {
            get => Parent;
            set
            {
                Parent = value as TParent;
                if (Parent != null)
                {
                    Type type = GetType();
                    if (Parent.ChildElements.ContainsKey(type))
                        Parent.ChildElements[type].Add(this);
                    else
                        Parent.ChildElements.Add(type, new List<IElement>() { this });

                    if (Parent is IRootElement c)
                        RootElement = c;
                    else if (Parent.RootElement != null)
                        RootElement = Parent.RootElement;

                    if (RootElement == null)
                        throw new Exception($"Generic root is null. Make sure the root element implements the {nameof(IRootElement)} interface.");
                }
            }
        }

        /// <summary>
        /// Reference to the parent element.
        /// </summary>
        [Browsable(false)]
        public TParent Parent { get; private set; }

        public void ClearChildElements() => ChildElements.Clear();

        public T2 GetChild<T2>() where T2 : IElement
        {
            T2[] array = GetChildren<T2>();
            if (array.Length > 0)
                return array[0];

            return default;
        }
        public T2[] GetChildren<T2>() where T2 : IElement
        {
            List<T2> elems = new List<T2>();
            Type t = typeof(T2);
            while (t != null)
            {
                if (t == typeof(object) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BaseElement<>)))
                    break;
                
                Type[] matchingParsed = ChildElements.Keys.Where(x => t.IsAssignableFrom(x)).ToArray();
                foreach (Type match in matchingParsed)
                {
                    IEnumerable<T2> matchElems = ChildElements[match].Where(x => x is T2).Select(x => (T2)x);
                    foreach (T2 m in matchElems)
                        if (!elems.Contains(m))
                            elems.Add(m);
                }
                t = t.BaseType;
            }
            return elems.ToArray();
        }

        [Browsable(false)]
        protected Dictionary<Type, List<IElement>> ChildElements { get; } = new Dictionary<Type, List<IElement>>();

        /// <summary>
        /// The expected type of the parent element.
        /// </summary>
        [Browsable(false)]
        public Type ParentType => typeof(TParent);

        /// <summary>
        /// If true, calls <see cref="ManualReadAttribute"/>
        /// and <see cref="ManualReadChildElement(string, string)"/>
        /// for you to read attributes and child elements manually.
        /// </summary>
        protected virtual bool WantsManualRead => false;

        /// <summary>
        /// Called before the element is read.
        /// Can be used for parsing preparation.
        /// </summary>
        protected virtual void PreRead() { }
        /// <summary>
        /// Called after the element is read.
        /// Can be used to transform parsed data.
        /// </summary>
        protected virtual void PostRead() { }
        /// <summary>
        /// Called after all attributes have been read and before any child elements are read.
        /// </summary>
        protected virtual void OnAttributesRead() { }
        /// <summary>
        /// Sets an attribute by name. Must be overridden when <see cref="WantsManualRead"/> is true.
        /// </summary>
        /// <param name="name">The name of the attribute to set.</param>
        /// <param name="value">The value to set the attribute to.</param>
        protected virtual void ManualReadAttribute(string name, string value) { }

        /// <summary>
        /// All child elements in order of appearance in the file.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<IElement> ChildElementsInOrder()
            => ChildElements.Values.SelectMany(x => x).OrderBy(x => x.ElementIndex);
        /// <summary>
        /// Reads a child element by name. Must be overridden when <see cref="WantsManualRead"/> is true.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        protected virtual IElement ManualReadChildElement(string name, string version) => null;

        /// <summary>
        /// How many child elements this element contains within its start and end tags.
        /// </summary>
        public int ChildElementCount => ChildElements.Values.Sum(a => a.Count);

        /// <summary>
        /// Adds one or more elements as children of this one.
        /// </summary>
        /// <param name="elements"></param>
        public void AddChildElements(params IElement[] elements)
        {
            if (elements == null || elements.Length == 0)
                return;
            
            Type elementType = GetType();

            Child[] childAttribs = elementType.GetCustomAttributesExt<Child>();
            MultiChild[] multiChildAttribs = elementType.GetCustomAttributesExt<MultiChild>();

            elements = elements.Where(elem => childAttribs.Any(attrib => attrib.ChildEntryType.IsAssignableFrom(elem.GetType()))).ToArray();
            int currentCount = ChildElementCount;

            for (int i = 0; i < elements.Length; ++i)
            {
                IElement element = elements[i];
                element.ElementIndex = currentCount + i;
                Type elemType = element.GetType();

                if (!ChildElements.ContainsKey(elemType))
                    ChildElements.Add(elemType, new List<IElement>() { element });
                else
                {
                    List<IElement> list = ChildElements[elemType];
                    if (list == null)
                    {
                        list = new List<IElement>();
                        ChildElements[elemType] = list;
                    }
                    list.Add(element);
                }
            }
        }
        /// <summary>
        /// Removes one or more child elements.
        /// </summary>
        /// <param name="elements"></param>
        public void RemoveChildElements(params IElement[] elements)
        {
            if (elements == null || elements.Length == 0)
                return;

            Type t = GetType();
            for (int i = 0; i < elements.Length; ++i)
            {
                IElement element = elements[i];
                Type elemType = element.GetType();
                if (ChildElements.ContainsKey(elemType))
                {
                    List<IElement> list = ChildElements[elemType];
                    if (list != null)
                    {
                        list.Remove(element);
                        if (list.Count == 0)
                            ChildElements.Remove(elemType);
                    }
                }
            }
        }

        #region IElement Implementation

        Dictionary<Type, List<IElement>> IElement.ChildElements => ChildElements;
        bool IElement.WantsManualRead => WantsManualRead;
        void IElement.PreRead() => PreRead();
        void IElement.PostRead() => PostRead();
        void IElement.OnAttributesRead() => OnAttributesRead();
        void IElement.ManualReadAttribute(string name, string value) => ManualReadAttribute(name, value);
        IElement IElement.ManualReadChildElement(string name, string version) => ManualReadChildElement(name, version);

        #endregion
    }
}