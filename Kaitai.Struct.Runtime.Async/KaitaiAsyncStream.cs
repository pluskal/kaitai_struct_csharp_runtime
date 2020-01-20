﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;

namespace Kaitai.Async
{
    public partial class KaitaiAsyncStream : KaitaiStreamBase, IKaitaiAsyncStream
    {
        protected Stream BaseStream;

        private ulong _bits = 0;
        private int _bitsLeft = 0;

        protected AsyncBinaryReader AsyncBinaryReader { get; }

        #region Constructors

        public KaitaiAsyncStream(Stream stream)
        {
            BaseStream = stream;
            AsyncBinaryReader = new AsyncBinaryReader(BaseStream);

        }

        ///<summary>
        /// Creates a IKaitaiAsyncStream backed by a file (RO)
        ///</summary>
        public KaitaiAsyncStream(string file) : this(File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
        }

        ///<summary>
        ///Creates a IKaitaiAsyncStream backed by a byte buffer
        ///</summary>
        public KaitaiAsyncStream(byte[] bytes) : this(new MemoryStream(bytes))
        {
        }

        #endregion

        #region Stream positioning

        public override bool IsEof => BaseStream.Position >= BaseStream.Length && _bitsLeft == 0;

        public virtual Task SeekAsync(long position)
        {
            BaseStream.Seek(position, SeekOrigin.Begin);
            return Task.CompletedTask;
        }

        public override long Pos => BaseStream.Position;

        public override long Size => BaseStream.Length;

        #endregion

        #region Integer types

        #region Signed

        public async Task<sbyte> ReadS1Async() => await AsyncBinaryReader.ReadSByteAsync();

        #region Big-endian

        public async Task<short> ReadS2beAsync() => BitConverter.ToInt16(await ReadBytesNormalisedBigEndianAsync(2), 0);

        public async Task<int> ReadS4beAsync() => BitConverter.ToInt32(await ReadBytesNormalisedBigEndianAsync(4), 0);

        public async Task<long> ReadS8beAsync() => BitConverter.ToInt64(await ReadBytesNormalisedBigEndianAsync(8), 0);

        #endregion

        #region Little-endian

        public async Task<short> ReadS2leAsync() => BitConverter.ToInt16(await ReadBytesNormalisedLittleEndianAsync(2), 0);

        public async Task<int> ReadS4leAsync() => BitConverter.ToInt32(await ReadBytesNormalisedLittleEndianAsync(4), 0);

        public async Task<long> ReadS8leAsync() => BitConverter.ToInt64(await ReadBytesNormalisedLittleEndianAsync(8), 0);

        #endregion

        #endregion

        #region Unsigned

        public async Task<byte> ReadU1Async() => await AsyncBinaryReader.ReadByteAsync();

        #region Big-endian

        public async Task<ushort> ReadU2beAsync() => BitConverter.ToUInt16(await ReadBytesNormalisedBigEndianAsync(2), 0);

        public async Task<uint> ReadU4beAsync() => BitConverter.ToUInt32(await ReadBytesNormalisedBigEndianAsync(4), 0);

        public async Task<ulong> ReadU8beAsync() => BitConverter.ToUInt64(await ReadBytesNormalisedBigEndianAsync(8), 0);

        #endregion

        #region Little-endian

        public async Task<ushort> ReadU2leAsync() => BitConverter.ToUInt16(await ReadBytesNormalisedLittleEndianAsync(2), 0);

        public async Task<uint> ReadU4leAsync() => BitConverter.ToUInt32(await ReadBytesNormalisedLittleEndianAsync(4), 0);

        public async Task<ulong> ReadU8leAsync() => BitConverter.ToUInt64(await ReadBytesNormalisedLittleEndianAsync(8), 0);

        #endregion

        #endregion

        #endregion

        #region Floating point types

        #region Big-endian

        public async Task<float> ReadF4beAsync() => BitConverter.ToSingle(await ReadBytesNormalisedBigEndianAsync(4), 0);

        public async Task<double> ReadF8beAsync() => BitConverter.ToDouble(await ReadBytesNormalisedBigEndianAsync(8), 0);

        #endregion

        #region Little-endian

        public async Task<float> ReadF4leAsync() => BitConverter.ToSingle(await ReadBytesNormalisedLittleEndianAsync(4), 0);

        public async Task<double> ReadF8leAsync() => BitConverter.ToDouble(await ReadBytesNormalisedLittleEndianAsync(8), 0);

        #endregion

        #endregion

        #region Unaligned bit values

        public override void AlignToByte()
        {
            _bits = 0;
            _bitsLeft = 0;
        }

        public async Task<ulong> ReadBitsIntAsync(int n)
        {
            int bitsNeeded = n - _bitsLeft;
            if (bitsNeeded > 0)
            {
                // 1 bit  => 1 byte
                // 8 bits => 1 byte
                // 9 bits => 2 bytes
                int bytesNeeded = ((bitsNeeded - 1) / 8) + 1;
                byte[] buf = await ReadBytesAsync(bytesNeeded);
                for (int i = 0; i < buf.Length; i++)
                {
                    _bits <<= 8;
                    _bits |= buf[i];
                    _bitsLeft += 8;
                }
            }

            // raw mask with required number of 1s, starting from lowest bit
            ulong mask = GetMaskOnes(n);
            // shift mask to align with highest bits available in "bits"
            int shiftBits = _bitsLeft - n;
            mask = mask << shiftBits;
            // derive reading result
            ulong res = (_bits & mask) >> shiftBits;
            // clear top bits that we've just read => AND with 1s
            _bitsLeft -= n;
            mask = GetMaskOnes(_bitsLeft);
            _bits &= mask;

            return res;
        }

