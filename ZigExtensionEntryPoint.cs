using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FrySharp.Sdk;
using PdfEditorApp.Plugins.CSharpEditor.Models;
using PdfEditorApp.Plugins.CSharpEditor.Services.Documentation;

namespace ZigSupportExtension;

/// <summary>
/// Dynamic entry point for the Zig Language Support extension.
/// Registers the full-featured ZigLanguage definition with FrySharp's language engine
/// and contributes documentation to the Documentation & Learning Center.
/// </summary>
public class ZigExtensionEntryPoint : IExtensionEntryPoint
{
    public Task InitializeAsync(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var zigLanguage = new ZigLanguage(context);
        try { context.App?.Languages?.Unregister(zigLanguage.Id); } catch { }

        // Automatically tracked by the extension lifetime bag and cleanly unregistered on unload/reload
        if (context.App?.Languages != null)
        {
            var reg = context.App.Languages.Register(zigLanguage);
            if (reg != null)
            {
                context.TrackDisposable(reg);
            }
        }

        // Register rich Zig documentation category in the Documentation & Learning Center
        var docCategory = new DocCategory
        {
            Id = "zig_guide",
            Title = "Zig Guide & Systems Scripting",
            IconKind = Material.Icons.MaterialIconKind.CodeBraces,
            AccentColor = "#F7A41D",
            Badge = "Zig 0.13+",
            Description = "Guide to Zig systems automation, fast single-file scripting, memory safety, comptime, and language server setup.",
            Articles = new List<DocArticle>
            {
                new()
                {
                    Id = "zig_getting_started",
                    Title = "Getting Started with Zig in FrySharp",
                    Subtitle = "Write, compile, and execute Zig scripts with live diagnostics.",
                    ReadingTime = "2 min read",
                    Summary = "FrySharp provides first-class Zig support including 'zig run' execution, interactive terminal stdin, Problems panel error diagnostics, and ZLS intelligence.",
                    Keywords = new List<string> { "zig", "systems", "zls", "compiler", "toolchain" },
                    Sections = new List<DocSection>
                    {
                        new()
                        {
                            Heading = "Running Zig Scripts",
                            Content = "Press F5 or Ctrl+F5 to compile and execute any .zig file. FrySharp invokes the Zig compiler and streams output directly to the Terminal panel.",
                            CalloutType = DocCalloutType.Tip,
                            CalloutText = "You can install Zig quickly via 'brew install zig' (macOS) or 'winget install zig.zig' (Windows)."
                        },
                        new()
                        {
                            Heading = "ZLS Language Server Integration",
                            Content = "When 'zls' is installed on your PATH, FrySharp automatically connects to it via stdio to provide real-time code completion, function signatures, and doc hovers.",
                            CalloutType = DocCalloutType.Info,
                            CalloutText = "Configure a custom ZLS path in Settings > Customization if not in your standard PATH."
                        }
                    },
                    CodeSnippets = new List<DocCodeSnippet>
                    {
                        new()
                        {
                            Language = "zig",
                            Description = "Basic Zig CLI program with formatted stdout.",
                            Code = """
                                const std = @import("std");

                                pub fn main() !void {
                                    const stdout = std.io.getStdOut().writer();
                                    try stdout.print("Hello from FrySharp Zig!\n", .{});
                                }
                                """
                        }
                    }
                }
            }
        };

        var docToken = DocumentationService.Instance.RegisterCategory(docCategory);
        context.TrackDisposable(docToken);

        return Task.CompletedTask;
    }

    public Task DeactivateAsync() => Task.CompletedTask;
}
