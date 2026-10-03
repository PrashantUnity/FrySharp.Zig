#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfEditorApp.Plugins.CSharpEditor.Services.Languages;
using PdfEditorApp.Plugins.CSharpEditor.Services.Processes;

namespace ZigSupportExtension;

/// <summary>
/// Prepares process run plans for Zig scripts and executables with full interactive stdin support.
/// Automatically injects fry_display.zig in an isolated temporary runtime directory for Display APIs
/// without polluting the user's workspace.
/// </summary>
public sealed class ZigScriptRunner : IScriptRunner
{
    private readonly ZigToolchainProvider _toolchain;

    public ZigScriptRunner(ZigToolchainProvider toolchain)
    {
        _toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
    }

    public async Task<ScriptRunPlan> PlanAsync(ScriptRunContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var toolchain = context.Toolchain;
        var executable = toolchain?.ExecutablePath ?? "zig";
        var scriptPath = context.SourceFilePath;
        var workDir = context.WorkingDirectory;

        string targetScriptPath = scriptPath;
        if (File.Exists(scriptPath))
        {
            var content = await File.ReadAllTextAsync(scriptPath, ct).ConfigureAwait(false);
            bool needsDisplay = content.Contains("fry_display") || content.Contains("Display.") || content.Contains("fry.");
            bool needsCompat = content.Contains("std.io.getStdOut") || content.Contains("std.io.getStdErr");

            if (needsDisplay || needsCompat)
            {
                var compatDir = Path.Combine(Path.GetTempPath(), "FryStudio", "zig_scripts", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(compatDir);
                await ZigDisplayRuntime.EnsureInDirectoryAsync(compatDir, ct).ConfigureAwait(false);

                var patched = content;
                if (needsCompat)
                {
                    patched = patched.Replace("std.io.getStdOut", "fry.getStdOut")
                                     .Replace("std.io.getStdErr", "fry.getStdErr");
                    if (!patched.Contains("fry_display.zig"))
                    {
                        patched = """
                            const fry = @import("fry_display.zig");

                            """ + patched;
                    }
                }

                targetScriptPath = Path.Combine(compatDir, Path.GetFileName(scriptPath));
                await File.WriteAllTextAsync(targetScriptPath, patched, ct).ConfigureAwait(false);
            }
        }

        var step = new ProcessStep(
            "run",
            new ProcessStartSpec
            {
                FileName = executable,
                Arguments = ["run", targetScriptPath],
                WorkingDirectory = workDir
            },
            IsBuildStep: false);

        return new ScriptRunPlan([step]);
    }
}
