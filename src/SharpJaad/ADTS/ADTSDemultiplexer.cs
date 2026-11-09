using SharpJaad.Tools;
using System.IO;

namespace SharpJaad.ADTS
{
    public class ADTSDemultiplexer 
	{
		private const int MAXIMUM_FRAME_SIZE = 6144;
		private Stream _input;
		private DataInputStream _din;
		private bool _first;
		private ADTSFrame _frame;

		public ADTSDemultiplexer(Stream input) 
		{
			// the sync search reads one byte ahead: a pushback stream gives it back, also for streams that cannot seek
			this._input = new PushbackStream(input);
			_din = new DataInputStream(this._input);
			_first = true;
			if(!FindNextFrame()) 
				throw new IOException("no ADTS header found");
		}

		public byte[] GetDecoderSpecificInfo() 
		{
			return _frame.CreateDecoderSpecificInfo();
		}

		public byte[] ReadNextFrame()
		{
			if(_first) _first = false;
			else if (!FindNextFrame()) throw new EndOfStreamException();

			int length = _frame.GetFrameLength();
			if (length < 0) throw new IOException("invalid ADTS frame length");
			byte[] b = new byte[length]; // at most 8191 bytes (13 bits)
			_din.ReadFully(b);
			return b;
		}

		private bool FindNextFrame()
		{
            //find next ADTS ID
            bool found = false;
			int left = MAXIMUM_FRAME_SIZE;
			int i;
			while(!found&&left>0) 
			{
				i = _input.ReadByte();
				if (i < 0) return false; // end of stream
				left--;
				if(i==0xFF)
				{
					i = _input.ReadByte();
					if (i < 0) return false;
					if(((i>>4)&0xF)==0xF) found = true;
					((PushbackStream)_input).Unread(i);
				}
			}

			if(found) 
				_frame = new ADTSFrame(_din);
			return found;
		}

		public int GetSampleFrequency() 
		{
			return _frame.GetSampleFrequency();
		}

		public int GetChannelCount() 
		{
			return _frame.GetChannelCount();
		}
	}
}
