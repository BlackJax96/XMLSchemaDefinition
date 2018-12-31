using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace XMLSchemaDefinition
{
    public partial class XMLSchemaDefinition<T> : BaseXMLSchemaDefinition where T : class, IElement
    {
        public static XmlWriterSettings GetDefaultWriterSettings() => new XmlWriterSettings()
        {
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

        #region Async

        public async Task ExportAsync(string path, T rootElement)
            => await ExportAsync(path, rootElement, Encoding.UTF8, null, CancellationToken.None);

        public async Task ExportAsync(
            string path,
            T rootElement,
            Encoding textEncoding,
            IProgress<float> progress,
            CancellationToken cancel)
        {
            XmlWriterSettings settings = GetDefaultWriterSettings();
            settings.Encoding = textEncoding;
            await ExportAsync(path, rootElement, settings, progress, cancel);
        }

        public async Task ExportAsync(
            string path,
            T rootElement,
            XmlWriterSettings settings,
            IProgress<float> progress,
            CancellationToken cancel)
        {
            if (settings == null)
                settings = GetDefaultWriterSettings();
            
            settings.Async = true;
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                await writer.WriteStartDocumentAsync();
                await WriteElementAsync(rootElement, writer, stream, progress, cancel);
                await writer.WriteEndDocumentAsync();
            }
        }

        //TODO:
        //I don't think this line reports progress very well when the XmlWriter is in async mode:
        //progress?.Report((float)stream.Position / stream.Length);
        //Report progress by amount of elements and attributes written instead

        private async Task WriteElementAsync(
            IElement element,
            XmlWriter writer,
            FileStream stream,
            IProgress<float> progress,
            CancellationToken cancel)
        {
            if (!cancel.IsCancellationRequested)
            {
                await writer.WriteStartElementAsync(null, element.ElementName, null);
                progress?.Report((float)stream.Position / stream.Length);

                Type elementType = element.GetType();
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                MemberInfo[] members = elementType.GetMembers(flags).
                    Where(x => x.GetCustomAttribute<Attr>() != null).ToArray();

                foreach (MemberInfo member in members)
                {
                    Attr attr = member.GetCustomAttribute<Attr>();
                    object value = member is PropertyInfo prop ? prop.GetValue(element) : (member is FieldInfo field ? field.GetValue(element) : null);
                    if (!attr.Required && value == elementType.GetDefaultValue())
                        continue;

                    await writer.WriteAttributeStringAsync(null, attr.AttributeName, null, value.ToString());

                    if (cancel.IsCancellationRequested)
                        break;
                    else
                        progress?.Report((float)stream.Position / stream.Length);
                }
                if (!cancel.IsCancellationRequested)
                {
                    if (element is IStringElement stringEntry)
                    {
                        string value = stringEntry.GenericStringContent.WriteToString();
                        await writer.WriteStringAsync(value);

                        progress?.Report((float)stream.Position / stream.Length);
                    }
                    else
                    {
                        IOrderedEnumerable<IElement> orderedChildren = element.ChildElements.Values.SelectMany(x => x).OrderBy(x => x.ElementIndex);
                        foreach (IElement child in orderedChildren)
                            await WriteElementAsync(child, writer, stream, progress, cancel);
                    }
                }
            }
            await writer.WriteEndElementAsync();
        }

        #endregion

        #region Sync

        public void Export(string path, T rootElement)
            => Export(path, rootElement, Encoding.UTF8);

        public void Export(
            string path,
            T rootElement,
            Encoding textEncoding)
        {
            XmlWriterSettings settings = GetDefaultWriterSettings();
            settings.Encoding = textEncoding;
            Export(path, rootElement, settings);
        }

        public void Export(
            string path,
            T rootElement,
            XmlWriterSettings settings)
        {
            if (settings == null)
                settings = GetDefaultWriterSettings();

            settings.Async = false;
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                writer.WriteStartDocument();
                WriteElement(rootElement, writer, stream);
                writer.WriteEndDocument();
            }
        }

        private void WriteElement(IElement element, XmlWriter writer, FileStream stream)
        {
            writer.WriteStartElement(null, element.ElementName, null);

            Type elementType = element.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MemberInfo[] members = elementType.GetMembers(flags).
                Where(x => x.GetCustomAttribute<Attr>() != null).ToArray();

            foreach (MemberInfo member in members)
            {
                Attr attr = member.GetCustomAttribute<Attr>();
                object value = member is PropertyInfo prop ? prop.GetValue(element) : (member is FieldInfo field ? field.GetValue(element) : null);
                if (!attr.Required && value == elementType.GetDefaultValue())
                    continue;

                writer.WriteAttributeString(null, attr.AttributeName, null, value.ToString());
            }
            if (element is IStringElement stringEntry)
            {
                string value = stringEntry.GenericStringContent.WriteToString();
                writer.WriteString(value);
            }
            else
            {
                IOrderedEnumerable<IElement> orderedChildren = element.ChildElements.Values.SelectMany(x => x).OrderBy(x => x.ElementIndex);
                foreach (IElement child in orderedChildren)
                    WriteElement(child, writer, stream);
            }
            writer.WriteEndElement();
        }

        #endregion
    }
}