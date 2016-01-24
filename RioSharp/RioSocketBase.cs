﻿using System;
using System.Threading;

namespace RioSharp
{
    public unsafe class RioSocketBase : IDisposable
    {
        internal IntPtr _socket;
        internal IntPtr _requestQueue;
        internal RioFixedBufferPool SendBufferPool, ReciveBufferPool;

        internal RioSocketBase(RioFixedBufferPool sendBufferPool, RioFixedBufferPool reciveBufferPool,
            uint maxOutstandingReceive, uint maxOutstandingSend, IntPtr SendCompletionQueue, IntPtr ReceiveCompletionQueue)
        {
            if ((_socket = Imports.WSASocket(ADDRESS_FAMILIES.AF_INET, SOCKET_TYPE.SOCK_STREAM, PROTOCOL.IPPROTO_TCP, IntPtr.Zero, 0, SOCKET_FLAGS.REGISTERED_IO | SOCKET_FLAGS.WSA_FLAG_OVERLAPPED)) == IntPtr.Zero)
                Imports.ThrowLastWSAError();
            
            SendBufferPool = sendBufferPool;
            ReciveBufferPool = reciveBufferPool;

            _requestQueue = RioStatic.CreateRequestQueue(_socket, maxOutstandingReceive, 1, maxOutstandingSend, 1, ReceiveCompletionQueue, SendCompletionQueue, GetHashCode());
            Imports.ThrowLastWSAError();
        }

        public void WritePreAllocated(RioBufferSegment Segment)
        {
            unsafe
            {
                if (!RioStatic.Send(_requestQueue, Segment.segmentPointer, 1, RIO_SEND_FLAGS.DEFER, Segment.Index))
                    Imports.ThrowLastWSAError();
            }
        }

        internal unsafe void CommitSend()
        {
            if (!RioStatic.Send(_requestQueue, RIO_BUFSEGMENT.NullSegment, 0, RIO_SEND_FLAGS.COMMIT_ONLY, 0))
                Imports.ThrowLastWSAError();
        }


        internal unsafe void SendInternal(RioBufferSegment segment, RIO_SEND_FLAGS flags)
        {
            if (!RioStatic.Send(_requestQueue, segment.segmentPointer, 1, flags, segment.Index))
                Imports.ThrowLastWSAError();
        }

        internal unsafe void ReciveInternal()
        {
            RioBufferSegment buf;
            if (ReciveBufferPool.TryGetBuffer(out buf))
            {
                if (!RioStatic.Receive(_requestQueue, buf.segmentPointer, 1, RIO_RECEIVE_FLAGS.NONE, buf.Index))
                    Imports.ThrowLastWSAError();
            }
            else
                ThreadPool.QueueUserWorkItem(o =>
                {
                    var b = ReciveBufferPool.GetBuffer();
                    if (!RioStatic.Receive(_requestQueue, ReciveBufferPool.GetBuffer().segmentPointer, 1, RIO_RECEIVE_FLAGS.NONE, b.Index))
                        Imports.ThrowLastWSAError();
                }, null);
        }

        public unsafe void WriteFixed(byte[] buffer)
        {
            var currentSegment = SendBufferPool.GetBuffer();
            fixed (byte* p = &buffer[0])
            {
                Buffer.MemoryCopy(p, currentSegment.rawPointer, currentSegment.TotalLength, buffer.Length);
            }
            currentSegment.segmentPointer->Length = buffer.Length;
            SendInternal(currentSegment, RIO_SEND_FLAGS.NONE);
        }

        public virtual void Dispose()
        {

        }
    }
}
