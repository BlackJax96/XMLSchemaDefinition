using System.Linq;
using System.Reflection;

namespace XMLSchemaDefinition
{
    public abstract partial class BaseXMLSchemaDefinition
    {
        public class MultiChildInfo
        {
            public MultiChildInfo(MultiChild data)
            {
                Attrib = data;
                Occurrences = new int[Attrib.Types.Length];
                for (int i = 0; i < Occurrences.Length; ++i)
                    Occurrences[i] = 0;
                
                ElementNames = Attrib.Types.Select(x => x.GetCustomAttribute<ElementName>()).ToArray();
            }
            public MultiChild Attrib { get; private set; }
            public int[] Occurrences { get; private set; }
            public ElementName[] ElementNames { get; private set; }

            public override string ToString()
                => string.Join(" ", ElementNames.Select(x => x.Name)) + " " + string.Join(" ", Occurrences);
        }
    }
}