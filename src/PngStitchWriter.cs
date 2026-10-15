using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Astonia.Editor
{
    // Rows are compressed as they are stitched; the complete image is never held in RAM.
    public sealed class PngStitchWriter : IDisposable
    {
        private readonly Stream file;
        private readonly IdatStream chunks;
        private readonly DeflateStream compressor;
        private readonly byte[] row;
        private readonly int height;
        private int written;
        private uint adlerOne = 1, adlerTwo;
        private bool disposed;
        private static readonly uint[] crcTable = CrcTable();
        private static uint[] CrcTable() { var table = new uint[256]; for (uint i = 0; i < 256; i++) { uint value = i; for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1; table[i] = value; } return table; }
        private static void UInt(Stream stream, uint value) { stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16)); stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
        private static void Chunk(Stream stream, string type, byte[] data, int count)
        {
            UInt(stream, (uint)count); byte[] name = Encoding.ASCII.GetBytes(type); stream.Write(name, 0, name.Length); stream.Write(data, 0, count);
            uint crc = 0xffffffff; foreach (byte b in name) crc = crcTable[(crc ^ b) & 255] ^ (crc >> 8); for (int i = 0; i < count; i++) crc = crcTable[(crc ^ data[i]) & 255] ^ (crc >> 8); UInt(stream, crc ^ 0xffffffff);
        }
        public PngStitchWriter(string path, int width, int imageHeight)
        {
            if (width <= 0 || imageHeight <= 0) throw new ArgumentOutOfRangeException("width");
            height = imageHeight; row = new byte[checked(width * 4 + 1)]; file = File.Create(path);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 }; file.Write(signature, 0, signature.Length);
            using (var header = new MemoryStream()) { UInt(header, (uint)width); UInt(header, (uint)height); header.WriteByte(8); header.WriteByte(6); header.WriteByte(0); header.WriteByte(0); header.WriteByte(0); Chunk(file, "IHDR", header.ToArray(), 13); }
            chunks = new IdatStream(file); chunks.WriteByte(0x78); chunks.WriteByte(0x9c); compressor = new DeflateStream(chunks, CompressionLevel.Optimal, true);
        }
        public void WriteBgraRow(byte[] band, int offset)
        {
            if (disposed || written >= height) throw new InvalidOperationException("Too many PNG rows.");
            row[0] = 1; int oldR = 0, oldG = 0, oldB = 0, oldA = 0;
            for (int i = 1; i < row.Length; i += 4)
            {
                int b = band[offset++], g = band[offset++], r = band[offset++], a = band[offset++];
                row[i] = unchecked((byte)(r - oldR)); row[i + 1] = unchecked((byte)(g - oldG)); row[i + 2] = unchecked((byte)(b - oldB)); row[i + 3] = unchecked((byte)(a - oldA)); oldR = r; oldG = g; oldB = b; oldA = a;
            }
            foreach (byte value in row) { adlerOne = (adlerOne + value) % 65521; adlerTwo = (adlerTwo + adlerOne) % 65521; }
            compressor.Write(row, 0, row.Length); written++;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { compressor.Dispose(); UInt(chunks, (adlerTwo << 16) | adlerOne); chunks.Flush(); if (written != height) throw new IOException("PNG row count does not match its image height."); Chunk(file, "IEND", new byte[0], 0); }
            finally { file.Dispose(); }
        }
        private sealed class IdatStream : Stream
        {
            private readonly Stream target; private readonly byte[] buffer = new byte[65536]; private int used;
            public IdatStream(Stream stream) { target = stream; }
            public override void Write(byte[] data, int offset, int count) { while (count > 0) { int copy = Math.Min(buffer.Length - used, count); Buffer.BlockCopy(data, offset, buffer, used, copy); used += copy; offset += copy; count -= copy; if (used == buffer.Length) Flush(); } }
            public override void Flush() { if (used > 0) { Chunk(target, "IDAT", buffer, used); used = 0; } }
            public override bool CanRead { get { return false; } } public override bool CanWrite { get { return true; } } public override bool CanSeek { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); } public override void SetLength(long value) { throw new NotSupportedException(); }
        }
    }
}
