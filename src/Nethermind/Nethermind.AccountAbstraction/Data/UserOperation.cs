//  Copyright (c) 2021 Demerzel Solutions Limited
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
// 

using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.AccountAbstraction.Data
{
    public class UserOperation
    {
        public UserOperation(Address target, UInt256 callGas, UInt256 postCallGas, UInt256 gasPrice, byte[] callData, Signature signature)
        {
            Target = target;
            CallGas = callGas;
            PostCallGas = postCallGas;
            GasPrice = gasPrice;
            CallData = callData;
            Signature = signature;
        }

        public Address Target { get; set; }
        public UInt256 CallGas { get; set; }
        public UInt256 PostCallGas { get; set; }
        public UInt256 GasPrice { get; set; }
        public byte[] CallData { get; set; }
        public Signature Signature { get; set; }
    }
}