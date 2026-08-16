using System;

namespace FsLiveDocs;

/// <summary>Marks a parameterless static method as setup for named XML documentation examples.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DocScenarioAttribute : Attribute
{
    /// <summary>Creates a scenario marker with the name referenced by an XML example's <c>scenario</c> attribute.</summary>
    public DocScenarioAttribute(string name) => Name = name;

    /// <summary>The unique scenario name referenced by an XML example.</summary>
    public string Name { get; }
}
