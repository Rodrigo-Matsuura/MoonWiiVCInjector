# C# Port of NUSPacker (CNUS_Packer)

This module provides a pure C# in-process packaging engine for Wii U installable WUP packages (`.app`, `.h3`, `title.tmd`, `title.tik`, `title.cert`), completely removing the requirement for an external Java Runtime Environment (`java.exe`) or `NUSPacker.jar`.

## Original Project & Attribution

* **Base Project**: [CNUS_Packer](https://github.com/NicoAICP/CNUS_Packer)
* **Authors**: [NicoAICP](https://github.com/NicoAICP) and [Morilli](https://github.com/Morilli)
* **Original Java Tool**: [NUSPacker](https://github.com/Maschell/NUSPacker) by [timogus](https://github.com/timogus) and [Maschell](https://github.com/Maschell)

## Enhancements in MoonWiiVCInjector

1. **Modern .NET 9 Architecture**:
   - Modernized syntax, file-scoped namespaces, and nullable reference types.
   - Low-allocation streaming SHA-1 hashing in `HashUtil` (replaces whole-file in-memory buffer allocations).
2. **In-App Progress & UI Integration**:
   - Injected real-time progress callbacks (`IProgress<(string Status, double Percentage)>`) reporting packing status directly to Avalonia UI.
3. **Safe Async & Cancellation Support**:
   - Integrated `CancellationToken` checks across directory scanning, hashing, decrypted staging, and AES-CBC encryption.
4. **Reliability & State Safety**:
   - Fixed FST static state reuse bug to allow multiple consecutive packaging runs in the same application session without file table corruption.
   - Replaced all `Environment.Exit()` calls with informative C# exceptions (`InvalidOperationException`).
   - Configurable isolated temporary directory with immediate cleanup of intermediate `.dec` files to prevent double disk space consumption.

