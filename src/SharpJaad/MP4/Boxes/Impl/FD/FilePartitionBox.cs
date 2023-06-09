﻿using System.Text;

namespace SharpJaad.MP4.Boxes.Impl.FD
{
    public class FilePartitionBox : FullBox
    {
        private int _itemID, _packetPayloadSize, _fecEncodingID, _fecInstanceID,
                _maxSourceBlockLength, _encodingSymbolLength, _maxNumberOfEncodingSymbols;
        private string _schemeSpecificInfo;
        private int[] _blockCounts;
        private long[] _blockSizes;

        public FilePartitionBox() : base("File Partition Box")
        { }

        public override void Decode(MP4InputStream input)
        {
            base.Decode(input);

            _itemID = (int)input.ReadBytes(2);
            _packetPayloadSize = (int)input.ReadBytes(2);
            input.SkipBytes(1); //reserved
            _fecEncodingID = input.Read();
            _fecInstanceID = (int)input.ReadBytes(2);
            _maxSourceBlockLength = (int)input.ReadBytes(2);
            _encodingSymbolLength = (int)input.ReadBytes(2);
            _maxNumberOfEncodingSymbols = (int)input.ReadBytes(2);
            _schemeSpecificInfo = Encoding.UTF8.GetString(Base64Decoder.Decode(input.ReadTerminated((int)GetLeft(input), 0)));

            int entryCount = (int)input.ReadBytes(2);
            _blockCounts = new int[entryCount];
            _blockSizes = new long[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                _blockCounts[i] = (int)input.ReadBytes(2);
                _blockSizes[i] = (int)input.ReadBytes(4);
            }
        }

        /**
         * The item ID references the item in the item location box that the file
         * partitioning applies to.
         *
         * @return the item ID
         */
        public int GetItemID()
        {
            return _itemID;
        }

        /**
         * The packet payload size gives the target ALC/LCT or FLUTE packet payload
         * size of the partitioning algorithm. Note that UDP packet payloads are
         * larger, as they also contain ALC/LCT or FLUTE headers.
         * 
         * @return the packet payload size
         */
        public int GetPacketPayloadSize()
        {
            return _packetPayloadSize;
        }

        /**
         * The FEC encoding ID is subject to IANA registration (see RFC 3452). Note
         * that
         * - value zero corresponds to the "Compact No-Code FEC scheme" also known
         * as "Null-FEC" (RFC 3695);
         * - value one corresponds to the "MBMS FEC" (3GPP TS 26.346);
         * - for values in the range of 0 to 127, inclusive, the FEC scheme is
         * Fully-Specified, whereas for values in the range of 128 to 255,
         * inclusive, the FEC scheme is Under-Specified.
         *
         * @return the FEC encoding ID
         */
        public int GetFECEncodingID()
        {
            return _fecEncodingID;
        }

        /**
         * The FEC instance ID provides a more specific identification of the FEC
         * encoder being used for an Under-Specified FEC scheme. This value should
         * be set to zero for Fully-Specified FEC schemes and shall be ignored when
         * parsing a file with an FEC encoding ID in the range of 0 to 127,
         * inclusive. The FEC instance ID is scoped by the FEC encoding ID. See RFC
         * 3452 for further details.
         *
         * @return the FEC instance ID
         */
        public int GetFECInstanceID()
        {
            return _fecInstanceID;
        }

        /**
         * The maximum source block length gives the maximum number of source
         * symbols per source block.
         *
         * @return the maximum source block length
         */
        public int GetMaxSourceBlockLength()
        {
            return _maxSourceBlockLength;
        }

        /**
         * The encoding symbol length gives the size (in bytes) of one encoding
         * symbol. All encoding symbols of one item have the same length, except the
         * last symbol which may be shorter.
         *
         * @return the encoding symbol length
         */
        public int GetEncodingSymbolLength()
        {
            return _encodingSymbolLength;
        }

        /**
         * The maximum number of encoding symbols  that can be generated for a
         * source block for those FEC schemes in which the maximum number of
         * encoding symbols is relevant, such as FEC encoding ID 129 defined in RFC
         * 3452. For those FEC schemes in which the maximum number of encoding
         * symbols is not relevant, the semantics of this field is unspecified.
         *
         * @return the maximum number of encoding symbols
         */
        public int GetMaxNumberOfEncodingSymbols()
        {
            return _maxNumberOfEncodingSymbols;
        }

        /**
         * The scheme specific info is a String of the scheme-specific object 
         * transfer information (FEC-OTI-Scheme-Specific-Info). The definition of 
         * the information depends on the EC encoding ID.
         * 
         * @return the scheme specific info
         */
        public string GetSchemeSpecificInfo()
        {
            return _schemeSpecificInfo;
        }

        /**
         * A block count indicates the number of consecutive source blocks with a
         * specified size.
         *
         * @return all block counts
         */
        public int[] GetBlockCounts()
        {
            return _blockCounts;
        }

        /**
         * A block size indicates the size of a block (in bytes). A block_size that 
         * is not a multiple of the encoding symbol length indicates with Compact 
         * No-Code FEC that the last source symbols includes padding that is not 
         * stored in the item. With MBMS FEC (3GPP TS 26.346) the padding may extend
         * across multiple symbols but the size of padding should never be more than
         * the encoding symbol length.
         *
         * @return all block sizes
         */
        public long[] GetBlockSizes()
        {
            return _blockSizes;
        }
    }
}