        //Method ported from algorithm specified @ issue#155
        public async Task<ulong> ReadBitsIntLeAsync(int n)
        {
            int bitsNeeded = n - _bitsLeft;

            if (bitsNeeded > 0)
            {
                // 1 bit  => 1 byte
                // 8 bits => 1 byte
                // 9 bits => 2 bytes
                int bytesNeeded = ((bitsNeeded - 1) / 8) + 1;
                byte[] buf = await ReadBytesAsync(bytesNeeded);
                for (int i = 0; i < buf.Length; i++)
                {
                    ulong v = (ulong)((ulong)buf[i] << _bitsLeft);
                    _bits |= v;
                    _bitsLeft += 8;
                }
            }

            // raw mask with required number of 1s, starting from lowest bit
            ulong mask = GetMaskOnes(n);

            // derive reading result
            ulong res = (_bits & mask);

            // remove bottom bits that we've just read by shifting
            _bits >>= n;
            _bitsLeft -= n;

            return res;
        }

        #endregion

        #region Byte arrays

        public async Task<byte[]> ReadBytesAsync(long count)
        {
            if (count < 0 || count > Int32.MaxValue)
                throw new ArgumentOutOfRangeException("requested " + count + " bytes, while only non-negative int32 amount of bytes possible");
            byte[] bytes = await AsyncBinaryReader.ReadBytesAsync((int)count);
            if (bytes.Length < count)
                throw new EndOfStreamException("requested " + count + " bytes, but got only " + bytes.Length + " bytes");
            return bytes;
        }

        public async Task<byte[]> ReadBytesAsync(ulong count)
        {
            if (count > Int32.MaxValue)
                throw new ArgumentOutOfRangeException("requested " + count + " bytes, while only non-negative int32 amount of bytes possible");
            byte[] bytes = await AsyncBinaryReader.ReadBytesAsync((int)count);
            if (bytes.Length < (int)count)
                throw new EndOfStreamException("requested " + count + " bytes, but got only " + bytes.Length + " bytes");
            return bytes;
        }

        /// <summary>
        /// Read bytes from the stream in little endian format and convert them to the endianness of the current platform
        /// </summary>
        /// <param name="count">The number of bytes to read</param>
        /// <returns>An array of bytes that matches the endianness of the current platform</returns>
        protected async Task<byte[]> ReadBytesNormalisedLittleEndianAsync(int count)
        {
            byte[] bytes = await ReadBytesAsync(count);
            if (!IsLittleEndian) Array.Reverse(bytes);
            return bytes;
        }

        /// <summary>
        /// Read bytes from the stream in big endian format and convert them to the endianness of the current platform
        /// </summary>
        /// <param name="count">The number of bytes to read</param>
        /// <returns>An array of bytes that matches the endianness of the current platform</returns>
        protected async Task<byte[]> ReadBytesNormalisedBigEndianAsync(int count)
        {
            byte[] bytes = await ReadBytesAsync(count);
            if (IsLittleEndian) Array.Reverse(bytes);
            return bytes;
        }

        /// <summary>
        /// Read all the remaining bytes from the stream until the end is reached
        /// </summary>
        /// <returns></returns>
        /// //TODO Handle asynchronously, BaseStream.Length is troublesome
        public virtual async Task<byte[]> ReadBytesFullAsync() => await ReadBytesAsync(BaseStream.Length - BaseStream.Position);

        /// <summary>
        /// Read a terminated string from the stream
        /// </summary>
        /// <param name="terminator">The string terminator value</param>
        /// <param name="includeTerminator">True to include the terminator in the returned string</param>
        /// <param name="consumeTerminator">True to consume the terminator byte before returning</param>
        /// <param name="eosError">True to throw an error when the EOS was reached before the terminator</param>
        /// <returns></returns>
        public async Task<byte[]> ReadBytesTermAsync(byte terminator, bool includeTerminator, bool consumeTerminator, bool eosError)
        {
            List<byte> bytes = new List<byte>();
            while (true)
            {
                if (IsEof)
                {
                    if (eosError) throw new EndOfStreamException(
                      $"End of stream reached, but no terminator `{terminator}` found");
                    break;
                }

                byte b = await AsyncBinaryReader.ReadByteAsync();
                if (b == terminator)
                {
                    if (includeTerminator) bytes.Add(b);
                    if (!consumeTerminator) await SeekAsync(Pos - 1);
                    break;
                }
                bytes.Add(b);
            }
            return bytes.ToArray();
        }

        /// <summary>
        /// Read a specific set of bytes and assert that they are the same as an expected result
        /// </summary>
        /// <param name="expected">The expected result</param>
        /// <returns></returns>
        public async Task<byte[]> EnsureFixedContentsAsync(byte[] expected)
        {
            byte[] bytes = await ReadBytesAsync(expected.Length);

            if (bytes.Length != expected.Length) //TODO Is this necessary?
            {
                throw new Exception(
                  $"Expected bytes: {Convert.ToBase64String(expected)} ({expected.Length} bytes), Instead got: {Convert.ToBase64String(bytes)} ({bytes.Length} bytes)");
            }
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != expected[i])
                {
                    throw new Exception(
                      $"Expected bytes: {Convert.ToBase64String(expected)} ({expected.Length} bytes), Instead got: {Convert.ToBase64String(bytes)} ({bytes.Length} bytes)");
                }
            }

            return bytes;
        }

        #endregion
    }
}
