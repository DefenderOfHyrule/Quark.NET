namespace Quark.Cf;

public interface ICommandBlock
{
    bool IsValid();
    int  ValidateCommand();

    int    Read32();
    long   Read64();
    string ReadString();

    void Write32(int val);
    void Write64(long val);
    void WriteString(string val);

    void   SendBuffer(byte[] buf, int length);
    byte[] GetBuffer(int len);

    void ResponseStart();
    void ResponseEnd();
    void RespondFailure(int rc);
    void RespondEmpty();
}
