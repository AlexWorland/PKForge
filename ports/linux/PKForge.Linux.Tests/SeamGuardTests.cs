using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace PKForge.Linux.Tests;

/// <summary>
/// Fails loudly when an upstream merge breaks one of the hooks the Linux head relies on.
/// </summary>
public class SeamGuardTests
{
    private static readonly string Root = FindRoot();
    private static readonly string AppDir = Path.Combine(Root, "src", "PKForge.App");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PKForge.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("PKForge.sln not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void SharedMauiProgramStillCallsUseSkiaSharpOnTheBuilder()
    {
        // The Linux hosting seam binds to this exact call and to the namespace; if either changes upstream,
        // the seam stops finding the composition root and the head needs its own copy of the service list.
        var text = File.ReadAllText(Path.Combine(AppDir, "MauiProgram.cs"));
        Assert.Matches(@"UseMauiApp<App>\(\)\s*\.UseSkiaSharp\(\)", text);
        Assert.Matches(@"(?m)^namespace PKForge\.App;\s*$", text);
    }

    [Fact]
    public void ReplacedFilesStillExistUpstream()
    {
        // These four are excluded from the head's compile and replaced by Linux stand-ins (see the Compile
        // Exclude in PKForge.Linux.csproj). If one is renamed or deleted upstream the exclusion silently
        // stops excluding anything and the stand-in collides or goes stale.
        string[] replaced =
        {
            "Services/VsyncLoop.cs",
            "Services/AppUpdateService.cs",
            "Views/WidgetMenu.cs",
            "Views/BadAppleEgg.cs",
        };
        foreach (var rel in replaced)
            Assert.True(File.Exists(Path.Combine(AppDir, rel)), $"src/PKForge.App/{rel} no longer exists; update the head's Compile Exclude and its stand-in.");
    }

    [Fact]
    public void NoNewUnguardedAndroidFileAppeared()
    {
        // Files (relative to src/PKForge.App) that reference Android types outside an `#if ANDROID` region
        // (or a DIAGNOSTIC one, which Linux never defines), found by the scanner below on today's tree.
        // Four of them are excluded from the head and replaced; the rest compile on Linux because the head's
        // AndroidShim supplies the referenced names. A NEW entry means upstream added Android-only code
        // that probably will not compile or behave on Linux: decide whether to guard it, shim it, or
        // exclude and replace it. A REMOVED entry means upstream guarded it; delete it from this list.
        string[] expected =
        {
            "App.cs",
            "Services/AppDataTransfer.cs",
            "Services/AppUpdateService.cs",
            "Services/VsyncLoop.cs",
            "Views/BadAppleEgg.cs",
            "Views/BankPage.cs",
            "Views/BoxBrowserPage.cs",
            "Views/HomePage.cs",
            "Views/MusicMenu.cs",
            "Views/SecondScreenBoxPage.cs",
            "Views/WidgetMenu.cs",
        };

        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(AppDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(AppDir, file).Replace('\\', '/');
            if (rel.StartsWith("Platforms/") || rel.StartsWith("bin/") || rel.StartsWith("obj/")) continue;
            if (HasUnguardedAndroidReference(File.ReadAllLines(file))) found.Add(rel);
        }
        Assert.Equal(expected, found);
    }

    private static readonly Regex AndroidRef = new(
        @"(?<![\w.])(?:Android|AndroidX|Java|Google\.Android)\.[A-Z]|Platforms\.Android|\b(?:HomeWidgets|MainActivity)\b",
        RegexOptions.Compiled);

    internal static bool HasUnguardedAndroidReference(string[] lines)
    {
        var stack = new List<string>();
        var inBlockComment = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!inBlockComment && line.StartsWith('#'))
            {
                var directive = Regex.Match(line, @"^#\s*(if|elif|else|endif)\b\s*(.*)$");
                if (!directive.Success) continue;
                var cond = Regex.Replace(directive.Groups[2].Value, @"//.*$", "").Trim();
                switch (directive.Groups[1].Value)
                {
                    case "if": stack.Add(cond); break;
                    case "else": if (stack.Count > 0) stack[^1] = Negate(stack[^1]); break;
                    case "elif": if (stack.Count > 0) stack[^1] = cond; break;
                    case "endif": if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); break;
                }
                continue;
            }

            var code = StripCommentsAndStrings(line, ref inBlockComment);
            if (code.Length == 0) continue;
            if (stack.Contains("ANDROID") || stack.Contains("DIAGNOSTIC")) continue;
            if (AndroidRef.IsMatch(code)) return true;
        }
        return false;
    }

    private static string Negate(string cond) =>
        cond.StartsWith('!') ? cond[1..] : "!" + cond;

    // Single-line scanner: removes block and line comments and blanks string/char literal contents.
    // Verbatim and interpolated strings are treated like plain ones, which is enough for a line scan.
    private static string StripCommentsAndStrings(string line, ref bool inBlock)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';
            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                continue;
            }
            if (c == '/' && next == '/') break;
            if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < line.Length && line[i] != quote)
                {
                    if (line[i] == '\\') i++;
                    i++;
                }
                sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
