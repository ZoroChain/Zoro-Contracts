using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace LockContract
{
    public class LockContract : SmartContract
    {
        //先用锁仓的账户往合约转账，用Txid来完成锁仓，每笔锁仓的Key为锁仓账户地址+资产ID

        //解锁后的提现需要锁仓账户签名，提现地址可以是任意地址

        //管理员设置解锁时间间隔和每次解锁数额
        //管理员可以提取所有已锁资产
        //各类资产通用
        //可以查询锁仓数量、当前可解锁数额
        //可查询解锁条件
        //查询上次解锁提现时间
        //Key address+assetId

        public delegate object NEP5Contract(string method, object[] args);

        [DisplayName("lock")]
        public static event Action<byte[], byte[], BigInteger> EmitLocked; // (address, assetID, amount)

        [DisplayName("unlock")]
        public static event Action<byte[], byte[], byte[], BigInteger> EmitUnlocked; // (locker, assetId, toAddress, amount)

        private static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AGc2HfoP5w823frEnDo2j3cnaJNcMsS1iY");

        public static object Main(string operation, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                //查询可解锁资产
                if (operation == "getCanWithdrawAmount")//locker,assetId
                {
                    byte[] locker = (byte[])args[0];
                    byte[] assetId = (byte[])args[1];

                    BigInteger unlockInterval = GetUnlockInterval(locker, assetId);
                    if (unlockInterval <= 0) return false;
                    BigInteger unlockAmount = GetUnlockAmount(locker, assetId);
                    if (unlockAmount <= 0) return false;

                    Header header = Blockchain.GetHeader(Blockchain.GetHeight());

                    BigInteger lockTimestamp = GetLockTimestamp(locker, assetId);
                    if (lockTimestamp <= 0 || header.Timestamp <= lockTimestamp) return false;

                    BigInteger timeInterval = header.Timestamp - lockTimestamp;

                    var lockBalance = GetBalance(locker, assetId);

                    if (lockBalance <= 0) return false;

                    if (timeInterval < unlockInterval) return 0;

                    BigInteger multiple = timeInterval / unlockInterval;

                    BigInteger withdrawAmount = multiple * unlockAmount;

                    if (lockBalance < withdrawAmount)
                        withdrawAmount = lockBalance;

                    return withdrawAmount;
                }

                //查询解锁条件
                if (operation == "getUnlockInterval")// locker,assetId
                {
                    return GetUnlockInterval((byte[])args[0], (byte[])args[1]);
                }
                if (operation == "getUnlockAmount")// locker,assetId
                {
                    return GetUnlockAmount((byte[])args[0], (byte[])args[1]);
                }

                //查询当前锁仓数额
                if (operation == "getLockAomunt") // locker,assetId
                {
                    return GetBalance((byte[])args[0], (byte[])args[1]);
                }

                //查询上次锁仓刷新时间
                if (operation == "getLockTimestamp")// locker,assetId
                {
                    return GetLockTimestamp((byte[])args[0], (byte[])args[1]);
                }

                //设置提取条件 提取地址，资产ID，解锁间隔，每次解锁数额
                if (operation == "setCondition") // locker,assetID,unlockInterval,unlockAmount
                {
                    if (args.Length != 4) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return SetCondition((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (BigInteger)args[3]);
                }

                //管理员可以取走全部资产
                if (operation == "withdrawAll") // locker, assetId, toAddress, isGlobal
                {
                    if (args.Length != 4) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    WithdrawalAll((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3]);
                    return true;
                }

                //管理员设置锁仓开始时间为当前区块时间
                if (operation == "setLockTimestamp") // locker, assetId
                {
                    if (args.Length != 2) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    SetLockTimestamp((byte[])args[0], (byte[])args[1]);
                    return true;
                }

                //存钱 锁仓
                if (operation == "lock") // (txid, locker, assetID, isGlobal)
                {
                    if (args.Length != 4) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return Lock((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3]);
                }

                //取钱 提现
                if (operation == "withdraw") // locker, assetId, address, isGlobal
                {
                    if (args.Length != 4) return false;

                    byte[] locker = (byte[])args[0];
                    if (!Runtime.CheckWitness(locker)) return false;

                    byte[] assetId = (byte[])args[1];
                    byte[] toAddress = (byte[])args[2];
                    if (toAddress.Length != 20) return false;

                    BigInteger unlockInterval = GetUnlockInterval(locker, assetId);
                    if (unlockInterval <= 0) return false;
                    BigInteger unlockAmount = GetUnlockAmount(locker, assetId);
                    if (unlockAmount <= 0) return false;

                    Header header = Blockchain.GetHeader(Blockchain.GetHeight());

                    BigInteger lockTimestamp = GetLockTimestamp(locker, assetId);
                    if (lockTimestamp <= 0 || header.Timestamp <= lockTimestamp) return false;

                    BigInteger timeInterval = header.Timestamp - lockTimestamp;

                    var lockBalance = GetBalance(locker, assetId);

                    if (lockBalance <= 0) return false;

                    if (timeInterval < unlockInterval) return false;

                    BigInteger multiple = timeInterval / unlockInterval;

                    BigInteger withdrawAmount = multiple * unlockAmount;

                    if (lockBalance < withdrawAmount)
                        withdrawAmount = lockBalance;

                    return Withdrawal(locker, assetId, toAddress, withdrawAmount, (BigInteger)args[3]);
                }
            }
            return false;
        }

        private static bool SetLockTimestamp(byte[] locker, byte[] assetId)
        {
            Header header = Blockchain.GetHeader(Blockchain.GetHeight());
            var key = LockTimeKey(locker, assetId);
            Storage.Put(Context(), key, header.Timestamp);
            return true;
        }

        private static BigInteger GetLockTimestamp(byte[] locker, byte[] assetId)
        {
            Header header = Blockchain.GetHeader(Blockchain.GetHeight());
            var key = LockTimeKey(locker, assetId);
            return Storage.Get(Context(), key).AsBigInteger();
        }

        private static BigInteger GetUnlockAmount(byte[] locker, byte[] assetId)
        {
            byte[] amountKey = AmountConditionKey(locker, assetId);
            var amountCondition = Storage.Get(Context(), amountKey);
            return amountCondition.AsBigInteger();
        }

        private static BigInteger GetUnlockInterval(byte[] locker, byte[] assetId)
        {
            byte[] percentageKey = TimeConditionKey(locker, assetId);
            var percentageCondition = Storage.Get(Context(), percentageKey);
            return percentageCondition.AsBigInteger();
        }

        private static bool SetCondition(byte[] locker, byte[] assetId, BigInteger unlockInterval, BigInteger unlockAmount)
        {
            byte[] timeKey = TimeConditionKey(locker, assetId);
            //var timeCondition = Storage.Get(Context(), timeKey);
            if (unlockInterval <= 0) return false;
            Storage.Put(Context(), timeKey, unlockInterval);

            byte[] amountKey = AmountConditionKey(locker, assetId);
            if (unlockAmount <= 0) return false;
            Storage.Put(Context(), amountKey, unlockAmount);

            return true;
        }

        private static bool Lock(byte[] txid, byte[] locker, byte[] assetId, BigInteger isGlobal)
        {
            if (TxidUsed(txid))
                return false;

            byte[] lockAddress = ExecutionEngine.ExecutingScriptHash;

            //全局资产 native nep5
            if (isGlobal == 1)
            {
                var tx = NativeAsset.GetTransferLog(assetId, txid);

                if (tx.From.Length == 20 && tx.Value > 0 && tx.To == lockAddress)
                {
                    IncreaseBalance(locker, assetId, tx.Value);
                    EmitLocked(locker, assetId, tx.Value);
                }
            }
            else
            {
                var tx = GetTxInfo(assetId, txid);

                if (tx.from.Length == 20 && tx.value > 0 && tx.to == lockAddress)
                {
                    IncreaseBalance(locker, assetId, tx.value);
                    EmitLocked(locker, assetId, tx.value);
                }
            }

            SetTxidUsed(txid);

            return true;
        }

        private static bool Withdrawal(byte[] locker, byte[] assetId, byte[] address, BigInteger amount, BigInteger isGlobal)
        {
            if (address.Length != 20) return false;

            bool success = false;
            byte[] from = ExecutionEngine.ExecutingScriptHash;

            if (isGlobal == 1)
            {
                success = NativeAsset.Call("TransferApp", assetId, from, address, amount);
            }
            else
            {
                var args = new object[] { from, address, amount };
                var contract = (NEP5Contract)assetId.ToDelegate();
                success = (bool)contract("transferApp", args);
            }

            if (success)
            {
                ReduceBalance(locker, assetId, amount);

                SetLockTimestamp(locker, assetId);

                EmitUnlocked(locker, assetId, address, amount);
                return true;
            }
            else
                throw new Exception("Failed to withdrawal transfer");
        }

        private static bool WithdrawalAll(byte[] locker, byte[] assetId, byte[] address, BigInteger isGlobal)
        {
            if (address.Length != 20) return false;

            byte[] from = ExecutionEngine.ExecutingScriptHash;

            BigInteger balance = GetBalance(locker, assetId);

            bool success = false;

            if (isGlobal == 1)
            {
                success = NativeAsset.Call("TransferApp", assetId, from, address, balance);
            }
            else
            {
                var args = new object[] { from, address, balance };
                var contract = (NEP5Contract)assetId.ToDelegate();
                success = (bool)contract("transferApp", args);
            }

            if (success)
            {
                ReduceBalance(locker, assetId, balance);
                EmitUnlocked(locker, assetId, address, balance);
                return true;
            }
            else
                throw new Exception("Failed to withdrawal transfer");
        }

        private static bool ReduceBalance(byte[] address, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            var key = BalanceKey(address, assetID);
            var currentBalance = Storage.Get(Context(), key).AsBigInteger();

            if (currentBalance < amount) throw new Exception("Invalid available balance!");
            var newBalance = currentBalance - amount;

            if (newBalance > 0) Storage.Put(Context(), key, newBalance);
            else Storage.Delete(Context(), key);

            return true;
        }

        private static BigInteger GetBalance(byte[] address, byte[] assetID)
        {
            if (address.Length != 20 || assetID.Length != 20) return 0;
            return Storage.Get(Context(), BalanceKey(address, assetID)).AsBigInteger();
        }

        private static bool IncreaseBalance(byte[] address, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            byte[] key = BalanceKey(address, assetID);
            BigInteger currentBalance = Storage.Get(Context(), key).AsBigInteger();
            Storage.Put(Context(), key, currentBalance + amount);

            return true;
        }

        public static TransferInfo GetTxInfo(byte[] assetid, byte[] txid)
        {
            var tInfo = new TransferInfo();

            object[] _p = new object[1] { txid };
            NEP5Contract call = (NEP5Contract)assetid.ToDelegate();
            var info = call("getTxInfo", _p);
            if (((object[])info).Length == 3)
                return info as TransferInfo;

            return tInfo;
        }

        public static bool TxidUsed(byte[] txid)
        {
            var key = TxidUsedKey(txid);
            var v = Storage.Get(Context(), key).AsBigInteger();
            if (v == 1) return true;
            else return false;
        }

        public static void SetTxidUsed(byte[] txid)
        {
            var key = TxidUsedKey(txid);
            Storage.Put(Context(), key, 1);
        }

        private static StorageContext Context() => Storage.CurrentContext;

        // Keys        
        private static byte[] BalanceKey(byte[] locker, byte[] assetID) => "balance".AsByteArray().Concat(locker).Concat(assetID);
        private static byte[] LockTimeKey(byte[] locker, byte[] assetID) => "LockTime".AsByteArray().Concat(locker).Concat(assetID);
        private static byte[] TimeConditionKey(byte[] locker, byte[] assetID) => "timeCondition".AsByteArray().Concat(locker).Concat(assetID);
        private static byte[] AmountConditionKey(byte[] locker, byte[] assetID) => "amountCondition".AsByteArray().Concat(locker).Concat(assetID);
        private static byte[] TxidUsedKey(byte[] txid) => "txidUsed".AsByteArray().Concat(txid);

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

    }
}