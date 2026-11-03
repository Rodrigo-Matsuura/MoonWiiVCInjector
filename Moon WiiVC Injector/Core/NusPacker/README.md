# C# Port of NUSPacker (CNUS_Packer)

This module provides a pure C# in-process packaging engine for Wii U installable WUP packages (`.app`, `.h3`, `title.tmd`, `title.tik`, `title.cert`), completely removing the requirement for an external Java Runtime Environment (`java.exe`) or `NUSPacker.jar`.

## Original Project & Attribution

* **Base Project**: [CNUS_Packer](https://github.com/NicoAICP/CNUS_Packer)
* **Authors**: [NicoAICP](https://github.com/NicoAICP) and [Morilli](https://github.com/Morilli)
* **Original Java Tool**: [NUSPacker](https://github.com/Maschell/NUSPacker) by [timogus](https://github.com/timogus) and [Maschell](https://github.com/Maschell)

## Consolidated Architecture & Modernizations

The original 25 scattered classes were consolidated into 6 domain-focused modules:

1. **`NusPackerEngine.cs`**:
   - High-level asynchronous entry point (`PackAsync`), orchestrating scanning, rule application, FST generation, and content packaging with cancellation and UI progress reporting.
2. **`Metadata/`**:
   - `AppXmlParser.cs`: Clean model and XML reader for `app.xml` (dead fields `sdkVersion`, `osMask`, `commonID` eliminated).
   - `TitleMetadata.cs`: WUP title metadata generators (`TitleTmd`, `TitleTicket`, and static cached `TitleCert`).
3. **`FileSystem/`**:
   - `FstTable.cs`: File System Table hierarchy (`FstTable`, `FstEntry`, `FstEntriesCollection`, and typed `FstEntryType`).
4. **`Packaging/`**:
   - `NusPackage.cs`: Packages contents into `.app` archives and stages intermediate `.dec` files with immediate cleanup.
   - `ContentRules.cs`: File grouping rules (`ContentRule`, `ContentRulesService`, and `ContentDetails`).
5. **`Crypto/`**:
   - `NusCrypto.cs`: Unified AES-128-CBC encryption (`NusEncryption`) and tree hashing (`ContentHashes`). Redundant wrapper classes (`Key`, `IV`, `HashUtil`) eliminated in favor of native .NET BCL primitives.
6. **`Utils/`**:
   - `NusUtils.cs`: Binary alignment formulas, directory cleanup, and `BigEndianMemoryStream` powered by `BinaryPrimitives`.
