using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace XMLSchemaDefinition
{
    public abstract partial class BaseXMLSchemaDefinition
    {
        /// <summary>
        /// Event called every time a debug line is printed.
        /// </summary>
        public static event Action<string> OutputLine;

        public static void WriteLine(string line)
        {
            Console.WriteLine(line);
            OutputLine?.Invoke(line);
        }

        public class ChildInfo
        {
            private static readonly Type[] ElementTypes;
            static ChildInfo()
            {
                Type elemType = typeof(IElement);
                ElementTypes = FindTypes((Type t) => elemType.IsAssignableFrom(t) && t.GetCustomAttribute<ElementName>() != null).ToArray();
            }
            public ChildInfo(Child data)
            {
                Data = data;
                Occurrences = 0;
                Types = ElementTypes.Where((Type t) => Data.ChildEntryType.IsAssignableFrom(t)).ToArray();
                ElementNames = new ElementName[Types.Length];
                for (int i = 0; i < Types.Length; ++i)
                {
                    Type t = Types[i];
                    ElementName nameAttrib = t.GetCustomAttribute<ElementName>();
                    ElementNames[i] = nameAttrib;
                    if (nameAttrib == null)
                        WriteLine(Data.ChildEntryType.GetFriendlyName() + " has no 'Name' attribute");
                }
            }
            private static IEnumerable<Type> FindTypes(Predicate<Type> matchPredicate, params Assembly[] assemblies)
            {
                if (assemblies == null || assemblies.Length == 0)
                    assemblies = AppDomain.CurrentDomain.GetAssemblies();

                IEnumerable<Assembly> validAssemblies = assemblies.Where(x => !x.IsDynamic);
                IEnumerable<Type> allTypes = validAssemblies.SelectMany(x => x.GetExportedTypes());
                return allTypes.Where(x => matchPredicate(x)).OrderBy(x => x.Name);
            }

            public Type[] Types { get; private set; }
            public ElementName[] ElementNames { get; private set; }
            public Child Data { get; private set; }
            public int Occurrences { get; set; }

            public override string ToString()
                => string.Join(" ", ElementNames.Select(x => x.Name)) + " " + Occurrences;
        }
    }
}