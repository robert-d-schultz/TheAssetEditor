﻿using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Filetypes.ByteParsing
{
    public class ByteChunk
    {
        byte[] _buffer;
        int _currentIndex;
        public ByteChunk(byte[] buffer, int index = 0)
        {
            _buffer = buffer;
            _currentIndex = index;
        }

        public void Reset()
        {
            Index = 0;
        }

        public static ByteChunk FromFile(string fileName)
        {
            var bytes = File.ReadAllBytes(fileName);
            return new ByteChunk(bytes);
        }

        public int BytesLeft => _buffer.Length - _currentIndex;
        public int Index { get { return _currentIndex; } set { _currentIndex = value;} }

        public byte[] Buffer { get { return _buffer; } }

        T Read<T>(SpesificByteParser<T> parser)
        {
            if (!parser.TryDecodeValue(_buffer, _currentIndex, out T value, out int bytesRead, out string error))
                throw new Exception("Unable to parse :" + error);

            _currentIndex += bytesRead;
            return value;
        }

        string ReadFixedLengthString(StringParser parser, int length)
        {
            if (!parser.TryDecodeFixedLength(_buffer, _currentIndex, length, out var value, out int bytesRead))
                throw new Exception("Unable to parse");

            _currentIndex += bytesRead;
            return value;
        }



        string ReadZeroTerminatedString(StringParser parser)
        {
            if (!parser.TryDecodeZeroTerminatedString(_buffer, _currentIndex, out var value, out int bytesRead))
                throw new Exception("Unable to parse");

            _currentIndex += bytesRead;
            return value;
            
        }

        T Peak<T>(SpesificByteParser<T> parser)
        {
            if (!parser.TryDecodeValue(_buffer, _currentIndex, out T value, out int bytesRead, out string error))
                throw new Exception("Unable to parse :" + error);

            return value;
        }

        public byte[] ReadBytesUntil(int index)
        {
            var length = index - _currentIndex;
            byte[] destination = new byte[length];
            Array.Copy(_buffer, index, destination, 0, length);
            _currentIndex += length;
            return destination;
        }

        public byte[] ReadBytes(int count)
        {
            byte[] destination = new byte[count];
            Array.Copy(_buffer, _currentIndex, destination, 0, count);
            _currentIndex += count;
            return destination;
        }

        public void Advance(int byteCount)
        {
            _currentIndex += byteCount;
        }

        public void Read(IByteParser parser, out string value, out string error)
        {
            if (!parser.TryDecode(_buffer, _currentIndex, out  value, out int bytesRead, out error))
                throw new Exception("Unable to parse :" + error);

            _currentIndex += bytesRead;
        }

        public void Read<T>(SpesificByteParser<T> parser, out T value, out string error)
        {
            if (!parser.TryDecodeValue(_buffer, _currentIndex, out value, out int bytesRead, out error))
                throw new Exception("Unable to parse :" + error);

            _currentIndex += bytesRead;
        }

        public string ReadStringAscii() => Read(ByteParsers.StringAscii);
        public string ReadString() => Read(ByteParsers.String);
        public int ReadInt32() => Read(ByteParsers.Int32);
        public uint ReadUInt32() => Read(ByteParsers.UInt32);
        public long ReadInt64() => Read(ByteParsers.Int64);
        public float ReadSingle() => Read(ByteParsers.Single);
        public Half ReadFloat16() => Read(ByteParsers.Float16);
        public short ReadShort() => Read(ByteParsers.Short);
        public ushort ReadUShort() => Read(ByteParsers.UShort);
        public bool ReadBool() => Read(ByteParsers.Bool);
        public byte ReadByte() => Read(ByteParsers.Byte);
        public string ReadStringTableIndex(IEnumerable<string> stringTable) => stringTable.ElementAt(ReadInt32());


        public uint PeakUint32() => Peak(ByteParsers.UInt32);
        public long PeakInt64() => Peak(ByteParsers.Int64);
        public byte PeakByte() => Peak(ByteParsers.Byte);

        public UnknownParseResult PeakUnknown()
        {
            var parsers = ByteParsers.GetAllParsers();
            var output = new List<string>();
            foreach (var parser in parsers)
            {
             
                    var result = parser.TryDecode(_buffer, _currentIndex, out string value, out int bytesRead, out string error);
                    if (!result)
                        output.Add($"{parser.TypeName} - Failed:{error}");
                    else
                        output.Add($"{parser.TypeName} - {value}");

            }

            return new UnknownParseResult() { Data = output.ToArray()};
        }


        public ByteChunk CreateSub(int size)
        {
            var data = ReadBytes(size);
            return new ByteChunk(data);
        }
        public string ReadFixedLength(int length) => ReadFixedLengthString(ByteParsers.String, length);
        public string ReadZeroTerminatedStr() => ReadZeroTerminatedString(ByteParsers.String);


        public class UnknownParseResult
        {
            public string[] Data { get; set; }
            public override string ToString()
            {
                var strOuput = "";
                if (Data != null)
                {
                    foreach (var s in Data)
                        strOuput += s + "\n";
                }
                return strOuput;

            }
        }

       
        public byte[] Debug_LookForDataAfterFixedStr(int size)
        {
            var dataCpy = new ByteChunk(ReadBytes(size));
            Index -= size;

            var str = dataCpy.ReadFixedLength(size);
            var strClean = Util.SanatizeFixedString(str);

            dataCpy.Reset();
            dataCpy.Index = strClean.Length;
            var bytesAfterClean = dataCpy.ReadBytes(dataCpy.BytesLeft);
            var nonZeroBytes = bytesAfterClean.Count(x => x != 0);
            if (nonZeroBytes != 0)
            {
                return bytesAfterClean;
            }

            return null;
        }


        public string Debug_LookForStrAfterFixedStr(int size)
        {
            var dataCpy = new ByteChunk(ReadBytes(size));
            Index -= size;

            var str = dataCpy.ReadFixedLength(size);
            var strClean = Util.SanatizeFixedString(str);

            dataCpy.Reset();
            dataCpy.Index = strClean.Length;
            var bytesAfterClean = dataCpy.ReadBytes(dataCpy.BytesLeft);
            var nonZeroBytes = bytesAfterClean.Count(x => x != 0);
            if (nonZeroBytes != 0)
            {
                dataCpy.Index = strClean.Length;
                var strAfterClean = dataCpy.ReadFixedLength(dataCpy.BytesLeft);
                return strAfterClean;
            }

            return null;
        }


        public override string ToString()
        {
            return $"ByteParser[Size = {_buffer?.Length}, index = {_currentIndex}]";
        }
    }
}
