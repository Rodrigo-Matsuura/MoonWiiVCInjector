// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System.Collections.Generic;
using System.IO;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public class NusPackageConfiguration
{
    public readonly string dir;
    public readonly AppXMLInfo appInfo;
    public readonly Key encryptionKey;
    public readonly Key encryptKeyWith;
    public readonly List<ContentRule> rules;
    public string TempDirectory { get; set; }

    public NusPackageConfiguration(string dir, AppXMLInfo appInfo, Key encryptionKey, Key encryptKeyWith, List<ContentRule> rules, string? tempDirectory = null)
    {
        this.dir = dir;
        this.appInfo = appInfo;
        this.encryptionKey = encryptionKey;
        this.encryptKeyWith = encryptKeyWith;
        this.rules = rules;
        this.TempDirectory = string.IsNullOrWhiteSpace(tempDirectory) ? Path.Combine(dir, "tmp") : tempDirectory;
    }
}

