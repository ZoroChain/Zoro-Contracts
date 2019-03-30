using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace ZoroLockContract
{
    public class NeoLockContract : SmartContract
    {
        // superAdmin
        private static readonly byte[] superAdmin = "AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s".ToScriptHash();

        public delegate object NEP5Contract(string method, object[] args);

        [DisplayName("lock")]
        public static event Action<byte[], byte[], BigInteger> EmitLocked; // (address, assetID, amount)

        [DisplayName("send")]
        public static event Action<byte[], byte[], byte[], BigInteger> EmitSend; // (txid, address, assetID, amount)

        // Contract States
        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        public static object Main(string operation, object[] args)
        {
            var magicstr = "NeoBank";
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

                if (operation == "oneLevelMutiSign") return GetOneLevelMutiSign();
                if (operation == "twoLevelMutiSign") return GetTwoLevelMutiSign();                
                if (operation == "twoLevelAmount") return GetTwoLevelAmount();

                if (GetState() != Active) return false;

                //存钱
                if (operation == "lock")
                {
                    if (args.Length != 3) return false;
                    return Lock((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                }

                //发钱
                if (operation == "send") //txid, originator, assetId, amount
                {
                    if (args.Length != 4) return false;
                    return Send((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3]);
                }

                if (!Runtime.CheckWitness(superAdmin)) return false;

                if (operation == "setOneLevelMutiSign")
                {
                    if (args.Length != 1) return false;
                    var address = (byte[])args[0];
                    if (address.Length != 20) return false;
                    Storage.Put(Context(), "oneLevelMutiSignature", address);
                }

                if (operation == "setTwoLevelMutiSign")
                {
                    if (args.Length != 1) return false;
                    var address = (byte[])args[0];
                    if (address.Length != 20) return false;
                    Storage.Put(Context(), "twoLevelMutiSignature", address);
                }              

                if (operation == "setTwoLevelAmount")
                {
                    if (args.Length != 1) return false;
                    var amount = (BigInteger)args[0];
                    if (amount <= 0) return false;
                    Storage.Put(Context(), "twoLevelAmount", amount);
                }

            }
            return false;
        }

        private static bool Lock(byte[] assetId, byte[] from, BigInteger value)
        {
            if (!Runtime.CheckWitness(from)) return false;
            if (assetId.Length != 20) return false;
            if (value <= 0) return false;

            byte[] to = ExecutionEngine.ExecutingScriptHash;
            bool success = false;

            var args = new object[] { from, to, value };
            var contract = (NEP5Contract)assetId.ToDelegate();
            success = (bool)contract("transferFrom", args);

            if (success)
            {
                EmitLocked(from, assetId, value);
                return true;
            }
            else
                throw new Exception("Failed to TransferFrom");
        }

        private static bool Send(byte[] txid, byte[] originator, byte[] assetId, BigInteger value)
        {
            if (assetId.Length != 20) return false;
            if (originator.Length != 20) return false;
            if (value <= 0) return false;

            var txidUsedKey = TxidUsedKey(txid);
            var txidIsUsed = Storage.Get(Context(), txidUsedKey);
            if (txidIsUsed.Length > 0) return false;

            var twoLevelAmount = GetTwoLevelAmount();
            if (value >= twoLevelAmount)
            {
                var twoLevelMutiSign = GetTwoLevelMutiSign();
                if (!Runtime.CheckWitness(twoLevelMutiSign)) return false;
            }
            else
            {
                var oneLevelMutiSign = GetOneLevelMutiSign();
                if (!Runtime.CheckWitness(oneLevelMutiSign)) return false;
            }

            bool success = false;
            byte[] from = ExecutionEngine.ExecutingScriptHash;

            var args = new object[] { from, originator, value };
            var contract = (NEP5Contract)assetId.ToDelegate();
            success = (bool)contract("transferApp", args);

            if (success)
            {
                Storage.Put(Context(), txidUsedKey, 1);

                EmitSend(txid, originator, assetId, value);
                return true;
            }
            else
                throw new Exception("Failed to withdrawal transfer");
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

        private static byte[] GetOneLevelMutiSign() => Storage.Get(Context(), "oneLevelMutiSignature");
        private static byte[] GetTwoLevelMutiSign() => Storage.Get(Context(), "twoLevelMutiSignature");

        private static BigInteger GetOneLevelAmount() => Storage.Get(Context(), "oneLevelAmount").AsBigInteger();
        private static BigInteger GetTwoLevelAmount() => Storage.Get(Context(), "twoLevelAmount").AsBigInteger();

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] BalanceKey(byte[] originator, byte[] assetID) => "balance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] TxidUsedKey(byte[] txid) => "txidUsed".AsByteArray().Concat(txid);
    }
}
