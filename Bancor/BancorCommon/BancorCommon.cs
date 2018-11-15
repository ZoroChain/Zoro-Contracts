using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;
using System.Security.Policy;
using Helper = Neo.SmartContract.Framework.Helper;

namespace BancorCommon
{
    /// <summary>
    /// 通用Bancor合约，所有代币公用的Bancor合约，总管理员可以设置各类代币的管理员账户、连接器信息，各代币管理员可以设置自己代币的连接器权重、余额等信息
    /// </summary>
    public class BancorCommon : SmartContract
    {
        //总管理员账户 用来设置白名单等
        static readonly byte[] superAdmin = Helper.ToScriptHash("AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s");
        delegate object deleCall(string method, object[] args);

        public static object Main(string method, object[] args)
        {
            string magicStr = "BancorCommon_v1.0";

            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                //invoke
                if (method == "getWhiteList")
                    return GetWhiteList();

                if (method == "getAssetInfo")
                {
                    byte[] assetid = (byte[]) args[0];
                    return GetAssetInfo(assetid);
                }

                //总管理员权限
                if (method == "setMathContract")
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    return SetMathContract((byte[])args[0]);
                }
                
                if (method == "setWhiteList")
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var assetid = (byte[])args[0];
                    var connectAssetId = (byte[])args[1];
                    var admin = (byte[])args[2];
                    if (assetid.Length == 0 || connectAssetId.Length == 0 || admin.Length == 0)
                        return false;
                    var whiteList = GetWhiteList();
                    AssetInfo assetInfo = new AssetInfo();
                    if (whiteList.HasKey(assetid)) //设置过就不能改了
                        return false;
                    assetInfo.connectAssetId = connectAssetId;
                    assetInfo.admin = admin;
                    if (SetAssetInfo(assetid, assetInfo))
                        return SetWhiteList(assetid, admin);
                    return false;
                }

                //应用币管理员权限
                if (method == "setConnectWeight")
                {
                    var assetid = (byte[])args[0];
                    var connectWeight = (BigInteger)args[1];
                    if (assetid.Length == 0 || connectWeight <= 0)
                        return false;
                    var assetInfo = GetAssetInfo(assetid);
                    if (!Runtime.CheckWitness(assetInfo.admin))
                        return false;
                    assetInfo.connectWeight = connectWeight;
                    return SetAssetInfo(assetid, assetInfo);
                }

                if (method == "setMaxConnectWeight")
                {
                    var assetid = (byte[])args[0];
                    var maxConnectWeight = (BigInteger)args[1];
                    if (assetid.Length == 0 || maxConnectWeight <= 0)
                        return false;
                    var assetInfo = GetAssetInfo(assetid);
                    if (!Runtime.CheckWitness(assetInfo.admin))
                        return false;
                    assetInfo.maxConnectWeight = maxConnectWeight;
                    return SetAssetInfo(assetid, assetInfo);
                }

                if (method == "setConnectBalanceIn")
                {
                    var assetid = (byte[])args[0];
                    var txid = (byte[])args[1];
                    var assetInfo = GetAssetInfo(assetid);
                    if (!Runtime.CheckWitness(assetInfo.admin))
                        return false;
                    if (assetid.Length == 0 || txid.Length == 0 || assetInfo.connectAssetId.Length == 0)
                        return false;
                    var tx = GetTxInfo(assetInfo.connectAssetId, txid);
                    if (tx.from.AsBigInteger() != assetInfo.admin.AsBigInteger() || tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger() || tx.value <= 0)
                        return false;
                    assetInfo.connectBalance += tx.value;
                    if (SetAssetInfo(assetid, assetInfo))
                    {
                        SetTxUsed(txid);
                        return true;
                    }
                    return false;
                }

                if (method == "setSmartTokenSupplyIn")
                {
                    var assetid = (byte[])args[0];
                    var txid = (byte[])args[1];
                    if (assetid.Length == 0 || txid.Length == 0)
                        return false;
                    var assetInfo = GetAssetInfo(assetid);
                    if (!Runtime.CheckWitness(assetInfo.admin))
                        return false;
                    var tx = GetTxInfo(assetid, txid);
                    if (tx.from.AsBigInteger() != assetInfo.admin.AsBigInteger() || tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger() || tx.value <= 0)
                        return false;
                    assetInfo.smartTokenSupply += tx.value;
                    if (SetAssetInfo(assetid, assetInfo))
                    {
                        SetTxUsed(txid);
                        return true;
                    }
                    return false;
                }

