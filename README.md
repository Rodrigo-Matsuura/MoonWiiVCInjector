# 🚀 Moon WiiVC Injector - Modernized Edition

[![Build Status](https://img.shields.io/badge/Build-Passing-success?style=for-the-badge&logo=.net&color=31c754)](https://github.com/Rodrigo-Matsuura/TeconmoonWiiVCInjector)
[![Framework](https://img.shields.io/badge/.NET-9.0-512bd4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![UI Framework](https://img.shields.io/badge/UI-Avalonia%2012-purple?style=for-the-badge&logo=avalonia)](https://avaloniaui.net/)
[![Vibe Coding](https://img.shields.io/badge/Developed%20with-Vibe%20Coding-pink?style=for-the-badge&logo=ai)](https://github.com/Rodrigo-Matsuura/TeconmoonWiiVCInjector)
[![License](https://img.shields.io/badge/License-GPLv3-blue?style=for-the-badge)](LICENSE)

A modernized, optimized, and cross-platform fork of the classic **[piratesephiroth/TeconmoonWiiVCInjector](https://github.com/piratesephiroth/TeconmoonWiiVCInjector)**, maintained under **[Rodrigo-Matsuura/TeconmoonWiiVCInjector](https://github.com/Rodrigo-Matsuura/TeconmoonWiiVCInjector)**.

This project completely rewrites and modernizes the original Wii and GameCube Virtual Console Injector for Wii U. The tool has been re-architected from the ground up to eliminate legacy technical debt, adopt modern asynchronous C# and .NET standards, implement a decoupled MVVM architecture, and deliver major performance and user experience improvements.

---

## 🎯 Project Focus & Vision

The primary mission of this modernized edition is to provide a **clean, fast, portable, and reliable** injection experience:

1. **Modern Foundation**: Fully powered by **.NET 9** and **Avalonia UI**, removing dependencies on legacy Windows Forms and obsolete .NET Framework runtimes.
2. **Robust Architecture**: Built with clean separation of concerns using the **MVVM (Model-View-ViewModel)** pattern and dependency-injected services.
3. **High Efficiency**: Minimized memory footprint and CPU overhead during heavy ISO extraction and NFS repackaging.
4. **Enhanced UX**: Modern desktop interactions including Drag & Drop, real-time logging, and safe task cancellation.
5. **Zero-Elevation Portability**: Completely self-contained and portable with no administrator privileges or Windows Registry dependencies.

---

## ⚡ Key Improvements Over the Original

### 🖥️ 1. Modern Cross-Platform UI & MVVM Architecture
* **Avalonia UI Modernization**: Replaced legacy Windows Forms with Avalonia UI for clean rendering, consistent themes, and cross-platform UI capabilities.
* **CommunityToolkit.Mvvm**: Clean separation between presentation and business logic using reactive ViewModels, observable properties, and asynchronous commands.
* **Decoupled Dialog Services**: Abstracted file pickers, folder dialogs, and notifications behind a mockable and testable `IDialogService`.

### 🚀 2. Performance & Low-Level Memory Optimization
* **`ArrayPool<byte>` Buffer Management**: Reusable buffer pooling in `Nfs2Iso2Nfs` drastically reduces Garbage Collector (GC) allocations and memory spikes when handling multi-gigabyte disk images.
* **Instant In-Memory GameTDB Caching**: Game database queries (`wiitdb.txt`) are parsed once into static in-memory lookup dictionaries (`O(1)`), eliminating slow, line-by-line disk reads.
* **Singleton `HttpClient` Stack**: Replaced deprecated `WebClient` and `HttpWebRequest` with an optimized, pooled `HttpClient` for fast remote banner/sound downloads and lightweight `HEAD` connectivity checks.

### 🎮 3. Expanded Game Format & Toolchain Support
* **Out-of-the-Box Compression Support**: Native support for compressed disk images, including **NKIT** (`.nkit.iso`) and **NASOS** (`.iso.dec` / `.dec`), without requiring manual decompression.
* **Smart Tool & JAR Resolution**: Enhanced process launcher automatically discovers local Java `.jar` dependencies and native binaries before falling back to system environments or Wine wrappers.

### ✨ 4. Enhanced User Experience & Controls
* **Drag-and-Drop Workflow**: Drag game ROMs, icons, banners, and boot sounds directly into the interface.
* **Build Cancellation Support**: Safely cancel long conversion processes at any moment with automatic cleanup of temporary files.
* **Real-Time Logs Viewer**: Dedicated in-app logging tab powered by `AppLogger` to inspect conversion progress and diagnose issues easily.

### 🔒 5. Portability, Cleanliness & Safety
* **Registry-Free Settings**: All encryption keys (Wii Common Key, Title Key, Ancast Key) and user preferences are stored in local configuration files rather than the Windows Registry.
* **No Administrator Privileges Required**: Runs fully with standard user permissions.
* **Clean Codebase**: 0 warnings, 0 errors, modernized with file-scoped namespaces and nullable reference types enabled.

---

## 🛠️ How to Build and Run

### Prerequisites
* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or higher.

### Run Locally
```bash
dotnet run --project "Moon WiiVC Injector"
```

### Build Solution
```bash
# Build in Release mode
dotnet build "Moon WiiVC Injector.sln" -c Release
```

### Publish Self-Contained Executables

#### Windows (x64)
```bash
dotnet publish "Moon WiiVC Injector/Moon WiiVC Injector.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o publish/win-x64
```

#### Linux (x64)
```bash
dotnet publish "Moon WiiVC Injector/Moon WiiVC Injector.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o publish/linux-x64
```

---

## 🤝 Contributing

This project is built and maintained with **Vibe Coding**. Contributions, optimizations, and ideas are warmly welcomed! If you spot any unexpected behavior, want to suggest additional optimizations, or port and modernize other aspects of the codebase, feel free to open an **Issue** or submit a **Pull Request (PR)**!

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0 (GPLv3)**. See the [LICENSE](LICENSE) file for details.
