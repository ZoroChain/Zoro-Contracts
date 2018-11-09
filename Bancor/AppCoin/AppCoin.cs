using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Bcp2Bct
{
    public class AppCoin : SmartContract
    {
        //管理员账户
        static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s");

        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")] public static event deleTransfer Transferred;

        readonly static BigInteger maxConnectWeight = 100000;

        //Math合约
        [Appcall("9f8d9b7dd380c187dadb887a134bf56e3e1d3453")]
        static extern object mathCall(string method, object[] arr);
        
        //抵押币hash
        [Appcall("04e31cee0443bb916534dad2adf508458920e66d")]
        static extern object bcpCall(string method, object[] arr);

        public static string name()
        {
            return "App Test Coin01";
        }

        public static string symbol()
        {
            return "ATT";
        }

        private const ulong factor = 100000000; //精度
        private const ulong totalCoin = 1 * 100000000 * factor;

        public static byte decimals()
        {
            return 8;
        }

        public static object Main(string method, object[] args)
        {
            string magicStr = "Bcp2Att";
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                if (method == "totalSupply")
                    return totalSupply();
                if (method == "name")
                    return name();
                if (method == "symbol")
                    return symbol();
                if (method == "decimals")
                    return decimals();
                if (method == "deploy")
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
                    if (total_supply.Length != 0)
                        return false;
                    var keySuperAdmin = new byte[] {0x11}.Concat(superAdmin);
                    Storage.Put(Storage.CurrentContext, keySuperAdmin, totalCoin);
                    Storage.Put(Storage.CurrentContext, "totalSupply", totalCoin);
                    Transferred(null, superAdmin, totalCoin);
                }

                if (method == "balanceOf")
                {
                    byte[] who = (byte[]) args[0];
                    if (who.Length != 20)
                        return false;
                    return balanceOf(who);
                }

                if (method == "transfer")
                {
                    byte[] from = (byte[]) args[0];
                    byte[] to = (byte[]) args[1];
                    BigInteger value = (BigInteger) args[2];
                    if (!Runtime.CheckWitness(from))
                        return false;
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger())
                        return false;
                    if (!IsPayable(to))
                        return false;
                    return transfer(from, to, value);
                }

                if (method == "transfer_app")
                {
                    byte[] from = (byte[]) args[0];
                    byte[] to = (byte[]) args[1];
                    BigInteger value = (BigInteger) args[2];

                    if (from.AsBigInteger() != callscript.AsBigInteger())
                        return false;
                    return transfer(from, to, value);
                }

                if (method == "getTxInfo")
                {
                    byte[] txid = (byte[]) args[0];
                    return getTxInfo(txid);
                }
               
                //invoke 即可得到值的方法
                if ("calculatePurchaseReturn" == method)
                {
                    var amount = (BigInteger) args[0];
                    var connectBalance = GetConnectBalance();
                    var smartTokenSupply = GetSmartTokenSupply();
                    var connectWeight = GetConnetWeight();

                    if (connectBalance == 0 || smartTokenSupply == 0 || connectWeight == 0)
                        return 0;
                    return mathCall("purchase",
                        new object[5] {amount, connectBalance, smartTokenSupply, connectWeight, maxConnectWeight});
                }

                if ("getConnectBalance" == method)
                {
                    return GetConnectBalance();
                }

                if ("getSmartTokenSupply" == method)
                {
                    return GetSmartTokenSupply();
                }

                if ("getConnetWeight" == method)
                {
                    return GetConnetWeight();
                }

                if ("getMaxConnectWeight" == method)
                {
                    return maxConnectWeight;
                }
                
                //需要发送交易调用的
                //管理员权限
                if ("setConnectBalanceIn" == method)
                {
                    var txid = (byte[]) args[0];
                    var tx = GetBcpTxInfo(txid);
                    if (tx.from.Length == 0 || tx.from.AsBigInteger() != superAdmin.AsBigInteger())
                        return false;
                    if (tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                        return false;
                    if (tx.value <= 0)
                        return false;
                    var connectBalance = GetConnectBalance();
                    PutConnectBalance(connectBalance + tx.value);
                    SetBcpTxUsed(txid);
                    return true;
                }

                if ("setSmartTokenSupplyIn" == method)
                {
                    BigInteger value = (BigInteger) args[0];
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger())
                        return false;
                    if (transfer(superAdmin, ExecutionEngine.ExecutingScriptHash, value))
                    {
                        var smartTokenSupply = GetSmartTokenSupply();
                        PutSmartTokenSupply(smartTokenSupply + value);
                        return true;
                    }
                }

                if ("setConnectWeight" == method)
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    BigInteger connectWeight = (BigInteger) args[0];
                    StorageMap connectWeightMap = Storage.CurrentContext.CreateMap("connectWeightMap");
                    connectWeightMap.Put("connectWeight", connectWeight);
                }

                if ("getConnectBalanceBack" == method)
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    BigInteger amount = (BigInteger) args[0];
                    var connectBalance = GetConnectBalance();
                    if (connectBalance < amount)
                        return false;
                    if ((bool) bcpCall("transfer_app",
                        new object[3] {ExecutionEngine.ExecutingScriptHash, superAdmin, amount}))
                    {
                        PutConnectBalance(connectBalance - amount);
                        return true;
                    }
                }

                if ("getSmartTokenSupplyBack" == method)
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    BigInteger amount = (BigInteger) args[0];
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger())
                        return false;
                    var smartTokenSupply = GetSmartTokenSupply();
                    if (smartTokenSupply < amount)
                        return false;
                    if (transfer(ExecutionEngine.ExecutingScriptHash, superAdmin, amount))
                    {
                        PutSmartTokenSupply(smartTokenSupply - amount);
                        return true;
                    }
                }

                //无需管理员权限
                //转入一定的抵押币换取智能代币
                if ("purchase" == method)
                {
                    var txid = (byte[]) args[0];
                    var tx = GetBcpTxInfo(txid);
                    if (tx.@from.Length <= 0 || tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                        return false;

                    var connectBalance = GetConnectBalance();
                    var smartTokenSupply = GetSmartTokenSupply();
                    var connectWeight = GetConnetWeight();

                    //如果有任意一个小于0  即认为没有初始化完成或者被套空了  不允许继续
                    if (tx.value <= 0 || connectBalance <= 0 || smartTokenSupply <= 0 || connectWeight <= 0)
                        return false;

                    BigInteger T = (BigInteger) mathCall("purchase",new object[5] {tx.value, connectBalance, smartTokenSupply, connectWeight, maxConnectWeight});
                    if (transfer(ExecutionEngine.ExecutingScriptHash, tx.@from, T))
                    {
                        PutConnectBalance(connectBalance + tx.value);
                        PutSmartTokenSupply(smartTokenSupply - T);
                        SetBcpTxUsed(txid);
                        return true;
                    }

                    return false;
                }

                //清算一定的智能代币换取抵押币
                if ("sale" == method)
                {
                    byte[] from = (byte[]) args[0];
                    BigInteger amount = (BigInteger) args[1];
                    if (!Runtime.CheckWitness(from))
                        return false;
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger())
                        return false;
                    if (transfer(from, ExecutionEngine.ExecutingScriptHash, amount))
                    {
                        var connectBalance = GetConnectBalance();
                        var smartTokenSupply = GetSmartTokenSupply();
                        var connectWeight = GetConnetWeight();

                        //如果有任意一个小于0  即认为没有初始化完成或者被套空了  不允许继续
                        if (amount <= 0 || connectBalance <= 0 || smartTokenSupply <= 0 || connectWeight <= 0)
                            return false;
                        BigInteger E = (BigInteger) mathCall("sale",
                            new object[5] {amount, connectBalance, smartTokenSupply, connectWeight, maxConnectWeight});
                       
                        if (connectBalance < E) //应该不会出现这种情况
                            return false;

                        if ((bool) bcpCall("transfer_app", new object[3] {ExecutionEngine.ExecutingScriptHash, from, E}))
                        {
                            PutConnectBalance(connectBalance - E);
                            PutSmartTokenSupply(smartTokenSupply + E);
                            return true;
                        }
                    }
                    return false;
                }
            }
            return true;
        }

        private static object totalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }

        private static bool transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (from.Length != 20 || to.Length != 20)
                return false;
            if (value <= 0)
                return false;
            if (from == to)
                return true;
            if (from.Length > 0)
            {
                var keyFrom = new byte[] {0x11}.Concat(from);
                BigInteger from_value = Storage.Get(Storage.CurrentContext, keyFrom).AsBigInteger();
                if (from_value < value)
                    return false;
                if (from_value == value)
                    Storage.Delete(Storage.CurrentContext, keyFrom);
                else
                {
                    Storage.Put(Storage.CurrentContext, keyFrom, from_value - value);
                }
            }

            if (to.Length > 0)
            {
                var keyTo = new byte[] {0x11}.Concat(to);
                BigInteger to_value = Storage.Get(Storage.CurrentContext, keyTo).AsBigInteger();
                Storage.Put(Storage.CurrentContext, keyTo, to_value + value);
            }
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
            byte[] txInfo = Neo.SmartContract.Framework.Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            var keyTxid = new byte[] {0x13}.Concat(txid);
            Storage.Put(Storage.CurrentContext, keyTxid, txInfo);
        }

        private static object balanceOf(byte[] who)
        {
            var keyAddress = new byte[] {0x11}.Concat(who);
            return Storage.Get(Storage.CurrentContext, keyAddress).AsBigInteger();
        }

        private static TransferInfo getTxInfo(byte[] txid)
        {
            byte[] keyTxid = new byte[] {0x13}.Concat(txid);
            byte[] v = Storage.Get(Storage.CurrentContext, keyTxid);
            if (v.Length == 0)
                return null;
            return Neo.SmartContract.Framework.Helper.Deserialize(v) as TransferInfo;
        }

        public static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            if (c.Equals(null))
                return true;
            return c.IsPayable;
        }

        public static void PutConnectBalance(BigInteger _amount)
        {
            StorageMap connectBalanceMap = Storage.CurrentContext.CreateMap("connectBalanceMap");

            if (_amount <= 0)
                connectBalanceMap.Delete("connectBalance");
            else
                connectBalanceMap.Put("connectBalance", _amount);
        }

        public static BigInteger GetConnectBalance()
        {
            StorageMap connectBalanceMap = Storage.CurrentContext.CreateMap("connectBalanceMap");
            return connectBalanceMap.Get("connectBalance").AsBigInteger();
        }

        public static BigInteger GetConnetWeight()
        {
            StorageMap connectWeightMap = Storage.CurrentContext.CreateMap("connectWeightMap");
            return connectWeightMap.Get("connectWeight").AsBigInteger();
        }

        public static void PutSmartTokenSupply(BigInteger _supply)
        {
            StorageMap smartTokenSupplyMap = Storage.CurrentContext.CreateMap("smartTokenSupplyMap");
            if (_supply == 0)
                smartTokenSupplyMap.Delete("smartTokenSupply");
            else
                smartTokenSupplyMap.Put("smartTokenSupply", _supply);
        }

        public static BigInteger GetSmartTokenSupply()
        {
            StorageMap smartTokenSupplyMap = Storage.CurrentContext.CreateMap("smartTokenSupplyMap");
            return smartTokenSupplyMap.Get("smartTokenSupply").AsBigInteger();
        }

        public static TransferInfo GetBcpTxInfo(byte[] txid)
        {
            StorageMap bcpTxInfoMap = Storage.CurrentContext.CreateMap("bcpTxInfoMap");
            var v = bcpTxInfoMap.Get(txid).AsBigInteger();
            if (v == 0)
            {
                object[] _p = new object[1];
                _p[0] = txid;
                var info = bcpCall("getTxInfo", _p);
                return info as TransferInfo;
            }
            return new TransferInfo();
        }

        public static void SetBcpTxUsed(byte[] txid)
        {
            StorageMap bcpTxInfoMap = Storage.CurrentContext.CreateMap("bcpTxInfoMap");
            bcpTxInfoMap.Put(txid, 1);
        }

    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }
}
