using System;
using System.IO;
using System.Text;

namespace SharpJaad.MP4
{
    public class MP4InputStream
	{
		public const int MASK8 = 0xFF;
		public const int MASK16 = 0xFFFF;
		public const string UTF8 = "UTF-8";
		public const string UTF16 = "UTF-16";
		private const int BYTE_ORDER_MASK = 0xFEFF;
		private readonly Stream _input;
		//private readonly RandomAccessFile fin;
		private int _peeked;
		private long _offset; //only used with InputStream

		/**
         * Constructs an <code>MP4InputStream</code> that reads from an 
         * <code>InputStream</code>. It will have no random access, thus seeking 
         * will not be possible.
         * 
         * @param in an <code>InputStream</code> to read from
         */
		public MP4InputStream(Stream input)
		{
			this._input = input;
			//fin = null;
			_peeked = -1;
			_offset = 0;
		}

		/**
         * Constructs an <code>MP4InputStream</code> that reads from a 
         * <code>RandomAccessFile</code>. It will have random access and seeking 
         * will be possible.
         * 
         * @param in a <code>RandomAccessFile</code> to read from
         */
		/*MP4InputStream(RandomAccessStream fin)
		{
			this.fin = fin;
			input = null;
			peeked = -1;
		}*/

		/**
         * Reads the next byte of data from the input. The value byte is returned as
         * an int in the range 0 to 255. If no byte is available because the end of 
         * the stream has been reached, an EOFException is thrown. This method 
         * blocks until input data is available, the end of the stream is detected, 
         * or an I/O error occurs.
         * 
         * @return the next byte of data
         * @throws IOException If the end of the stream is detected or any I/O error occurs.
         */
		public int Read()
		{
			int i = 0;
			if (_peeked >= 0) {
				i = _peeked;
				_peeked = -1;
			}
			else if (_input != null) i = _input.ReadByte();
			//else if (fin != null) i = fin.read();

			if (i == -1) throw new EndOfStreamException();
			else if (_input != null) _offset++;
			return i;
		}

		/**
		 * Reads <code>len</code> bytes of data from the input into the array 
		 * <code>b</code>. If len is zero, then no bytes are read.
		 * 
		 * This method blocks until all bytes could be read, the end of the stream 
		 * is detected, or an I/O error occurs.
		 * 
		 * If the stream ends before <code>len</code> bytes could be read an 
		 * EOFException is thrown.
		 * 
		 * @param b the buffer into which the data is read.
		 * @param off the start offset in array <code>b</code> at which the data is written.
		 * @param len the number of bytes to read.
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 */
		public void Read(byte[] b, int off, int len)
		{
			int read = 0;
			int i = 0;

			if (_peeked >= 0 && len > 0) {
				b[off] = (byte)_peeked;
				_peeked = -1;
				read++;
			}

			while (read < len) {
				if (_input != null) i = _input.Read(b, off + read, len - read);

				//else if (fin != null) i = fin.read(b, off + read, len - read);
				// Stream.Read returns 0 (not -1) at the end of the stream
				if (i <= 0) throw new EndOfStreamException();
				else read += i;
			}

			_offset += read;
		}

		/**
		 * Reads up to eight bytes as a long value. This method blocks until all 
		 * bytes could be read, the end of the stream is detected, or an I/O error 
		 * occurs.
		 * 
		 * @param n the number of bytes to read >0 and <=8
		 * @return the read bytes as a long value
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 * @throws IndexOutOfBoundsException if <code>n</code> is not in the range 
		 * [1...8] inclusive.
		 */
		public long ReadBytes(int n)
		{
			if (n < 1 || n > 8) throw new MP4Exception("invalid number of bytes to read: " + n);
			// unsigned, also for 4 bytes; 8 bytes above long.MaxValue wrap and are rejected by the callers as negative
			long result = 0;
			for (int i = 0; i < n; i++)
			{
				result = (result << 8) | (long)Read();
			}
			return result;
		}

		/**
		 * Reads data from the input stream and stores them into the buffer array b.
		 * This method blocks until all bytes could be read, the end of the stream 
		 * is detected, or an I/O error occurs.
		 * If the length of b is zero, then no bytes are read.
		 * 
		 * @param b the buffer into which the data is read.
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 */
		public void ReadBytes(byte[] b)
		{
			Read(b, 0, b.Length);
		}

