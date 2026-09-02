using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NKit;

public class Converter : ILog
{
    public event EventHandler<MessageEventArgs> LogMessage;
    public event EventHandler<ProgressEventArgs> LogProgress;

    private List<Tuple<string, LogMessageType>> _logCache;
    private readonly Lock _logCacheLock = new();
    private bool _inProgress;
    private int _completedPasses;
    private int _totalPasses;
    private readonly bool _forcedWiiFullNkitRebuild = false;

    private Context _context;
    private readonly SourceFile _sourceFile;
    private readonly bool _cacheLogsWhileProcessing;
    private readonly Settings _settings;
    private readonly NStream _nstream;
    private int _detailLinesOutput;

    internal NStream NStream => _nstream;
    public Settings Settings => _settings;
    public string ConvertionName { get; private set; }

    public Converter(SourceFile sourceFile, bool cacheLogsWhileProcessing)
    {
        _inProgress = false;
        _cacheLogsWhileProcessing = cacheLogsWhileProcessing;
        _logCache = null;
        _sourceFile = sourceFile;
        _nstream = sourceFile.OpenNStream();
        _detailLinesOutput = 0;
        _settings = new Settings(_nstream.IsGameCube ? DiscType.GameCube : DiscType.Wii);
    }

    public OutputResults ConvertToIso(bool testMode = false)
    {
        return Process("ConvertToISO", _sourceFile, testMode, ns =>
        {
            List<Processor> p = [];

            if (ns.IsGameCube)
            {
                if (ns.IsNkit)
                {
                    p.Add(new Processor(new NkitReaderGc(), new IsoWriter(), "To ISO", this, true, false, ProcessorSizeMode.Image));
                    p[0].Reader.RequireValidationCrc = true;
                    p[0].Reader.RequireVerifyCrc = true;
                    p[0].Reader.VerifyIsWrite = false;
                }
                else
                {
                    p.Add(new Processor(new IsoReader(), new IsoWriter(), "To ISO", this, true, false, ProcessorSizeMode.Stream));
                    p[0].Reader.RequireValidationCrc = true;
                }
            }
            else
            {
                if (ns.IsNkit)
                {
                    p.Add(new Processor(new NkitReaderWii(), new IsoWriter(), "To ISO", this, true, false, ProcessorSizeMode.Image));
                    p[0].Reader.RequireValidationCrc = true;
                    p[0].Reader.RequireVerifyCrc = true;
                    p[0].Reader.VerifyIsWrite = false;
                }
                else
                {
                    p.Add(new Processor(new IsoReader { EncryptWiiPartitions = ns.IsIsoDec }, new IsoWriter(), "To ISO", this, true, false, ProcessorSizeMode.Stream));
                    p[0].Reader.RequireValidationCrc = true;
                }
            }
            return p;
        });
    }

    public OutputResults ConvertToIso()
    {
        return ConvertToIso(_settings.TestMode);
    }

