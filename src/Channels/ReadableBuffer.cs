﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Channels
{
    public struct ReadableBuffer : IDisposable, IEnumerable<BufferSpan>
    {
        private readonly BufferSpan _span;
        private readonly bool _isOwner;
        private readonly Channel _channel;

        private ReadCursor _start;
        private ReadCursor _end;
        private int _length;

        public int Length => _length >= 0 ? _length : GetLength();
        public bool IsEmpty => Length == 0;

        public bool IsSingleSpan => _start.Segment.Block == _end.Segment.Block;

        public BufferSpan FirstSpan => _span;

        public ReadCursor Start => _start;
        public ReadCursor End => _end;

        internal ReadableBuffer(Channel channel, ReadCursor start, ReadCursor end) :
            this(channel, start, end, isOwner: false)
        {

        }

        internal ReadableBuffer(Channel channel, ReadCursor start, ReadCursor end, bool isOwner)
        {
            _channel = channel;
            _start = start;
            _end = end;
            _isOwner = isOwner;

            var begin = start;
            begin.TryGetBuffer(end, out _span);

            _length = -1;
        }

        private ReadableBuffer(ref ReadableBuffer buffer)
        {
            _channel = buffer._channel;

            var begin = buffer._start;
            var end = buffer._end;

            MemoryBlockSegment segmentTail;
            var segmentHead = MemoryBlockSegment.Clone(begin, end, out segmentTail);

            begin = new ReadCursor(segmentHead);
            end = new ReadCursor(segmentTail, segmentTail.End);

            _start = begin;
            _end = end;
            _isOwner = true;
            _span = buffer._span;

            _length = buffer._length;
        }

        public ReadCursor IndexOf(ref Vector<byte> byte0Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector);
            return begin;
        }

        public ReadCursor IndexOfAny(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector, ref byte1Vector);
            return begin;
        }

        public ReadCursor IndexOfAny(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector, ref Vector<byte> byte2Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector, ref byte1Vector, ref byte2Vector);
            return begin;
        }

        public ReadableBuffer Slice(int start, int length)
        {
            var begin = _start;
            if (start != 0)
            {
                begin.Seek(start);
            }
            var end = begin;
            end.Seek(length);
            return Slice(begin, end);
        }

        public ReadableBuffer Slice(int start, ReadCursor end)
        {
            var begin = _start;
            if (start != 0)
            {
                begin.Seek(start);
            }
            return Slice(begin, end);
        }

        public ReadableBuffer Slice(ReadCursor start, ReadCursor end)
        {
            return new ReadableBuffer(_channel, start, end);
        }

        public ReadableBuffer Slice(ReadCursor start, int length)
        {
            var end = start;
            end.Seek(length);
            return Slice(start, end);
        }

        public ReadableBuffer Slice(ReadCursor start)
        {
            return new ReadableBuffer(_channel, start, _end);
        }

        public ReadableBuffer Slice(int start)
        {
            var begin = _start;
            begin.Seek(start);
            return new ReadableBuffer(_channel, begin, _end);
        }

        public int Peek()
        {
            if (IsEmpty)
            {
                return -1;
            }

            var span = FirstSpan;
            return span.Array[span.Offset];
        }

        public ReadableBuffer Preserve()
        {
            return new ReadableBuffer(ref this);
        }

        public unsafe void CopyTo(byte* destination, int length)
        {
            if (length < Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            var remaining = (uint)Length;
            foreach (var span in this)
            {
                if (remaining == 0)
                {
                    break;
                }

                var count = (uint)Math.Min(remaining, span.Length);
                Unsafe.CopyBlock(destination, (byte*)span.BufferPtr, count);
                remaining -= count;
            }
        }

        public void CopyTo(byte[] data)
        {
            CopyTo(data, 0);
        }

        public void CopyTo(byte[] data, int offset)
        {
            if (data.Length < Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            foreach (var span in this)
            {
                Buffer.BlockCopy(span.Array, span.Offset, data, offset, span.Length);
                offset += span.Length;
            }
        }

        public byte[] ToArray()
        {
            var buffer = new byte[Length];
            CopyTo(buffer);
            return buffer;
        }

        private int GetLength()
        {
            var begin = _start;
            var length = begin.GetLength(_end);
            _length = length;
            return length;
        }

        public void Dispose()
        {
            if (!_isOwner)
            {
                return;
            }

            var returnStart = _start.Segment;
            var returnEnd = _end.Segment;

            while (true)
            {
                var returnSegment = returnStart;
                returnStart = returnStart?.Next;
                returnSegment?.Dispose();

                if (returnSegment == returnEnd)
                {
                    break;
                }
            }

            _start = default(ReadCursor);
            _end = default(ReadCursor);
        }

        public void Consumed()
        {
            _channel.EndRead(End, End);
        }

        public void Consumed(ReadCursor consumed)
        {
            _channel.EndRead(consumed, consumed);
        }

        public void Consumed(ReadCursor consumed, ReadCursor examined)
        {
            _channel.EndRead(consumed, examined);
        }

        public override string ToString()
        {
            return FirstSpan.ToString();
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        IEnumerator<BufferSpan> IEnumerable<BufferSpan>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<BufferSpan>
        {
            private ReadableBuffer _buffer;
            private BufferSpan _current;

            public Enumerator(ref ReadableBuffer buffer)
            {
                _buffer = buffer;
                _current = default(BufferSpan);
            }

            public BufferSpan Current => _current;

            object IEnumerator.Current
            {
                get { return _current; }
            }

            public void Dispose()
            {

            }

            public bool MoveNext()
            {
                var start = _buffer.Start;
                bool moved = start.TryGetBuffer(_buffer.End, out _current);
                _buffer = _buffer.Slice(start);
                return moved;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        public unsafe bool Equals(byte[] value, int offset, int count)
        {
            if (Length != count) return false; // just nope

            fixed (byte* basePtr = value)
            {
                var ptr = basePtr + offset;
                if (IsSingleSpan)
                {
                    return Equals((byte*)FirstSpan.BufferPtr, ptr, count);
                }

                foreach (var span in this)
                {
                    if (!Equals((byte*)span.BufferPtr, ptr, span.Length)) return false;
                    ptr += span.Length;
                }
                return true;
            }
        }
        static readonly int
            VectorWidth = Vector<byte>.Count,
            VectorShift = (int)Math.Log(VectorWidth, 2),
            VectorRemainderMask = ~(~0 << VectorShift);
        private static unsafe bool Equals(byte* a, byte* b, int count)
        {
            switch (count)
            {
                case 0: return true;
                // special-case 1-7 - can't use qword checks
                case 1: return *a == *b;
                case 2: return *(ushort*)a == *(ushort*)b;
                case 3: return *(ushort*)a == *(ushort*)b && a[2] == b[2];
                case 4: return *(uint*)a == *(uint*)b;
                case 5: return *(uint*)a == *(uint*)b && a[4] == b[4];
                case 6: return *(uint*)a == *(uint*)b && *(ushort*)(a + 4) == *(ushort*)(b + 4);
                case 7: return *(uint*)a == *(uint*)b && *(uint*)(a + 3) == *(uint*)(b + 3);
                // special case a single qword, for simplicity and performance
                case 8: return *(ulong*)a == *(ulong*)b;
                default:
                    int chunks;
                    if (Vector.IsHardwareAccelerated && (chunks = count >> VectorShift) != 0)
                    {
                        do
                        {
                            if (Unsafe.Read<Vector<byte>>(a) != Unsafe.Read<Vector<byte>>(b)) return false;
                            a += VectorWidth;
                            b += VectorWidth;
                        } while (--chunks != 0);
                        count &= VectorRemainderMask;
                    }
                    // qword chunks
                    chunks = count >> 3;
                    if (chunks != 0)
                    {
                        ulong* a8 = (ulong*)a, b8 = (ulong*)b;
                        do
                        {
                            if (*a8++ != *b8++) return false;
                        } while (--chunks != 0);
                        a = (byte*)a8;
                        b = (byte*)b8;
                    }
                    // if we're checking a multiple of 8, we're done
                    if ((count & 7) == 0) return true;

                    // now check the last qword, noting that we have enough space
                    // to look backwards (because we're in the > 8 default case)
                    int delta = 8 - (count & 7);
                    return *(ulong*)(a - delta) == *(ulong*)(b - delta);
            }
        }
    }
}
