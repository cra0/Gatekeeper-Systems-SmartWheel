using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VLFLib.Realtime;

/// <summary>
/// Consumes the 1 ms '1'/'0' stream from ToneDetector and emits decoded bytes.
/// </summary>
public sealed class ToneDecoder
{
    // frame-length windows (in 1 ms frames)
    private const int START_LEN = 18;   // ≥18 ms  ≈ 21-ms marker
    private const int LEN_ONE_MIN = 7;   // 7–10 ms ⇒ 1
    private const int LEN_ONE_MAX = 10;
    private const int LEN_ZERO_MIN = 3;   // 3–5  ms ⇒ 0
    private const int LEN_ZERO_MAX = 5;

    // running state
    private int _runLen = 0;
    private bool _inPacket = false;
    private int _bitCnt = 0;
    private byte _byteAcc = 0;

    public event Action<byte>? OnByteDecoded;   // final output

    public void FeedBit(char bit)
    {
        bool isTone = bit == '1';

        if (isTone)
        {
            _runLen++;
        }
        else
        {
            // a 0 terminates the current tone run (if any)
            if (_runLen > 0)
            {
                ClassifyRun(_runLen);
                _runLen = 0;
            }
        }
    }

    private void ClassifyRun(int len)
    {
        if (len >= START_LEN)
        {
            // toggle packet mode (start ↔ end marker)
            _inPacket = !_inPacket;
            _bitCnt = 0;
            _byteAcc = 0;
            return;
        }

        if (!_inPacket) 
            return; // ignore anything outside markers

        if (len >= LEN_ONE_MIN && len <= LEN_ONE_MAX)
        {
            PushBit(1);
        }
        else if (len >= LEN_ZERO_MIN && len <= LEN_ZERO_MAX)
        {
            PushBit(0);
        }
        else
        {
            // ignore glitches
            // if the run length is not a valid bit length, we just drop it
            // this is a simple way to handle noise without complex state management
        }

    }

    private void PushBit(int bit)
    {
        _byteAcc = (byte)((_byteAcc << 1) | bit);
        _bitCnt++;

        if (_bitCnt == 8)
        {
            OnByteDecoded?.Invoke(_byteAcc);
            _bitCnt = 0;
            _byteAcc = 0;
        }
    }
}