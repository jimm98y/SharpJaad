namespace SharpJaad.AAC.Syntax
{
    public class DSE : Element
    {
        private const int MAX_DATA_STREAM_BYTES = 255 + 255; //count is 8 bits, plus 8 more bits when it is 255
        private readonly byte[] _dataStreamBytes = new byte[MAX_DATA_STREAM_BYTES];

        public void Decode(BitStream input)
        {
            ReadElementInstanceTag(input);

            bool byteAlign = input.ReadBool();
            int count = input.ReadBits(8);
            if (count == 255) count += input.ReadBits(8);

            if (byteAlign) input.ByteAlign();

            for (int i = 0; i < count; i++)
            {
                _dataStreamBytes[i] = (byte)input.ReadBits(8);
            }
        }
    }
}
