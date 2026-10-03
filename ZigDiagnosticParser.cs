#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using PdfEditorApp.Plugins.CSharpEditor.Models;
using PdfEditorApp.Plugins.CSharpEditor.Services.Processes;

namespace ZigSupportExtension;

/// <summary>
/// Parses compiler errors, build failures, and syntax diagnostics from the Zig compiler into structured Problems items.
/// </summary>
public sealed class ZigDiagnosticParser : IDiagnosticParser
{
    private static readonly Regex ZigErrorRegex = new(
        @"^(?<file>(?:[a-zA-Z]:)?[^:\r\n]+):(?<line>\d+):(?<col>\d+):\s*(?:(?<severity>error|warning|note):\s*)?(?<message>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DiagnosticParseResult Parse(string output, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(output)) return DiagnosticParseResult.Empty;

        var lines = output.Replace("\r\n", "\n").Split('\n');
        var diagnostics = new List<DiagnosticItem>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var match = ZigErrorRegex.Match(trimmed);
            if (match.Success)
            {
                int.TryParse(match.Groups["line"].Value, out var lineNum);
                int.TryParse(match.Groups["col"].Value, out var colNum);
                var message = match.Groups["message"].Value.Trim();
                var sevText = match.Groups["severity"].Value.ToLowerInvariant();

                if (message.Contains("struct 'std' has no member named 'io'"))
                {
                    message += " (Zig 0.15+ removed 'std.io.getStdOut()'. Use 'std.debug.print(\"...\", .{})' for unbuffered output, or 'std.Io.File.stdout().writer(io, &buf)' with 'pub fn main(init: std.process.Init) !void').";
                }

                var severity = DiagnosticSeverity.Error;
                if (sevText == "warning") severity = DiagnosticSeverity.Warning;
                else if (sevText == "note") severity = DiagnosticSeverity.Info;

                diagnostics.Add(new DiagnosticItem
                {
                    Id = $"ZIG_{(sevText.Length > 0 ? char.ToUpper(sevText[0]) + sevText[1..] : "Error")}",
                    Message = message,
                    Severity = severity,
                    Line = Math.Max(1, lineNum),
                    Column = Math.Max(1, colNum)
                });
            }
        }

        return new DiagnosticParseResult(diagnostics, MissingDependency: null);
    }
}
