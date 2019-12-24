using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

namespace NFTContract
{
    /// <summary>
    /// Non-Fungible Token Smart Contract Template
    /// </summary>
    public class ShNftContract : SmartContract
    {
        [DisplayName("approve")]
        public static event Action<byte[], byte[], byte[]> Approved;//(byte[] owner, byte[] to, byte[] TokenId); 

        [DisplayName("mintToken")]
        public static event Action<byte[], byte[], byte[]> MintedToken;//(byte[] to, byte[] tokenId, byte[] properties);

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], byte[]> Transferred; //(byte[] from , byte[] to, byte[] TokenId)

        [DisplayName("modifyProperties")]
        public static event Action<byte[], byte[]> ModifyProperties; //(byte[] tokenId, byte[] newProperties)       

        [DisplayName("freeze")]
        public static event Action<byte[]> Frozen; //(tokenId)

        [DisplayName("unfreeze")]
        public static event Action<byte[]> UnFrozen; //(tokenId)

        //super admin address
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AcQLYjGbQU2bEQ8RKFXUcf8XvromfUQodq");

        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        /// <summary>
        ///   NFT Contract Template
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        public static object Main(string method, object[] args)
        {
            var magicstr = "NFT Contract Template v0.3";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var entryscript = ExecutionEngine.EntryScriptHash;
                var callscript = ExecutionEngine.CallingScriptHash;

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
                    Runtime.Notify("setState", setValue);
                    return true;
                }

                //invoke
                if (method == "getState") return GetState();

                // stop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                //invoke
                if (method == "name") return "shgame nft";
                if (method == "symbol") return "SNFT";
                if (method == "supportedStandards") return "{\"NEP-10\"}";
                if (method == "totalSupply") return Storage.Get(Context(), TotalSupplyKey()).AsBigInteger();
                if (method == "getTxInfo") return GetTxInfo((byte[])args[0]);
                if (method == "allowance") return Storage.Get(Context(), AllowanceKey((byte[])args[0]));
                if (method == "ownerOf") return Storage.Get(Context(), TokenOwnerKey((byte[])args[0]));
                if (method == "balanceOf") return Storage.Get(Context(), BalanceKey((byte[])args[0]));
                if (method == "properties") return Storage.Get(Context(), PropertiesKey((byte[])args[0]));               
                if (method == "isFrozen") return Storage.Get(Context(), IsFreezeKey((byte[])args[0])).AsBigInteger();

