# 🚀 Moon WiiVC Injector - Modernized Edition (v4.0.0.0)

[![Build Status](https://img.shields.io/badge/Build-Succeeded-success?style=for-the-badge&logo=.net&color=31c754)](https://github.com/Rodrigo-Matsuura/TeconmoonWiiVCInjector)
[![Version](https://img.shields.io/badge/Version-4.0.0.0-blue?style=for-the-badge&color=007acc)](Moon%20WiiVC%20Injector/Properties/AssemblyInfo.cs)
[![Vibe Coding](https://img.shields.io/badge/Developed%20with-Vibe%20Coding-pink?style=for-the-badge&logo=ai)](https://github.com/Rodrigo-Matsuura/TeconmoonWiiVCInjector)

This is a modernized, optimized, and polished fork of the classic **[piratesephiroth/TeconmoonWiiVCInjector](https://github.com/piratesephiroth/TeconmoonWiiVCInjector)**, maintained under **[Rodrigo-Matsuura/TeconmoonWiiVCInjector](https://github.com/Rodrigo-Matsuura/TeconmoonWiiVCInjector)**.

The original tool for injecting Wii and GameCube games into the Wii U Virtual Console has been completely refactored to eliminate years of technical debt, introduce ultra-fast in-memory caching, modernize network connectivity, and add native support for modern compressed image formats.

---

## 🛠 Features & Improvements in v4.0.0.0

Here is the detailed changelog and architectural refactoring breakdown of all changes implemented since the first commit of this fork up to the stable **v4.0.0.0** release:

### 📦 1. Out-of-the-Box NKIT & NASOS Support
* **Compressed Formats**: Added full native support for reading and converting compressed images in `.nkit.iso` and `.iso.dec` / `.dec` formats, saving users the trouble of manually decompressing images beforehand.

### ⚡ 2. Drastic Database Performance Caching (wiitdb)
* **Instant `O(1)` Lookups**: Completely refactored the database query engine in `GameTdb.cs`. The dictionary database `wiitdb.txt` (334 KB) is now read and parsed **exactly once** in a thread-safe static constructor.
* **In-Memory Cache**: Game name and ID queries now utilize static in-memory `Dictionary` lookups, replacing the slow, sequential, line-by-line disk I/O reads (`O(N)`).

### 🌐 3. High-Performance Network Stack (`HttpClient`)
* **Deprecating Legacy WebClient/HttpWebRequest**: Modernized all network interactions across the codebase by completely removing obsolete classes.
* **Singleton HttpClient**: Introduced a single, static, reusable `HttpClient` instance (`Program.Client`) shared globally. This prevents socket exhaustion, optimizes connection pooling, and significantly accelerates download speeds for game banners (Cucholix's Repo) and Nintendont files.
* **Lightweight Connection Checks**: Centralized internet verification to perform lightweight `HEAD` requests (`HttpMethod.Head`) instead of full downloads, resulting in an instant app startup.

### 🔒 4. Enhanced Portability & Security (No Admin Required)
* **Registry-Free Key Storage**: Migrated critical Wii U cryptography keys (Common Key, Title Key, and Ancast Key) from the global Windows Registry to the application's internal **User Settings (`Settings`)**.
* **Zero Elevation Required**: The program now runs flawlessly without requiring "Run as Administrator" privileges, making it significantly safer and fully portable.

### 🧼 5. Clean Codebase & Warning-Free Build
* **0 Warnings, 0 Errors**: Resolved all compilation warnings (including `CS0169` and `CS0414`). Removed more of the class-level variables and dead fields leftover from older iterations.
* **Decoupled Process Execution**: Refactored the external program launcher (`LaunchProgram`) to eliminate class-level global flags in favor of safe, parameter-based execution arguments.
* **Path Safety**: Converted manual path concatenations to robust `Path.Combine` calls, ensuring path safety and cross-platform compatibility.

### 🐧 6. Cross-Platform Compilation out of the Box
* **Linux/Mono Friendly**: Disabled ClickOnce manifest generation in the `.csproj` file, resolving a classic Mono/MSBuild bug that blocked direct Linux compilation. You can now build and package the `.exe` seamlessly on any operating system!

### 🧹 7. Native Dialogs & Clean Dependencies
* **No Legacy CodePack**: Removed the desupported `Microsoft.WindowsAPICodePack` dependency. The project now uses the native `FolderBrowserDialog` from .NET Framework 4.8.1, which automatically uses the modern Vista/Windows 10+ explorer style layout.
* **NuGet Purge**: Cleaned up the `packages.config` and `.csproj` of dozens of redundant .NET Standard transitive packages, linking standard System APIs directly through the native .NET 4.8.1 GAC assemblies.

---

## 🚀 How to Build

### On Windows (Visual Studio)
1. Open the solution file `Moon WiiVC Injector.sln`.
2. Restore the NuGet packages.
3. Select the `Release` configuration and click **Build Solution**.

### On Linux (Mono / MSBuild)
Ensure you have the Mono development toolkit installed, then run the following in the repository root:
```bash
# To generate a Debug build
msbuild "Moon WiiVC Injector.sln" /t:Build /p:Configuration=Debug

# To generate the highly optimized Release build
msbuild "Moon WiiVC Injector.sln" /t:Build /p:Configuration=Release
```

---

## 🤝 Vibe Coding & Contributions

This project is actively maintained under the **Vibe Coding** philosophy! 🎸  
If you spot any unexpected behavior, want to suggest additional optimizations, or port more parts of the legacy codebase to modern C# standards, feel free to submit a **Pull Request (PR)**! All contributions are highly appreciated.

