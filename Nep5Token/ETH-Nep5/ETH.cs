using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;

namespace BCTContract
{
    public class Nep5TokenEx : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;//(byte[] from, byte[] to, BigInteger value)

        [DisplayName("approve")]
        public static event Action<byte[], byte[], BigInteger> Approved;//(byte[] from, byte[] to, BigInteger value)

        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Helper.ToScriptHash("ANnUWBdxpkpSaJzjAHw4Dk2gshX5WW3Guf");

        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        public static object Main(string method, object[] args)
        {
            var magicstr = "mapping-token";
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

                if (method == "setOperator")
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    var operatorAddr = (byte[])args[0];
                    if (operatorAddr.Length != 20) return false;
                    Storage.Put(Context(), "operator", operatorAddr);
                    return true;
                }

                if (method == "deploy")
                {
                    if (args.Length != 2) return false;
                    var operatorAddr = Storage.Get(Context(), "operator");
                    if (!Runtime.CheckWitness(operatorAddr)) return false;

                    byte[] to = (byte[])args[0];
                    BigInteger amount = (BigInteger)args[1];
                    if (amount <= 0) return false;

                    var keyTo = AddressKey(to);
                    BigInteger value = Storage.Get(Storage.CurrentContext, keyTo).AsBigInteger();
                    value += amount;
                    Storage.Put(Storage.CurrentContext, keyTo, value);

                    Transferred(null, to, amount);
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

        public static string Name() => "Zoro-Neo";//名称

        public static string Symbol() => "NEO";//简称

        public static byte Decimals() => 8;//精度

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static BigInteger TotalSupply() => 0;

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

            SetTxInfo(from, to, value);
            Transferred(from, to, value);
            return true;
        }

        private static void SetTxInfo(byte[] from, byte[] to, BigInteger value)
        {
            TransferInfo info = new TransferInfo();
            info.@from = from;
            info.to = to;
            info.value = value;
            byte[] txInfo = Helper.Serialize(info);
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

            if (value == 0)
                Storage.Delete(Context(), AllowanceKey(from, to));
            else
                Storage.Put(Context(), AllowanceKey(from, to), value);

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

            if (approvedTransferAmount == value)
                Storage.Delete(Context(), AllowanceKey(from, to));
            else
                Storage.Put(Context(), AllowanceKey(from, to), approvedTransferAmount - value);

            if (fromBalance == value)
                Storage.Delete(Context(), AddressKey(from));
            else
                Storage.Put(Context(), AddressKey(from), fromBalance - value);

            BigInteger recipientBalance = BalanceOf(to);
            Storage.Put(Context(), AddressKey(to), recipientBalance + value);

            SetTxInfo(from, to, value);

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
            return Helper.Deserialize(v) as TransferInfo;
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
