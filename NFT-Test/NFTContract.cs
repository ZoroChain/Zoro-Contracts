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

                //set contract state        
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
                    return true;
                }

                if (method == "getState") return GetState();

                //all stop
                if (GetState() == AllStop) return false;

                //invoke
                if (method == "totalSupply") return Storage.Get(Context(), totalSupplyKey).AsBigInteger();
                if (method == "name") return "NFT";
                if (method == "symbol") return "NFT Test";
                if (method == "getTxInfo") return GetTxInfo((byte[])args[0]);
                if (method == "allowance") return Storage.Get(Context(), AllowanceKey((byte[])args[0]));
                if (method == "getNftData") return Storage.Get(Context(), NftInfoKey((byte[])args[0]));
                if (method == "getNftOwner") return Storage.Get(Context(), NftIdKey((byte[])args[0]));

                if (GetState() != Active) return false;

                if (method == "mintNFT") //(address)
                {
                    if (args.Length != 2) return false;
                    var address = (byte[])args[0];
                    var data = (byte[])args[1];
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

            var owner = Storage.Get(Context(), NftIdKey(nftId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            Storage.Put(Context(), NftIdKey(nftId), to);

            SetTxInfo(from, to, nftId);

            //notify
            Transferred(from, to, nftId);
            return true;
        }

        private static bool TransferFrom(byte[] from, byte[] to, byte[] nftId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), NftIdKey(nftId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            var allowanceSpend = Storage.Get(Context(), AllowanceKey(nftId));
            if (allowanceSpend != from.Concat(to)) return false;

            Storage.Put(Context(), NftIdKey(nftId), to);

            SetTxInfo(from, to, nftId);

            //notify
            Transferred(from, to, nftId);
            return true;
        }

        private static bool Approve(byte[] from, byte[] to, byte[] nftId)
        {
            if (from == to) return true;

            var owner = Storage.Get(Context(), NftIdKey(nftId));
            if (owner.AsBigInteger() != from.AsBigInteger()) return false;

            Storage.Put(Context(), AllowanceKey(nftId), from.Concat(to));                       

            //notify
            Approved(from, to, nftId);
            return true;
        }

        private static bool MintNFT(byte[] address, byte[] data)
        {
            if (Runtime.CheckWitness(superAdmin) == false) return false;
            if (data.Length > 2048) return false;

            var nftId = Sha256(data);
            var owner = Storage.Get(Context(), NftIdKey(nftId));
            if (owner.Length > 0) return false;

            BigInteger totalSupply = Storage.Get(Context(), totalSupplyKey).AsBigInteger();

            Storage.Put(Context(), NftIdKey(nftId), address);
            Storage.Put(Context(), NftInfoKey(nftId), data);
            Storage.Put(Context(), totalSupplyKey, totalSupply + 1);

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

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static StorageContext Context() => Storage.CurrentContext;
        private static byte[] NftIdKey(byte[] nftId) => new byte[] { 0x11 }.Concat(nftId);
        private static byte[] NftInfoKey(byte[] nftId) => new byte[] { 0x12 }.Concat(nftId);
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
