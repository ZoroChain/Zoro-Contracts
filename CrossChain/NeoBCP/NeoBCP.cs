using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;

namespace BCTContract
{
    public class NeoBCP : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;//(byte[] from, byte[] to, BigInteger value)

        [DisplayName("approve")]
        public static event Action<byte[], byte[], BigInteger> Approved;//(byte[] from, byte[] to, BigInteger value)

        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("ATpJ9KzkZjN9PHTVuDNDB1dscboHU8hAxb");

        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        private const ulong factor = 100000000;//精度
        private const ulong oneHundredMillion = 100000000; //一亿
        private const ulong totalCoin = 200 * oneHundredMillion * factor;//总量

        public static object Main(string method, object[] args)
        {
            var magicstr = "bcp";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                var entryscript = ExecutionEngine.EntryScriptHash;

                //管理员权限     设置合约状态          
                if (method == "setState")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    BigInteger setValue = (BigInteger)args[0];
                    if (setValue == 0)
                        Storage.Put(Context(), "state", Active);
                    if (setValue == 1)
                        Storage.Put(Context(), "state", Inactive);
                    if (setValue == 2)
                        Storage.Put(Context(), "state", AllStop);
                    return true;
                }

                //invoke
                if (method == "getState") return GetState();

                // stop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                if (method == "totalSupply") return TotalSupply();
                if (method == "name") return Name();
                if (method == "symbol") return Symbol();
                if (method == "decimals") return Decimals();
                if (method == "balanceOf") return BalanceOf((byte[])args[0]);
                if (method == "getTxInfo") return GetTxInfo((byte[])args[0]);
                if (method == "allowance") return GetAllowance((byte[])args[0], (byte[])args[1]);
                if (method == "supportedStandards") return "{\"NEP-5\"}";

                // 如果合约不是 Active 状态、后面接口不可用
                if (GetState() != Active) return false;

                if (method == "deploy")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;

                    if (args.Length != 1) return false;

                    var address = (byte[])args[0];
                    if (address.Length != 20) return false; 

                    byte[] total_supply = Storage.Get(Context(), "totalSupply");
                    if (total_supply.Length != 0) return false;

                    var keyAddress = AddressKey(address);
                    Storage.Put(Context(), keyAddress, totalCoin);
                    Storage.Put(Context(), "totalSupply", totalCoin);

                    //notify
                    Transferred(null, address, totalCoin);
                }

                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from.Length != 20 || to.Length != 20) return false;

                    BigInteger value = (BigInteger)args[2];

                    if (!Runtime.CheckWitness(from)) return false;

                    if (entryscript != callscript) return false;                   

                    return Transfer(from, to, value);
                }

                //授权给 to 一定数量的 token
                if (method == "approve")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];

                    if (from.Length != 20 || to.Length != 20) return false;
                    if (!Runtime.CheckWitness(from)) return false;

                    BigInteger value = (BigInteger)args[2];

                    if (entryscript != callscript) return false;

                    return Approve(from, to, value);
                }

                //转走授权给 to 的 token
                if (method == "transferFrom")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];

                    if (from.Length != 20 || to.Length != 20) return false;

                    BigInteger value = (BigInteger)args[2];

                    return TransferFrom(from, to, value);
                }

                if (method == "transferApp")
                {
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from.Length != 20 || to.Length != 20) return false;

                    BigInteger value = (BigInteger)args[2];

                    if (from != callscript) return false;
                    return Transfer(from, to, value);
                }
            }

            return false;

        }

        public static string Name() => "BitCoin Play";//名称

        public static string Symbol() => "BCP";//简称

        public static byte Decimals() => 8;//精度

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static BigInteger TotalSupply() => Storage.Get(Context(), "totalSupply").AsBigInteger();

        private static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (from == to) return true;

            var keyFrom = AddressKey(from);
            BigInteger from_value = Storage.Get(Context(), keyFrom).AsBigInteger();
            if (from_value < value) return false;
            if (from_value == value)
                Storage.Delete(Context(), keyFrom);
            else
                Storage.Put(Context(), keyFrom, from_value - value);

            var keyTo = AddressKey(to);
            BigInteger to_value = Storage.Get(Context(), keyTo).AsBigInteger();
            Storage.Put(Context(), keyTo, to_value + value);

            SaveTxInfo(from, to, value);
            Transferred(from, to, value);
            return true;
        }

        private static void SaveTxInfo(byte[] from, byte[] to, BigInteger value)
        {
            TransferInfo info = new TransferInfo();
            info.@from = from;
            info.to = to;
            info.value = value;
            byte[] txInfo = Neo.SmartContract.Framework.Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            byte[] keyTxid = TxidKey(txid);
            Storage.Put(Context(), keyTxid, txInfo);
        }

        private static bool Approve(byte[] from, byte[] to, BigInteger value)
        {
            if (value < 0) return false;
            if (from == to) return false;

            BigInteger fromBalance = BalanceOf(from);
            if (fromBalance < value) return false;

            var allowanceKey = AllowanceKey(from, to);

            if (value == 0)
                Storage.Delete(Context(), allowanceKey);
            else
                Storage.Put(Context(), allowanceKey, value);

            Approved(from, to, value);
            return true;
        }

        private static bool TransferFrom(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (from == to) return false;

            BigInteger approvedTransferAmount = GetAllowance(from, to);    // how many tokens is this address authorised to transfer
            BigInteger fromBalance = BalanceOf(from);                   // retrieve balance of authorised account

            if (approvedTransferAmount < value || fromBalance < value) return false;

            var allowanceKey = AllowanceKey(from, to);
            if (approvedTransferAmount == value)
                Storage.Delete(Context(), allowanceKey);
            else
                Storage.Put(Context(), allowanceKey, approvedTransferAmount - value);

            var fromKey = AddressKey(from);
            if (fromBalance == value)
                Storage.Delete(Context(), fromKey);
            else
                Storage.Put(Context(), fromKey, fromBalance - value);

            var toKey = AddressKey(to);
            BigInteger recipientBalance = BalanceOf(to);
            Storage.Put(Context(), toKey, recipientBalance + value);

            SaveTxInfo(from, to, value);

            Transferred(from, to, value);
            return true;
        }

        private static BigInteger GetAllowance(byte[] from, byte[] to)
        {
            byte[] key = AllowanceKey(from, to);
            return Storage.Get(Context(), key).AsBigInteger();
        }

        private static BigInteger BalanceOf(byte[] who)
        {
            var keyAddress = AddressKey(who);
            return Storage.Get(Context(), keyAddress).AsBigInteger();
        }

        private static TransferInfo GetTxInfo(byte[] txid)
        {
            byte[] keyTxid = TxidKey(txid);
            byte[] v = Storage.Get(Context(), keyTxid);
            if (v.Length == 0)
                return null;
            return Neo.SmartContract.Framework.Helper.Deserialize(v) as TransferInfo;
        }

        private static byte[] AddressKey(byte[] address) => new byte[] { 0x11 }.Concat(address);
        private static byte[] TxidKey(byte[] txid) => new byte[] { 0x13 }.Concat(txid);
        private static byte[] AllowanceKey(byte[] from, byte[] to) => from.Concat(to);
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }
}
