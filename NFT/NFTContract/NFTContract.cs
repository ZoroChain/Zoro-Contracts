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

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], byte[]> Transferred; //(byte[] from , byte[] to, byte[] TokenId)

        //super admin address
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AcQLYjGbQU2bEQ8RKFXUcf8XvromfUQodq");

        private static readonly byte[] Active = { };          //all active
        private static readonly byte[] Inactive = { 0x01 };   //only invoke
        private static readonly byte[] AllStop = { 0x02 };    //all stop

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
                if (method == "symbol") return "NFT Test";
                if (method == "totalSupply") return Storage.Get(Context(), totalSupplyKey).AsBigInteger();
                if (method == "getTxInfo") return GetTxInfo((byte[])args[0]);
                if (method == "allowance") return Storage.Get(Context(), AllowanceKey((byte[])args[0]));
                if (method == "tokenRoData") return Storage.Get(Context(), tokenRoDataKey((byte[])args[0]));
                if (method == "tokenRwData") return Storage.Get(Context(), tokenRoDataKey((byte[])args[0]));
                if (method == "tokenUri") return Storage.Get(Context(), tokenRoDataKey((byte[])args[0]));
                if (method == "ownerOf") return Storage.Get(Context(), tokenOwnerKey((byte[])args[0]));
                if (method == "balanceOf") return Storage.Get(Context(), balanceKey((byte[])args[0]));

                if (method == "mintToken") //(address)
                {
                    if (args.Length != 4) return false;
                    var owner = (byte[])args[0];
                    var RoData = (byte[])args[1];
                    var RwData = (byte[])args[2];
                    var Uri = (string)args[3];

                    if (!Runtime.CheckWitness(superAdmin)) return false;

                    if (owner.Length != 20 || RoData.Length == 0) return false;
                    if (RoData.Length > 2048) return false;

                    return MintNFT(owner, RoData, RwData, Uri);
                }

                if (method == "modifyRwData")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 2) return false;
                    var tokenId = (byte[])args[0];
                    var RwData = (byte[])args[1];
                    var token = Storage.Get(Context(), tokenRwDateKey(tokenId));
                    if (token.Length == 0) return false;
                    Storage.Put(Context(), tokenRwDateKey(tokenId), RwData);
                    return true;
                }

                if (method == "modifyUri")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 2) return false;
                    var tokenId = (byte[])args[0];
                    string Uri = (string)args[1];
                    var token = Storage.Get(Context(), tokenUriKey(tokenId));
                    if (token.Length == 0) return false;
                    Storage.Put(Context(), tokenUriKey(tokenId), Uri);
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

            var owner = Storage.Get(Context(), tokenOwnerKey(tokenId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            Storage.Put(Context(), tokenOwnerKey(tokenId), to);

            var formBalance = Storage.Get(Context(), balanceKey(from)).AsBigInteger();
            Storage.Put(Context(), balanceKey(from), formBalance - 1);
            var toBalance = Storage.Get(Context(), balanceKey(to)).AsBigInteger();
            Storage.Put(Context(), balanceKey(to), toBalance + 1);

            SetTxInfo(from, to, tokenId);

            //notify
            Transferred(from, to, tokenId);
            return true;
        }

        private static bool TransferFrom(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), tokenOwnerKey(tokenId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            var allowanceSpend = Storage.Get(Context(), AllowanceKey(tokenId));
            if (allowanceSpend != from.Concat(to)) return false;

            Storage.Put(Context(), tokenOwnerKey(tokenId), to);

            var formBalance = Storage.Get(Context(), balanceKey(from)).AsBigInteger();
            Storage.Put(Context(), balanceKey(from), formBalance - 1);
            var toBalance = Storage.Get(Context(), balanceKey(to)).AsBigInteger();
            Storage.Put(Context(), balanceKey(to), toBalance + 1);

            SetTxInfo(from, to, tokenId);

            //notify
            Transferred(from, to, tokenId);
            return true;
        }

        private static bool Approve(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), tokenOwnerKey(tokenId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            Storage.Put(Context(), AllowanceKey(tokenId), from.Concat(to));

            //notify
            Approved(from, to, tokenId);
            return true;
        }

        private static bool MintNFT(byte[] owner, byte[] RoData, byte[] RwData, string Uri)
        {           
            var nftId = Hash256(RoData.Concat(RwData));

            var addr = Storage.Get(Context(), tokenOwnerKey(nftId));
            if (addr.Length > 0) return false;

            Storage.Put(Context(), tokenOwnerKey(nftId), owner);
            Storage.Put(Context(), tokenRoDataKey(nftId), RoData);
            Storage.Put(Context(), tokenRwDateKey(nftId), RwData);
            Storage.Put(Context(), tokenUriKey(nftId), Uri);

            var balance = Storage.Get(Context(), balanceKey(owner)).AsBigInteger();
            Storage.Put(Context(), balanceKey(owner), balance + 1);
            BigInteger totalSupply = Storage.Get(Context(), totalSupplyKey).AsBigInteger();
            Storage.Put(Context(), totalSupplyKey, totalSupply + 1);

            //notify
            Transferred(null, owner, nftId);
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
        private static byte[] tokenOwnerKey(byte[] tokenId) => "tokenOwner".AsByteArray().Concat(tokenId);
        private static byte[] balanceKey(byte[] address) => "balance".AsByteArray().Concat(address);
        private static byte[] tokenRoDataKey(byte[] tokenId) => "tokenRoData".AsByteArray().Concat(tokenId);
        private static byte[] tokenRwDateKey(byte[] tokenId) => "tokenRwDate".AsByteArray().Concat(tokenId);
        private static byte[] tokenUriKey(byte[] tokenId) => "tokenUri".AsByteArray().Concat(tokenId);
        private static byte[] TxidKey(byte[] txid) => new byte[] { 0x13 }.Concat(txid);
        private static byte[] AllowanceKey(byte[] tokenId) => new byte[] { 0x14 }.Concat(tokenId);
        private static string totalSupplyKey = "totalSupply";
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public byte[] tokenId;
    }

    public class TokenData
    {
        public byte[] tokenRoData;
        public byte[] tokenRwData;
        public byte[] tokenUri;
    }

}