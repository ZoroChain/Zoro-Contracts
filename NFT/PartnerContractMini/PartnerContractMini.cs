using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

namespace NFT_Token
{
    /// <summary>
    /// BlaCat合伙人证书 精简版、只保留基本接口，配置和参数都从外部传入
    /// </summary>
    public class NFTContract : SmartContract
    {
        //超级管理员、只用来设定操作管理员和合约状态
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AM5ho5nEodQiai1mCTFDV3YUNYApCorMCX");

        [DisplayName("buy")]
        public static event Action<byte[], int, BigInteger, Map<byte[], int>> Bought;//(byte[] owner, int num, int Value, Map Nfts);

        [DisplayName("exchange")]
        public static event Action<byte[], byte[], byte[]> Exchanged;//(byte[] from, byte[] to, byte[] tokenId);

        [DisplayName("upgrade")]
        public static event Action<byte[], byte[], BigInteger, BigInteger> Upgraded;//(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank);

        [DisplayName("addpoint")]
        public static event Action<byte[], byte[], BigInteger> AddPointed; //(tokenID, address, point)

        [DisplayName("bind")]
        public static event Action<byte[], byte[]> Bound; //(address, tokenId)

        private static readonly byte[] Active = { };          //所有接口可用
        private static readonly byte[] Inactive = { 0x01 };   //只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        /// <summary>
        ///   NFT 合伙人证书合约
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        /// <param name="method">
        ///   接口名称
        /// </param>
        /// <param name="args">
        ///   参数列表
        /// </param>
        public static object Main(string method, object[] args)
        {
            var magicstr = "BlaCat Partner Certificate Token v3.0";
            if (Runtime.Trigger == TriggerType.Application)
            {
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
                    return true;
                }

                if (method == "setAdmin")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1) return false;
                    byte[] adminAddress = (byte[])args[0];
                    if (adminAddress.Length != 20) return false;
                    Storage.Put(Context(), "adminAddress", adminAddress);
                    return true;
                }

                //invoke
                if (method == "getState") return GetState();

                // allstop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                if (method == "getBindNft") return GetTokenIdByAddress((byte[])args[0]);
                if (method == "getNftInfoById") return GetNftByTokenId((byte[])args[0]);
                if (method == "getTxInfo") return GetExInfoByTxid((byte[])args[0]);
                if (method == "getGatherAddress") return Storage.Get(Context(), "gatherAddress");
                if (method == "getUserNfts") return GetUserNfts((byte[])args[0]);

                // 以下接口只有 Active 时可用
                if (GetState() != Active) return false;

                byte[] admin = Storage.Get(Context(), "adminAddress");
                if (!Runtime.CheckWitness(admin)) return false;

                //设置参数
                if (method == "setGatherAddress")
                {
                    byte[] gatherAdress = (byte[])args[0];
                    Storage.Put(Context(), "gatherAddress", gatherAdress);
                    return true;
                }

                //第一本创世证书发行
                if (method == "deploy") //(address)
                {
                    if (args.Length != 1) return false;
                    return DeployFirstNft((byte[])args[0]);
                }

                //购买
                if (method == "buy") //(assetId, txid, count, inviterTokenId, receivableValue, oneLevelInviterPoint, twoLevelInviterPoint, threeLevelInviterPoint)
                {
                    if (args.Length != 8) return false;
                    return BuyNewNft((byte[])args[0], (byte[])args[1], (int)args[2], (byte[])args[3], (BigInteger)args[4], (BigInteger)args[5], (BigInteger)args[6], (BigInteger)args[7]);
                }

                //绑定
                if (method == "bind") //(address, tokenId)
                {
                    if (args.Length != 2) return false;
                    return BindNft((byte[])args[0], (byte[])args[1]);
                }

                //升级
                if (method == "upgrade") //(assetId, txid, tokenId, receivableValue, needPoint)
                {
                    if (args.Length != 5) return false;
                    return UpgradeNft((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3], (BigInteger)args[4]);
                }

