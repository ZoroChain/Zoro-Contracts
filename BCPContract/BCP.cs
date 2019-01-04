using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;

namespace BcpContract
{
    public class BCP : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;//(byte[] from, byte[] to, BigInteger value)

        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AcQLYjGbQU2bEQ8RKFXUcf8XvromfUQodq");

        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        private const ulong factor = 100000000;//精度
        private const ulong oneHundredMillion = 100000000; //一亿
        private const ulong totalCoin = 20 * oneHundredMillion * factor;//总量

        public static object Main(string method, object[] args)
        {
            var magicstr = "bcp-test";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                //管理员权限     设置合约状态          
                if (method == "setstate")
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
                if (method == "getstate") return GetState();

                // stop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                if (method == "totalSupply") return TotalSupply();
                if (method == "name") return Name();
                if (method == "symbol") return Symbol();
                if (method == "decimals") return Decimals();
                if (method == "balanceOf")
                {
                    byte[] who = (byte[])args[0];
                    if (who.Length != 20) return false;
                    return balanceOf(who);
                }

                if (method == "getTxInfo")
                {
                    byte[] txid = (byte[])args[0];
                    if (txid.Length != 32) return false;
                    return getTxInfo(txid);
                }

                // 如果合约不是 Active 状态、后面接口不可用
                if (GetState() != Active) return false;

                if (method == "deploy")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] total_supply = Storage.Get(Context(), "totalSupply");
                    if (total_supply.Length != 0) return false;

                    var keySuperAdmin = AddressKey(superAdmin);
                    Storage.Put(Context(), keySuperAdmin, totalCoin);
                    Storage.Put(Context(), "totalSupply", totalCoin);

                    //notify
                    Transferred(null, superAdmin, totalCoin);
                }

                if (method == "transfer")
                {
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from.Length != 20 || to.Length != 20) return false;

                    BigInteger value = (BigInteger)args[2];

                    if (!Runtime.CheckWitness(from)) return false;

                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger()) return false;
                    if (!IsPayable(to)) return false;

                    return Transfer(from, to, value);
                }

                if (method == "transfer_app")
                {
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from.Length != 20 || to.Length != 20) return false;

                    BigInteger value = (BigInteger)args[2];

                    if (from.AsBigInteger() != callscript.AsBigInteger()) return false;
                    return Transfer(from, to, value);
                }

                #region 升级合约,耗费490,仅限管理员
                if (method == "updatecontract")
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1 && args.Length != 9) return false;

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script) return false;

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)05;
                    string name = "bcp";
                    string version = "1.0";
                    string author = "ZoroChain";
                    string email = "0";
                    string description = "bcp";

                    if (args.Length == 9)
                    {
                        parameter_list = (byte[])args[1];
                        return_type = (byte)args[2];
                        need_storage = (bool)args[3];
                        name = (string)args[4];
                        version = (string)args[5];
                        author = (string)args[6];
                        email = (string)args[7];
                        description = (string)args[8];
                    }
                    Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
                    return true;
                }
                #endregion
            }

            return false;

        }

        public static string Name() => "BlaCat Point";//名称

        public static string Symbol() => "BCP";//简称

        public static byte Decimals() => 8;//精度

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static object TotalSupply() => Storage.Get(Context(), "totalSupply").AsBigInteger();

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

            setTxInfo(from, to, value);
            Transferred(from, to, value);
            return true;
        }

        private static void setTxInfo(byte[] from, byte[] to, BigInteger value)
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

        private static object balanceOf(byte[] who)
        {
            var keyAddress = AddressKey(who);
            return Storage.Get(Context(), keyAddress).AsBigInteger();
        }

        private static TransferInfo getTxInfo(byte[] txid)
        {
            byte[] keyTxid = TxidKey(txid);
            byte[] v = Storage.Get(Context(), keyTxid);
            if (v.Length == 0)
                return null;
            return Helper.Deserialize(v) as TransferInfo;
        }

        public static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            if (c.Equals(null))
                return true;
            return c.IsPayable;
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
