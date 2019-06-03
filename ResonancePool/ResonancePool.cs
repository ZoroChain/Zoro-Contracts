using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace ResonancePool
{
    public class ResonancePool : SmartContract
    {
        //总管理员账户 用来设置白名单等
        static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s");
        delegate object deleContract(string method, object[] args);

        [DisplayName("addedToWhitelist")]
        public static event Action<byte[]> EmitAddedToWhitelist; // (scriptHash)

        [DisplayName("removedFromWhitelist")]
        public static event Action<byte[]> EmitRemovedFromWhitelist; // (scriptHash)

        [DisplayName("operatorAddressSet")]
        public static event Action<byte[]> EmitOperatorAddressSet; // (address)

        [DisplayName("resonance")]
        public static event Action<byte[], byte[], BigInteger, BigInteger> EmitResonance; // (address, assetID, btcAmount, bcpAmount)

        [DisplayName("withdraw")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawn; // (address, assetID, amount)

        // Contract States
        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        public static object Main(string operation, object[] args)
        {
            string magicStr = "ResonancePool_v1.0";

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


                if (GetState() != Active) return false;

                // 设置交易费收取地址,设置交易员,设置操作员
                if (operation == "setOperator")
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    var opera = (byte[])args[0];
                    if (opera.Length != 20) return false;
                    Storage.Put(Context(), "operator", opera);
                    EmitOperatorAddressSet(opera);
                    return true;
                }

                if (operation == "resonance") //address, assetID, bcpId, value
                {
                    if (args.Length != 4) return false;
                    return Resonance((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3]);
                }

                var operatorAddr = Storage.Get(Context(), "operator");
                // 操作员签名
                if (!Runtime.CheckWitness(operatorAddr)) return false;

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

                //withdraw btc
                if (operation == "withdraw") // address, withdrawAssetId, withdrawAmount, isGlobal
                {
                    if (args.Length != 3) return false;
                    return Withdrawal((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                }
            }

            return false;
        }

        private static bool Resonance(byte[] address, byte[] assetId, byte[] bcpId, BigInteger value)
        {
            // Check that the contract is safe
            //if (!GetIsWhitelisted(assetId)) return false;

            byte[] contract = ExecutionEngine.ExecutingScriptHash;
            bool success = false;

            //转入 BTC/NEO 等
            var args = new object[] { address, contract, value };
            var deleCall = (deleContract)assetId.ToDelegate();
            success = (bool)deleCall("transferFrom", args);

            if (success)
            {
                //共振池总层数
                BigInteger allLayer = 8;
                BigInteger perLayerNum = 10 * 100000000;

                //na1+n(n-1)d/2
                //BigInteger allAmount = allLayer * 10 + allLayer * (allLayer - 1) * 10 / 2;

                //共振池 BCP 余额
                BigInteger balance = new BigInteger(NativeAsset.Call("BalanceOf", bcpId, contract));

                Runtime.Notify(0, balance);//debug

                //当前共振池所在层
                BigInteger curLayer = Storage.Get(Context(), "curLayer").AsBigInteger();
                if (curLayer == 0) curLayer = allLayer;
                BigInteger newLayer = curLayer;

                //可兑换 BCP 数量
                BigInteger amount = 0;

                //当前层 BCP 余额 = 共振池余额 - 当前层以下所有层的总和
                BigInteger curLayerBalance = balance - ((curLayer - 1) * perLayerNum + (curLayer - 1) * (curLayer - 2) * perLayerNum / 2);

                //当前层可供兑换的 BTC 额度
                BigInteger curLayerBtcLines = curLayerBalance / curLayer;

                if (value >= curLayerBtcLines)
                {
                    amount = curLayerBalance;

                    //未兑换的 BTC
                    BigInteger remainingBtc = value - curLayerBtcLines;
                    BigInteger n = remainingBtc / perLayerNum;
                    for (int i = 1; i <= n; i++)
                    {
                        amount += perLayerNum * (curLayer - i);
                    }

                    //整除 10 剩余部分
                    amount += (remainingBtc - n * perLayerNum) * (curLayer - n - 1);
                    newLayer = curLayer - n - 1;
                }
                else
                {
                    amount = curLayer * value;
                }

                Runtime.Notify(0, amount);

                success = NativeAsset.Call("TransferApp", bcpId, contract, address, amount);

                if (success)
                {
                    Storage.Put(Context(), "curLayer", newLayer);
                    EmitResonance(address, assetId, value, amount);
                }

                else
                {
                    throw new Exception("Invalid available balance!");
                }

            }

            return false;
        }

        private static bool Withdrawal(byte[] address, byte[] assetId, BigInteger amount)
        {
            if (address.Length != 20) return false;

            // Check that the contract is safe
            if (!GetIsWhitelisted(assetId)) return false;

            bool success = false;
            byte[] from = ExecutionEngine.ExecutingScriptHash;

            var args = new object[] { from, address, amount };
            var contract = (deleContract)assetId.ToDelegate();
            success = (bool)contract("transferApp", args);

            if (success)
            {
                EmitWithdrawn(address, assetId, amount);
                return true;
            }
            else
                throw new Exception("Failed to withdrawal transfer");
        }

        private static bool GetIsWhitelisted(byte[] assetID)
        {
            if (assetID.Length != 20) return false;
            if (Storage.Get(Context(), WhitelistKey(assetID)).AsBigInteger() == 1)
                return true;
            return false;
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

        private static bool SetState(BigInteger setValue)
        {
            if (setValue == 0)
                Storage.Put(Context(), "state", Active);
            if (setValue == 1)
                Storage.Put(Context(), "state", Inactive);
            if (setValue == 2)
                Storage.Put(Context(), "state", AllStop);
            Runtime.Notify("setState", setValue);
            return true;
        }

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] WhitelistKey(byte[] assetId) => "whiteList".AsByteArray().Concat(assetId);

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

        public static object TransferApp(byte[] assetid, byte[] to, BigInteger amount)
        {
            object[] _p = new object[3] { ExecutionEngine.ExecutingScriptHash, to, amount };
            deleContract call = (deleContract)assetid.ToDelegate();
            return call("transfer_app", _p);
        }               

        public static void SetTxUsed(byte[] txid)
        {
            StorageMap txInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
            txInfoMap.Put(txid, 1);
        }
    }
}