                // 如果合约不是 Active 状态、后面接口不可用
                if (GetState() != Active) return false;

                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];
                    if (from.Length != 20 || to.Length != 20) return false;

                    if (IsFrozen(tokenId)) return false;

                    if (!Runtime.CheckWitness(from)) return false;
                    if (entryscript != callscript) return false;
                    return Transfer(from, to, tokenId);
                }

                if (method == "approve")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];

                    if (from.Length != 20 || to.Length != 20) return false;
                    if (IsFrozen(tokenId)) return false;

                    if (!Runtime.CheckWitness(from)) return false;
                    if (entryscript != callscript) return false;

                    return Approve(from, to, tokenId);
                }

                if (method == "transferFrom")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];

                    if (from.Length != 20 || to.Length != 20) return false;
                    if (IsFrozen(tokenId)) return false;

                    return TransferFrom(from, to, tokenId);
                }

                if (method == "transferApp")
                {
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];
                    if (from.Length != 20 || to.Length != 20) return false;
                    if (IsFrozen(tokenId)) return false;

                    if (from != callscript) return false;
                    return Transfer(from, to, tokenId);
                }

                if (method == "setOperator")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1) return false;
                    var addr = (byte[])args[0];
                    if (addr.Length != 20) return false;
                    Storage.Put(Context(), "operator", addr);
                    Runtime.Notify("setOperator", addr);
                    return true;
                }

                //需要操作管理员签名
                var operatorAddr = Storage.Get(Context(), "operator");
                if (!Runtime.CheckWitness(operatorAddr)) return false;

                if (method == "mintToken") //(address, properties)
                {
                    if (args.Length != 2) return false;
                    var owner = (byte[])args[0];
                    var properties = (byte[])args[1];                   

                    if (owner.Length != 20) return false;                

                    return MintNFT(owner, properties);
                }

                if (method == "freeze")
                {
                    if (args.Length != 1) return false;
                    return FreezeNft((byte[])args[0]);
                }

                if (method == "unfreeze")
                {
                    if (args.Length != 1) return false;
                    return UnFreezeNft((byte[])args[0]);
                }

                if (method == "modifyProperties")
                {
                    if (args.Length != 2) return false;
                    var tokenId = (byte[])args[0];
                    var properties = (byte[])args[1];

                    if (properties.Length > 2048) return false;
                    var addr = Storage.Get(Context(), TokenOwnerKey(tokenId));
                    if (addr.Length != 20) return false;

                    Storage.Put(Context(), PropertiesKey(tokenId), properties);
                    ModifyProperties(tokenId, properties);
                    return true;
                }                               
            }

            return false;
        }

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static bool IsFrozen(byte[] tokenId)
        {
            if (Storage.Get(Context(), IsFreezeKey(tokenId)).AsBigInteger() == 1)
                return true;
            else
                return false;
        }

        private static bool FreezeNft(byte[] tokenId)
        {
            Storage.Put(Context(), IsFreezeKey(tokenId), 1);
            Frozen(tokenId);
            return true;
        }

        private static bool UnFreezeNft(byte[] tokenId)
        {
            Storage.Put(Context(), IsFreezeKey(tokenId), 0);
            UnFrozen(tokenId);
            return true;
        }

        private static bool Transfer(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), TokenOwnerKey(tokenId));
            if (owner != from) return false;

            Storage.Put(Context(), TokenOwnerKey(tokenId), to);

            var formBalance = Storage.Get(Context(), BalanceKey(from)).AsBigInteger();
            Storage.Put(Context(), BalanceKey(from), formBalance - 1);
            var toBalance = Storage.Get(Context(), BalanceKey(to)).AsBigInteger();
            Storage.Put(Context(), BalanceKey(to), toBalance + 1);

            var allowanceKey = AllowanceKey(tokenId);
            Storage.Delete(Context(), allowanceKey);

            SetTxInfo(from, to, tokenId);

            //notify
            Transferred(from, to, tokenId);
            return true;
        }

        private static bool TransferFrom(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), TokenOwnerKey(tokenId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            var allowanceKey = AllowanceKey(tokenId);
            var allowanceSpend = Storage.Get(Context(), allowanceKey);
            if (allowanceSpend != from.Concat(to)) return false;

            Storage.Put(Context(), TokenOwnerKey(tokenId), to);
            Storage.Delete(Context(), allowanceKey);

            var formBalance = Storage.Get(Context(), BalanceKey(from)).AsBigInteger();
            Storage.Put(Context(), BalanceKey(from), formBalance - 1);
            var toBalance = Storage.Get(Context(), BalanceKey(to)).AsBigInteger();
            Storage.Put(Context(), BalanceKey(to), toBalance + 1);

            SetTxInfo(from, to, tokenId);

            //notify
            Transferred(from, to, tokenId);
            return true;
        }

        private static bool Approve(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), TokenOwnerKey(tokenId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            Storage.Put(Context(), AllowanceKey(tokenId), from.Concat(to));

            //notify
            Approved(from, to, tokenId);
            return true;
        }

        private static bool MintNFT(byte[] owner, byte[] properties)
        {
            var tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash.Concat(properties));
            var addr = Storage.Get(Context(), TokenOwnerKey(tokenId));
            if (addr.Length > 0) return false;

            Storage.Put(Context(), PropertiesKey(tokenId), properties);
            Storage.Put(Context(), TokenOwnerKey(tokenId), owner);                        

            var keyTotalSupply = TotalSupplyKey();
            BigInteger totalSupply = Storage.Get(Context(), keyTotalSupply).AsBigInteger();
            Storage.Put(Context(), keyTotalSupply, totalSupply + 1);

            var keyBalance = BalanceKey(owner);
            BigInteger balance = Storage.Get(Context(), keyBalance).AsBigInteger();
            Storage.Put(Context(), keyBalance, balance + 1);
            Storage.Put(Context(), IsFreezeKey(tokenId), 1);
            //notify
            MintedToken(owner, tokenId, properties);
            return true;
        }

        private static void SetTxInfo(byte[] from, byte[] to, byte[] tokenId)
        {
            TransferInfo info = new TransferInfo();
            info.tokenId = tokenId;
            info.from = from;
            info.to = to;
            byte[] txinfo = Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            Storage.Put(Context(), TxidKey(txid), txinfo);
        }

        private static TransferInfo GetTxInfo(byte[] txid)
        {
            byte[] keyTxid = TxidKey(txid);
            byte[] v = Storage.Get(Context(), keyTxid);
            if (v.Length == 0) return null;
            return Helper.Deserialize(v) as TransferInfo;
        }

        private static StorageContext Context() => Storage.CurrentContext;
        private static byte[] TokenOwnerKey(byte[] tokenId) => new byte[] { 0x10 }.Concat(tokenId);
        private static byte[] BalanceKey(byte[] owner) => new byte[] { 0x11 }.Concat(owner);        
        private static byte[] TxidKey(byte[] txid) => new byte[] { 0x15 }.Concat(txid);
        private static byte[] AllowanceKey(byte[] tokenId) => new byte[] { 0x16 }.Concat(tokenId);
        private static byte[] PropertiesKey(byte[] tokenId) => new byte[] { 0x17 }.Concat(tokenId);
        private static byte[] IsFreezeKey(byte[] tokenId) => new byte[] { 0x18 }.Concat(tokenId);
        private static string TotalSupplyKey() => "totalSupply";
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public byte[] tokenId;
    }

}