using SixLabors.Fonts;
using System.Reflection;

namespace VLFLib;

internal static class Utilities
{
    internal static string GetAssemblyDirectory()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return Path.GetDirectoryName(assembly.Location) ?? Environment.CurrentDirectory;
    }


    internal static Font ResolveFont(string preferred, float sizePt = 12)
    {
        if (SystemFonts.TryGet(preferred, out FontFamily fam))
        {
            return fam.CreateFont(sizePt);
        }

        // Fallback to the first system family
        FontFamily first = SystemFonts.Families.First();
        return first.CreateFont(sizePt);
    }

    internal static string NormalizeOutputPath(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Directory.GetCurrentDirectory();
            path = Path.Combine(dir, Path.GetFileName(path));
        }

        Directory.CreateDirectory(dir);
        return path;
    }

}
