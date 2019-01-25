public class XMLSchemeDefinition<T> : BaseXMLSchemeDefinition where T : class, IElement
{
    public XMLSchemeDefinition() { }
    public async Task<T> ImportAsync(
        string path,
        ulong ignoreFlags)
        => await ImportAsync(path, ignoreFlags, null, CancellationToken.None);
    public async Task<T> ImportAsync(
        string path,
        ulong ignoreFlags,
        XmlReaderSettings settings)
        => await ImportAsync(path, ignoreFlags, settings, null, CancellationToken.None);
    public async Task<T> ImportAsync(
        string path,
        ulong ignoreFlags,
        IProgress<float> progress,
        CancellationToken cancel)
    {
        XmlReaderSettings settings = new XmlReaderSettings()
        {
            ConformanceLevel = ConformanceLevel.Document,
            DtdProcessing = DtdProcessing.Ignore,
            CheckCharacters = false,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = true,
            Async = true,
        };
        return await ImportAsync(path, ignoreFlags, settings, progress, cancel);
    }
    public async Task<T> ImportAsync(
        string path,
        ulong ignoreFlags,
        XmlReaderSettings settings,
        IProgress<float> progress,
        CancellationToken cancel)
    {
        long currentBytes = 0L;
        using (ProgressStream f = new ProgressStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), null, null))
        using (XmlReader r = XmlReader.Create(f, settings))
        {
            if (progress != null)
            {
                float length = f.Length;
                f.SetReadProgress(new BasicProgress<int>(i =>
                {
                    currentBytes += i;
                    progress.Report(currentBytes / length);
                }));
            }
            return await ImportAsync(r, ignoreFlags, cancel);
        }
    }
    private async Task<T> ImportAsync(
        XmlReader reader,
        ulong ignoreFlags,
        CancellationToken cancel)
    {
        string previousTree = "";
        Type t = typeof(T);
        ElementName name = t.GetCustomAttribute<ElementName>();
        if (name == null || string.IsNullOrEmpty(name.Name))
        {
            Engine.PrintLine(t.GetFriendlyName() + " has no 'Name' attribute.");
            return null;
        }

        bool found;
        while (!(found = await reader.MoveToContentAsync() == XmlNodeType.Element &&
            string.Equals(name.Name, reader.Name, StringComparison.InvariantCulture))) ;

        if (found)
        {
            IElement e = await ParseElementAsync(t, null, reader, null, ignoreFlags, previousTree, 0, cancel);
            return e as T;
        }

        return null;
    }
    private async Task<IElement> ParseElementAsync(
        Type elementType,
        IElement parent,
        XmlReader reader,
        string version,
        ulong ignoreFlags,
        string parentTree,
        int elementIndex,
        CancellationToken cancel)
        => await ParseElementAsync(Activator.CreateInstance(elementType) as IElement,
            parent, reader, version, ignoreFlags, parentTree, elementIndex, cancel);
    private async Task<IElement> ParseElementAsync(
        IElement entry,
        IElement parent,
        XmlReader reader,
        string version,
        ulong ignoreFlags,
        string parentTree,
        int elementIndex,
        CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested)
            return null;

        //DateTime startTime = DateTime.Now;

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
            Engine.PrintLine("Encountered an unexpected node: {0} '{1}'", reader.Name, reader.NodeType.ToString());
            await reader.SkipAsync();
            entry.PostRead();
            return entry;
        }

        if (entry.ParentType != typeof(IElement) && !entry.ParentType.IsAssignableFrom(parent.GetType()))
        {
            Engine.PrintLine("Parent mismatch. {0} expected {1}, but got {2}", elementType.GetFriendlyName(), entry.ParentType.GetFriendlyName(), parent.GetType().GetFriendlyName());
            await reader.SkipAsync();
            entry.PostRead();
            return entry;
        }

        if ((ignoreFlags & entry.TypeFlag) != 0)
        {
            await reader.SkipAsync();
            entry.PostRead();
            return entry;
        }

        #region Read attributes
        MemberInfo[] members = entry.WantsManualRead ? null : elementType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (reader.HasAttributes)
            while (reader.MoveToNextAttribute())
            {
                if (cancel.IsCancellationRequested)
                    return null;

                string name = reader.Name;
                string value = reader.Value;
                if (entry.WantsManualRead)
                    entry.ManualReadAttribute(name, value);
                else
                {
                    MemberInfo info = members.FirstOrDefault(x =>
                    {
                        var a = x.GetCustomAttribute<Attr>();
                        return a == null ? false : string.Equals(a.AttributeName, name, StringComparison.InvariantCultureIgnoreCase);
                    });

                    if (info == null)
                    {
                        //Engine.PrintLine("Attribute '{0}[{1}]' not supported by parser. Value = '{2}'", parentTree, name, value);
                    }
                    else if (info is FieldInfo field)
                        field.SetValue(entry, value.ParseAs(field.FieldType));
                    else if (info is PropertyInfo property)
                        property.SetValue(entry, value.ParseAs(property.PropertyType));
                }
            }
        #endregion

        if (entry is IVersion v)
            version = v.Version;

        entry.OnAttributesRead();

        #region Read child elements

        if (cancel.IsCancellationRequested)
            return null;

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
                    elementType.GetCustomAttributesExt<ElementChild>().Select(x => new ChildInfo(x)).ToArray();

                MultiChildInfo[] multiChildElements = entry.WantsManualRead ? null :
                    elementType.GetCustomAttributesExt<MultiChild>().Select(x => new MultiChildInfo(x)).ToArray();

                //Read all child elements
                while (reader.NodeType != XmlNodeType.EndElement)
                {
                    if (cancel.IsCancellationRequested)
                        return null;

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
                            //Engine.PrintLine("Element '{0}' not supported by parser.", parentTree + elementName + "/");
                            await reader.SkipAsync();
                        }
                        else
                            await ParseElementAsync(e, entry, reader, version, ignoreFlags, parentTree, childIndex, cancel);
                    }
                    else
                    {
                        bool isUnsupported = elementType.GetCustomAttributes<UnsupportedElementChild>().
                            Any(x => string.Equals(x.ElementName, elementName, StringComparison.InvariantCultureIgnoreCase));

                        if (isUnsupported)
                        {
                            if (string.IsNullOrEmpty(elementName))
                                throw new Exception("Null element name.");
                            //Engine.PrintLine("Element '{0}' not supported by parser.", parentTree + elementName + "/");
                            await reader.SkipAsync();
                        }
                        else
                        {
                            int typeIndex = -1;
                            foreach (ChildInfo child in childElements)
                            {
                                if (cancel.IsCancellationRequested)
                                    return null;

                                typeIndex = Array.FindIndex(child.ElementNames, name => name.Matches(elementName, version));

                                //If no exact name matches, find a null name child element.
                                //This means the class is for an element with ANY name.
                                if (typeIndex < 0)
                                    typeIndex = Array.FindIndex(child.ElementNames, name => name.Name == null && name.VersionMatches(version));

                                if (typeIndex >= 0)
                                {
                                    if (++child.Occurrences > child.Data.MaxCount && child.Data.MaxCount >= 0)
                                        Engine.PrintLine("Element '{0}' has occurred more times than expected.", parentTree);

                                    IElement elem = await ParseElementAsync(child.Types[typeIndex], entry, reader, version, ignoreFlags, parentTree, childIndex, cancel);
                                    elem.ElementName = elementName;
                                    break;
                                }
                            }
                            if (typeIndex < 0)
                            {
                                if (cancel.IsCancellationRequested)
                                    return null;

                                int i = 0;
                                MultiChildInfo info = multiChildElements.FirstOrDefault(c =>
                                {
                                    for (i = 0; i < c.Data.Types.Length; ++i)
                                    {
                                        ElementName name = c.ElementNames[i];
                                        if (name.Matches(elementName, version))
                                        {
                                            ++c.Occurrences[i];
                                            return true;
                                        }
                                    }
                                    return false;
                                });

                                if (info == null)
                                {
                                    //Engine.PrintLine("Element '{0}' not supported by parser.", parentTree + elementName + "/");
                                    await reader.SkipAsync();
                                }
                                else
                                {
                                    IElement elem = await ParseElementAsync(info.Data.Types[i], entry, reader, version, ignoreFlags, parentTree, childIndex, cancel);
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
                        foreach (ElementName c in underCounted)
                            Engine.PrintLine("Element '{0}' has occurred less times than expected.", c.Name);
                }

                if (reader.Name == parentElementName)
                    reader.ReadEndElement();
                else
                    throw new Exception("Encountered an unexpected node: " + reader.Name);
            }
        }

        #endregion

        entry.PostRead();

        //TimeSpan elapsed = DateTime.Now - startTime;
        //if (elapsed.TotalSeconds > 1.0f)
        //    if (entry is IID id && !string.IsNullOrEmpty(id.ID))
        //        Engine.PrintLine("Parsing {0}{2} took {1} seconds.", parentTree, elapsed.TotalSeconds.ToString(), id.ID);
        //    else
        //        Engine.PrintLine("Parsing {0} took {1} seconds.", parentTree, elapsed.TotalSeconds.ToString());

        return entry;
    }
    public async Task ExportAsync(string path, T file)
        => await ExportAsync(path, file, null, CancellationToken.None);
    public async Task ExportAsync(
        string path,
        T file,
        IProgress<float> progress,
        CancellationToken cancel)
    {
        XmlWriterSettings settings = new XmlWriterSettings()
        {
            Async = true,
            Encoding = Encoding.UTF8,
            Indent = true,
            IndentChars = "\t",
            NewLineHandling = NewLineHandling.Replace,
            NewLineChars = Environment.NewLine,
            OmitXmlDeclaration = false,
            NewLineOnAttributes = false,
            WriteEndDocumentOnClose = true,
            CloseOutput = true,
        };
        await ExportAsync(path, file, settings, progress, cancel);
    }
    public async Task ExportAsync(
        string path,
        T file,
        XmlWriterSettings settings,
        IProgress<float> progress,
        CancellationToken cancel)
    {
        long currentBytes = 0L;
        using (ProgressStream f = new ProgressStream(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None), null, null))
        using (XmlWriter r = XmlWriter.Create(f, settings))
        {
            if (progress != null)
            {
                float length = f.Length;
                f.SetWriteProgress(new BasicProgress<int>(i =>
                {
                    currentBytes += i;
                    progress.Report(currentBytes / length);
                }));
            }
            await ExportAsync(file, r, cancel);
        }
    }
    private async Task ExportAsync(T file, XmlWriter writer, CancellationToken cancel)
    {
        await writer.WriteStartDocumentAsync();
        await WriteElement(file, writer, cancel);
        await writer.WriteEndDocumentAsync();
    }
    private async Task WriteElement(
        IElement element,
        XmlWriter writer,
        CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested)
            return;

        Type elementType = element.GetType();
        List<MemberInfo> members = elementType.GetMembers(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(x => x.GetCustomAttribute<Attr>() != null).ToList();

        int xmlnsIndex = members.FindIndex(x => x.GetCustomAttribute<Attr>().AttributeName == "xmlns");
        if (xmlnsIndex >= 0)
        {
            MemberInfo member = members[xmlnsIndex];
            object value = member is PropertyInfo prop ? prop.GetValue(element) : (member is FieldInfo field ? field.GetValue(element) : null);
            members.RemoveAt(xmlnsIndex);
            await writer.WriteStartElementAsync(null, element.ElementName, value.ToString());
        }
        else
            await writer.WriteStartElementAsync(null, element.ElementName, null);

        foreach (MemberInfo member in members)
        {
            Attr attr = member.GetCustomAttribute<Attr>();
            object value = member is PropertyInfo prop ? prop.GetValue(element) : (member is FieldInfo field ? field.GetValue(element) : null);
            if (!attr.Required && value == elementType.GetDefaultValue())
                continue;

            await writer.WriteAttributeStringAsync(null, attr.AttributeName, null, value.ToString());

            if (cancel.IsCancellationRequested)
                return;
        }
        if (element is IStringElement stringEntry)
        {
            string value = stringEntry.GenericStringContent.WriteToString();
            await writer.WriteStringAsync(value);
        }
        else
        {
            var orderedChildren = element.ChildElements.Values.SelectMany(x => x).OrderBy(x => x.ElementIndex);
            foreach (IElement child in orderedChildren)
            {
                await WriteElement(child, writer, cancel);
            }
        }
        await writer.WriteEndElementAsync();
    }
}
