#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PdfEditorApp.Plugins.CSharpEditor.Services.Common;
using PdfEditorApp.Plugins.CSharpEditor.Services.Processes;
using PdfEditorApp.Plugins.CSharpEditor.Services.Toolchains;

namespace ZigSupportExtension;

/// <summary>
/// Finds the Zig compiler (zig) on this machine, probes its version (0.11+ / 0.12+ / 0.13+ / 0.14+),
/// and displays active toolchain status in the Status Bar.
/// </summary>
public sealed class ZigToolchainProvider : IToolchainProvider
{
    public static readonly Version MinimumVersion = new(0, 11, 0);

    private readonly IHostEnvironment _host;
    private readonly IProcessLauncher _launcher;
    private readonly ToolchainSettingsStore _settings;
    private readonly ConcurrentDictionary<string, Lazy<Task<ProbeResult?>>> _probes = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex VersionRegex = new(
        @"^(?<version>\d+\.\d+\.\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ZigToolchainProvider(IHostEnvironment host, IProcessLauncher launcher, ToolchainSettingsStore settings)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string LanguageId => "zig";
    public string ToolName => "Zig Compiler";
    public string? SelectedPath => _settings.GetSelectedPath("zig");

    public IReadOnlyList<ToolchainAction> Actions
    {
        get
        {
            var actions = new List<ToolchainAction>();
            if (_host.IsMacOS)
            {
                actions.Add(new ToolchainAction("brew-install-zig", "Install Zig via Homebrew", "Runs 'brew install zig' in terminal."));
                actions.Add(new ToolchainAction("open-download", "Download Zig Tarball", "Opens https://ziglang.org/download/ in browser."));
            }
            else if (_host.IsWindows)
            {
                actions.Add(new ToolchainAction("winget-install-zig", "Install Zig via Winget", "Runs 'winget install zig.zig' in PowerShell."));
                actions.Add(new ToolchainAction("choco-install-zig", "Install Zig via Chocolatey", "Runs 'choco install zig' in admin command prompt."));
                actions.Add(new ToolchainAction("open-download", "Download Zig for Windows", "Opens https://ziglang.org/download/ in browser."));
            }
            else
            {
                actions.Add(new ToolchainAction("snap-install-zig", "Install Zig via Snap", "Runs 'sudo snap install zig --classic --beta'."));
                actions.Add(new ToolchainAction("open-download", "Download Zig Linux Tarball", "Opens https://ziglang.org/download/ in browser."));
            }
            return actions;
        }
    }

    public void Select(string? executablePath) => _settings.SetSelectedPath("zig", executablePath);

    public void Refresh() => _probes.Clear();

    public async Task<ToolchainResolution> ResolveAsync(ToolchainQuery query, CancellationToken ct = default)
    {
        var candidates = await GetCandidatesAsync(query, ct).ConfigureAwait(false);

        foreach (var path in candidates)
        {
            var probe = await ProbeAsync(path, ct).ConfigureAwait(false);
            if (probe == null) continue;

            if (probe.Version < MinimumVersion)
            {
                continue;
            }

            return ToolchainResolution.Found(new ToolchainInfo
            {
                LanguageId = "zig",
                ExecutablePath = probe.Executable,
                DisplayName = $"Zig {probe.Version} ({Path.GetFileName(probe.Executable)})",
                Version = probe.Version,
                Source = "System"
            });
        }

        var guidance = new MissingToolchainGuidance(
            "Zig Compiler Not Found",
            "Zig compiler is not installed or not available on PATH.",
            _host.IsMacOS
                ? ["Run 'brew install zig' in your terminal.", "Or download Zig from https://ziglang.org/download/."]
                : _host.IsWindows
                    ? ["Run 'winget install zig.zig' in PowerShell.", "Or download from https://ziglang.org/download/."]
                    : ["Install via 'sudo snap install zig --classic --beta' or package manager."],
            "https://ziglang.org/download/");

        return ToolchainResolution.NotFound(guidance);
    }

    public async Task<IReadOnlyList<ToolchainInfo>> ListAsync(ToolchainQuery query, CancellationToken ct = default)
    {
        var candidates = await GetCandidatesAsync(query, ct).ConfigureAwait(false);
        var list = new List<ToolchainInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidates)
        {
            var probe = await ProbeAsync(path, ct).ConfigureAwait(false);
            if (probe == null || probe.Version < MinimumVersion) continue;

            if (seen.Add(probe.Executable))
            {
                list.Add(new ToolchainInfo
                {
                    LanguageId = "zig",
                    ExecutablePath = probe.Executable,
                    DisplayName = $"Zig {probe.Version}",
                    Version = probe.Version,
                    Source = "System"
                });
            }
        }

        return list;
    }

