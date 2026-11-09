using System;
using System.IO;

namespace SharpJaad.ADTS
{
    /// <summary>
    /// A read-only stream that can give back the last byte read, for the ADTS sync search.
    /// </summary>
    internal sealed class PushbackStream : Stream
    {
        private readonly Stream _input;
        private int _pushedBack = -1;

        public PushbackStream(Stream input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public void Unread(int b)
        {
            if (_pushedBack >= 0) throw new InvalidOperationException("only one byte can be given back");
            _pushedBack = b & 0xFF;
        }

        public override int ReadByte()
        {
            if (_pushedBack >= 0)
            {
                int b = _pushedBack;
                _pushedBack = -1;
                return b;
            }
            return _input.ReadByte();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            if (_pushedBack >= 0)
            {
                buffer[offset] = (byte)_pushedBack;
                _pushedBack = -1;
                return 1 + Math.Max(0, _input.Read(buffer, offset + 1, count - 1));
            }
            return _input.Read(buffer, offset, count);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
