using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace ZoroLockContract
{
    public class ZoroLockContract : SmartContract
    {
        // superAdmin
        private static readonly byte[] superAdmin = "AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s".ToScriptHash();

        public delegate object NEP5Contract(string method, object[] args);

        [DisplayName("addedToWhitelist")]
        public static event Action<byte[]> EmitAddedToWhitelist; // (scriptHash)

        [DisplayName("removedFromWhitelist")]
        public static event Action<byte[]> EmitRemovedFromWhitelist; // (scriptHash)

        [DisplayName("deposit")]
        public static event Action<byte[], byte[], BigInteger> EmitDepositted; // (address, assetID, amount)

        [DisplayName("withdraw")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawn; // (address, assetID, amount)

        // Contract States
        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        public static object Main(string operation, object[] args)
        {
            var magicstr = "ZoroBankTest";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                // 设置合约状态          
                if (operation == "setState")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1) return false;
                    return SetState((BigInteger)args[0]);
                }

                if (operation == "getState") return GetState();

                if (GetState() == AllStop) return false;

                if (operation == "getBalance") return GetBalance((byte[])args[0], (byte[])args[1]); //address, assetID
                if (operation == "getIsWhitelisted") return GetIsWhitelisted((byte[])args[0]);  // (assetID)

                if (GetState() != Active) return false;

                //冻结
                if (operation == "deposit")
                {
                    if (args.Length != 4) return false;
                    return Deposit((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (BigInteger)args[3]);
                }

                //取钱 提现
                if (operation == "withdraw") // originator, withdrawAssetId, withdrawAmount
                {
                    if (args.Length != 4) return false;
                    return Withdrawal((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (BigInteger)args[3]);
                }

                if (operation == "addToWhitelist")
                {
                    if (args.Length != 1) return false;
                    return AddToWhitelist((byte[])args[0]);
                }
                if (operation == "removeFromWhitelist")
                {
                    if (args.Length != 1) return false;
                    return RemoveFromWhitelist((byte[])args[0]);
                }
            }
            return false;
        }

        private static bool Deposit(byte[] assetId, byte[] from, BigInteger value, BigInteger isGlobal)
        {
            if (!Runtime.CheckWitness(from)) return false;
            if (!GetIsWhitelisted(assetId)) return false;
            if (value <= 0) return false;

            byte[] to = ExecutionEngine.ExecutingScriptHash;
            bool success = false;

            //全局资产 native nep5
            if (isGlobal == 1)
            {
                success = NativeAsset.Call("TransferFrom", assetId, from, to, value);
            }
            else
            {
                var args = new object[] { from, to, value };
                var contract = (NEP5Contract)assetId.ToDelegate();
                success = (bool)contract("transferFrom", args);
            }

            if (success)
            {
                IncreaseBalance(from, assetId, value);

                EmitDepositted(from, assetId, value);
                return true;
            }
            else
                throw new Exception("Failed to transferFrom");
        }

        private static bool Withdrawal(byte[] originator, byte[] assetId, BigInteger amount, BigInteger isGlobal)
        {
            if (!Runtime.CheckWitness(superAdmin)) return false;

            if (originator.Length != 20) return false;

            var balance = GetBalance(originator, assetId);

            if (balance < amount) return false;

            bool success = false;
            byte[] from = ExecutionEngine.ExecutingScriptHash;

            if (isGlobal == 1)
            {
                success = NativeAsset.Call("TransferApp", assetId, from, originator, amount);
            }
            else
            {
                var args = new object[] { from, originator, amount };
                var contract = (NEP5Contract)assetId.ToDelegate();
                success = (bool)contract("transferApp", args);
            }

            if (success)
            {
                ReduceBalance(originator, assetId, amount);

                EmitWithdrawn(originator, assetId, amount);
                return true;
            }
            else
                throw new Exception("Failed to withdrawal transfer");
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

        private static bool ReduceBalance(byte[] address, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            var key = BalanceKey(address, assetID);
            var currentBalance = Storage.Get(Context(), key).AsBigInteger();
            var newBalance = currentBalance - amount;

            if (newBalance < 0) throw new Exception("Invalid available balance!");

            if (newBalance > 0) Storage.Put(Context(), key, newBalance);
            else Storage.Delete(Context(), key);

            return true;
        }

        private static bool AddToWhitelist(byte[] scriptHash)
        {
            if (scriptHash.Length != 20) return false;
            var key = WhitelistKey(scriptHash);
            Storage.Put(Context(), key, 1);
            EmitAddedToWhitelist(scriptHash);
            return true;
        }

        private static bool RemoveFromWhitelist(byte[] scriptHash)
        {
            if (scriptHash.Length != 20) return false;
            var key = WhitelistKey(scriptHash);
            Storage.Delete(Context(), key);
            EmitRemovedFromWhitelist(scriptHash);
            return true;
        }

        private static bool GetIsWhitelisted(byte[] assetID)
        {
            if (assetID.Length != 20) return false;
            if (Storage.Get(Context(), WhitelistKey(assetID)).AsBigInteger() == 1)
                return true;
            return false;
        }

        private static bool SetState(BigInteger setValue)
        {
            if (setValue == 0)
                Storage.Put(Context(), "state", Active);
            if (setValue == 1)
                Storage.Put(Context(), "state", Inactive);
            if (setValue == 2)
                Storage.Put(Context(), "state", AllStop);
            return true;
        }

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] BalanceKey(byte[] originator, byte[] assetID) => "balance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] WhitelistKey(byte[] assetId) => "whiteList".AsByteArray().Concat(assetId);
    }
}
