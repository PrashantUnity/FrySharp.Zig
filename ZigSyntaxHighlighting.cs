#nullable enable
using System;
using System.IO;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace ZigSupportExtension;

/// <summary>
/// Provides syntax highlighting definitions for Zig source files (.zig, .zon) in AvaloniaEdit.
/// </summary>
public static class ZigSyntaxHighlighting
{
    private static IHighlightingDefinition? _definition;
    private static readonly object _lock = new();

    public static IHighlightingDefinition? Get(bool isDark)
    {
        lock (_lock)
        {
            if (_definition != null) return _definition;

            try
            {
                // 1. Try local syntaxes directory
                var baseDir = Path.GetDirectoryName(typeof(ZigSyntaxHighlighting).Assembly.Location)
                              ?? AppContext.BaseDirectory;
                var xshdPath = Path.Combine(baseDir, "syntaxes", "zig.xshd");

                if (!File.Exists(xshdPath))
                {
                    // Fallback to sample extensions path
                    xshdPath = Path.Combine(AppContext.BaseDirectory, "samples", "extensions", "zig-support", "syntaxes", "zig.xshd");
                }

                if (File.Exists(xshdPath))
                {
                    using var stream = File.OpenRead(xshdPath);
                    using var reader = XmlReader.Create(stream);
                    _definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    return _definition;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ZigSyntaxHighlighting] Failed to load XSHD: {ex.Message}");
            }

            // Fallback: register standard C/C++ or C# highlighting
            return HighlightingManager.Instance.GetDefinition("C#");
        }
    }
}
