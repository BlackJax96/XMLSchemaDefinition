using System;
using System.Collections.Generic;
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

        public async Task ExportAsync(string path, T file)
            => await ExportAsync(path, file, Encoding.UTF8, null, CancellationToken.None);

        public async Task ExportAsync(
            string path,
            T file,
            Encoding textEncoding,
            IProgress<float> progress,
            CancellationToken cancel)
        {
            XmlWriterSettings settings = GetDefaultWriterSettings();
            settings.Encoding = textEncoding;
            await ExportAsync(path, file, settings, progress, cancel);
        }

        public async Task ExportAsync(
            string path,
            T file,
            XmlWriterSettings settings,
            IProgress<float> progress,
            CancellationToken cancel)
        {
            if (settings == null)
                settings = GetDefaultWriterSettings();
            
            settings.Async = true;

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
            await WriteElementAsync(file, writer, cancel);
            await writer.WriteEndDocumentAsync();
        }
        private async Task WriteElementAsync(
            IElement element,
            XmlWriter writer,
            CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
                return;

            Type elementType = element.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            List<MemberInfo> members = elementType.GetMembers(flags).Where(x => x.GetCustomAttribute<Attr>() != null).ToList();

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
                    await WriteElementAsync(child, writer, cancel);
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