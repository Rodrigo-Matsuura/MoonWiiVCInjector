using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NKit;

/// <summary>
/// A class to coordinate the reader and writer classes that are running in tandem
/// </summary>
internal class Coordinator
{
    public event EventHandler<StartedEventArgs> Started;
    public event EventHandler<CompletedEventArgs> Completed;

    private enum StateEnum { Error, Unset, ReaderCheckPoint1PreWrite, WriterCheckPoint1WriteReady, WriterCheckPoint2Complete, ReaderCheckPoint2Complete, WriterCheckPoint3ApplyPatches, Complete };

    private string _aliasJunkId;
    private StateEnum _state;
    private readonly Lock _stateLock = new();
    private NCrc _crcs;
    private long _processSize;
    private byte[] _header;
    private string _resultMessage;
    private bool _isRecoverable;

    private uint _validationCrc;
    private uint _verifiedCrc;
    private bool _verifyIsWrite;
    private byte[] _md5;
    private byte[] _sha1;

    private readonly bool _readerVerifyCrc;
    private readonly bool _writerVerifyCrc;
    private readonly bool _readerValidationCrc;
    private readonly bool _writerValidationCrc;
    private HandledException _readerException;
    private HandledException _writerException;
    private bool _writerFirst;


    public NCrc Patches { get; private set; }
    public NCrc ReaderCrcs { get; private set; }
    public NCrc WriterCrcs { get; private set; }

    public HandledException Exception { get { return _readerException == null || _writerFirst ? _writerException : _readerException; } }
    public long OutputSize { get { return _processSize; } }

    internal Coordinator(uint validationCrc, IValidation reader, IValidation writer, long processSize)
    {
        _processSize = processSize;
        _crcs = null;
        _validationCrc = validationCrc;
        _state = StateEnum.Unset;
        _readerVerifyCrc = reader.RequireVerifyCrc;
        _readerValidationCrc = reader.RequireValidationCrc;
        _writerVerifyCrc = writer.RequireVerifyCrc;
        _writerValidationCrc = writer.RequireValidationCrc;
        _readerException = null;
        _writerException = null;
        _writerFirst = false;
    }

    public HandledException SetReaderException(Exception ex, string message, params string[] args)
    {
        if (ex is HandledException handled)
            return this.SetReaderException(handled);
        return this.SetReaderException(new HandledException(ex, message, args));
    }

    public HandledException SetReaderException(HandledException ex)
    {
        lock (_stateLock)
        {
            _readerException ??= ex;
            _state = StateEnum.Error;
        }
        return _readerException;
    }

    public HandledException SetWriterException(Exception ex, string message, params string[] args)
    {
        if (ex is HandledException handled)
            return this.SetWriterException(handled);
        return this.SetWriterException(new HandledException(ex, message, args));
    }
    public HandledException SetWriterException(HandledException ex)
    {
        lock (_stateLock)
        {
            if (_writerException == null)
            {
                _writerFirst = _readerException == null;
                _writerException = ex;
            }
            _state = StateEnum.Error;
        }
        return _writerException;
    }


    public void ReaderCheckPoint1PreWrite(string aliasJunkId, uint nkitSourceCrc)
    {
        _aliasJunkId = aliasJunkId;
        if (_readerValidationCrc)
            _validationCrc = nkitSourceCrc;
        Progress(StateEnum.Unset, StateEnum.ReaderCheckPoint1PreWrite);
        Progress(StateEnum.WriterCheckPoint1WriteReady, StateEnum.WriterCheckPoint1WriteReady);
        this.Started?.Invoke(this, new StartedEventArgs(_processSize, aliasJunkId));
    }

    public void WriterCheckPoint1WriteReady(out string aliasJunkId)
    {
        Progress(StateEnum.ReaderCheckPoint1PreWrite, StateEnum.WriterCheckPoint1WriteReady);
        aliasJunkId = _aliasJunkId;
    }

    public void WriterCheckPoint2Complete(out NCrc crcsPatches, out uint validationCrc, byte[] header, long outputSize)
    {
        Progress(StateEnum.WriterCheckPoint1WriteReady, StateEnum.WriterCheckPoint2Complete);
        Progress(StateEnum.ReaderCheckPoint2Complete, StateEnum.ReaderCheckPoint2Complete);
        validationCrc = _validationCrc; //let the 
        crcsPatches = _crcs;

        if (outputSize != 0)
            _processSize = outputSize;

        if (header != null) //overwrite reader
            _header = header;
    }

