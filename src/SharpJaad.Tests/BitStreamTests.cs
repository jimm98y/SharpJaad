using SharpJaad.AAC;
using SharpJaad.AAC.Syntax;
using Xunit;

namespace SharpJaad.Tests
{
    public class BitStreamTests
    {
        [Fact]
        public void ReadsBitsMostSignificantFirst()
        {
            var bs = new BitStream(new byte[] { 0xA5, 0x0F, 0xF0, 0x3C, 0x81 });

            Assert.Equal(40, bs.GetBitsLeft());
            Assert.Equal(0x5, bs.ReadBits(3));
            Assert.Equal(0, bs.ReadBit());
            Assert.Equal(0x50F, bs.ReadBits(12));
            Assert.Equal(16, bs.GetPosition());
            Assert.Equal(0xF03C81, bs.ReadBits(24));
            Assert.Equal(0, bs.GetBitsLeft());
        }

        [Fact]
        public void ShorterDataAfterLongerDataEndsWhereTheShorterDataEnds()
        {
            var bs = new BitStream();
            bs.SetData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            bs.SetData(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 });

            Assert.Equal(64, bs.GetBitsLeft());
            Assert.Equal(unchecked((int)0xDEADBEEF), bs.ReadBits(32));
            Assert.Equal(0x01020304, bs.ReadBits(32));
            Assert.Equal(0, bs.GetBitsLeft());

            var e = Assert.Throws<AACException>(() => bs.ReadBits(8));
            Assert.True(e.IsEndOfStream);
        }

        [Fact]
        public void LongerDataAfterShorterDataIsReadCompletely()
        {
            var bs = new BitStream();
            bs.SetData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            bs.SetData(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD });

            Assert.Equal(104, bs.GetBitsLeft());
            Assert.Equal(0x11, bs.ReadBits(8));
            Assert.Equal(0x22334455, bs.ReadBits(32));
            Assert.Equal(0x66778899, bs.ReadBits(32));
            Assert.Equal(unchecked((int)0xAABBCCDD), bs.ReadBits(32));
            Assert.Throws<AACException>(() => bs.ReadBit());
        }

        [Fact]
        public void PeekAndSkipStayWithinTheCurrentData()
        {
            var bs = new BitStream();
            bs.SetData(new byte[64]);
            bs.SetData(new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 1 });

            Assert.Equal(1, bs.PeekBit());
            Assert.Equal(0x80, bs.PeekBits(8));
            bs.SkipBits(63);
            Assert.Equal(1, bs.ReadBit());
            Assert.Throws<AACException>(() => bs.PeekBits(1));
        }
    }
}
