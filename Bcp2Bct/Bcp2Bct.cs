using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace Bcp2Bct
{
    public class Bcp2Bct : SmartContract
    {
        //管理员账户
        static readonly byte[] superAdmin =
            Neo.SmartContract.Framework.Helper.ToScriptHash("AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s");

        static BigInteger maxConnectWeight = 100000;

        //bancor管理合约的hash
        [Appcall("834eabcaa02d3184f0d6767f1ab28b039209546d")]
        static extern object rootCall(string method, object[] arr);

        [Appcall("04e31cee0443bb916534dad2adf508458920e66d")]
        static extern object bcpCall(string method, object[] arr);

        [Appcall("40a80749ef62da6fc3d74dbf6fc7745148922372")]
        static extern object bctCall(string method, object[] arr);

        public static object Main(string method, object[] args)
        {
            string magicStr = "Bcp2Bct";
            if (Runtime.Trigger == TriggerType.Verification)
            {
                //鉴权部分
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                /*
                 * invoke 即可得到值的方法
                 */
                if ("calculatePurchaseReturn" == method)
                {
                    var amount = (BigInteger) args[0];

                    var connectBalance = GetConnectBalance();
                    var smartTokenSupply = GetSmartTokenSupply();
                    var connectWeight = GetConnetWeight();

                    if (connectBalance == 0 || smartTokenSupply == 0 || connectWeight == 0)
                        return 0;
                    return rootCall("purchase", new object[5] {amount, connectBalance, smartTokenSupply, connectWeight, maxConnectWeight});
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

                /*
               * 需要发送交易调用的
               */
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
                    var txid = (byte[]) args[0];
                    var tx = GetBctTxInfo(txid);
                    if (tx.from.Length == 0 || tx.from.AsBigInteger() != superAdmin.AsBigInteger())
                        return false;
                    if (tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                        return false;
                    if (tx.value <= 0)
                        return false;
                    var smartTokenSupply = GetSmartTokenSupply();
                    PutSmartTokenSupply(smartTokenSupply + tx.value);
                    SetBctTxUsed(txid);
                    return true;
                }

                if ("setConnectWeight" == method)
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    BigInteger connectWeight = (BigInteger)args[0];
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
                    if ((bool) bcpCall("transfer",
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
                    BigInteger amount = (BigInteger)args[0];
                    var smartTokenSupply = GetSmartTokenSupply();
                    if (smartTokenSupply < amount)
                        return false;
                    if ((bool)bctCall("transfer_app", new object[3] { ExecutionEngine.ExecutingScriptHash, superAdmin, amount }))
                    {
                        PutSmartTokenSupply(smartTokenSupply - amount);
                        return true;

                    }
                }

                //无需管理员权限
                //转入一定的抵押币换取智能代币
                if ("purchase" == method)
                {
                    var txid = (byte[])args[0];
                    var tx = GetBcpTxInfo(txid);
                    if (tx.from.Length == 0)
                        return false;
                    if (tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                        return false;
                    if (tx.value <= 0)
                        return false;

                    var amount = (BigInteger)tx.value; // 转入的抵押币的数量

                    var connectBalance = GetConnectBalance();
                    var smartTokenSupply = GetSmartTokenSupply();
                    var connectWeight = GetConnetWeight();

                    //如果有任意一个小于0  即认为没有初始化完成或者被套空了  不允许继续
                    if (amount <= 0 || connectBalance <= 0 || smartTokenSupply <= 0 || connectWeight <= 0)
                        return false;
                    BigInteger T = (BigInteger)rootCall("purchase", new object[5] { amount, connectBalance, smartTokenSupply, connectWeight, maxConnectWeight });

                    if (T <= 0)
                        return false;

                    if (smartTokenSupply < T)//应该不会出现这种情况
                        return false;
                    
                    if ((bool)bctCall("transfer_app", new object[3] { ExecutionEngine.ExecutingScriptHash, tx.to, T }))
                    {
                        PutConnectBalance(connectBalance + amount);
                        PutSmartTokenSupply(smartTokenSupply - T);
                        SetBcpTxUsed(txid);
                        return true;

                    }
                    return false;
                }

                //清算一定的智能代币换取抵押币
                if ("sale" == method)
                {
                    var txid = (byte[]) args[0];
                    var tx = GetBctTxInfo(txid);
                    if (tx.from.Length == 0)
                        return false;
                    if (tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                        return false;
                    if (tx.value <= 0)
                        return false;
                    var amount = (BigInteger) tx.value; // 转入的智能代币的数量  T

                    var connectBalance = GetConnectBalance();
                    var smartTokenSupply = GetSmartTokenSupply();
                    var connectWeight = GetConnetWeight();

                    //如果有任意一个小于0  即认为没有初始化完成或者被套空了  不允许继续
                    if (amount <= 0 || connectBalance <= 0 || smartTokenSupply <= 0 || connectWeight <= 0)
                        return false;
                    BigInteger E = (BigInteger) rootCall("sale",
                        new object[5] {amount, connectBalance, smartTokenSupply, connectWeight, maxConnectWeight});
                    if (E <= 0)
                        return false;

                    if (connectBalance < E) //应该不会出现这种情况
                        return false;

                    if ((bool) bcpCall("transfer", new object[3] {ExecutionEngine.ExecutingScriptHash, tx.to, E}))
                    {
                        PutConnectBalance(connectBalance - E);
                        PutSmartTokenSupply(smartTokenSupply + E);
                        SetBctTxUsed(txid);
                        return true;

                    }

                    return false;
                }
            }

            return true;

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

        public static BigInteger GetSmartTokenSupply()
        {
            StorageMap smartTokenSupplyMap = Storage.CurrentContext.CreateMap("smartTokenSupplyMap");
            return smartTokenSupplyMap.Get("smartTokenSupply").AsBigInteger();
        }

        public static void PutSmartTokenSupply(BigInteger _supply)
        {
            StorageMap smartTokenSupplyMap = Storage.CurrentContext.CreateMap("smartTokenSupplyMap");
            if (_supply == 0)
                smartTokenSupplyMap.Delete("smartTokenSupply");
            else
                smartTokenSupplyMap.Put("smartTokenSupply", _supply);
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
                if (((object[])info).Length == 3)
                    return info as TransferInfo;
            }
            var tInfo = new TransferInfo();
            tInfo.from = new byte[0];
            return tInfo;
        }

        private static TransferInfo GetBctTxInfo(byte[] txid)
        {
            StorageMap bctTxInfoMap = Storage.CurrentContext.CreateMap("bctTxInfoMap");
            var v = bctTxInfoMap.Get(txid).AsBigInteger();
            if (v == 0)
            {
                object[] _p = new object[1];
                _p[0] = txid;
                var info = bctCall("getTxInfo", _p);
                if (((object[])info).Length == 3)
                    return info as TransferInfo;
            }
            var tInfo = new TransferInfo();
            tInfo.from = new byte[0];
            return tInfo;
        }

        public static void PutConnectBalance(BigInteger _amount)
        {
            StorageMap connectBalanceMap = Storage.CurrentContext.CreateMap("connectBalanceMap");

            if (_amount <= 0)
                connectBalanceMap.Delete("connectBalance");
            else
                connectBalanceMap.Put("connectBalance", _amount);
        }

        public static void SetBcpTxUsed(byte[] txid)
        {
            StorageMap bcpTxInfoMap = Storage.CurrentContext.CreateMap("bcpTxInfoMap");
            bcpTxInfoMap.Put(txid, 1);
        }

        static void SetBctTxUsed(byte[] txid)
        {
            StorageMap bctTxInfoMap = Storage.CurrentContext.CreateMap("bctTxInfoMap");
            bctTxInfoMap.Put(txid, 1);
        }
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }
}
