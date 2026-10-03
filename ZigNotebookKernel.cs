#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PdfEditorApp.Plugins.CSharpEditor.Models;
using PdfEditorApp.Plugins.CSharpEditor.Services.Kernels;
using PdfEditorApp.Plugins.CSharpEditor.Services.Processes;
using PdfEditorApp.Plugins.CSharpEditor.Services.Toolchains;

namespace ZigSupportExtension;

/// <summary>
/// Polyglot interactive notebook kernel for Zig.
/// Evaluates Zig notebook cells via 'zig run', supporting both full programs and cell snippets.
/// </summary>
public sealed class ZigNotebookKernel : INotebookKernel
{
    private readonly ZigToolchainProvider _toolchain;
    private readonly IProcessLauncher _processes;
    private readonly IHostEnvironment _host;
    private readonly KernelCreationContext _context;

    private int _executionCount;

    public ZigNotebookKernel(
        ZigToolchainProvider toolchain,
        IProcessLauncher processes,
        IHostEnvironment host,
        KernelCreationContext context)
    {
        _toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public string LanguageId => "zig";
    public string DisplayName => "Zig (Compiler)";
    public bool IsSessionActive => _executionCount > 0;
    public bool CanForceStop => true;

    public async Task<KernelExecutionResult> ExecuteAsync(KernelExecutionRequest request, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        _executionCount++;

        var workingFolder = _context.WorkingDirectory();
        if (string.IsNullOrWhiteSpace(workingFolder) || !Directory.Exists(workingFolder))
        {
            workingFolder = _host.HomeDirectory;
        }

        var resolution = await _toolchain.ResolveAsync(new ToolchainQuery(workingFolder, _context.WorkspaceRoot?.Invoke()), ct).ConfigureAwait(false);
        if (!resolution.IsFound || resolution.Toolchain == null)
        {
            var guidance = resolution.Missing?.Summary ?? "Zig compiler not found on PATH. Install via 'brew install zig' or download from https://ziglang.org/download/.";
            var err = guidance + "\n";
            request.OnConsole?.Invoke(err);
            return new KernelExecutionResult
            {
                Success = false,
                ErrorMessage = guidance,
                ConsoleOutput = err,
                Elapsed = clock.Elapsed
            };
        }

        var code = request.Code?.Trim() ?? string.Empty;
        var fullProgram = BuildCellProgram(code);

        var hash = Math.Abs((workingFolder + "_zig_cell_" + _executionCount).GetHashCode(StringComparison.OrdinalIgnoreCase)).ToString("x8");
        var outDir = Path.Combine(Path.GetTempPath(), "FryStudio", "zig_cells", hash);
        Directory.CreateDirectory(outDir);

        var sourcePath = Path.Combine(outDir, "cell.zig");
        await File.WriteAllTextAsync(sourcePath, fullProgram, ct).ConfigureAwait(false);

        var zigExecutable = resolution.Toolchain.ExecutablePath;
        var consoleBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        int exitCode;
        try
        {
            var startSpec = new ProcessStartSpec
            {
                FileName = zigExecutable,
                Arguments = ["run", sourcePath],
                WorkingDirectory = workingFolder
            };

            using var runProcess = _processes.Start(
                startSpec,
                outText =>
                {
                    consoleBuilder.Append(outText);
                    request.OnConsole?.Invoke(outText);
                },
                errText =>
                {
                    errorBuilder.Append(errText);
                    request.OnConsole?.Invoke(errText);
                });

            runProcess.CloseInput();
            exitCode = await runProcess.Completion.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new KernelExecutionResult
            {
                Success = false,
                ErrorMessage = "Execution was cancelled.",
                ConsoleOutput = consoleBuilder.ToString(),
                Elapsed = clock.Elapsed
            };
        }
        catch (Exception ex)
        {
            var msg = $"Process execution error: {ex.Message}\n";
            request.OnConsole?.Invoke(msg);
            return new KernelExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ConsoleOutput = msg,
                Elapsed = clock.Elapsed
            };
        }

        return new KernelExecutionResult
        {
            Success = exitCode == 0,
            ErrorMessage = exitCode != 0 ? errorBuilder.ToString() : null,
            ConsoleOutput = consoleBuilder.ToString(),
            Elapsed = clock.Elapsed
        };
    }

    public Task StopAsync() => Task.CompletedTask;

    public void Dispose() { }

    private static string BuildCellProgram(string code)
    {
        // If the cell defines main, run it directly
        if (code.Contains("fn main(") || code.Contains("pub fn main("))
        {
            return code;
        }

        // Otherwise wrap bare expressions or statements inside a main function
        return $$"""
            const std = @import("std");

            pub fn main() void {
                {{code}}
            }
            """;
    }
}
