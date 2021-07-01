using System;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Blockchain.Find;
using Nethermind.Core;
using Nethermind.Int256;

namespace Nethermind.JsonRpc.Modules.Eth
{
    public class GasPriceOracle : IGasPriceOracle
    {
        public UInt256? DefaultGasPrice { get; private set; }
        private Block? _lastHeadBlock;
        private UInt256? _lastGasPrice;
        private readonly UInt256? _ignoreUnder;
        private IBlockFinder? _blockFinder;
        private readonly int _blockLimit;
        private readonly int _softTxThreshold;
        private bool _eip1559Enabled;
        private readonly UInt256 _baseFee;

        public GasPriceOracle(bool eip1559Enabled = false, UInt256? ignoreUnder = null, 
            int? blockLimit = null, UInt256? baseFee = null)
        {
            _eip1559Enabled = eip1559Enabled;
            _ignoreUnder = ignoreUnder ?? UInt256.Zero;
            _blockLimit = blockLimit ?? GasPriceConfig.DefaultBlocksLimit;
            _softTxThreshold = GasPriceConfig.SoftTxLimit;
            _baseFee = baseFee ?? GasPriceConfig.DefaultBaseFee;
        }

        public ResultWrapper<UInt256?> GasPriceEstimate(IBlockFinder blockFinder)
        {
            if (_blockFinder == null)
            {
                _blockFinder = blockFinder ?? throw new ArgumentNullException(nameof(blockFinder));
            }

            Tuple<bool, ResultWrapper<UInt256?>> earlyExitResult = EarlyExitAndResult();
            if (earlyExitResult.Item1 == true)
            {
                return earlyExitResult.Item2;
            }
            
            SetDefaultGasPrice(_lastHeadBlock!.Number);
            
            List<UInt256> gasPricesList = CreateAndAddTxGasPricesToSet().OrderBy(tx => tx).ToList();
            
            UInt256? gasPriceEstimate = GasPriceAtPercentile(gasPricesList);

            gasPriceEstimate = FindMinOfThisAndMaxPrice(gasPriceEstimate);

            SetLastGasPrice(gasPriceEstimate);
            
            return ResultWrapper<UInt256?>.Success((UInt256) gasPriceEstimate!);
        }

        private Tuple<bool, ResultWrapper<UInt256?>> EarlyExitAndResult()
        {
            Block? headBlock = GetHeadBlock();
            Block? genesisBlock = GetGenesisBlock();
            ResultWrapper<UInt256?> resultWrapper;

            resultWrapper = HandleMissingHeadOrGenesisBlockCase(headBlock, genesisBlock);
            if (ResultWrapperWasNotSuccessful(resultWrapper))
            {
                return BoolAndWrapperTuple(true, resultWrapper);
            }

            resultWrapper = HandleNoHeadBlockChange(headBlock);
            if (ResultWrapperWasSuccessful(resultWrapper))
            {
                return BoolAndWrapperTuple(true, resultWrapper);
            }
            SetLastHeadBlock(headBlock);
            return BoolAndWrapperTuple(false, resultWrapper);
        }
        
        private ResultWrapper<UInt256?> HandleMissingHeadOrGenesisBlockCase(Block? headBlock, Block? genesisBlock)
        {
            if (BlockDoesNotExist(headBlock))
            {
                return ResultWrapper<UInt256?>.Fail("The head block had a null value.");
            }
            else if (BlockDoesNotExist(genesisBlock))
            {
                return ResultWrapper<UInt256?>.Fail("The genesis block had a null value.");
            }
            else
            {
                return ResultWrapper<UInt256?>.Success(UInt256.Zero);
            }
        }

        private static bool BlockDoesNotExist(Block? block)
        {
            return block == null;
        }
        private ResultWrapper<UInt256?> HandleNoHeadBlockChange(Block? headBlock)
        {
            ResultWrapper<UInt256?> resultWrapper;
            
            if (LastGasPriceExists() && LastHeadBlockExists() && LastHeadIsSameAsCurrentHead(headBlock))
            {
                resultWrapper = ResultWrapper<UInt256?>.Success(_lastGasPrice);
#if DEBUG
                resultWrapper.ErrorCode = GasPriceConfig.NoHeadBlockChangeErrorCode;
#endif
                return resultWrapper;
            }
            else
            {
                return ResultWrapper<UInt256?>.Fail("");
            }
        }

