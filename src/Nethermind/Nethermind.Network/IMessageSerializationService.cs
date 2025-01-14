﻿//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
//
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System.Reflection;
using DotNetty.Buffers;

namespace Nethermind.Network
{
    public interface IMessageSerializationService
    {
        IByteBuffer ZeroSerialize<T>(T message, ByteBufferAllocator allocator = ByteBufferAllocator.PooledByteBufferAllocator) where T : MessageBase;
        T Deserialize<T>(byte[] bytes) where T : MessageBase;
        T Deserialize<T>(IByteBuffer buffer) where T : MessageBase;
        void Register(Assembly assembly);
        void Register<T>(IZeroMessageSerializer<T> messageSerializer) where T : MessageBase;
    }

    public enum ByteBufferAllocator
    {
        UnpooledByteBufferAllocator,
        PooledByteBufferAllocator
    }
}