                //加分
                if (method == "addPoint") //(tokenId, pointValue)
                {
                    if (args.Length != 2) return false;
                    var nftInfo = GetNftByTokenId((byte[])args[0]);
                    if (nftInfo.Owner.Length != 20) return false;
                    return AddPoint(nftInfo, (BigInteger)args[1]);
                }

                //交易
                if (method == "exchange") //(byte[] from, byte[] to, byte[] tokenId)
                {
                    if (args.Length != 3) return false;
                    return ExchangeNft((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                }

                //降级
                if (method == "reduceGrade") //(tokenId)
                {
                    if (args.Length != 1) return false;
                    return ReduceGrade((byte[])args[0]);
                }

            }

            return false;
        }

        private static bool BuyNewNft(byte[] assetId, byte[] txid, int count, byte[] inviterTokenId, BigInteger receivableValue, BigInteger oneLevelInviterPoint, BigInteger twoLevelInviterPoint, BigInteger threeLevelInviterPoint)
        {
            if (assetId.Length != 20 || txid.Length != 32 || inviterTokenId.Length != 32) return false;
            if (count < 1 || receivableValue < 1) return false;

            byte[] v = Storage.Get(Storage.CurrentContext, BctTxidUsedKey(txid));
            if (v.Length != 0) return false;

            //获取邀请者证书信息
            var inviterNftInfo = GetNftByTokenId(inviterTokenId);
            if (inviterNftInfo.Owner.Length != 20) return false;

            byte[] gatherAddress = Storage.Get(Context(), "gatherAddress");

            //获取 bct 转账信息
            TransferLog tx = NativeAsset.GetTransferLog(assetId, txid);

            //钱没给够或收款地址不对 false
            if (tx.From.Length == 0 || tx.To != gatherAddress || (BigInteger)tx.Value < receivableValue) return false;

            Map<byte[], int> allNftsMap = GetUserNfts(tx.From);
            Map<byte[], int> newNftsMap = new Map<byte[], int>();

            for (int i = 1; i <= count; i++)
            {
                NFTInfo nftInfo = CreateNft(tx.From, inviterTokenId, i);

                SaveNftInfo(nftInfo);

                allNftsMap[nftInfo.TokenId] = 1;
                newNftsMap[nftInfo.TokenId] = 1;
            }

            Storage.Put(Context(), UserNftMapKey(tx.From), allNftsMap.Serialize());

            //上线加分
            AddPoint(inviterNftInfo, oneLevelInviterPoint);

            //二级上线
            var twoLevelInviterNftInfo = GetNftByTokenId(inviterNftInfo.InviterTokenId);
            if (twoLevelInviterNftInfo.Owner.Length == 20)
            {
                AddPoint(twoLevelInviterNftInfo, twoLevelInviterPoint);
                //三级上线
                var threeLevelInviterNftInfo = GetNftByTokenId(twoLevelInviterNftInfo.InviterTokenId);
                if (threeLevelInviterNftInfo.Owner.Length == 20)
                    AddPoint(threeLevelInviterNftInfo, threeLevelInviterPoint);
            }

            //notify
            Bought(tx.From, count, (BigInteger)tx.Value, newNftsMap);

            SetTxUsed(txid);

            return true;
        }

        private static bool BindNft(byte[] address, byte[] tokenId)
        {
            if (address.Length != 20 || tokenId.Length != 32) return false;

            NFTInfo nftInfo = GetNftByTokenId(tokenId);

            var userTokenId = GetTokenIdByAddress(address);

            //重复绑定
            if (userTokenId == nftInfo.TokenId) return true;

            //不是自己的证书不能绑定
            if (nftInfo.Owner != address) return false;

            Storage.Put(Context(), BindNftKey(address), nftInfo.TokenId);

            //notify
            Bound(address, tokenId);

            return true;
        }

