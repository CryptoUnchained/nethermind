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

#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Find;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Int256;
using Nethermind.JsonRpc.Modules.Eth;
using NSubstitute;
using NSubstitute.Extensions;
using NUnit.Framework;

namespace Nethermind.JsonRpc.Test.Modules.Eth
{
    public partial class EthRpcModuleTests
    {
        [Test]
        public async Task Eth_gasPrice_WhenHeadBlockIsNull_ThrowsException()
        {
            using Context ctx = await Context.Create();
            IBlockFinder blockFinder = Substitute.For<IBlockFinder>();
            blockFinder.FindHeadBlock().Returns(null as Block);
            ctx._test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev).WithBlockFinder(blockFinder).Build();
          
            string serialized = ctx._test.TestEthRpc("eth_gasPrice");
            
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Head Block was not found.\"},\"id\":67}",
                serialized);
        }
        
        [Test]
        public async Task Eth_gasPrice_GivenValidHeadBlock_CallsGasPriceEstimateFromGasPriceOracle()
        {
            using Context ctx = await Context.Create(); 
            Block testBlock = Build.A.Block.Genesis.TestObject;
            IBlockFinder blockFinder = Substitute.For<IBlockFinder>();
            blockFinder.FindHeadBlock().Returns(testBlock);
            blockFinder.FindBlock(Arg.Is<long>(a => a == 0)).Returns(testBlock);
            ctx._test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev).WithBlockFinder(blockFinder).Build();
            IGasPriceOracle gasPriceOracle = Substitute.For<IGasPriceOracle>();
            ctx._test.EthRpcModule.GasPriceOracle.Returns(gasPriceOracle);
            
            ctx._test.TestEthRpc("eth_gasPrice");
            
            gasPriceOracle.Received(1).GasPriceEstimate(Arg.Any<Block>(), Arg.Any<IBlockFinder>());
        }

        [TestCase(true, "0x4")] //Gas Prices: 1,2,3,4,5,6 | Max Index: 5 | 60th Percentile: 5 * (3/5) = 3 | Result: 4 (0x4)
        [TestCase(false, "0x4")] //Gas Prices: 1,2,3,4,5,6 | Max Index: 5 | 60th Percentile: 5 * (3/5) = 3 | Result: 4 (0x4)
        public async Task Eth_gasPrice_BlocksAvailableLessThanBlocksToCheck_ShouldGiveCorrectResult(bool eip1559Enabled, string expected)
        {
            using Context ctx = await Context.Create();
            Block[] blocks = GetThreeTestBlocks();
            BlockTree blockTree = Build.A.BlockTree(blocks[0]).WithBlocks(blocks).TestObject;
            ctx._test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev).WithBlockFinder(blockTree)
                .Build();
            GasPriceOracle gasPriceOracle = Substitute.ForPartsOf<GasPriceOracle>(SpecProviderWithEip1559EnabledAs(eip1559Enabled), null);
            ctx._test.EthRpcModule.GasPriceOracle.Returns(gasPriceOracle);
            
            string serialized = ctx._test.TestEthRpc("eth_gasPrice");  
            
            Assert.AreEqual($"{{\"jsonrpc\":\"2.0\",\"result\":\"{expected}\",\"id\":67}}", serialized);
        }
        
        [TestCase(true, "0x3")] //Gas Prices: 1,2,3,3,4,5 | Max Index: 5 | 60th Percentile: 5 * (3/5) = 3 | Result: 3 (0x4)
        [TestCase(false, "0x1")] //Gas Prices: 1,1,1 | Max Index: 2 | 60th Percentile: 2 * (3/5) = 1 | Result: 1 (0x1)
        public async Task Eth_gasPrice_BlocksAvailableLessThanBlocksToCheckWith1559Tx_ShouldGiveCorrectResult(bool eip1559Enabled, string expected)
        {
            using Context ctx = await Context.Create();
            Block[] blocks = GetThreeTestBlocksWith1559Tx();
            BlockTree blockTree = Build.A.BlockTree(blocks[0]).WithBlocks(blocks).TestObject;
            ctx._test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev).WithBlockFinder(blockTree)
                .Build();
            GasPriceOracle gasPriceOracle = Substitute.ForPartsOf<GasPriceOracle>(SpecProviderWithEip1559EnabledAs(eip1559Enabled), null);
            ctx._test.EthRpcModule.GasPriceOracle.Returns(gasPriceOracle);
            
            string serialized = ctx._test.TestEthRpc("eth_gasPrice");
            
            Assert.AreEqual($"{{\"jsonrpc\":\"2.0\",\"result\":\"{expected}\",\"id\":67}}", serialized);
        }
        
        public static ISpecProvider SpecProviderWithEip1559EnabledAs(bool isEip1559)
        {
            IReleaseSpec specEip1559 = Substitute.For<IReleaseSpec>();
            specEip1559.IsEip1559Enabled.Returns(isEip1559);
            ISpecProvider specProvider = Substitute.For<ISpecProvider>();
            specProvider.GetSpec(Arg.Any<long>()).Returns(specEip1559);
            return specProvider;
        }
        [Test]
        public async Task Eth_gasPrice_NumTxInMinBlocksGreaterThanBlockLimit_GetTxFromBlockLimitBlocks()
        {
            using Context ctx = await Context.Create();
            Block[] blocks = GetThreeTestBlocks();
            BlockTree blockTree = Build.A.BlockTree(blocks[0]).WithBlocks(blocks).TestObject;
            ctx._test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev).WithBlockFinder(blockTree)
                .Build();
            GasPriceOracle gasPriceOracle = Substitute.ForPartsOf<GasPriceOracle>(Substitute.For<ISpecProvider>(), null);
            gasPriceOracle.Configure().GetBlockLimit().Returns(2);
            ctx._test.EthRpcModule.GasPriceOracle.Returns(gasPriceOracle);
            List<UInt256> expected = new() {3, 4, 5, 6};
            
            ctx._test.TestEthRpc("eth_gasPrice");
            
            gasPriceOracle.TxGasPriceList.Should().Equal(expected);
        }

        private static Block[] GetThreeTestBlocks()
        {
            Block firstBlock = Build.A.Block.WithNumber(0).WithParentHash(Keccak.Zero).WithTransactions(
                Build.A.Transaction.WithGasPrice(1).SignedAndResolved(TestItem.PrivateKeyA).WithNonce(0).TestObject,
                Build.A.Transaction.WithGasPrice(2).SignedAndResolved(TestItem.PrivateKeyB).WithNonce(0).TestObject
            ).TestObject;

            Block secondBlock = Build.A.Block.WithNumber(1).WithParentHash(firstBlock.Hash!).WithTransactions(
                Build.A.Transaction.WithGasPrice(3).SignedAndResolved(TestItem.PrivateKeyC).WithNonce(0).TestObject,
                Build.A.Transaction.WithGasPrice(4).SignedAndResolved(TestItem.PrivateKeyD).WithNonce(0).TestObject
            ).TestObject;

            Block thirdBlock = Build.A.Block.WithNumber(2).WithParentHash(secondBlock.Hash!).WithTransactions(
                Build.A.Transaction.WithGasPrice(5).SignedAndResolved(TestItem.PrivateKeyA).WithNonce(1).TestObject,
                Build.A.Transaction.WithGasPrice(6).SignedAndResolved(TestItem.PrivateKeyB).WithNonce(1).TestObject
            ).TestObject;
           
            return new[]{firstBlock, secondBlock, thirdBlock};
        }

        private static Block[] GetThreeTestBlocksWith1559Tx()
        {
            Block firstBlock = Build.A.Block.WithNumber(0).WithParentHash(Keccak.Zero).WithBaseFeePerGas(3).WithTransactions(
                Build.A.Transaction.WithMaxFeePerGas(1).WithMaxPriorityFeePerGas(1).SignedAndResolved(TestItem.PrivateKeyA).WithNonce(0).WithType(TxType.EIP1559).TestObject, //Min(1, 1 + 3) = 1
                Build.A.Transaction.WithMaxFeePerGas(2).WithMaxPriorityFeePerGas(2).SignedAndResolved(TestItem.PrivateKeyB).WithNonce(0).WithType(TxType.EIP1559).TestObject  //Min(2, 2 + 3) = 2
            ).TestObject;

            Block secondBlock = Build.A.Block.WithNumber(1).WithParentHash(firstBlock.Hash!).WithBaseFeePerGas(3).WithTransactions(
                Build.A.Transaction.WithMaxFeePerGas(3).WithMaxPriorityFeePerGas(3).SignedAndResolved(TestItem.PrivateKeyC).WithNonce(0).WithType(TxType.EIP1559).TestObject, //Min(3, 2 + 3) = 3
                Build.A.Transaction.WithMaxFeePerGas(4).WithMaxPriorityFeePerGas(0).SignedAndResolved(TestItem.PrivateKeyD).WithNonce(0).WithType(TxType.EIP1559).TestObject  //Min(4, 0 + 3) = 3
            ).TestObject;

            Block thirdBlock = Build.A.Block.WithNumber(2).WithParentHash(secondBlock.Hash!).WithBaseFeePerGas(3).WithTransactions(
                Build.A.Transaction.WithMaxFeePerGas(5).WithMaxPriorityFeePerGas(1).SignedAndResolved(TestItem.PrivateKeyA).WithNonce(1).WithType(TxType.EIP1559).TestObject, //Min(5, 1 + 3) = 4
                Build.A.Transaction.WithMaxFeePerGas(6).WithMaxPriorityFeePerGas(2).SignedAndResolved(TestItem.PrivateKeyB).WithNonce(1).WithType(TxType.EIP1559).TestObject  //Min(6, 2 + 3) = 5
            ).TestObject;
           
            return new[]{firstBlock, secondBlock, thirdBlock};
        }
    }
}