    public async Task<ToolchainActionResult> RunActionAsync(string actionId, ToolchainQuery query, Action<string> output, CancellationToken ct = default)
    {
        switch (actionId)
        {
            case "open-download":
                BrowserLauncher.Open("https://ziglang.org/download/");
                return new ToolchainActionResult(true, "Opened https://ziglang.org/download/ in browser.");

            case "brew-install-zig":
                output("Installing Zig via Homebrew (brew install zig)...\n");
                var brewResult = await ToolchainSetupRunner.ExecuteAsync("brew install zig", _host, _launcher, output, ct);
                Refresh();
                return new ToolchainActionResult(brewResult, brewResult ? "Zig compiler installed." : "Failed to install Zig via brew.");

            default:
                return new ToolchainActionResult(false, $"Unknown action {actionId}");
        }
    }

    private async Task<List<string>> GetCandidatesAsync(ToolchainQuery query, CancellationToken ct)
    {
        var list = new List<string>();

        var selected = SelectedPath;
        if (!string.IsNullOrWhiteSpace(selected) && _host.FileExists(selected))
        {
            list.Add(selected);
        }

        // 1. Common system paths
        if (_host.IsMacOS)
        {
            list.Add("/opt/homebrew/bin/zig");
            list.Add("/usr/local/bin/zig");
            list.Add(Path.Combine(_host.HomeDirectory, ".zig", "zig"));
        }
        else if (_host.IsWindows)
        {
            list.Add(@"C:\Program Files\zig\zig.exe");
            list.Add(@"C:\ProgramData\chocolatey\bin\zig.exe");
            list.Add(Path.Combine(_host.HomeDirectory, "scoop", "shims", "zig.exe"));
            list.Add(Path.Combine(_host.HomeDirectory, ".zig", "zig.exe"));
        }
        else
        {
            list.Add("/usr/bin/zig");
            list.Add("/usr/local/bin/zig");
            list.Add("/snap/bin/zig");
            list.Add(Path.Combine(_host.HomeDirectory, ".zig", "zig"));
        }

        // 2. PATH resolution (including login shell path for macOS / Linux)
        var pathEnv = _host.GetEnvironmentVariable("PATH") ?? string.Empty;
        var loginPath = await _host.GetLoginShellPathAsync(ct).ConfigureAwait(false);
        var separator = _host.IsWindows ? ';' : ':';
        var binaryName = _host.IsWindows ? "zig.exe" : "zig";

        var combined = string.IsNullOrEmpty(loginPath) ? pathEnv : $"{pathEnv}{separator}{loginPath}";
        foreach (var dir in combined.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), binaryName);
            if (_host.FileExists(full))
            {
                list.Add(full);
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<ProbeResult?> ProbeAsync(string executable, CancellationToken ct)
    {
        var probe = _probes.GetOrAdd(executable, p => new Lazy<Task<ProbeResult?>>(() => RunProbeAsync(p, ct)));
        try
        {
            return await probe.Value.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _probes.TryRemove(new KeyValuePair<string, Lazy<Task<ProbeResult?>>>(executable, probe));
            throw;
        }
    }

    private async Task<ProbeResult?> RunProbeAsync(string executable, CancellationToken ct)
    {
        if (!_host.FileExists(executable)) return null;

        try
        {
            var result = await _host.RunAsync(executable, ["version"], TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return null;
            }

            var text = result.StandardOutput.Trim();
            var match = VersionRegex.Match(text);
            if (match.Success && Version.TryParse(match.Groups["version"].Value, out var ver))
            {
                return new ProbeResult(executable, ver);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private sealed record ProbeResult(string Executable, Version Version);
}
