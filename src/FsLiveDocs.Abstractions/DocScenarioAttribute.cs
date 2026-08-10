using System;

namespace FsLiveDocs.Core;

/// <summary>Marks a public parameterless method as setup for named documentation examples.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DocScenarioAttribute : Attribute
{
    /// <summary>Creates a scenario marker with the name used by XML examples.</summary>
    public DocScenarioAttribute(string name) => Name = name;

    /// <summary>The unique scenario name referenced by an XML example.</summary>
    public string Name { get; }
}
