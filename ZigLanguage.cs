#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Indentation;
using FrySharp.Sdk;
using PdfEditorApp.Plugins.CSharpEditor.Services.Languages;
using PdfEditorApp.Plugins.CSharpEditor.Services.Languages.Lsp;
using PdfEditorApp.Plugins.CSharpEditor.Services.Processes;
using PdfEditorApp.Plugins.CSharpEditor.Services.Toolchains;

namespace ZigSupportExtension;

/// <summary>
/// Full-featured language definition for Zig, bringing Zig into the VS Code script studio,
/// toolchain manager, Problems deck, syntax coloring, and ZLS autocompletion.
/// </summary>
public sealed class ZigLanguage : LanguageDefinition
{
    private readonly ZigToolchainProvider _toolchain;
    private readonly ZigScriptRunner _runner;
    private readonly ZigDiagnosticParser _diagnostics;
    private readonly IEditorAssistantFactory? _assistants;

    public override string Id => "zig";
    public override string DisplayName => "Zig";
    public override string ShortName => "ZIG";
    public override IReadOnlyList<string> FileExtensions => [".zig", ".zon"];
    public override IReadOnlyList<string> Aliases => ["zig", "zon"];
    public override string AccentHex => "#F7A41D";
    public override string IconKind => "FileCodeOutline";
    public override string LineCommentPrefix => "//";
    public override string RuntimeDescription => "Zig Compiler (zig run / zig build)";
    public override string NewFileTemplate => """
        const std = @import("std");

        pub fn main() !void {
            const stdout = std.io.getStdOut().writer();
            try stdout.print("Hello from FrySharp Zig!\n", .{});
        }

        """;

    public override LanguageCapabilities Capabilities =>
        LanguageCapabilities.StandardInput |
        LanguageCapabilities.LiveDiagnostics |
        LanguageCapabilities.Templates |
        LanguageCapabilities.Completion |
        LanguageCapabilities.QuickInfo |
        LanguageCapabilities.ExecutionModes;

    public override IToolchainProvider? Toolchain => _toolchain;
    public override IScriptRunner? ScriptRunner => _runner;
    public override IDiagnosticParser? RunDiagnostics => _diagnostics;
    public override IEditorAssistantFactory? EditorAssistants => _assistants;

    public ZigLanguage(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IHostEnvironment host = new HostEnvironment();
        IProcessLauncher launcher = new ProcessLauncher();
        var defaultSettingsPath = Path.Combine(host.HomeDirectory, ".frysharp", "toolchains.json");
        var settings = new ToolchainSettingsStore(defaultSettingsPath);

        if (context.App is PdfEditorApp.Plugins.CSharpEditor.Services.Extensibility.StudioAppContext studioApp)
        {
            try
            {
                if (studioApp.LanguageServices != null)
                {
                    if (studioApp.LanguageServices.Host != null) host = studioApp.LanguageServices.Host;
                    if (studioApp.LanguageServices.Processes != null) launcher = studioApp.LanguageServices.Processes;
                    if (studioApp.LanguageServices.ToolchainSettings != null) settings = studioApp.LanguageServices.ToolchainSettings;
                }
            }
            catch { }
        }

        _toolchain = new ZigToolchainProvider(host, launcher, settings);
        _runner = new ZigScriptRunner(_toolchain);
        _diagnostics = new ZigDiagnosticParser();

        // Enable ZLS (Zig Language Server) LSP assistant for real-time completion and hover
        string zlsCmd = context.GetSetting<string>("zig.zlsPath") ?? "zls";
        _assistants = new GenericLspEditorAssistantFactory(zlsCmd, ["--stdio"], "zig");
    }

    public override IHighlightingDefinition? GetHighlighting(bool isDark) =>
        ZigSyntaxHighlighting.Get(isDark);

    public override IIndentationStrategy CreateIndentationStrategy(TextEditorOptions options) =>
        new BraceIndentationStrategy(options);
}
