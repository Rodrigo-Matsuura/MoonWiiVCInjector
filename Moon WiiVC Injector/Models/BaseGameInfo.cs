using System;
using System.Collections.Generic;
using System.Linq;

namespace Moon_WiiVC_Injector.Models;

public record BaseGameInfo(
    string RegionCode,
    string DisplayName,
    string GameName,
    string Version,
    string TitleId,
    string KeyExpectedHash,
    string FolderName,
    string HtkHash
)
{
    public static readonly BaseGameInfo USA = new(
        RegionCode: "USA",
        DisplayName: "USA - Rhythm Heaven Fever",
        GameName: "Rhythm Heaven Fever",
        Version: "v0",
        TitleId: "00050000101B0700",
        KeyExpectedHash: "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F",
        FolderName: "Rhythm Heaven Fever [VAKE01]",
        HtkHash: "C99CAF5995E395F39C3FCAB4A8AF20E0"
    );

    public static readonly BaseGameInfo EUR = new(
        RegionCode: "EUR",
        DisplayName: "EUR - Beat the Beat: Rhythm Paradise",
        GameName: "Beat the Beat: Rhythm Paradise",
        Version: "v0",
        TitleId: "00050000101B0800",
        KeyExpectedHash: "10-20-7C-95-47-CA-80-4F-F1-B5-D5-26-E1-C7-6B-EB",
        FolderName: "Beat the Beat Rhythm Paradise [VAKP01]",
        HtkHash: "C6E40973C6D1F8983A6A1DF65D90F6A6"
    );

    public static readonly IReadOnlyList<BaseGameInfo> SupportedBases = [USA, EUR];

    public static BaseGameInfo FindByRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return USA;
        return SupportedBases.FirstOrDefault(b => string.Equals(b.RegionCode, region, StringComparison.OrdinalIgnoreCase)) ?? USA;
    }

    public static BaseGameInfo? FindByKeyHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;
        return SupportedBases.FirstOrDefault(b => string.Equals(b.KeyExpectedHash, hash, StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString() => DisplayName;
}