                if (method == "getConnectBalanceBack")
                {
                    var assetid = (byte[])args[0];
                    BigInteger amount = (BigInteger)args[1];
                    var assetInfo = GetAssetInfo(assetid);
                    if (!Runtime.CheckWitness(assetInfo.admin))
                        return false;
                    if (assetid.Length == 0 || assetInfo.connectAssetId.Length == 0 || amount <= 0 || assetInfo.connectBalance < amount)
                        return false;
                    if ((bool)TransferApp(assetInfo.connectAssetId, assetInfo.admin, amount))
                    {
                        assetInfo.connectBalance -= amount;
                        return SetAssetInfo(assetid, assetInfo);
                    }
                    return false;
                }

                if (method == "getSmartTokenSupplyBack")
                {
                    var assetid = (byte[])args[0];
                    BigInteger amount = (BigInteger)args[1];
                    var assetInfo = GetAssetInfo(assetid);
                    if (!Runtime.CheckWitness(assetInfo.admin))
                        return false;
                    if (assetid.Length == 0 || amount <= 0 || assetInfo.smartTokenSupply < amount)
                        return false;
                    if ((bool)TransferApp(assetid, assetInfo.admin, amount))
                    {
                        assetInfo.smartTokenSupply -= amount;
                        return SetAssetInfo(assetid, assetInfo);
                    }
                    return false;
                }

                byte[] mathContract = GetMathContract();
                if (mathContract.Length == 0) return true;

                if (method == "getMathContract")
                    return GetMathContract();

                if (method == "calculatePurchaseReturn")
                {
                    var assetid = (byte[])args[0];
                    var amount = (BigInteger)args[1];
                    var assetInfo = GetAssetInfo(assetid);
                    if (amount == 0 || assetInfo.connectBalance == 0 || assetInfo.smartTokenSupply == 0 ||
                        assetInfo.connectWeight == 0)
                        return 0;
                    deleCall mathCall = (deleCall)mathContract.ToDelegate();
                    return mathCall("purchase",
                        new object[5]
                        {
                            amount, assetInfo.connectBalance, assetInfo.smartTokenSupply, assetInfo.connectWeight,
                            assetInfo.maxConnectWeight
                        });
                }

                //无需管理员权限
                //转入一定的抵押币换取智能代币
                if (method == "purchase")
                {
                    var assetid = (byte[])args[0];
                    var txid = (byte[])args[1];
                    if (assetid.Length == 0 || txid.Length == 0)
                        return false;
                    var assetInfo = GetAssetInfo(assetid);
                    var tx = GetTxInfo(assetInfo.connectAssetId, txid);
                    if (tx.from.Length == 0 || tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger() || tx.value <= 0)
                        return false;
                    deleCall mathCall = (deleCall)mathContract.ToDelegate();
                    var amount = (BigInteger)mathCall("purchase",
                        new object[5]
                        {
                            tx.value, assetInfo.connectBalance, assetInfo.smartTokenSupply, assetInfo.connectWeight,
                            assetInfo.maxConnectWeight
                        });
                    if ((bool)TransferApp(assetid, tx.from, amount))
                    {
                        assetInfo.connectBalance += tx.value;
                        assetInfo.smartTokenSupply -= amount;
                        SetAssetInfo(assetid, assetInfo);
                        SetTxUsed(txid);
                        return true;
                    }
                    return false;
                }