    private OutputResults Process(string conversion, SourceFile sourceFile, bool testMode, Func<NStream, IEnumerable<Processor>> passes)
    {
        OutputResults results = null;
        NStream nstream = _nstream;
        string lastTmp = null;
        string tmp = null;

        ConvertionName = conversion;

        try
        {
            SourceFile sf = null;
            long sourceSize = nstream.SourceSize;

            _context = new Context();
            _context.Initialise(ConvertionName, sourceFile, _settings, this);

            List<Processor> processors = [.. passes(nstream).Where(a => a != null)];
            _totalPasses = processors.Count;

            if (processors.Count == 0)
                return null;

            DateTime dt = DateTime.Now;
            _completedPasses = 0;

            Log("PROCESSING" + (testMode ? " [TEST MODE]" : ((_context.Settings.DeleteSource ? " [DELETE SOURCE]" : ""))));
            Log("-------------------------------------------------------------------------------");
            if (_forcedWiiFullNkitRebuild)
            {
                LogBlank();
                Log($"Nkit Reencode forced: NkitUpdatePartitionRemoval is {_context.Settings.NkitUpdatePartitionRemoval} and source image has {(nstream.IsNkitUpdateRemoved ? "no" : "an")} Update Partition");
                LogBlank();
            }
            Log($"{Friendly(nstream.Title)} [{Friendly(nstream.Id)}]  {(nstream.IsGameCube ? "GameCube" : "Wii")}  [MiB:{(sourceSize / (double)(1024 * 1024)):#0.0}]");
            LogBlank();
            string passesText = GetPassesLine(nstream, processors);
            Log(passesText);

            int i = 1;
            foreach (Processor pro in processors.Where(pro => pro != null))
                LogDebug($"Pass {i++}: {pro}");
            LogBlank();

            foreach (Processor pro in processors)
            {
                if (sf != null)
                {
                    nstream = sf.OpenNStream(true);
                    sf = null;
                }

                tmp = null;
                FileStream tmpFs = null;

                try
                {
                    if (pro.HasWriteStream)
                    {
                        tmp = Path.Combine(_context.Settings.TempPath, Path.GetFileName(Path.GetTempFileName()));
                        if (!Directory.Exists(_context.Settings.TempPath))
                            Directory.CreateDirectory(_context.Settings.TempPath);
                        tmpFs = File.Create(tmp, 0x400 * 0x400 * 4, FileOptions.SequentialScan);
                    }

                    _logCache = [];
                    OutputResults nr = pro.Process(_context, nstream, tmpFs ?? Stream.Null);
                    _logCache = null;

                    if (results == null)
                    {
                        results = nr;
                        results.DiscType = nstream.IsGameCube ? DiscType.GameCube : DiscType.Wii;
                        results.InputFileName = sourceFile.AllFiles.Length != 0 ? sourceFile.AllFiles[0] : sourceFile.FilePath;
                        results.InputDiscNo = nstream.DiscNo;
                        results.InputDiscVersion = nstream.Version;
                        results.InputTitle = nstream.Title;
                        results.InputId8 = nstream.Id8;
                        results.InputSize = sourceSize;
                        results.FullSize = nstream.ImageSize;
                        results.Passes = passesText;
                        if (pro.IsVerify)
                            results.OutputSize = results.InputSize;
                    }
                    else
                    {
                        if (nr.AliasJunkId != null)
                            results.AliasJunkId = nr.AliasJunkId;
                        if (nr.OutputTitle != null)
                        {
                            results.OutputDiscNo = nr.OutputDiscNo;
                            results.OutputDiscVersion = nr.OutputDiscVersion;
                            results.OutputTitle = nr.OutputTitle;
                        }
                        results.OutputId8 = nr.OutputId8;
                        results.OutputCrc = nr.OutputCrc;
                        results.OutputPrePatchCrc = nr.OutputPrePatchCrc;
                        results.FullSize = nstream.ImageSize;
                        if (tmp != null)
                            results.OutputSize = nr.OutputSize;

                        if (nr.ValidationCrc != 0 && results.VerifyCrc != 0)
                            results.VerifyCrc = 0;

                        if (nr.VerifyCrc != 0)
                            results.VerifyCrc = nr.VerifyCrc;
                        if (nr.ValidationCrc != 0)
                            results.ValidationCrc = nr.ValidationCrc;
                        if (nr.ValidateReadResult != VerifyResult.Unverified)
                            results.ValidateReadResult = nr.ValidateReadResult;
                        if (nr.VerifyOutputResult != VerifyResult.Unverified)
                        {
                            if (results.ValidateReadResult == VerifyResult.Unverified && nstream.IsNkit)
                                results.ValidateReadResult = nr.VerifyOutputResult;
                            results.VerifyOutputResult = nr.VerifyOutputResult;
                        }
                        if (nr.IsRecoverable)
                            results.IsRecoverable = nr.IsRecoverable;
                    }
                }
                finally
                {
                    tmpFs?.Dispose();
                    nstream.Close();
                }

                if (lastTmp != null && tmp != null)
                    File.Delete(lastTmp);

                _completedPasses++;

                if (results.ValidateReadResult == VerifyResult.VerifyFailed || results.VerifyOutputResult == VerifyResult.VerifyFailed)
                {
                    results.OutputFileName = null;
                    break;
                }

                if (_completedPasses != _totalPasses)
                    sf = SourceFiles.OpenFile(tmp ?? lastTmp);

                if (tmp != null)
                    lastTmp = tmp;
            }

            TimeSpan ts = DateTime.Now - dt;
            results.ProcessingTime = ts;

            if (results.ValidateReadResult == VerifyResult.VerifyFailed || results.VerifyOutputResult == VerifyResult.VerifyFailed)
            {
                LogBlank();
                Log($"Verification Failed Crc:{results.OutputCrc:X8} - Failed Test Crc:{results.ValidationCrc:X8}");

                if (lastTmp != null)
                {
                    Log("Deleting Output" + (Settings.OutputLevel != 3 ? "" : " (Skipped as OutputLevel is 3:Debug)"));
                    results.OutputFileName = null;
                    if (Settings.OutputLevel != 3)
                        File.Delete(lastTmp);

                    LogBlank();
                }
            }
            else
            {
                LogBlank();
                Log($"Completed ~ {(int)ts.TotalMinutes}m {ts.Seconds}s  [MiB:{(results.OutputSize / (double)(1024 * 1024)):#0.0}]");
                LogBlank();
                Log("RESULTS");
                Log("-------------------------------------------------------------------------------");

                results.OutputFileExt = "." + SourceFiles.ExtensionString(false, false, false, false).ToLower();
                results.RedumpInfo = new RedumpInfo { MatchType = MatchType.MatchFail, MatchName = "ISO" };
                LogBlank();

                OutputResultsInternal(results);

                if (lastTmp != null)
                {
                    if (testMode)
                    {
                        Log("TestMode: Deleting Output");
                        results.OutputFileName = null;
                        if (File.Exists(lastTmp))
                            File.Delete(lastTmp);
                    }
                    else
                    {
                        results.OutputFileName = SourceFiles.GetUniqueName(sourceFile.CreateOutputFilename(results.OutputFileExt));
                        Log("Renaming Output Based on Source File" + (sourceFile.AllFiles.Length > 1 ? "s" : ""));
                        LogBlank();

                        string path = Path.GetDirectoryName(results.OutputFileName);
                        if (!Directory.Exists(path))
                            Directory.CreateDirectory(path);

                        File.Move(lastTmp, results.OutputFileName);

                        Log($"Output: {Path.GetDirectoryName(results.OutputFileName)}");
                        Log($"    {Path.GetFileName(results.OutputFileName)}");

                        if (_context.Settings.DeleteSource && !testMode && results.VerifyOutputResult == VerifyResult.VerifySuccess)
                        {
                            LogBlank();
                            Log("Deleting Source:");
                            foreach (string s in sourceFile.AllFiles.Length == 0 ? [sourceFile.FilePath] : sourceFile.AllFiles)
                            {
                                Log($"    {s}");
                                File.Delete(s);
                            }
                        }
                    }
                    LogBlank();
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                lastTmp ??= tmp;
                if (lastTmp != null)
                {
                    LogBlank();
                    Log("Deleting Output" + (Settings.OutputLevel != 3 ? "" : " (Skipped as OutputLevel is 3:Debug)"));
                    if (results != null)
                        results.OutputFileName = null;
                    if (Settings.OutputLevel != 3)
                        File.Delete(lastTmp);
                }
            }
            catch { }

            if (_context.Settings.EnableSummaryLog)
            {
                results ??= new OutputResults
                {
                    Conversion = ConvertionName,
                    DiscType = (nstream?.IsGameCube ?? true) ? DiscType.GameCube : DiscType.Wii,
                    InputFileName = (sourceFile?.AllFiles?.Length ?? 0) == 0 ? (sourceFile?.FilePath ?? "") : (sourceFile?.AllFiles.FirstOrDefault() ?? ""),
                    InputDiscNo = nstream?.DiscNo ?? 0,
                    InputDiscVersion = nstream?.Version ?? 0,
                    InputTitle = nstream?.Title ?? "",
                    InputId8 = nstream?.Id8 ?? "",
                    InputSize = sourceFile?.Length ?? 0
                };
                results.VerifyOutputResult = VerifyResult.Error;
                HandledException hex = (ex as HandledException) ?? new HandledException(ex, "Unhandled Exception");
                results.ErrorMessage = hex.FriendlyErrorMessage;
            }
            throw;
        }
        finally
        {
            if (_context.Settings.EnableSummaryLog)
            {
                SummaryLog(_context.Settings, results);
                Log("Summary Log Written" + (results.VerifyOutputResult != VerifyResult.Error ? "" : " as Errored!"));
                LogBlank();
            }
        }

        return results;
    }

    private static void SummaryLog(Settings settings, OutputResults results)
    {
        try
        {
            if (!File.Exists(settings.SummaryLog))
                File.AppendAllText(settings.SummaryLog, string.Join("\t", "TimeStamp", "Conversion", "System", "ReadResult", "OutputResult", "OutputCrc", "OutputID4", "RedumpMatch", "RedumpName", "InputSize", "OutputSize", "FullSize", "InputFilename", "OutputFilename", "MD5", "SHA1", "Passes", "SecondsElapsed", "ErrorMessage") + Environment.NewLine);

            if (settings.EnableSummaryLog)
                File.AppendAllText(settings.SummaryLog, string.Join("\t",
                    DateTime.Now.ToString(),
                    results.Conversion,
                    results.DiscType.ToString(),
                    results.ValidateReadResult.ToString(),
                    results.VerifyOutputResult.ToString(),
                    results.OutputCrc.ToString("X8") ?? "",
                    results.OutputId4 ?? "",
                    (results.RedumpInfo?.MatchType.ToString() ?? "") + (results.IsRecoverable ? "Recoverable" : ""),
                    results.RedumpInfo?.MatchName ?? "",
                    results.InputSize.ToString(),
                    results.OutputSize.ToString(),
                    results.FullSize.ToString(),
                    results.InputFileName ?? "",
                    results.OutputFileName ?? "",
                    results.OutputMd5 == null ? "" : BitConverter.ToString(results.OutputMd5).Replace("-", ""),
                    results.OutputSha1 == null ? "" : BitConverter.ToString(results.OutputSha1).Replace("-", ""),
                    results.Passes ?? "",
                    ((int)results.ProcessingTime.TotalSeconds).ToString(),
                    (results.ErrorMessage ?? "").Replace("\r", "").Replace('\t', ' ').Trim('\n', ' ').Replace("\n", " : ")
                    ) + Environment.NewLine);
        }
        catch { }
    }

    private void OutputResultsInternal(OutputResults r)
    {
        bool output = false;
        if (r.InputId4 != r.OutputId4)
        {
            Log($"ID changed from {r.InputId4} to {r.OutputId4}");
            output = true;
        }
        if (r.InputTitle != r.OutputTitle)
        {
            Log($"Title changed from '{r.InputTitle}' to '{r.OutputTitle}'");
            output = true;
        }
        if (r.InputDiscVersion != r.OutputDiscVersion)
        {
            Log($"Version changed from v1.{r.InputDiscVersion:D2} to v1.{r.OutputDiscVersion:D2}");
            output = true;
        }
        if (r.InputDiscNo != r.OutputDiscNo)
        {
            Log($"Disc No. changed from {r.InputDiscNo} to {r.OutputDiscNo}");
            output = true;
        }
        if (output)
            LogBlank();
    }

    private static string Friendly(string text)
    {
        return text.Trim('\0') ?? "<NULL>";
    }

    private static string GetPassesLine(NStream nstream, List<Processor> passes)
    {
        StringBuilder sb = new();
        sb.Append($"{passes.Count} Pass{(passes.Count == 1 ? "" : "es")}: ");
        sb.Append($"[{nstream.ExtensionString()}]");

        for (int i = 0; i < passes.Count; i++)
        {
            sb.Append(" >> [");
            if (passes.Count > 1)
                sb.Append($"{i + 1}:");
            sb.Append(passes[i].Title);
            sb.Append(']');
        }

        return sb.ToString();
    }

    public void ProcessingStart(long inputSize, string message)
    {
        LogProgress?.Invoke(this, new ProgressEventArgs { IsStart = true, Progress = 0, TotalProgress = 0, StartMessage = message, Size = inputSize });
        _detailLinesOutput = 0;
        _inProgress = true;
    }

    public void ProcessingComplete(long outputSize, string message, bool success)
    {
        _completedPasses++;

        if (LogProgress != null)
        {
            if (success)
                LogProgress.Invoke(this, new ProgressEventArgs { IsComplete = true, Progress = 1, TotalProgress = ((float)_completedPasses / _totalPasses), CompleteMessage = message, Size = outputSize });
            else
                LogBlank();
        }

        _inProgress = false;

        if (_cacheLogsWhileProcessing)
        {
            lock (_logCacheLock)
            {
                if (_logCache != null && _logCache.Count != 0)
                {
                    OutputDetailHF(true);
                    foreach (Tuple<string, LogMessageType> m in _logCache)
                        Msg(m.Item1, m.Item2, true);
                    OutputDetailHF(false);
                    _logCache?.Clear();
                }
            }
        }
        else if (_detailLinesOutput != 0)
            OutputDetailHF(false);
    }

    public void ProcessingProgress(float value)
    {
        float total = 0;

        if (value != 0)
            total = (float)((double)(_completedPasses + value) / _totalPasses);

        LogProgress?.Invoke(this, new ProgressEventArgs { Progress = value, TotalProgress = total });
    }

    public void Log(string message) => Msg(message, LogMessageType.Info, false);
    public void LogDetail(string message) => Msg("    |" + message, LogMessageType.Detail, false);
    public void LogDebug(string message)
    {
        if (Settings.OutputLevel == 3)
            Msg("    >" + message, LogMessageType.Debug, false);
    }
    public void LogBlank() => Msg("", LogMessageType.Info, false);

    private void Msg(string message, LogMessageType type, bool force)
    {
        bool detail = false;
        lock (_logCacheLock)
        {
            if (type != LogMessageType.Info && (_inProgress || _logCache != null) && !force)
            {
                if (_cacheLogsWhileProcessing)
                {
                    Debug.WriteLine(message);
                    _logCache.Add(new Tuple<string, LogMessageType>(message, type));
                    return;
                }
                detail = true;
            }
        }

        int level = _context?.Settings?.OutputLevel ?? 1;

        if (LogMessage != null && (int)type <= level)
        {
            if (detail && _detailLinesOutput == 0)
            {
                _detailLinesOutput++;
                OutputDetailHF(true);
            }

            LogMessage.Invoke(this, new MessageEventArgs { Message = message, Type = type });
        }
    }

    private void OutputDetailHF(bool header)
    {
        if (header)
        {
            Msg("", LogMessageType.Detail, true);
            Msg("    |DETAIL", LogMessageType.Detail, true);
            Msg("    |...............................", LogMessageType.Detail, true);
        }
        else
        {
            Msg("    |...............................", LogMessageType.Detail, true);
            Msg("", LogMessageType.Detail, true);
        }
    }
}
