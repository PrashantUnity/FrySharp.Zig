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

    public async Task<IReadOnlyList<ToolchainInfo>> DiscoverAsync(CancellationToken ct = default)
    {
        var discovered = new List<ToolchainInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. User-selected path
        var selected = SelectedPath;
        if (!string.IsNullOrWhiteSpace(selected) && _host.FileExists(selected))
        {
            var probed = await ProbeAsync(selected, ct).ConfigureAwait(false);
            if (probed != null && seenPaths.Add(probed.ExecutablePath))
            {
                discovered.Add(probed.ToToolchainInfo(isCustomUserSelection: true));
            }
        }

        // 2. PATH resolution via login shell
        var pathEnv = await _host.GetLoginShellPathAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(segment.Trim(), _host.IsWindows ? "zig.exe" : "zig");
                if (_host.FileExists(candidate) && seenPaths.Add(candidate))
                {
                    var probed = await ProbeAsync(candidate, ct).ConfigureAwait(false);
                    if (probed != null)
                    {
                        discovered.Add(probed.ToToolchainInfo());
                    }
                }
            }
        }

        // 3. Known well-known installation locations
        foreach (var candidate in GetWellKnownLocations())
        {
            if (_host.FileExists(candidate) && seenPaths.Add(candidate))
            {
                var probed = await ProbeAsync(candidate, ct).ConfigureAwait(false);
                if (probed != null)
                {
                    discovered.Add(probed.ToToolchainInfo());
                }
            }
        }

        return discovered;
    }

    public async Task<ToolchainInfo?> ResolveActiveAsync(CancellationToken ct = default)
    {
        var all = await DiscoverAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(t => t.IsSelected) ?? all.FirstOrDefault();
    }

    public Task SelectAsync(string executablePath, CancellationToken ct = default)
    {
        _settings.SetSelectedPath("zig", executablePath);
        return Task.CompletedTask;
    }

    public Task ClearSelectionAsync(CancellationToken ct = default)
    {
        _settings.ClearSelectedPath("zig");
        return Task.CompletedTask;
    }

    public async Task<ProbeResult?> ProbeAsync(string executablePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        var lazy = _probes.GetOrAdd(executablePath, path => new Lazy<Task<ProbeResult?>>(() => ProbeInternalAsync(path, ct)));
        return await lazy.Value.ConfigureAwait(false);
    }

    private async Task<ProbeResult?> ProbeInternalAsync(string executablePath, CancellationToken ct)
    {
        try
        {
            var result = await _host.RunAsync(executablePath, ["version"], TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            if (result.ExitCode != 0) return null;

            var match = VersionRegex.Match(result.StandardOutput.Trim());
            if (match.Success && Version.TryParse(match.Groups["version"].Value, out var ver))
            {
                return new ProbeResult(
                    LanguageId: "zig",
                    ExecutablePath: executablePath,
                    Version: ver,
                    RawVersionString: result.StandardOutput.Trim(),
                    DisplayName: $"Zig {ver.Major}.{ver.Minor}.{ver.Build}");
            }
        }
        catch { }

        return null;
    }

    private IEnumerable<string> GetWellKnownLocations()
    {
        if (_host.IsMacOS)
        {
            yield return "/opt/homebrew/bin/zig";
            yield return "/usr/local/bin/zig";
            yield return Path.Combine(_host.HomeDirectory, ".zig", "zig");
        }
        else if (_host.IsWindows)
        {
            yield return @"C:\Program Files\zig\zig.exe";
            yield return @"C:\ProgramData\chocolatey\bin\zig.exe";
            yield return Path.Combine(_host.HomeDirectory, "scoop", "shims", "zig.exe");
            yield return Path.Combine(_host.HomeDirectory, ".zig", "zig.exe");
        }
        else
        {
            yield return "/usr/bin/zig";
            yield return "/usr/local/bin/zig";
            yield return "/snap/bin/zig";
            yield return Path.Combine(_host.HomeDirectory, ".zig", "zig");
        }
    }
}
