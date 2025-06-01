using System;
using System.IO;
using System.Reflection;

namespace VLFLib;

internal static class Utilities
{
    internal static string GetAssemblyDirectory()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return Path.GetDirectoryName(assembly.Location) ?? Environment.CurrentDirectory;
    }

}