                //清算一定的智能代币换取抵押币
                if (method == "sale")
                {
                    var assetid = (byte[])args[0];
                    var txid = (byte[])args[1];
                    if (assetid.Length == 0 || txid.Length == 0)
                        return false;
                    var assetInfo = GetAssetInfo(assetid);
                    var tx = GetTxInfo(assetid, txid);
                    if (tx.from.Length == 0 || tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger() || tx.value <= 0)
                        return false;
                    deleCall mathCall = (deleCall)mathContract.ToDelegate();
                    var amount = (BigInteger)mathCall("sale",
                        new object[5]
                        {
                            tx.value, assetInfo.connectBalance, assetInfo.smartTokenSupply, assetInfo.connectWeight,
                            assetInfo.maxConnectWeight
                        });
                    if ((bool)TransferApp(assetInfo.connectAssetId, tx.from, amount))
                    {
                        assetInfo.smartTokenSupply += tx.value;
                        assetInfo.connectBalance -= amount;
                        SetAssetInfo(assetid, assetInfo);
                        SetTxUsed(txid);
                        return true;
                    }
                    return false;
                }

            }

            return false;
        }

        /// <summary>
        /// 设置可以使用Bancor的白名单
        /// </summary>
        /// <param name="key">nep5资产hash</param>
        /// <param name="admin">代币管理员地址</param>
        /// <returns></returns>
        public static bool SetWhiteList(byte[] key, byte[] admin)
        {
            StorageMap whiteListMap = Storage.CurrentContext.CreateMap("whiteListMap");
            byte[] whiteListBytes = whiteListMap.Get("whiteList");
            Map<byte[], byte[]> map = new Map<byte[], byte[]>();
            if (whiteListBytes.Length > 0)
                map = whiteListBytes.Deserialize() as Map<byte[], byte[]>;
            map[key] = admin;
            whiteListMap.Put("whiteList", map.Serialize());
            return true;
        }

        public static byte[] GetMathContract()
        {
            StorageMap mathContractMap = Storage.CurrentContext.CreateMap("mathContractMap");
            return mathContractMap.Get("mathContract");
        }

        public static bool SetMathContract(byte[] contractHash)
        {
            StorageMap mathContractMap = Storage.CurrentContext.CreateMap("mathContractMap");
            mathContractMap.Put("mathContract", contractHash);
            return true;
        }


        public static bool SetAssetInfo(byte[] assetid, AssetInfo assetInfo)
        {
            StorageMap assentInfoMap = Storage.CurrentContext.CreateMap("assentInfoMap");
            byte[] assetInfoBytes = Helper.Serialize(assetInfo);
            assentInfoMap.Put(assetid, assetInfoBytes);
            return true;
        }

        public static AssetInfo GetAssetInfo(byte[] assetid)
        {
            StorageMap assentInfoMap = Storage.CurrentContext.CreateMap("assentInfoMap");
            byte[] assetInfoBytes = assentInfoMap.Get(assetid);
            AssetInfo assetInfo = new AssetInfo();
            if (assetInfoBytes.Length > 0)
                assetInfo = assetInfoBytes.Deserialize() as AssetInfo;
            return assetInfo;
        }

        public static Map<byte[], byte[]> GetWhiteList()
        {
            StorageMap whiteListMap = Storage.CurrentContext.CreateMap("whiteListMap");
            byte[] data = whiteListMap.Get("whiteList");
            if (data.Length == 0)
                return new Map<byte[], byte[]>();
            return data.Deserialize() as Map<byte[], byte[]>;
        }

        public static object TransferApp(byte[] assetid, byte[] to, BigInteger amount)
        {
            object[] _p = new object[3] { ExecutionEngine.ExecutingScriptHash, to, amount};
            deleCall call = (deleCall)assetid.ToDelegate();
            return call("transfer_app", _p);
        }

        public static TransferInfo GetTxInfo(byte[] assetid, byte[] txid)
        {
            StorageMap txInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
            var tInfo = new TransferInfo();
            var v = txInfoMap.Get(txid).AsBigInteger();
            if (v == 0)
            {
                object[] _p = new object[1] { txid };
                deleCall call = (deleCall)assetid.ToDelegate();
                var info = call("getTxInfo", _p);
                if (((object[])info).Length == 3)
                    return info as TransferInfo;
            }
            return tInfo;
        }

        public static void SetTxUsed(byte[] txid)
        {
            StorageMap txInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
            txInfoMap.Put(txid, 1);
        }

    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }

    public class AssetInfo
    {
        public byte[] connectAssetId; //连接器hash
        public byte[] admin; //管理员
        public BigInteger connectWeight; //连接器权重
        public BigInteger maxConnectWeight; //最大权重
        public BigInteger connectBalance; //连接器代币余额
        public BigInteger smartTokenSupply; //智能代币余额
    }
}
