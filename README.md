# FrySharp.Zig

[![FrySharp Extension](https://img.shields.io/badge/FrySharp-Extension-007ACC.svg)](https://github.com/PrashantUnity/CSharpPlayground)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Zig](https://img.shields.io/badge/Zig-0.13%2B-F7A41D.svg)](https://ziglang.org)

**First-class Zig language extension for FrySharp**, bringing modern systems programming, instant script execution, and ZLS autocompletion to your .NET 10 document automation and code studio environment.

---

## Features

- ⚡ **Instant Execution**: Run `.zig` scripts instantly using `zig run` with live interactive stdin/stdout in the Terminal panel.
- 🛠️ **Automatic Toolchain Discovery**: Finds `zig` on your system (`PATH`, Homebrew, Scoop, Chocolatey, Snap, or `~/.zig/`) with one-click installation guidance.
- 🔍 **Problems Diagnostics**: Real-time compiler error and warning parsing (`file:line:col`) with click-to-navigate code jumps.
- 🧠 **ZLS Language Server**: Connects automatically to `zls --stdio` for autocompletion, hover documentation, and signature hints.
- 🎨 **AvaloniaEdit Highlighting**: Full dark and light syntax coloring definitions.
- 📚 **Learning Center Integration**: Built-in Zig guide and systems programming articles.

---

## Installation in FrySharp

### Method 1: Git URL (Unity UPM-style)
In FrySharp, open the Command Palette (`Ctrl+Shift+P` / `Cmd+Shift+P`) and choose:
```
Extensions: Install from Git URL
```
Enter the repository URL:
```
https://github.com/PrashantUnity/FrySharp.Zig.git#v1.0.0
```

Or add it directly to your workspace's `.frysharp/extensions.json`:
```json
{
  "dependencies": {
    "com.frysharp.zig": "https://github.com/PrashantUnity/FrySharp.Zig.git#v1.0.0"
  }
}
```

### Method 2: Local Extension
Clone this repository into your workspace `extensions/` directory:
```bash
git clone https://github.com/PrashantUnity/FrySharp.Zig.git extensions/zig-support
```
FrySharp will automatically detect, compile in-memory, and activate the extension with hot-reload enabled.

---

## Requirements

- **FrySharp** v1.0.0 or higher.
- **Zig Compiler** (v0.11, v0.12, v0.13, or v0.14-dev) — [Download Zig](https://ziglang.org/download/).
- *(Optional)* **ZLS (Zig Language Server)** for code completion — [Install ZLS](https://github.com/zigtools/zls).

---

## License

MIT License. Copyright (c) 2026 Code Fry Dev.
