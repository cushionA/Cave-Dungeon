using System;

namespace Game.Core
{
    /// <summary>
    /// Marks a class as an SoA data container for SourceGenerator.
    /// Currently containers are hand-written; this attribute is reserved for future code generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ContainerSettingAttribute : Attribute
    {
        public Type[] StructTypes { get; }
        public Type ManagedType { get; }

        public ContainerSettingAttribute(Type managedType, params Type[] structTypes)
        {
            ManagedType = managedType;
            StructTypes = structTypes;
        }
    }
}
