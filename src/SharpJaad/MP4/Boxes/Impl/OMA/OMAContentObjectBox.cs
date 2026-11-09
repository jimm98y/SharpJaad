namespace SharpJaad.MP4.Boxes.Impl.OMA
{
    public class OMAContentObjectBox : FullBox
    {
        private byte[] _data;

        public OMAContentObjectBox() : base("OMA Content Object Box")
        { }

        public override void Decode(MP4InputStream input)
        {
            base.Decode(input);

            long len = input.ReadBytes(4);
            if (len > GetLeft(input)) throw new MP4Exception("invalid content object length: " + len);
            _data = input.ReadFully((int)len);
        }

        /**
         * Returns the data of this content object.
         * 
         * @return the data
         */
        public byte[] GetData()
        {
            return _data;
        }
    }
}
