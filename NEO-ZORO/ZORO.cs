using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;

namespace ZOROContract
{
    public class ZORO : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;//(byte[] from, byte[] to, BigInteger value)        
                
        private static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("ARhGmWHsVR4bjfWLrxTfFWT6rktwwVVGBF");

        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        private const ulong factor = 100000000;//精度
        private const ulong oneHundredMillion = 100000000; //一亿
        private const ulong totalCoin = 20 * oneHundredMillion * factor;//总量

        public static object Main(string method, object[] args)
        {
            var magicstr = "zoro-v2.0";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                var entryscript = ExecutionEngine.EntryScriptHash;
                                   
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

                // stop 
                if (GetState() == AllStop) return false;

                if (method == "totalSupply") return TotalSupply();
                if (method == "name") return Name();
                if (method == "symbol") return Symbol();
                if (method == "decimals") return Decimals();
                if (method == "balanceOf") return BalanceOf((byte[])args[0]);
                if (method == "getTxInfo") return GetTxInfo((byte[])args[0]);               
                if (method == "supportedStandards") return "{\"NEP-5\"}";
                                
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

        public static string Name() => "ZORO";//名称

        public static string Symbol() => "ZORO";//简称

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
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }
}