		/**
		 * Reads <code>n</code> bytes from the input as a String. The bytes are 
		 * directly converted into characters. If not enough bytes could be read, an
		 * EOFException is thrown.
		 * This method blocks until all bytes could be read, the end of the stream 
		 * is detected, or an I/O error occurs.
		 * 
		 * @param n the length of the String.
		 * @return the String, that was read
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 */
		public string ReadString(int n)
		{
			if (n < 0) throw new MP4Exception("invalid string length: " + n);
			// grows with the bytes that are actually there: a length from the file allocates nothing up front
			var c = new StringBuilder(Math.Min(n, 256));
			for (int pos = 0; pos < n; pos++)
			{
				c.Append((char)Read());
			}
			return c.ToString();
		}

		/**
		 * Reads a null-terminated UTF-encoded String from the input. The maximum 
		 * number of bytes that can be read before the null must appear must be 
		 * specified.
		 * Although the method is preferred for unicode, the encoding can be any 
		 * charset name, that is supported by the system.
		 * 
		 * This method blocks until all bytes could be read, the end of the stream 
		 * is detected, or an I/O error occurs.
		 * 
		 * @param max the maximum number of bytes to read, before the null-terminator
		 * must appear.
		 * @param encoding the charset used to encode the String
		 * @return the decoded String
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 */
		public string ReadUTFString(int max, string encoding)
		{
			return Encoding.GetEncoding(encoding).GetString(ReadTerminated(max, 0));
		}

		/**
		 * Reads a null-terminated UTF-encoded String from the input. The maximum 
		 * number of bytes that can be read before the null must appear must be 
		 * specified.
		 * The encoding is detected automatically, it may be UTF-8 or UTF-16 
		 * (determined by a byte order mask at the beginning).
		 * 
		 * This method blocks until all bytes could be read, the end of the stream 
		 * is detected, or an I/O error occurs.
		 * 
		 * @param max the maximum number of bytes to read, before the null-terminator
		 * must appear.
		 * @return the decoded String
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 */
		public string ReadUTFString(int max)
		{
			//read byte order mask
			byte[]
			bom = new byte[2];
			Read(bom, 0, 2);
			if (bom[0] == 0 || bom[1] == 0) return "";
			int i = (bom[0] << 8) | bom[1];

			//read null-terminated
			byte[] b = ReadTerminated(Math.Max(max - 2, 0), 0);
			//copy bom
			byte[] b2 = new byte[b.Length + bom.Length];
			System.Array.Copy(bom, 0, b2, 0, bom.Length);
			System.Array.Copy(b, 0, b2, bom.Length, b.Length);

            return (i == BYTE_ORDER_MASK) ? Encoding.Unicode.GetString(b2) : Encoding.UTF8.GetString(b2);
		}

		/**
		 * Reads a byte array from the input that is terminated by a specific byte 
		 * (the 'terminator'). The maximum number of bytes that can be read before 
		 * the terminator must appear must be specified.
		 * 
		 * The terminator will not be included in the returned array.
		 * 
		 * This method blocks until all bytes could be read, the end of the stream 
		 * is detected, or an I/O error occurs.
		 * 
		 * @param max the maximum number of bytes to read, before the terminator 
		 * must appear.
		 * @param terminator the byte that indicates the end of the array
		 * @return the buffer into which the data is read.
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 */
		public byte[] ReadTerminated(int max, int terminator)
		{
			// up to the terminator (which is consumed) or max bytes; grows with the bytes that are actually there
			var b = new MemoryStream(Math.Max(0, Math.Min(max, 256)));
			for (int pos = 0; pos < max; pos++)
			{
				int i = Read();
				if (i == terminator) break;
				b.WriteByte((byte)i);
			}
			return b.ToArray();
		}

		/**
		 * Reads a fixed point number from the input. The number is read as a 
		 * <code>m.n</code> value, that results from deviding an integer by 
		 * 2<sup>n</sup>.
		 * 
		 * @param m the number of bits before the point
		 * @param n the number of bits after the point
		 * @return a floating point number with the same value
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 * @throws IllegalArgumentException if the total number of bits (m+n) is not
		 * a multiple of eight
		 */
		public double ReadFixedPoint(int m, int n)
		{
			int bits = m + n;
			if ((bits % 8) != 0) throw new ArgumentException("number of bits is not a multiple of 8: " + (m + n));
			long l = ReadBytes(bits / 8);
			double x = Math.Pow(2, n);
			double d = ((double)l) / x;
			return d;
		}

