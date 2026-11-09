using System;
using System.Collections.Generic;
using System.IO;

namespace SharpJaad.MP4
{
    public class MP4Exception : IOException
    {
        public MP4Exception(string message) : base(message)
        { }

        internal MP4Exception(string message, Exception innerException) : base(message, innerException)
        { }

        /// <summary>
        /// The exception types that malformed data causes in the parsing code (missing boxes, wrong box types, values
        /// out of range): they are reported as an MP4Exception.
        /// </summary>
        internal static bool IsMalformedData(Exception e)
        {
            return e is NullReferenceException || e is InvalidCastException || e is IndexOutOfRangeException
                || e is ArgumentException || e is OverflowException || e is InvalidOperationException
                || e is DivideByZeroException || e is KeyNotFoundException || e is FormatException;
        }
    }
}
