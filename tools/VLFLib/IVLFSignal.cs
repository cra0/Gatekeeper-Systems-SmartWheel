namespace VLFLib;


public interface IVLFSignal
{
    public delegate void VLFEvent();
    public delegate void VLFSenderEvent(object sender);
    public delegate void VLFResultEvent(object sender, bool result);
    public delegate void VLFExceptionEvent(object sender, Exception ex);
    public delegate void VLFConsolePrintEvent(string message);

    public event VLFEvent? OnSignalParseStarted;
    public event VLFResultEvent? OnSignalParseCompleted;
    public event VLFExceptionEvent? OnSignalError;
    public event VLFConsolePrintEvent? OnSignalConsolePrint;

}