        private static bool UpgradeNft(byte[] assetId, byte[] txid, byte[] tokenId, BigInteger receivableValue, BigInteger needPoint)
        {
            if (assetId.Length != 20 || txid.Length != 32 || tokenId.Length != 32) return false;

            byte[] v = Storage.Get(Storage.CurrentContext, BctTxidUsedKey(txid));
            if (v.Length != 0) return false;

            var nftInfo = GetNftByTokenId(tokenId);

            byte[] gatherAddress = Storage.Get(Context(), "gatherAddress");
            //获取 bct 转账信息
            TransferLog tx = NativeAsset.GetTransferLog(assetId, txid);

            if (tx.To != gatherAddress || (BigInteger)tx.Value < receivableValue) return false;
            if (nftInfo.Owner != tx.From) return false;

            if (nftInfo.AvailablePoint < needPoint) return false;
            //升级
            nftInfo.Grade += 1;
            //扣除消耗贡献值
            nftInfo.AvailablePoint -= needPoint;

            SaveNftInfo(nftInfo);
            SetTxUsed(txid);

            //notify
            Upgraded(tokenId, nftInfo.Owner, nftInfo.Grade - 1, nftInfo.Grade);
            AddPointed(tokenId, nftInfo.Owner, 0 - needPoint);

            return true;
        }

        private static bool ReduceGrade(byte[] tokeId)
        {
            if (tokeId.Length != 32) return false;
            var nftInfo = GetNftByTokenId(tokeId);
            if (nftInfo.Owner.Length != 20) return false;
            if (nftInfo.Grade < 2) return false;
            nftInfo.Grade -= 1;
            SaveNftInfo(nftInfo);

            //(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank)
            Upgraded(tokeId, nftInfo.Owner, nftInfo.Grade + 1, nftInfo.Grade);
            return true;
        }

        private static bool DeployFirstNft(byte[] address)
        {
            if (address.Length != 20) return false;

            //判断初始发行是否已完成
            byte[] deploy_data = Storage.Get(Context(), "initDeploy");
            if (deploy_data.Length != 0) return false;

            byte[] userNftsBytes = Storage.Get(Context(), UserNftMapKey(address));
            Map<byte[], int> nftsMap = new Map<byte[], int>();

            if (userNftsBytes.Length > 0)
                nftsMap = userNftsBytes.Deserialize() as Map<byte[], int>;

            //构建一个证书
            NFTInfo newNftInfo = CreateNft(address, new byte[] { }, 1);

            nftsMap[newNftInfo.TokenId] = 1;

            Storage.Put(Context(), UserNftMapKey(address), nftsMap.Serialize());

            //保存nft信息
            SaveNftInfo(newNftInfo);

            //notify
            Bought(address, 1, 0, nftsMap);

            Storage.Put(Context(), "initDeploy", 1);

            return true;
        }

        private static bool ExchangeNft(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from.Length != 20 || to.Length != 20 || tokenId.Length != 32) return false;

            if (from == to) return true;

            var fromNftInfo = GetNftByTokenId(tokenId);

            //from 没有证书、false
            if (fromNftInfo.Owner != from) return false;

            Map<byte[], int> fromNftsMap = GetUserNfts(from);

            Map<byte[], int> toNftsMap = GetUserNfts(to);

            fromNftsMap.Remove(fromNftInfo.TokenId);

            toNftsMap[fromNftInfo.TokenId] = 1;

            Storage.Put(Context(), UserNftMapKey(from), fromNftsMap.Serialize());
            Storage.Put(Context(), UserNftMapKey(to), toNftsMap.Serialize());

            //更换所有者
            fromNftInfo.Owner = to;

            SaveNftInfo(fromNftInfo);

            var fromTokenId = GetTokenIdByAddress(from);

            //如果把绑定的卖了、就删除绑定
            if (fromTokenId == tokenId)
                Storage.Delete(Context(), BindNftKey(from));

            SaveExInfo(from, to, tokenId);
            //notify
            Exchanged(from, to, tokenId);

