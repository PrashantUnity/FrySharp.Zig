#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PdfEditorApp.Plugins.CSharpEditor.Models;
using PdfEditorApp.Plugins.CSharpEditor.Services.Kernels;
using PdfEditorApp.Plugins.CSharpEditor.Services.Processes;
using PdfEditorApp.Plugins.CSharpEditor.Services.Toolchains;
using PdfEditorApp.Plugins.CSharpEditor.Visuals.Interaction;

namespace ZigSupportExtension;

/// <summary>
/// Polyglot interactive notebook kernel for Zig.
/// Evaluates Zig notebook cells via 'zig run', supporting both full programs and cell snippets.
/// Integrates ExternalOutputProcessor and ZigDisplayRuntime for genuine Display.show / Display.chart / Display.surface APIs.
/// </summary>
public sealed class ZigNotebookKernel : INotebookKernel
{
    private readonly ZigToolchainProvider _toolchain;
    private readonly IProcessLauncher _processes;
    private readonly IHostEnvironment _host;
    private readonly KernelCreationContext _context;
    private readonly Dictionary<string, (string TypeName, string JsonValue)> _sharedVariables = new(StringComparer.Ordinal);

    private int _executionCount;
    private bool _isDisposed;

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

        var workingFolder = _context.WorkingDirectory?.Invoke();
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

        // Ensure genuine Zig display runtime is available in cell directory
        await ZigDisplayRuntime.EnsureInDirectoryAsync(outDir, ct).ConfigureAwait(false);

        var sourcePath = Path.Combine(outDir, "cell.zig");
        await File.WriteAllTextAsync(sourcePath, fullProgram, ct).ConfigureAwait(false);

        var zigExecutable = resolution.Toolchain.ExecutablePath;
        using var visuals = new ExternalVisualSession();
        var consoleBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var processor = new ExternalOutputProcessor(
            onConsoleText: text =>
            {
                consoleBuilder.Append(text);
                request.OnConsole?.Invoke(text);
            },
            onRichOutput: bundle =>
            {
                request.OnRichOutput?.Invoke(bundle);
            },
            onShare: (name, json) =>
            {
                _sharedVariables[name] = ("dynamic", json);
            },
            visuals: visuals.Visuals);

        int exitCode;
        try
        {
            var startSpec = visuals.Apply(new ProcessStartSpec
            {
                FileName = zigExecutable,
                Arguments = ["run", sourcePath],
                WorkingDirectory = workingFolder
            });

            using var runProcess = _processes.Start(
                startSpec,
                outText => processor.ProcessChunk(outText),
                errText =>
                {
                    errorBuilder.Append(errText);
                    processor.ProcessChunk(errText);
                });

            runProcess.CloseInput();
            exitCode = await runProcess.Completion.WaitAsync(ct).ConfigureAwait(false);
            processor.Flush();
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

        bool success = exitCode == 0;
        string errOutput = errorBuilder.ToString();
        return new KernelExecutionResult
        {
            Success = success,
            ErrorMessage = success ? null : (!string.IsNullOrEmpty(errOutput) ? errOutput : $"Process exited with code {exitCode}"),
            ConsoleOutput = consoleBuilder.ToString(),
            Elapsed = clock.Elapsed
        };
    }

    public Task<IReadOnlyList<NotebookVariableInfo>> GetVariablesAsync(CancellationToken ct)
    {
        var list = new List<NotebookVariableInfo>();
        foreach (var kvp in _sharedVariables)
        {
            list.Add(new NotebookVariableInfo
            {
                Name = kvp.Key,
                TypeName = kvp.Value.TypeName,
                ValueDisplay = kvp.Value.JsonValue,
                Kernel = DisplayName
            });
        }
        return Task.FromResult<IReadOnlyList<NotebookVariableInfo>>(list);
    }

    public Task<string> GetValueJsonAsync(string name, CancellationToken ct)
    {
        if (_sharedVariables.TryGetValue(name, out var val))
        {
            return Task.FromResult(val.JsonValue);
        }
        return Task.FromResult("null");
    }

    public Task SetValueFromJsonAsync(string name, string json, CancellationToken ct)
    {
        _sharedVariables[name] = ("dynamic", json);
        return Task.CompletedTask;
    }

    public void HardReset()
    {
        _sharedVariables.Clear();
        _executionCount = 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _sharedVariables.Clear();
    }

    private static string BuildCellProgram(string code)
    {
        bool hasMain = code.Contains("fn main(") || code.Contains("pub fn main(");
        bool hasStd = code.Contains("const std") || code.Contains("@import(\"std\")");
        bool hasImport = code.Contains("fry_display.zig");

        var sb = new StringBuilder();
        if (!hasStd)
        {
            sb.AppendLine("""const std = @import("std");""");
        }
        if (!hasImport)
        {
            sb.AppendLine("""
                const fry = @import("fry_display.zig");
                const Display = fry.Display;
                const Visualizer = fry.Visualizer;
                const display = fry.display;
                const show = fry.show;
                const dump = fry.dump;
                """);
        }

        if (hasMain)
        {
            return sb.ToString() + "\n" + code;
        }

        SplitZigCode(code, out var topLevel, out var body);

        foreach (var item in topLevel)
        {
            sb.AppendLine(item);
            sb.AppendLine();
        }

        sb.AppendLine("pub fn main() !void {");
        foreach (var line in body)
        {
            sb.AppendLine("    " + line);
        }
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void SplitZigCode(string code, out List<string> topLevel, out List<string> body)
    {
        topLevel = new List<string>();
        body = new List<string>();

        var lines = code.Split('\n');
        var currentBlock = new List<string>();
        bool isTopLevelBlock = false;
        int braceDepth = 0;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();

            if (braceDepth == 0)
            {
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                // Check if this begins a top-level construct: function, struct, enum, union, etc.
                if (trimmed.StartsWith("fn ") || trimmed.StartsWith("pub fn ") || trimmed.StartsWith("export fn ") ||
                    trimmed.StartsWith("test ") ||
                    (trimmed.StartsWith("const ") && (trimmed.Contains("struct {") || trimmed.Contains("enum {") || trimmed.Contains("union {") || trimmed.Contains("opaque {"))) ||
                    (trimmed.StartsWith("pub const ") && (trimmed.Contains("struct {") || trimmed.Contains("enum {") || trimmed.Contains("union {") || trimmed.Contains("opaque {"))))
                {
                    isTopLevelBlock = true;
                    currentBlock.Add(line);
                }
                else
                {
                    isTopLevelBlock = false;
                    body.Add(line);
                }
            }
            else
            {
                if (isTopLevelBlock)
                {
                    currentBlock.Add(line);
                }
                else
                {
                    body.Add(line);
                }
            }

            foreach (var ch in trimmed)
            {
                if (ch == '{') braceDepth++;
                else if (ch == '}') braceDepth = Math.Max(0, braceDepth - 1);
            }

            if (braceDepth == 0 && isTopLevelBlock && currentBlock.Count > 0)
            {
                topLevel.Add(string.Join("\n", currentBlock));
                currentBlock.Clear();
                isTopLevelBlock = false;
            }
        }

        if (currentBlock.Count > 0)
        {
            if (isTopLevelBlock)
                topLevel.Add(string.Join("\n", currentBlock));
            else
                body.AddRange(currentBlock);
        }
    }
}
