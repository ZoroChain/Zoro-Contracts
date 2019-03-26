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
    public class NFTContract : SmartContract
    {
        [DisplayName("approve")]
        public static event Action<byte[], byte[], byte[]> Approved;//(byte[] owner, byte[] to, byte[] TokenId);

        [DisplayName("mintToken")]
        public static event Action<byte[], byte[]> MintedToken;//(byte[] owner, byte[] TokenId);

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], byte[]> Transferred; //(byte[] from , byte[] to, byte[] TokenId)

        //super admin address
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AcQLYjGbQU2bEQ8RKFXUcf8XvromfUQodq");

        /// <summary>
        ///   NFT Contract Template
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        public static object Main(string method, object[] args)
        {
            var magicstr = "NFT Contract Template v0.1";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var entryscript = ExecutionEngine.EntryScriptHash;
                var callscript = ExecutionEngine.CallingScriptHash;

                //invoke
                if (method == "name") return "NFT";
                if (method == "symbol") return "NFT";
                if (method == "totalSupply") return Storage.Get(Context(), TotalSupplyKey()).AsBigInteger();
                if (method == "getTxInfo") return GetTxInfo((byte[])args[0]);
                if (method == "allowance") return Storage.Get(Context(), AllowanceKey((byte[])args[0]));                

                if (method == "ownerOf") return Storage.Get(Context(), TokenOwnerKey((byte[])args[0]));
                if (method == "balanceOf") return Storage.Get(Context(), BalanceKey((byte[])args[0]));
                if (method == "uri") return Storage.Get(Context(), TokenUriKey((byte[])args[0]));
                if (method == "properties") return Storage.Get(Context(), PropertiesKey((byte[])args[0]));

                if (method == "tokenRwData") return Storage.Get(Context(), TokenRwDataKey((byte[])args[0]));

                if (method == "mintToken") //(address)
                {
                    if (args.Length != 3) return false;
                    var owner = (byte[])args[0];
                    var uri = (byte[])args[1]; 
                    var properties = (byte[])args[2];

                    if (!Runtime.CheckWitness(superAdmin)) return false;

                    if (owner.Length != 20 || uri.Length == 0) return false;
                    if (properties.Length > 2048) return false;

                    return MintNFT(owner, uri, properties);
                }

                if (method == "modifyUri")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 2) return false;
                    var tokenId = (byte[])args[0];
                    var uri = (byte[])args[1];
                    var token = Storage.Get(Context(), TokenUriKey(tokenId));
                    if (token.Length == 0) return false;
                    Storage.Put(Context(), TokenUriKey(tokenId), uri);
                    return true;
                }

                if (method == "modifyRwData")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 2) return false;
                    var tokenId = (byte[])args[0];
                    var RwData = (byte[])args[1];
                    var token = Storage.Get(Context(), TokenRwDataKey(tokenId));
                    if (token.Length == 0) return false;
                    Storage.Put(Context(), TokenRwDataKey(tokenId), RwData);
                    return true;
                }

                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];
                    if (from.Length != 20 || to.Length != 20) return false;

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
                    return TransferFrom(from, to, tokenId);
                }

                if (method == "transferApp")
                {
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];
                    if (from.Length != 20 || to.Length != 20) return false;

                    if (from != callscript) return false;
                    return Transfer(from, to, tokenId);
                }
            }

            return false;
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

        private static bool MintNFT(byte[] owner, byte[] uri, byte[] properties)
        {
            var tokenId = Hash256(properties.Concat(uri));
            var addr = Storage.Get(Context(), TokenOwnerKey(tokenId));
            if (addr.Length > 0) return false;

            Storage.Put(Context(), TokenUriKey(tokenId), uri);
            Storage.Put(Context(), PropertiesKey(tokenId), properties);
            Storage.Put(Context(), TokenOwnerKey(tokenId), owner);

            var keyTotalSupply = TotalSupplyKey();           
            BigInteger totalSupply = Storage.Get(Context(), keyTotalSupply).AsBigInteger();
            Storage.Put(Context(), keyTotalSupply, totalSupply + 1);

            var keyBalance = BalanceKey(owner);
            BigInteger balance = Storage.Get(Context(), keyBalance).AsBigInteger();
            Storage.Put(Context(), keyBalance, balance + 1);
      
            //notify
            MintedToken(owner, tokenId);
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
        private static byte[] TokenRoDataKey(byte[] tokenId) => new byte[] { 0x12 }.Concat(tokenId);
        private static byte[] TokenRwDataKey(byte[] tokenId) => new byte[] { 0x13 }.Concat(tokenId);
        private static byte[] TokenUriKey(byte[] tokenId) => new byte[] { 0x14 }.Concat(tokenId);
        private static byte[] TxidKey(byte[] txid) => new byte[] { 0x15 }.Concat(txid);
        private static byte[] AllowanceKey(byte[] tokenId) => new byte[] { 0x16 }.Concat(tokenId);
        private static byte[] PropertiesKey(byte[] tokenId) => new byte[] { 0x17 }.Concat(tokenId);
        private static string TotalSupplyKey() => "totalSupply";
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public byte[] tokenId;
    }

}