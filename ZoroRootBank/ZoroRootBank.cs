using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

namespace ZoroRootBank
{
    public class ZoroRootBank : SmartContract
    {
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AdsNmzKPPG7HfmQpacZ4ixbv9XJHJs2ACz");
        delegate object deleCall(string method, object[] args);

        public delegate void deleDeposit(byte[] txid, byte[] chainHash, byte[] assetHash, byte[] to, BigInteger amount);
        [DisplayName("deposit")] public static event deleDeposit Deposited;

        public delegate void deleSendMoney(byte[] chainHash, byte[] assetHash, byte[] to, BigInteger amount);
        [DisplayName("sendmoney")] public static event deleSendMoney SendedMoney;

        public static object Main(string method,object[] args)
        {
            string magicStr = "RootBank_v_1.0";

            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                //总管理员权限
                //设置跨链白名单
                if (method == "setWhiteList")
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var chainHash = (byte[])args[0];//链hash
                    var assetHash = (byte[])args[1];//资产
                    var admin = (byte[])args[2];//管理员 要不要用个多签地址？
                    if (assetHash.Length == 0 || admin.Length == 0 || admin.Length == 0)
                        return false;
                    SetWhiteList(chainHash, assetHash, admin);
                    return true;
                }

                //invoke
                if (method == "getWhiteList")
                {
                    var chainHash = (byte[]) args[0];
                    return GetWhiteList(chainHash);
                }

                if (method == "getAccountInfo")
                {
                    byte[] address = (byte[]) args[0];
                    return GetAccountInfo(address);
                }

                //存钱
                if (method == "deposit")
                {
                    var chainHash = (byte[])args[0];//目标链hash
                    var assetHash = (byte[]) args[1];//资产hash
                    var txid = (byte[]) args[2];//交易txid
                    var to = (byte[]) args[3];//收款账户
                    if (chainHash.Length == 0 || assetHash.Length == 0 || txid.Length == 0)
                        return false;
                    var whiteList = GetWhiteList(chainHash);
                    if (!whiteList.HasKey(assetHash))
                        return false;
                    var tx = GetTxInfo(assetHash, txid);
                    if (tx.@from.Length == 0 ||
                        tx.@from.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger() || tx.value <= 0)
                        return false;
                    var accountInfo = GetAccountInfo(tx.@from);
                    if (accountInfo.HasKey(assetHash))
                        accountInfo[assetHash] = accountInfo[assetHash] + tx.value;
                    else
                        accountInfo[assetHash] = tx.value;
                    StorageMap accountMap = Storage.CurrentContext.CreateMap("accountMap");
                    accountMap.Put(tx.@from,accountInfo.Serialize());
                    SetTxidUsed(txid);
                    //notify
                    Deposited(txid, chainHash, assetHash, to, tx.value);
                    return true;
                }

                //发钱
                if (method == "sendmoney")
                {
                    var chainHash = (byte[]) args[0];
                    var assetHash = (byte[])args[1];//资产hash
                    var to = (byte[])args[2];//交易txid
                    var amount = (BigInteger) args[3];
                    if (chainHash.Length == 0 || assetHash.Length == 0 || to.Length == 0 || amount <= 0)
                        return false;
                    var whiteList = GetWhiteList(chainHash);
                    if (!whiteList.HasKey(assetHash))
                        return false;
                    var admin = whiteList[assetHash];
                    if (!Runtime.CheckWitness(admin))
                        return false;
                    object[] _p = new object[3] { ExecutionEngine.ExecutingScriptHash, to, amount };
                    deleCall call = (deleCall)assetHash.ToDelegate();
                    if ((bool) call("transfer_app", _p))
                    {
                        SendedMoney(chainHash, assetHash, to, amount);
                        return true;
                    }

                    return false;
                }

            }
          
            return false;
        }

        public static void SetWhiteList(byte[] chainHash, byte[] assetHash, byte[] admin)
        {
            StorageMap whiteListMap = Storage.CurrentContext.CreateMap("whiteListMap");
            byte[] whiteListBytes = whiteListMap.Get(chainHash);
            Map<byte[], byte[]> map = new Map<byte[], byte[]>();
            if (whiteListBytes.Length > 0)
                map = whiteListBytes.Deserialize() as Map<byte[], byte[]>;
            map[assetHash] = admin;
            whiteListMap.Put(chainHash, map.Serialize());
        }

        private static Map<byte[], BigInteger> GetAccountInfo(byte[] address)
        {
            StorageMap accountMap = Storage.CurrentContext.CreateMap("accountMap");
            byte[] accountInfoBytes = accountMap.Get(address);
            Map<byte[], BigInteger> map = new Map<byte[], BigInteger>();
            if (accountInfoBytes.Length > 0)
                map = accountInfoBytes.Deserialize() as Map<byte[], BigInteger>;
            return map;
        }

        private static Map<byte[], byte[]> GetWhiteList(byte[] chainHash)
        {
            StorageMap whiteListMap = Storage.CurrentContext.CreateMap("whiteListMap");
            byte[] whiteListBytes = whiteListMap.Get(chainHash);
            Map<byte[], byte[]> map = new Map<byte[], byte[]>();
            if (whiteListBytes.Length > 0)
                map = whiteListBytes.Deserialize() as Map<byte[], byte[]>;
            return map;
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

        public static void SetTxidUsed(byte[] txid)
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

    internal class AccountInfo
    {
        internal byte[] connectAssetId;
        internal byte[] admin;
    }
}
