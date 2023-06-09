﻿using System;
using System.IO;

namespace GPTStudio.Infrastructure.Azure;

internal class ReadFullyStream : Stream
{
    private readonly Stream sourceStream;
    private long pos;
    private readonly byte[] readAheadBuffer;
    private int readAheadLength;
    private int readAheadOffset;

    public ReadFullyStream(Stream sourceStream)
    {
        this.sourceStream = sourceStream;
        readAheadBuffer = new byte[4096];
    }
    public override bool CanRead  => true;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;
    public override long Length   => pos;

    public override long Position
    {
        get => pos;
        set => throw new NotImplementedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = 0;
        while (bytesRead < count)
        {
            int readAheadAvailableBytes = readAheadLength - readAheadOffset;
            int bytesRequired = count - bytesRead;
            if (readAheadAvailableBytes > 0)
            {
                int toCopy = Math.Min(readAheadAvailableBytes, bytesRequired);
                Array.Copy(readAheadBuffer, readAheadOffset, buffer, offset + bytesRead, toCopy);
                bytesRead += toCopy;
                readAheadOffset += toCopy;
            }
            else
            {
                readAheadOffset = 0;
                readAheadLength = sourceStream.Read(readAheadBuffer, 0, readAheadBuffer.Length);
                //Debug.WriteLine(String.Format("Read {0} bytes (requested {1})", readAheadLength, readAheadBuffer.Length));
                if (readAheadLength == 0)
                {
                    break;
                }
            }
        }
        pos += bytesRead;
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
    public override void SetLength(long value) => throw new NotImplementedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    public override void Flush() => throw new NotImplementedException();

}
