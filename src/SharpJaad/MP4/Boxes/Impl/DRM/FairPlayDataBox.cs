namespace SharpJaad.MP4.Boxes.Impl.DRM
{
    public class FairPlayDataBox : BoxImpl
    {
        private byte[] _data;

        public FairPlayDataBox() : base("iTunes FairPlay Data Box")
        { }

        public override void Decode(MP4InputStream input)
        {
            //base.Decode(input);

            _data = input.ReadFully((int)GetLeft(input));
        }

        public byte[] GetData()
        {
            return _data;
        }
    }
}