		/**
		 * Skips <code>n</code> bytes in the input. This method blocks until all 
		 * bytes could be skipped, the end of the stream is detected, or an I/O 
		 * error occurs.
		 * 
		 * @param n the number of bytes to skip
		 * @throws IOException If the end of the stream is detected, the input 
		 * stream has been closed, or if some other I/O error occurs.
		 */
		public void SkipBytes(long n)
		{
			if (n <= 0) return;
			long l = 0;
			if (_peeked >= 0)
			{
				_peeked = -1;
				l++;
			}

			if (_input.CanSeek)
			{
				// seek instead of reading; past the end is the end of the stream
				long left = _input.Length - _input.Position;
				if (n - l > left)
				{
					_input.Seek(0, SeekOrigin.End);
					_offset += l + left;
					throw new EndOfStreamException();
				}
				_input.Seek(n - l, SeekOrigin.Current);
				l = n;
			}
			else
			{
				byte[] skip = new byte[(int)Math.Min(n - l, 4096)];
				while (l < n)
				{
					int i = _input.Read(skip, 0, (int)Math.Min(n - l, skip.Length));
					if (i <= 0)
					{
						_offset += l;
						throw new EndOfStreamException();
					}
					l += i;
				}
			}

			_offset += l;
		}

		/**
		 * Returns the current offset in the stream.
		 * 
		 * @return the current offset
		 * @throws IOException if an I/O error occurs (only when using a RandomAccessFile)
		 */
		public long GetOffset()
		{
			long l = -1;
			if (_input != null) 
				l = _offset;
			//else if (fin != null) l = fin.getFilePointer();
			return l;
		}

		/**
		 * Seeks to a specific offset in the stream. This is only possible when 
		 * using a RandomAccessFile. If an InputStream is used, this method throws 
		 * an IOException.
		 * 
		 * @param pos the offset position, measured in bytes from the beginning of the
		 * stream
		 * @throws IOException if an InputStream is used, pos is less than 0 or an 
		 * I/O error occurs
		 */
		public void Seek(long pos)
		{
			//if (fin != null) fin.seek(pos);
			//else throw new IOException("could not seek: no random access");
			if (_input.CanSeek)
			{
				if (pos < 0) throw new MP4Exception("invalid offset: " + pos);
				_input.Seek(pos, SeekOrigin.Begin);
				_offset = pos;
				_peeked = -1;
			}
			else
			{
				throw new IOException("could not seek: no random access");
			}
        }

		/**
		 * Indicates, if random access is available. That is, if this 
		 * <code>MP4InputStream</code> was constructed with a RandomAccessFile. If 
		 * this method returns false, seeking is not possible.
		 * 
		 * @return true if random access is available
		 */
		/// <summary>
		/// Reads n bytes into a new array that grows with the data actually read, so that a length from the file
		/// allocates no more than the stream holds.
		/// </summary>
		internal byte[] ReadFully(int n)
		{
			if (n < 0) throw new MP4Exception("invalid length: " + n);
			long remaining = GetRemaining();
			if (remaining != long.MaxValue && n > remaining) throw new EndOfStreamException();
			if (n <= 65536 || remaining != long.MaxValue)
			{
				// small, or known to be in the stream
				byte[] b = new byte[n];
				Read(b, 0, n);
				return b;
			}
			var m = new MemoryStream();
			byte[] chunk = new byte[65536];
			for (int left = n; left > 0;)
			{
				int c = Math.Min(left, chunk.Length);
				Read(chunk, 0, c);
				m.Write(chunk, 0, c);
				left -= c;
			}
			return m.ToArray();
		}

		/// <summary>
		/// The length of the stream, long.MaxValue if that is not known (a stream that cannot seek).
		/// </summary>
		internal long GetLength()
		{
			return _input.CanSeek ? _input.Length : long.MaxValue;
		}

		/// <summary>
		/// The bytes left in the stream, long.MaxValue if that is not known (a stream that cannot seek).
		/// </summary>
		internal long GetRemaining()
		{
			if (!_input.CanSeek) return long.MaxValue;
			return _input.Length - _input.Position + (_peeked >= 0 ? 1 : 0);
		}

		public bool HasRandomAccess()
		{
			//return fin != null;
			return _input.CanSeek;
		}

		/**
		 * Indicates, if the input has some data left.
		 * 
		 * @return true if there is at least one byte left
		 * @throws IOException if an I/O error occurs
		 */
		public bool HasLeft()
		{
			bool b;
			/*if (fin != null) b = fin.getFilePointer() < (fin.length() - 1);
			else*/
			if (_peeked >= 0)
			{
				b = true;
			}
			else
			{
				int i = _input.ReadByte();
				b = (i != -1);
				if (b) _peeked = i;
			}
			return b;
		}

		/**
		 * Closes the input and releases any system resources associated with it. 
		 * Once the stream has been closed, further reading or skipping will throw 
		 * an IOException. Closing a previously closed stream has no effect.
		 * 
		 * @throws IOException if an I/O error occurs
		 */
		public void Close()
		{
			_peeked = -1;
			if (_input != null) _input.Close();
			//else if (fin != null) fin.close();
		}
	}
}