        private bool LastGasPriceExists()
        {
            return _lastGasPrice != null;
        }

        private bool LastHeadBlockExists()
        {
            return _lastHeadBlock != null;
        }
        
        private bool LastHeadIsSameAsCurrentHead(Block? headBlock)
        {
            return headBlock!.Hash == _lastHeadBlock!.Hash;
        }
        
        private void SetDefaultGasPrice(long headBlockNumber)
        {
            Transaction[] transactions;
            int blocksToCheck = GasPriceConfig.BlockLimitForDefaultGasPrice;
            
            while (headBlockNumber >= 0 && DefaultGasPriceBlockLimitNotReached(ref blocksToCheck))
            {
                transactions = GetTxFromBlockWithNumber(headBlockNumber);
                if (_eip1559Enabled == false)
                {
                    transactions = transactions.Where(tx => tx.IsEip1559 == false).ToArray();
                }

                if (TransactionsExistIn(transactions))
                {
                    DefaultGasPrice = transactions[^1].GasPrice;
                    return;
                }
                
                headBlockNumber--;
            }
            DefaultGasPrice = 1; 
        }

        private static bool DefaultGasPriceBlockLimitNotReached(ref int blocksToCheck)
        {
            return blocksToCheck-- > 0;
        }
        
        private Transaction[] GetTxFromBlockWithNumber(long headBlockNumber)
        {
            Block block = _blockFinder!.FindBlock(headBlockNumber);
            if (block == null)
            {
                ThrowBlockNotFoundException(headBlockNumber);
            }
            return block!.Transactions;
        }
        
        private List<UInt256> CreateAndAddTxGasPricesToSet()
        {
            List<UInt256> gasPricesSetHandlingDuplicates = new List<UInt256>();
            gasPricesSetHandlingDuplicates = AddingTxPricesFromNewestToOldestBlock(gasPricesSetHandlingDuplicates);
            return gasPricesSetHandlingDuplicates;
        }
        
        private List<UInt256> AddingTxPricesFromNewestToOldestBlock(List<UInt256> txGasPriceList)
        {
            long currentBlockNumber = GetHeadBlock()!.Number;
            int blocksToGoBack = _blockLimit;
            while (MoreBlocksToGoBack(blocksToGoBack) && CurrentBlockNumberIsValid(currentBlockNumber)) 
            {
                Block? block = _blockFinder!.FindBlock(currentBlockNumber);
                if (BlockExists(block))
                {
                    int txsAdded = AddValidTxAndReturnCount(block!, ref txGasPriceList);
                    if (txsAdded > 1 || BonusBlockLimitReached(txGasPriceList, blocksToGoBack))
                    {
                        blocksToGoBack--;
                    }
                }
                else
                {
                    ThrowBlockNotFoundException(currentBlockNumber);
                }
                currentBlockNumber--;
            }

            return txGasPriceList;
        }

        private Block? GetHeadBlock()
        {
            return _blockFinder!.FindHeadBlock();
        }

        private static bool MoreBlocksToGoBack(long blocksToGoBack)
        {
            return blocksToGoBack > 0;
        }
        
        private static bool CurrentBlockNumberIsValid(long currBlockNumber)
        {
            return currBlockNumber > -1;
        }
        
        private static bool BlockExists(Block? foundBlock)
        {
            return foundBlock != null;
        }
        private int AddTxAndReturnCountAdded(List<UInt256> txGasPriceList, Transaction[] txInBlock)
        {
            int countTxAdded = 0;
            
            IEnumerable<Transaction> txSortedByEffectiveGasPrice = txInBlock.OrderBy(EffectiveGasPrice);
            foreach (Transaction transaction in txSortedByEffectiveGasPrice)
            {
                if (TransactionCanBeAdded(transaction, _eip1559Enabled)) //how should i set to be null?
                {
                    txGasPriceList.Add(EffectiveGasPrice(transaction));
                    countTxAdded++;
                }

                if (countTxAdded >= GasPriceConfig.TxLimitFromABlock)
                {
                    break;
                }
            }

            return countTxAdded;
        }

