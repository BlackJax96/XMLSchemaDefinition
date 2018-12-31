using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace XMLSchemaDefinition
{
    public partial class XMLSchemaDefinition<T> : BaseXMLSchemaDefinition where T : class, IElement
    {
        public XMLSchemaDefinition() { }

        public static XmlReaderSettings GetDefaultReaderSettings() => new XmlReaderSettings()
        {
            ConformanceLevel = ConformanceLevel.Document,
            DtdProcessing = DtdProcessing.Ignore,
            CheckCharacters = false,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = true,
        };

        #region Async

        /// <summary>
        /// Reads a file with root class T asynchronously and returns a new instance.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        /// <param name="ignoreFlags">Flags that are bitwise masked against each BaseElement's TypeFlag. If matching, the element is skipped.</param>
        /// <returns>A new instance of class T.</returns>
        public async Task<T> ImportAsync(
            string path,
            ulong ignoreFlags)
            => await ImportAsync(path, ignoreFlags, null, CancellationToken.None);
        /// <summary>
        /// Reads a file with root class T asynchronously and returns a new instance.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        /// <param name="ignoreFlags">Flags that are bitwise masked against each BaseElement's TypeFlag. If matching, the element is skipped.</param>
        /// <param name="settings">The settings for reading an XML file.</param>
        /// <returns>A new instance of class T.</returns>
        public async Task<T> ImportAsync(
            string path,
            ulong ignoreFlags,
            XmlReaderSettings settings)
            => await ImportAsync(path, ignoreFlags, settings, null, CancellationToken.None);
        /// <summary>
        /// Reads a file with root class T asynchronously and returns a new instance.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        /// <param name="ignoreFlags">Flags that are bitwise masked against each BaseElement's TypeFlag. If matching, the element is skipped.</param>
        /// <param name="progress">Handler for progress as the file is read.</param>
        /// <param name="cancel">Handler for if the user wishes to cancel the import.</param>
        /// <returns>A new instance of class T.</returns>
        public async Task<T> ImportAsync(
            string path,
            ulong ignoreFlags,
            IProgress<float> progress,
            CancellationToken cancel)
            => await ImportAsync(path, ignoreFlags, null, progress, cancel);
        /// <summary>
        /// Reads a file with root class T asynchronously and returns a new instance.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        /// <param name="ignoreFlags">Flags that are bitwise masked against each BaseElement's TypeFlag. If matching, the element is skipped.</param>
        /// <param name="settings">The settings for reading an XML file.</param>
        /// <param name="progress">Handler for progress as the file is read.</param>
        /// <param name="cancel">Handler for if the user wishes to cancel the import.</param>
        /// <returns>A new instance of class T.</returns>
        public async Task<T> ImportAsync(
            string path,
            ulong ignoreFlags,
            XmlReaderSettings settings,
            IProgress<float> progress,
            CancellationToken cancel)
        {
            if (settings == null)
                settings = GetDefaultReaderSettings();

            settings.Async = true;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                string previousTree = "";
                Type rootType = typeof(T);
                ElementName name = rootType.GetCustomAttribute<ElementName>();
                if (name == null || string.IsNullOrEmpty(name.Name))
                {
                    WriteLine($"{rootType.GetFriendlyName()} has no '{nameof(ElementName)}' attribute.");
                    return null;
                }

                bool found;
                while (!(found = await reader.MoveToContentAsync() == XmlNodeType.Element &&
                    string.Equals(name.Name, reader.Name, StringComparison.InvariantCulture))) ;
                
                if (found)
                {
                    IElement e = await ParseElementAsync(rootType, null, reader, null, ignoreFlags, previousTree, 0, stream, progress, cancel);
                    progress?.Report(1.0f);
                    return e as T;
                }

                progress?.Report(1.0f);
                return null;
            }
        }

        private async Task<IElement> ParseElementAsync(
            Type elementType,
            IElement parent,
            XmlReader reader,
            string version,
            ulong ignoreFlags,
            string parentTree,
            int elementIndex,
            FileStream stream,
            IProgress<float> progress,
            CancellationToken cancel)
            => await ParseElementAsync(Activator.CreateInstance(elementType) as IElement,
                parent, reader, version, ignoreFlags, parentTree, elementIndex, stream, progress, cancel);

        private async Task<IElement> ParseElementAsync(
            IElement entry,
            IElement parent,
            XmlReader reader,
            string version,
            ulong ignoreFlags,
            string parentTree,
            int elementIndex,
            FileStream stream,
            IProgress<float> progress,
            CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
                return null;
            else
                progress?.Report((float)stream.Position / stream.Length);
            
            Type elementType = entry.GetType();
            entry.Parent = parent;
            entry.ElementIndex = elementIndex;
            entry.PreRead();

            string parentElementName = reader.Name;
            if (string.IsNullOrEmpty(parentElementName))
                throw new Exception("Null parent element name.");
            
            parentTree += parentElementName + "/";
            entry.Tree = parentTree;

            if (reader.NodeType != XmlNodeType.Element)
            {
                WriteLine($"Encountered an unexpected node: {reader.Name} '{reader.NodeType.ToString()}'");
                await reader.SkipAsync();
                entry.PostRead();
                return entry;
            }

            if (entry.ParentType != typeof(IElement) && !entry.ParentType.IsAssignableFrom(parent.GetType()))
            {
                WriteLine($"Parent mismatch. {elementType.GetFriendlyName()} expected {entry.ParentType.GetFriendlyName()}, but got {parent.GetType().GetFriendlyName()}");
                await reader.SkipAsync();
                entry.PostRead();
                return entry;
            }

            if ((ignoreFlags & entry.TypeFlag) != 0)
            {
                await reader.SkipAsync();
                entry.PostRead();
                progress?.Report((float)stream.Position / stream.Length);
                return entry;
            }

            #region Read attributes

            MemberInfo[] members = entry.WantsManualRead ? null : elementType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return null;
                    }
                    else
                    {
                        progress?.Report((float)stream.Position / stream.Length);
                    }

                    string name = reader.Name;
                    string value = reader.Value;
                    if (entry.WantsManualRead)
                    {
                        entry.ManualReadAttribute(name, value);
                    }
                    else
                    {
                        MemberInfo info = members.FirstOrDefault(x =>
                        {
                            Attr a = x.GetCustomAttribute<Attr>();
                            return a == null ? false : string.Equals(a.AttributeName, name, StringComparison.InvariantCultureIgnoreCase);
                        });

                        if (info == null)
                        {
                            WriteLine($"Attribute '{parentTree}[{name}]' not supported by parser. Value = '{value}'");
                        }
                        else if (info is FieldInfo field)
                        {
                            field.SetValue(entry, value.ParseAs(field.FieldType));
                        }
                        else if (info is PropertyInfo property)
                        {
                            property.SetValue(entry, value.ParseAs(property.PropertyType));
                        }
                    }
                }
            }

            #endregion

            if (entry is IVersion v)
                version = v.Version;

            entry.OnAttributesRead();

            #region Read child elements

            if (cancel.IsCancellationRequested)
                return null;
            else
                progress?.Report((float)stream.Position / stream.Length);
            
            reader.MoveToElement();
            if (entry is IStringElement stringEntry)
            {
                stringEntry.GenericStringContent = Activator.CreateInstance(stringEntry.GenericStringType) as BaseElementString;
                stringEntry.GenericStringContent.ReadFromString(await reader.ReadElementContentAsStringAsync());
            }
            else
            {
                if (reader.IsEmptyElement)
                    await reader.ReadAsync();
                else
                {
                    reader.ReadStartElement();
                    int childIndex = 0;

                    ChildInfo[] childElements = entry.WantsManualRead ? null :
                        elementType.GetCustomAttributesExt<Child>().Select(x => new ChildInfo(x)).ToArray();

                    MultiChildInfo[] multiChildElements = entry.WantsManualRead ? null :
                        elementType.GetCustomAttributesExt<MultiChild>().Select(x => new MultiChildInfo(x)).ToArray();

                    //Read all child elements
                    while (reader.NodeType != XmlNodeType.EndElement)
                    {
                        if (cancel.IsCancellationRequested)
                            return null;
                        else
                            progress?.Report((float)stream.Position / stream.Length);
                        
                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            await reader.SkipAsync();
                            continue;
                        }

                        string elementName = reader.Name;
                        if (string.IsNullOrEmpty(elementName))
                            throw new Exception("Null element name.");
                        
                        if (entry.WantsManualRead)
                        {
                            IElement e = entry.ManualReadChildElement(elementName, version);
                            if (e == null)
                            {
                                //Console.WriteLine("Element '{0}' not supported by parser.", parentTree + elementName + "/");
                                await reader.SkipAsync();
                            }
                            else
                                await ParseElementAsync(e, entry, reader, version, ignoreFlags, parentTree, childIndex, stream, progress, cancel);
                        }
                        else
                        {
                            bool isUnsupported = elementType.GetCustomAttributesExt<UnsupportedChild>().
                                Any(x => string.Equals(x.ElementName, elementName, StringComparison.InvariantCultureIgnoreCase));

                            if (isUnsupported)
                            {
                                WriteLine($"Element '{parentTree + elementName}/' not supported by parser.");
                                await reader.SkipAsync();
                            }
                            else
                            {
                                int typeIndex = -1;
                                foreach (ChildInfo child in childElements)
                                {
                                    if (cancel.IsCancellationRequested)
                                        return null;
                                    else
                                        progress?.Report((float)stream.Position / stream.Length);
                                    
                                    typeIndex = Array.FindIndex(child.ElementNames, name => name.Matches(elementName, version));

                                    //If no exact name matches, find a null name child element.
                                    //This means the class is for an element with ANY name.
                                    if (typeIndex < 0)
                                        typeIndex = Array.FindIndex(child.ElementNames, name => name.Name == null && name.VersionMatches(version));
                                    
                                    if (typeIndex >= 0)
                                    {
                                        if (++child.Occurrences > child.Data.MaxCount && child.Data.MaxCount >= 0)
                                            WriteLine($"Element '{parentTree}' has occurred more times than expected.");
                                        
                                        IElement elem = await ParseElementAsync(child.Types[typeIndex], entry, reader, version, ignoreFlags, parentTree, childIndex, stream, progress, cancel);
                                        elem.ElementName = elementName;
                                        break;
                                    }
                                }
                                if (typeIndex < 0)
                                {
                                    if (cancel.IsCancellationRequested)
                                        return null;
                                    else
                                        progress?.Report((float)stream.Position / stream.Length);

                                    int i = 0;
                                    MultiChildInfo[] infos = multiChildElements.Where(attribInfo =>
                                    {
                                        for (i = 0; i < attribInfo.Attrib.Types.Length; ++i)
                                        {
                                            ElementName name = attribInfo.ElementNames[i];
                                            if (name.Matches(elementName, version))
                                            {
                                                ++attribInfo.Occurrences[i];
                                                return true;
                                            }
                                        }
                                        return false;
                                    }).ToArray();

                                    if (infos.Length == 0)
                                    {
                                        //Console.WriteLine("Element '{0}' not supported by parser.", parentTree + elementName + "/");
                                        await reader.SkipAsync();
                                    }
                                    else
                                    {
                                        //TODO: verify the multi child type
                                        IElement elem = await ParseElementAsync(infos[0].Attrib.Types[i], entry, reader, version, ignoreFlags, parentTree, childIndex, stream, progress, cancel);
                                        elem.ElementName = elementName;
                                    }
                                }
                            }
                        }
                        ++childIndex;
                    }

                    if (!entry.WantsManualRead)
                    {
                        ElementName[] underCounted = childElements.
                            Where(x => x.Occurrences < x.Data.MinCount).
                            SelectMany(x => x.ElementNames).
                            Where(x => x.VersionMatches(version)).ToArray();

                        if (underCounted.Length > 0)
                            foreach (ElementName elem in underCounted)
                                WriteLine($"Element '{elem.Name}' has occurred less times than expected.");
                    }

                    if (reader.Name == parentElementName)
                        reader.ReadEndElement();
                    else
                        throw new Exception("Encountered an unexpected node: " + reader.Name);
                }
            }

            #endregion

            entry.PostRead();

            progress?.Report((float)stream.Position / stream.Length);

            return entry;
        }

        #endregion Async

        #region Sync

        /// <summary>
        /// Reads a file with root class T and returns a new instance.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        /// <param name="ignoreFlags">Flags that are bitwise masked against each BaseElement's TypeFlag. If matching, the element is skipped.</param>
        /// <returns>A new instance of class T.</returns>
        public T Import(
            string path,
            ulong ignoreFlags)
            => Import(path, ignoreFlags, null);
        /// <summary>
        /// Reads a file with root class T and returns a new instance.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        /// <param name="ignoreFlags">Flags that are bitwise masked against each BaseElement's TypeFlag. If matching, the element is skipped.</param>
        /// <param name="settings">The settings for reading an XML file.</param>
        /// <returns>A new instance of class T.</returns>
        public T Import(
            string path,
            ulong ignoreFlags,
            XmlReaderSettings settings)
        {
            if (settings == null)
                settings = GetDefaultReaderSettings();

            settings.Async = false;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                string previousTree = "";
                Type rootType = typeof(T);
                ElementName name = rootType.GetCustomAttribute<ElementName>();
                if (name == null || string.IsNullOrEmpty(name.Name))
                {
                    WriteLine($"{rootType.GetFriendlyName()} has no '{nameof(ElementName)}' attribute.");
                    return null;
                }

                bool found;
                while (!(found = reader.MoveToContent() == XmlNodeType.Element &&
                    string.Equals(name.Name, reader.Name, StringComparison.InvariantCulture))) ;
                
                if (found)
                {
                    IElement e = ParseElement(rootType, null, reader, null, ignoreFlags, previousTree, 0, stream);
                    return e as T;
                }

                return null;
            }
        }

        private IElement ParseElement(
            Type elementType,
            IElement parent,
            XmlReader reader,
            string version,
            ulong ignoreFlags,
            string parentTree,
            int elementIndex,
            FileStream stream)
            => ParseElement(Activator.CreateInstance(elementType) as IElement,
                parent, reader, version, ignoreFlags, parentTree, elementIndex, stream);

        private IElement ParseElement(
            IElement entry,
            IElement parent,
            XmlReader reader,
            string version,
            ulong ignoreFlags,
            string parentTree,
            int elementIndex,
            FileStream stream)
        {
            Type elementType = entry.GetType();
            entry.Parent = parent;
            entry.ElementIndex = elementIndex;
            entry.PreRead();

            string parentElementName = reader.Name;
            if (string.IsNullOrEmpty(parentElementName))
                throw new Exception("Null parent element name.");
            
            parentTree += parentElementName + "/";
            entry.Tree = parentTree;

            if (reader.NodeType != XmlNodeType.Element)
            {
                WriteLine($"Encountered an unexpected node: {reader.Name} '{reader.NodeType.ToString()}'");
                reader.Skip();
                entry.PostRead();
                return entry;
            }

            if (entry.ParentType != typeof(IElement) && !entry.ParentType.IsAssignableFrom(parent.GetType()))
            {
                WriteLine($"Parent mismatch. {elementType.GetFriendlyName()} expected {entry.ParentType.GetFriendlyName()}, but got {parent.GetType().GetFriendlyName()}");
                reader.Skip();
                entry.PostRead();
                return entry;
            }

            if ((ignoreFlags & entry.TypeFlag) != 0)
            {
                reader.Skip();
                entry.PostRead();
                return entry;
            }

            #region Read attributes

            MemberInfo[] members = entry.WantsManualRead ? null : elementType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    string name = reader.Name;
                    string value = reader.Value;
                    if (entry.WantsManualRead)
                    {
                        entry.ManualReadAttribute(name, value);
                    }
                    else
                    {
                        MemberInfo info = members.FirstOrDefault(x =>
                        {
                            Attr a = x.GetCustomAttribute<Attr>();
                            return a == null ? false : string.Equals(a.AttributeName, name, StringComparison.InvariantCultureIgnoreCase);
                        });

                        if (info == null)
                            WriteLine($"Attribute '{parentTree}[{name}]' not supported by parser. Value = '{value}'");
                        else if (info is FieldInfo field)
                            field.SetValue(entry, value.ParseAs(field.FieldType));
                        else if (info is PropertyInfo property)
                            property.SetValue(entry, value.ParseAs(property.PropertyType));
                    }
                }
            }

            #endregion

            if (entry is IVersion v)
                version = v.Version;
            
            entry.OnAttributesRead();

            #region Read child elements

            reader.MoveToElement();
            if (entry is IStringElement stringEntry)
            {
                stringEntry.GenericStringContent = Activator.CreateInstance(stringEntry.GenericStringType) as BaseElementString;
                stringEntry.GenericStringContent.ReadFromString(reader.ReadElementContentAsString());
            }
            else
            {
                if (reader.IsEmptyElement)
                    reader.Read();
                else
                {
                    reader.ReadStartElement();
                    int childIndex = 0;

                    ChildInfo[] childElements = entry.WantsManualRead ? null :
                        elementType.GetCustomAttributesExt<Child>().Select(x => new ChildInfo(x)).ToArray();

                    MultiChildInfo[] multiChildElements = entry.WantsManualRead ? null :
                        elementType.GetCustomAttributesExt<MultiChild>().Select(x => new MultiChildInfo(x)).ToArray();

                    //Read all child elements
                    while (reader.NodeType != XmlNodeType.EndElement)
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            reader.Skip();
                            continue;
                        }

                        string elementName = reader.Name;
                        if (string.IsNullOrEmpty(elementName))
                            throw new Exception("Null element name.");

                        if (entry.WantsManualRead)
                        {
                            IElement e = entry.ManualReadChildElement(elementName, version);
                            if (e == null)
                            {
                                //Console.WriteLine("Element '{0}' not supported by parser.", parentTree + elementName + "/");
                                reader.Skip();
                            }
                            else
                                ParseElement(e, entry, reader, version, ignoreFlags, parentTree, childIndex, stream);
                        }
                        else
                        {
                            bool isUnsupported = elementType.GetCustomAttributesExt<UnsupportedChild>().
                                Any(x => string.Equals(x.ElementName, elementName, StringComparison.InvariantCultureIgnoreCase));

                            if (isUnsupported)
                            {
                                WriteLine($"Element '{parentTree + elementName}/' not supported by parser.");
                                reader.Skip();
                            }
                            else
                            {
                                int typeIndex = -1;
                                foreach (ChildInfo child in childElements)
                                {
                                    typeIndex = Array.FindIndex(child.ElementNames, name => name.Matches(elementName, version));

                                    //If no exact name matches, find a null name child element.
                                    //This means the class is for an element with ANY name.
                                    if (typeIndex < 0)
                                        typeIndex = Array.FindIndex(child.ElementNames, name => name.Name == null && name.VersionMatches(version));
                                    
                                    if (typeIndex >= 0)
                                    {
                                        if (++child.Occurrences > child.Data.MaxCount && child.Data.MaxCount >= 0)
                                            WriteLine($"Element '{parentTree}' has occurred more times than expected.");

                                        IElement elem = ParseElement(child.Types[typeIndex], entry, reader, version, ignoreFlags, parentTree, childIndex, stream);
                                        elem.ElementName = elementName;
                                        break;
                                    }
                                }
                                if (typeIndex < 0)
                                {
                                    int i = 0;
                                    MultiChildInfo[] infos = multiChildElements.Where(attribInfo =>
                                    {
                                        for (i = 0; i < attribInfo.Attrib.Types.Length; ++i)
                                        {
                                            ElementName name = attribInfo.ElementNames[i];
                                            if (name.Matches(elementName, version))
                                            {
                                                ++attribInfo.Occurrences[i];
                                                return true;
                                            }
                                        }
                                        return false;
                                    }).ToArray();

                                    if (infos.Length == 0)
                                    {
                                        //Console.WriteLine("Element '{0}' not supported by parser.", parentTree + elementName + "/");
                                        reader.Skip();
                                    }
                                    else
                                    {
                                        //TODO: verify the multi child type
                                        IElement elem = ParseElement(infos[0].Attrib.Types[i], entry, reader, version, ignoreFlags, parentTree, childIndex, stream);
                                        elem.ElementName = elementName;
                                    }
                                }
                            }
                        }
                        ++childIndex;
                    }

                    if (!entry.WantsManualRead)
                    {
                        ElementName[] underCounted = childElements.
                            Where(x => x.Occurrences < x.Data.MinCount).
                            SelectMany(x => x.ElementNames).
                            Where(x => x.VersionMatches(version)).ToArray();

                        if (underCounted.Length > 0)
                            foreach (ElementName elem in underCounted)
                                WriteLine($"Element '{elem.Name}' has occurred less times than expected.");
                    }

                    if (reader.Name == parentElementName)
                        reader.ReadEndElement();
                    else
                        throw new Exception("Encountered an unexpected node: " + reader.Name);
                }
            }

            #endregion

            entry.PostRead();

            return entry;
        }

        #endregion Sync
    }
}