    public void ReaderCheckPoint2Complete(NCrc crcsPatches, bool isRecoverable, uint validationCrc, uint verifiedCrc, bool verifyIsWrite, byte[] header, string resultMessage)
    {
        _resultMessage = resultMessage;
        this.ReaderCrcs = crcsPatches;
        _crcs = crcsPatches;
        if (_validationCrc == 0)
            _validationCrc = validationCrc;
        _header = header;
        if (_readerVerifyCrc)
        {
            _verifiedCrc = verifiedCrc;
            _verifyIsWrite = verifyIsWrite;
        }
        if (_readerValidationCrc)
            _validationCrc = validationCrc;

        if (isRecoverable)
            _isRecoverable = true;

        Progress(StateEnum.WriterCheckPoint2Complete, StateEnum.ReaderCheckPoint2Complete);
    }
    public void WriterCheckPoint3ApplyPatches(NCrc crcsPatches, bool isRecoverable, uint validationCrc, ChecksumsResult checksums, bool verifyIsWrite, string resultMessage)
    {
        _md5 = checksums.Md5;
        _sha1 = checksums.Sha1;
        this.WriterCheckPoint3ApplyPatches(crcsPatches, isRecoverable, validationCrc, checksums.Crc, verifyIsWrite, resultMessage);
    }

    public void WriterCheckPoint3ApplyPatches(NCrc crcsPatches, bool isRecoverable, uint validationCrc, uint verifiedCrc, bool verifyIsWrite, string resultMessage)
    {
        //apply the patches  get the final crcs
        this.WriterCrcs = crcsPatches;

        Progress(StateEnum.ReaderCheckPoint2Complete, StateEnum.Complete);
        if (_resultMessage != null && resultMessage != null)
            _resultMessage = string.Format("{0} / {1}", _resultMessage, resultMessage);
        else if (resultMessage != null)
            _resultMessage = resultMessage;
        if (_writerVerifyCrc)
        {
            _verifiedCrc = verifiedCrc;
            _verifyIsWrite = verifyIsWrite;
        }
        if (_writerValidationCrc)
            _validationCrc = validationCrc;

        if (isRecoverable)
            _isRecoverable = true;
        this.Patches = crcsPatches ?? _crcs; //the reader might be the patch issuer and the writer might be a plain iso writer
    }

    public void ReaderCheckPoint3Complete()
    {
        Progress(StateEnum.Complete, StateEnum.Complete);
        this.Completed?.Invoke(this, new CompletedEventArgs(this.Patches?.FullCrc(true) ?? 0, this.Patches?.FullCrc(false) ?? 0, _processSize, _header, _resultMessage, _validationCrc, _verifiedCrc, _verifyIsWrite, _isRecoverable, _md5, _sha1));
    }

    private void Progress(StateEnum testState, StateEnum setState)
    {
        StateEnum s;
        lock (_stateLock)
        {
            s = _state;
            if (s == testState)
            {
#if DEBUG
                //                    Console.WriteLine(string.Format("\r\nState a: {1}(Test={2})({0})", Thread.CurrentThread.ManagedThreadId.ToString(), setState.ToString(), testState.ToString()));
#endif

                _state = setState;
                return;
            }
            else if (s > testState)
                throw new Exception(string.Format("State is {0} (beyond {1})", s.ToString(), testState.ToString()));
        }

#if DEBUG
        //            lock (_stateLock)
        //                Console.WriteLine(string.Format("\r\nState b: {1}(Test={2})({0})", Thread.CurrentThread.ManagedThreadId.ToString(), setState.ToString(), testState.ToString()));
#endif

        while (_state != testState)
        {
            Thread.Sleep(250); //lazy wait, it's not time critical
            if (_state == StateEnum.Error)
                throw new Exception("Exception reported to ProcessCoordinator - Exceptioning out");
        }

        lock (_stateLock)
        {
#if DEBUG
            //                Console.WriteLine(string.Format("\r\nState c: {1}(Test={2})({0})", Thread.CurrentThread.ManagedThreadId.ToString(), setState.ToString(), testState.ToString()));
#endif
            _state = setState;
        }
    }
}

public class StartedEventArgs(long readerLength, string aliasJunkId) : EventArgs
{
    public long ReaderLength { get; } = readerLength;
    public string AliasJunkId { get; } = aliasJunkId;
}

public class CompletedEventArgs(uint patchedCrc, uint unpatchedCrc, long outputSize, byte[] header, string resultMessage, uint validationCrc, uint verifyCrc, bool verifyIsWrite, bool isRecoverable, byte[] md5, byte[] sha1) : EventArgs
{
    public uint PatchedCrc { get; } = patchedCrc;
    public uint UnpatchedCrc { get; } = unpatchedCrc;
    public long OutputSize { get; } = outputSize;
    public uint NkitSourceCrc { get; }
    public byte[] Header { get; } = header;
    public string ResultMessage { get; } = resultMessage;
    public uint ValidationCrc { get; } = validationCrc;
    public uint VerifyCrc { get; } = verifyCrc;
    public bool VerifyIsWrite { get; } = verifyIsWrite;
    public bool IsRecoverable { get; } = isRecoverable;
    public byte[] Md5 { get; } = md5;
    public byte[] Sha1 { get; } = sha1;
}