        private UInt256 EffectiveGasPrice(Transaction transaction)
        {
            return transaction.CalculateEffectiveGasPrice(_eip1559Enabled, _baseFee);
        }

        private bool TransactionCanBeAdded(Transaction transaction, bool eip1559Enabled)
        {
            bool res = IsAboveMinPrice(transaction) && Eip1559ModeCompatible(transaction, eip1559Enabled);
            return res;
        }
        
        private bool IsAboveMinPrice(Transaction transaction)
        {
            return transaction.GasPrice >= _ignoreUnder;
        }
        
        private bool Eip1559ModeCompatible(Transaction transaction, bool eip1559Enabled)
        {
            if (eip1559Enabled == false)
            {
                return TransactionIsNotEip1559(transaction);
            }
            else
            {
                return true;
            }
        }

        private static bool TransactionIsNotEip1559(Transaction transaction)
        {
            return !transaction.IsEip1559;
        }
        
        private int AddValidTxAndReturnCount(Block block, ref List<UInt256> txGasPriceList)
        {
            Transaction[] transactionsInBlock = block.Transactions;
            int countTxAdded;

            if (TransactionsExistIn(transactionsInBlock))
            {
                countTxAdded = AddTxAndReturnCountAdded(txGasPriceList, transactionsInBlock);

                if (countTxAdded == 0)
                {
                    AddDefaultPriceTo(txGasPriceList);
                    countTxAdded++;
                }

                return countTxAdded;
            }
            else
            {
                AddDefaultPriceTo(txGasPriceList);
                return 1;
            }
        }
        
        private static bool TransactionsExistIn(Transaction[] transactions)
        {
            return transactions.Length > 0;
        }
        
        private void AddDefaultPriceTo(List<UInt256> txGasPriceList)
        {
            txGasPriceList.Add((UInt256) DefaultGasPrice!);
        }
        
        private bool BonusBlockLimitReached(List<UInt256> txGasPriceList, int blocksToGoBack)
        {
            return txGasPriceList.Count + blocksToGoBack >= _softTxThreshold;
        }
        
        private static void ThrowBlockNotFoundException(long blockNumber)
        {
            throw new Exception($"Block {blockNumber} was not found.");
        }
        
        private static UInt256? GasPriceAtPercentile(List<UInt256> txGasPriceList)
        {
            int roundedIndex = GetRoundedIndexAtPercentile(txGasPriceList.Count);

            UInt256? gasPriceEstimate = GetElementAtIndex(txGasPriceList, roundedIndex);

            return gasPriceEstimate;
        }
        
        private static UInt256 GetElementAtIndex(List<UInt256> txGasPriceList, int roundedIndex)
        {
            return txGasPriceList[roundedIndex];
        }
        
        private static UInt256? FindMinOfThisAndMaxPrice(UInt256? gasPriceEstimate)
        {
            if (gasPriceEstimate > GasPriceConfig._maxGasPrice)
            {
                gasPriceEstimate = GasPriceConfig._maxGasPrice;
            }

            return gasPriceEstimate;
        }

        private void SetLastGasPrice(UInt256? lastGasPrice)
        {
            _lastGasPrice = lastGasPrice;
        }
        
        private static int GetRoundedIndexAtPercentile(int count)
        {
            int lastIndex = count - 1;
            float percentileOfLastIndex = lastIndex * ((float)GasPriceConfig.Percentile / 100);
            int roundedIndex = (int) Math.Round(percentileOfLastIndex);
            return roundedIndex;
        }

        private void SetLastHeadBlock(Block? headBlock)
        {
            _lastHeadBlock = headBlock;
        }

        private static Tuple<bool, ResultWrapper<UInt256?>> BoolAndWrapperTuple(bool boolean, ResultWrapper<UInt256?> resultWrapper)
        {
            return new(boolean, resultWrapper);
        }

        private Block? GetGenesisBlock()
        {
            return _blockFinder!.FindGenesisBlock();
        }

        private static bool ResultWrapperWasSuccessful(ResultWrapper<UInt256?> resultWrapper)
        {
            return resultWrapper.Result == Result.Success;
        }
        
        private static bool ResultWrapperWasNotSuccessful(ResultWrapper<UInt256?> resultWrapper)
        {
            return resultWrapper.Result != Result.Success;
        }
        
    }
}