            return true;
        }

        private static bool AddPoint(NFTInfo nftInfo, BigInteger pointValue)
        {
            if (pointValue == 0) return true;

            nftInfo.AvailablePoint += pointValue;
            if (pointValue > 0)
                nftInfo.AllPoint += pointValue;

            SaveNftInfo(nftInfo);
            //notify
            AddPointed(nftInfo.TokenId, nftInfo.Owner, pointValue);
            return true;
        }

        private static Map<byte[], int> GetUserNfts(byte[] address)
        {
            byte[] userNftsBytes = Storage.Get(Context(), UserNftMapKey(address));
            Map<byte[], int> nftsMap = new Map<byte[], int>();
            if (userNftsBytes.Length > 0)
                nftsMap = userNftsBytes.Deserialize() as Map<byte[], int>;
            return nftsMap;
        }

        private static byte[] GetTokenIdByAddress(byte[] address)
        {
            var key = BindNftKey(address);
            return Storage.Get(Context(), key);
        }

        public static NFTInfo CreateNft(byte[] owner, byte[] inviterTokenId, int num)
        {
            BigInteger nonce = num;
            byte[] tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash.Concat(nonce.AsByteArray()));

            NFTInfo nftInfo = new NFTInfo()
            {
                TokenId= tokenId,
                AllPoint = 0,
                AvailablePoint = 0,
                Owner = owner,
                Grade = 1,
                InviterTokenId = inviterTokenId
            };
            return nftInfo;
        }

        public static void SaveNftInfo(NFTInfo nftInfo)
        {
            var key = NftInfoKey(nftInfo.TokenId);
            byte[] nftInfoBytes = Helper.Serialize(nftInfo);
            Storage.Put(Context(), key, nftInfoBytes);
        }

        public static void SaveExInfo(byte[] from, byte[] to, byte[] tokenId)
        {
            ExchangeInfo info = new ExchangeInfo();
            info.From = from;
            info.To = to;
            info.TokenId = tokenId;
            byte[] exInfo = Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            var key = TxInfoKey(txid);
            Storage.Put(Context(), key, exInfo);
        }

        public static NFTInfo GetNftByTokenId(byte[] tokenId)
        {
            var key = NftInfoKey(tokenId);
            byte[] data = Storage.Get(Context(), key);
            if (data.Length == 0)
                return new NFTInfo();
            return data.Deserialize() as NFTInfo;
        }

        public static ExchangeInfo GetExInfoByTxid(byte[] txid)
        {
            var key = TxInfoKey(txid);
            var data = Storage.Get(Context(), key);
            if (data.Length == 0)
                return null;
            return data.Deserialize() as ExchangeInfo;
        }

        public static void SetTxUsed(byte[] txid)
        {
            var key = BctTxidUsedKey(txid);
            Storage.Put(Context(), key, 1);
        }

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] UserNftMapKey(byte[] address) => new byte[] { 0x10 }.Concat(address);
        private static byte[] BindNftKey(byte[] address) => new byte[] { 0x11 }.Concat(address);
        private static byte[] NftInfoKey(byte[] tokenId) => new byte[] { 0x12 }.Concat(tokenId);
        private static byte[] TxInfoKey(byte[] txid) => new byte[] { 0x13 }.Concat(txid);
        private static byte[] BctTxidUsedKey(byte[] txid) => new byte[] { 0x14 }.Concat(txid);

    }

    public class ExchangeInfo
    {
        public byte[] From;
        public byte[] To;
        public byte[] TokenId;
    }

    //证书信息
    public class NFTInfo
    {
        public byte[] TokenId; //tokenid 证书ID
        public byte[] Owner; //所有者 address
        public BigInteger Grade; //等级
        public BigInteger AllPoint; //累积贡献值
        public BigInteger AvailablePoint; //可用贡献值
        public byte[] InviterTokenId; //邀请者证书ID
    }

}
