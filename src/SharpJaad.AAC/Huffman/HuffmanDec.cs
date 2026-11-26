using SharpJaad.AAC;
using SharpJaad.AAC.Syntax;
using System;

namespace SharpJaad.AAC.Huffman
{
    public static class HuffmanDec
    {
        private static readonly bool[] _UNSIGNED = { false, false, true, true, false, false, true, true, true, true, true };
        private static int _QUAD_LEN = 4, _PAIR_LEN = 2;

        // Of each codebook, of the next 10 bits: the offset of the codeword they start with, or -1 of a longer one - the
        // codewords of up to 10 bits are most of those read, found of one step rather than of a search of the table
        private const int _FAST_BITS = 10;
        private static readonly short[][] _FAST = BuildFast(Codebooks.CODEBOOKS);
        private static readonly short[] _FAST_SF = BuildFast(new[] { Codebooks.HCB_SF })[0];

        private static short[][] BuildFast(int[][][] tables)
        {
            var fast = new short[tables.Length][];
            for (int t = 0; t < tables.Length; t++)
            {
                fast[t] = new short[1 << _FAST_BITS];
                for (int i = 0; i < fast[t].Length; i++) fast[t][i] = -1;
                for (int off = 0; off < tables[t].Length && tables[t][off][0] <= _FAST_BITS; off++)
                {
                    int pad = _FAST_BITS - tables[t][off][0];
                    for (int i = 0; i < 1 << pad; i++) fast[t][(tables[t][off][1] << pad) + i] = (short)off;
                }
            }
            return fast;
        }

        private static int FindOffset(BitStream input, int[][] table, short[] fast)
        {
            // of the last bits of a frame, no 10 to peek at: searched
            if (input.GetBitsLeft() >= _FAST_BITS + 32)
            {
                int found = fast[input.PeekBits(_FAST_BITS)];
                if (found >= 0)
                {
                    input.SkipBits(table[found][0]);
                    return found;
                }
            }
            int off = 0;
            int len = table[off][0];
            int cw = input.ReadBits(len);
            int j;
            while (cw != table[off][1])
            {
                off++;
                j = table[off][0] - len;
                len = table[off][0];
                cw <<= j;
                cw |= input.ReadBits(j);
            }
            return off;
        }

        private static void SignValues(BitStream input, int[] data, int off, int len)
        {
            for (int i = off; i < off + len; i++)
            {
                if (data[i] != 0)
                {
                    if (input.ReadBool()) data[i] = -data[i];
                }
            }
        }

        private static int GetEscape(BitStream input, int s)
        {
            bool neg = s < 0;

            int i = 4;
            while (input.ReadBool())
            {
                i++;
                if (i > 12) throw new AACException("escape sequence too long"); //values are below 8192 (ISO/IEC 14496-3 4.6.3.3)
            }
            int j = input.ReadBits(i) | 1 << i;

            return neg ? -j : j;
        }

        public static int DecodeScaleFactor(BitStream input)
        {
            int offset = FindOffset(input, Codebooks.HCB_SF, _FAST_SF);
            return Codebooks.HCB_SF[offset][2];
        }

        public static void DecodeSpectralData(BitStream input, int cb, int[] data, int off)
        {
            int[][] HCB = Codebooks.CODEBOOKS[cb - 1];

            //find index
            int offset = FindOffset(input, HCB, _FAST[cb - 1]);

            //copy data
            data[off] = HCB[offset][2];
            data[off + 1] = HCB[offset][3];
            if (cb < 5)
            {
                data[off + 2] = HCB[offset][4];
                data[off + 3] = HCB[offset][5];
            }

            //sign & escape
            if (cb < 11)
            {
                if (_UNSIGNED[cb - 1]) SignValues(input, data, off, cb < 5 ? _QUAD_LEN : _PAIR_LEN);
            }
            else if (cb == 11 || cb > 15)
            {
                SignValues(input, data, off, cb < 5 ? _QUAD_LEN : _PAIR_LEN); //virtual codebooks are always unsigned
                if (Math.Abs(data[off]) == 16) data[off] = GetEscape(input, data[off]);
                if (Math.Abs(data[off + 1]) == 16) data[off + 1] = GetEscape(input, data[off + 1]);
            }
            else throw new AACException("Huffman: unknown spectral codebook: " + cb);
        }
    }
}
