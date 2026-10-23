using System;
using System.Text;

namespace AutoChess.Core.Net {
    /// <summary>Little-endian binary writer with zig-zag varints. Grows as needed; reuse it to avoid garbage.</summary>
    public sealed class NetWriter {
        byte[] buffer;
        public int Length { get; private set; }

        public NetWriter(int capacity = 64) {
            buffer = new byte[Math.Max(16, capacity)];
        }

        public void Reset() => Length = 0;

        public byte[] ToArray() {
            var result = new byte[Length];
            Buffer.BlockCopy(buffer, 0, result, 0, Length);
            return result;
        }

        void Ensure(int extra) {
            if (Length + extra <= buffer.Length) return;
            var grown = new byte[Math.Max(buffer.Length * 2, Length + extra)];
            Buffer.BlockCopy(buffer, 0, grown, 0, Length);
            buffer = grown;
        }

        public void WriteByte(byte value) {
            Ensure(1);
            buffer[Length++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteVarUInt(uint value) {
            Ensure(5);
            while (value >= 0x80) {
                buffer[Length++] = (byte)(value | 0x80);
                value >>= 7;
            }
            buffer[Length++] = (byte)value;
        }

        public void WriteVarInt(int value) => WriteVarUInt((uint)((value << 1) ^ (value >> 31)));

        public void WriteULong(ulong value) {
            Ensure(8);
            for (var i = 0; i < 8; i++) buffer[Length++] = (byte)(value >> (8 * i));
        }

        public void WriteString(string value) {
            if (value == null) {
                WriteVarInt(-1);
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(bytes.Length);
            Ensure(bytes.Length);
            Buffer.BlockCopy(bytes, 0, buffer, Length, bytes.Length);
            Length += bytes.Length;
        }
    }

    public sealed class NetReader {
        readonly byte[] buffer;
        int position;
        readonly int end;

        public NetReader(byte[] buffer, int offset = 0, int count = -1) {
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            position = offset;
            end = count < 0 ? buffer.Length : offset + count;
        }

        public bool AtEnd => position >= end;

        public byte ReadByte() {
            if (position >= end) throw new FormatException("unexpected end of message");
            return buffer[position++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public uint ReadVarUInt() {
            uint result = 0;
            for (var shift = 0; shift < 35; shift += 7) {
                var b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
            }
            throw new FormatException("varint too long");
        }

        public int ReadVarInt() {
            var raw = ReadVarUInt();
            return (int)(raw >> 1) ^ -(int)(raw & 1);
        }

        public ulong ReadULong() {
            ulong value = 0;
            for (var i = 0; i < 8; i++) value |= (ulong)ReadByte() << (8 * i);
            return value;
        }

        public string ReadString() {
            var length = ReadVarInt();
            if (length < 0) return null;
            if (position + length > end) throw new FormatException("string past end of message");
            var s = Encoding.UTF8.GetString(buffer, position, length);
            position += length;
            return s;
        }
    }
}
