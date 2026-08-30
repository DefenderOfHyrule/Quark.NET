using System.Buffers.Binary;
using System.Text;
using Quark.Cf;

namespace Quark.Net;

public sealed class TcpSessionCommandBlock : ICommandBlock
{
    public const int  BlockSize     = 0x1000;
    public const uint InputMagic    = 0x49434C47u;
    public const uint OutputMagic   = 0x4F434C47u;
    public const int  ResultSuccess = 0;
    public const int  InvalidCmdId  = 0;

    private readonly byte[]?           _in;
    private int                        _inPos;
    private readonly byte[]            _out = new byte[BlockSize];
    private int                        _outPos;
    private readonly TcpClientSession  _session;

    public TcpSessionCommandBlock(TcpClientSession session)
    {
        _session = session;
        _in      = session.ReadBytes(BlockSize);
    }

    public bool IsValid() => _in != null;

    public int ValidateCommand()
    {
        uint magic = Read32u();
        return magic == InputMagic ? Read32() : InvalidCmdId;
    }

    public int  Read32() => (int)Read32u();
    public long Read64() => (long)Read64u();

    public string ReadString()
    {
        int len = Read32();
        var b = new byte[len];
        Buffer.BlockCopy(_in!, _inPos, b, 0, len);
        _inPos += len;
        return Encoding.UTF8.GetString(b);
    }

    private uint  Read32u() { var v = BinaryPrimitives.ReadUInt32LittleEndian(_in.AsSpan(_inPos)); _inPos += 4; return v; }
    private ulong Read64u() { var v = BinaryPrimitives.ReadUInt64LittleEndian(_in.AsSpan(_inPos)); _inPos += 8; return v; }

    public void Write32(int val)  { BinaryPrimitives.WriteInt32LittleEndian (_out.AsSpan(_outPos), val); _outPos += 4; }
    public void Write64(long val) { BinaryPrimitives.WriteInt64LittleEndian(_out.AsSpan(_outPos), val); _outPos += 8; }

    public void WriteString(string val)
    {
        byte[] raw = Encoding.UTF8.GetBytes(val);
        Write32(raw.Length);
        raw.CopyTo(_out, _outPos);
        _outPos += raw.Length;
    }

    public void SendBuffer(byte[] buf, int length) => _session.WriteBytes(buf, length);
    public byte[] GetBuffer(int len)     => _session.ReadBytes(len) ?? Array.Empty<byte>();

    public void ResponseStart()
    {
        _outPos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(_out.AsSpan(0), OutputMagic);
        BinaryPrimitives.WriteInt32LittleEndian (_out.AsSpan(4), ResultSuccess);
        _outPos = 8;
    }

    public void ResponseEnd() => _session.WriteBytes(_out);

    public void RespondFailure(int rc)
    {
        _outPos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(_out.AsSpan(0), OutputMagic);
        BinaryPrimitives.WriteInt32LittleEndian (_out.AsSpan(4), rc);
        _outPos = 8;
        ResponseEnd();
    }

    public void RespondEmpty() { ResponseStart(); ResponseEnd(); }
}
