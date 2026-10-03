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
/// Executes via 'zig run <sourceFile>' or two-phase compilation.
/// </summary>
public sealed class ZigScriptRunner : IScriptRunner
{
    private readonly ZigToolchainProvider _toolchain;

    public ZigScriptRunner(ZigToolchainProvider toolchain)
    {
        _toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
    }

    public Task<ScriptRunPlan> PlanAsync(ScriptRunContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var toolchain = context.Toolchain;
        var executable = toolchain?.ExecutablePath ?? "zig";
        var scriptPath = context.SourceFilePath;
        var workDir = context.WorkingDirectory;

        // If directory has build.zig and the script is main or not specified, we can run 'zig build run'
        // Otherwise, run single-file mode directly via 'zig run <file>'
        var step = new ProcessStep(
            "run",
            new ProcessStartSpec
            {
                FileName = executable,
                Arguments = ["run", scriptPath],
                WorkingDirectory = workDir
            },
            IsBuildStep: false);

        return Task.FromResult(new ScriptRunPlan([step]));
    }
}
