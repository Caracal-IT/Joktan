using System;
using System.IO;

namespace Joktan.Core;

/// <summary>Where Joktan keeps its settings and the certificates it makes.</summary>
public static class DataFolder
{
    /// <summary>Set to keep Joktan's data somewhere other than the user's application data folder.</summary>
    public const string Variable = "JOKTAN_DATA_DIR";

    public static string Path => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } folder
        ? folder
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joktan");
}
