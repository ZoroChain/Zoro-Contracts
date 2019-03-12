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

        private static readonly string totalSupplyKey = "totalSupply";

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
                if (method == "getNftData") return Storage.Get(Context(), NftDataKey((byte[])args[0]));
                if (method == "getNftOwner") return Storage.Get(Context(), NftOwnerKey((byte[])args[0]));
                if (method == "balanceOf") return Storage.Get(Context(), OwnerNftCountKey((byte[])args[0]));

                if (method == "mintNFT") //(address)
                {
                    if (args.Length != 2) return false;
                    var address = (byte[])args[0];
                    var data = (byte[])args[1];
                    if (address.Length != 20 || data.Length == 0) return false;
                    if (Runtime.CheckWitness(superAdmin) == false) return false;
                    return MintNFT(address, data);
                }

                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] nftId = (byte[])args[2];
                    if (from.Length != 20 || to.Length != 20) return false;

                    if (!Runtime.CheckWitness(from)) return false;

                    if (entryscript != callscript) return false;

                    return Transfer(from, to, nftId);
                }

                if (method == "approve")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] nftId = (byte[])args[2];

                    if (from.Length != 20 || to.Length != 20) return false;
                    if (!Runtime.CheckWitness(from)) return false;
                    if (entryscript != callscript) return false;

                    return Approve(from, to, nftId);
                }

                if (method == "transferFrom")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] nftId = (byte[])args[2];

                    if (from.Length != 20 || to.Length != 20) return false;
                    return TransferFrom(from, to, nftId);
                }

                if (method == "transferApp")
                {
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] nftId = (byte[])args[2];
                    if (from.Length != 20 || to.Length != 20) return false;

                    if (from != callscript) return false;
                    return Transfer(from, to, nftId);
                }
            }

            return false;
        }

        private static bool Transfer(byte[] from, byte[] to, byte[] nftId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), NftOwnerKey(nftId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            Storage.Put(Context(), NftOwnerKey(nftId), to);

            var formBalance = Storage.Get(Context(), OwnerNftCountKey(from)).AsBigInteger();
            Storage.Put(Context(), OwnerNftCountKey(from), formBalance - 1);
            var toBalance = Storage.Get(Context(), OwnerNftCountKey(to)).AsBigInteger();
            Storage.Put(Context(), OwnerNftCountKey(to), toBalance + 1);

            SetTxInfo(from, to, nftId);

            //notify
            Transferred(from, to, nftId);
            return true;
        }

        private static bool TransferFrom(byte[] from, byte[] to, byte[] nftId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), NftOwnerKey(nftId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            var allowanceSpend = Storage.Get(Context(), AllowanceKey(nftId));
            if (allowanceSpend != from.Concat(to)) return false;

            Storage.Put(Context(), NftOwnerKey(nftId), to);

            var formBalance = Storage.Get(Context(), OwnerNftCountKey(from)).AsBigInteger();
            Storage.Put(Context(), OwnerNftCountKey(from), formBalance - 1);
            var toBalance = Storage.Get(Context(), OwnerNftCountKey(to)).AsBigInteger();
            Storage.Put(Context(), OwnerNftCountKey(to), toBalance + 1);

            SetTxInfo(from, to, nftId);

            //notify
            Transferred(from, to, nftId);
            return true;
        }

        private static bool Approve(byte[] from, byte[] to, byte[] nftId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), NftOwnerKey(nftId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            Storage.Put(Context(), AllowanceKey(nftId), from.Concat(to));

            //notify
            Approved(from, to, nftId);
            return true;
        }

        private static bool MintNFT(byte[] address, byte[] data)
        {            
            if (data.Length > 2048) return false;
            var nftId = Hash256(data);

            var owner = Storage.Get(Context(), NftOwnerKey(nftId));
            if (owner.Length > 0) return false;

            Storage.Put(Context(), NftOwnerKey(nftId), address);
            Storage.Put(Context(), NftDataKey(nftId), data);

            var balance = Storage.Get(Context(), OwnerNftCountKey(address)).AsBigInteger();
            Storage.Put(Context(), OwnerNftCountKey(address), balance + 1);
            BigInteger totalSupply = Storage.Get(Context(), totalSupplyKey).AsBigInteger();
            Storage.Put(Context(), totalSupplyKey, totalSupply + 1);

            //notify
            Transferred(null, address, nftId);
            return true;
        }

        private static void SetTxInfo(byte[] from, byte[] to, byte[] nftId)
        {
            TransferInfo info = new TransferInfo();
            info.nftId = nftId;
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
        private static byte[] NftOwnerKey(byte[] nftId) => new byte[] { 0x10 }.Concat(nftId);
        private static byte[] OwnerNftCountKey(byte[] address) => new byte[] { 0x11 }.Concat(address);
        private static byte[] NftDataKey(byte[] nftId) => new byte[] { 0x12 }.Concat(nftId);
        private static byte[] TxidKey(byte[] txid) => new byte[] { 0x13 }.Concat(txid);
        private static byte[] AllowanceKey(byte[] nftId) => new byte[] { 0x14 }.Concat(nftId);
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public byte[] nftId;
    }

}