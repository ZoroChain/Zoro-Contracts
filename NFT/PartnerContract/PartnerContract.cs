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
    /// BlaCat合伙人证书
    /// </summary>
    public class NFTContract : SmartContract
    {        
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AGc2HfoP5w823frEnDo2j3cnaJNcMsS1iY");

        [DisplayName("mintToken")]
        public static event Action<byte[], byte[], int, BigInteger, Map<byte[], int>> Bought;//(byte[] owner, byte[] inviterTokenId, int num, int Value, Map Nfts TokenId);

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], byte[]> Transferred;//(byte[] from, byte[] to, byte[] tokenId);

        [DisplayName("approve")]
        public static event Action<byte[], byte[], byte[]> Approved;//(byte[] owner, byte[] to, byte[] TokenId);

        [DisplayName("upgrade")]
        public static event Action<byte[], byte[], BigInteger, BigInteger> Upgraded;//(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank);

        [DisplayName("addPoint")]
        public static event Action<byte[], byte[], BigInteger, byte[]> AddPointed; //(tokenID, address, point, type)

        [DisplayName("bind")]
        public static event Action<byte[], byte[]> Bound; //(address, tokenId)

        [DisplayName("activate")]
        public static event Action<byte[], byte[]> Activated; //(address, tokenId)

        [DisplayName("destroy")]
        public static event Action<byte[], byte[]> Destroyed; //(address, tokenId)

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
            var magicstr = "BlaCat Partner Certificate Token v3.1";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                var entryscript = ExecutionEngine.EntryScriptHash;

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
    
                if (method == "getState") return GetState();

                // allstop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                //invoke
                if (method == "name") return "Partner";
                if (method == "symbol") return "Partner";
                if (method == "totalSupply") return Storage.Get(Context(), "allNftCount").AsBigInteger();
                if (method == "getTxInfo") return GetExInfoByTxid((byte[])args[0]);
                if (method == "allowance") return Storage.Get(Context(), AllowanceKey((byte[])args[0]));
                if (method == "ownerOf") return GetOwnerByTokenId((byte[])args[0]);
                if (method == "balanceOf") return GetUserNftCount((byte[])args[0]);
                if (method == "properties") return GetNftByTokenId((byte[])args[0]);

                if (method == "getBindNft") return GetTokenIdByAddress((byte[])args[0]);               
                if (method == "getGather") return Storage.Get(Context(), "gatherAddress");
                if (method == "getTotal") return Storage.Get(Context(), "totalCount").AsBigInteger();                
                if (method == "balanceOfActivated") return Storage.Get(Context(), "activatedCount").AsBigInteger();

                // 以下接口只有 Active 时可用
                if (GetState() != Active) return false;

                //交易
                if (method == "transfer") //(byte[] from, byte[] to, byte[] tokenId)
                {
                    if (args.Length != 3) return false;
                    if (callscript != entryscript) return false;

                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];

                    if (!Runtime.CheckWitness(from)) return false;
                    if (from.Length != 20 || to.Length != 20 || tokenId.Length != 32) return false;

                    return TransferNft((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                }
                if (method == "approve")
                {
                    if (args.Length != 3) return false;
                    if (callscript != entryscript) return false;

                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];

                    if (!Runtime.CheckWitness(from)) return false;
                    if (from.Length != 20 || to.Length != 20 || tokenId.Length != 32) return false;

                    return Approve(from, to, tokenId);
                }
                if (method == "transferFrom")
                {
                    if (args.Length != 3) return false;

                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];

                    if (from.Length != 20 || to.Length != 20 || tokenId.Length != 32) return false;

                    return TransferFrom(from, to, tokenId);
                }
                if (method == "transferApp")
                {
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    byte[] tokenId = (byte[])args[2];
                    if (from.Length != 20 || to.Length != 20 || tokenId.Length != 32) return false;
                    if (from != callscript) return false;

                    return TransferNft(from, to, tokenId);
                }

                //绑定
                if (method == "bind") //(address, tokenId)
                {
                    if (args.Length != 2) return false;
                    return BindNft((byte[])args[0], (byte[])args[1]);
                }

                //销毁
                if (method == "destroy") //(address, tokenId)
                {
                    if (args.Length != 2) return false;
                    return DestroyNft((byte[])args[0], (byte[])args[1]);
                }

                if (method == "setOperator")
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    var opera = (byte[])args[0];
                    if (opera.Length != 20) return false;
                    Storage.Put(Context(), "operator", opera);
                    return true;
                }

                //*****  需要 operator 权限   ******
                var operatorAddr = Storage.Get(Context(), "operator");
                if (!Runtime.CheckWitness(operatorAddr)) return false;

                //设置参数
                if (method == "setGather")
                {
                    byte[] gatherAdress = (byte[])args[0];
                    Storage.Put(Context(), "gatherAddress", gatherAdress);
                    return true;
                }

                //设置总量
                if (method == "setTotal")
                {
                    BigInteger total = (BigInteger)args[0];
                    Storage.Put(Context(), "totalCount", total);
                    return true;
                }

                //第一本创世证书发行
                if (method == "deploy") //(address)
                {
                    if (args.Length != 1) return false;
                    var address = (byte[])args[0];
                    return DeployFirstNft(address);
                }

                //购买
                if (method == "mintToken") //(assetId, txid, count, inviterTokenId, receivableValue)
                {
                    if (args.Length != 5) return false;
                    return BuyNewNft((byte[])args[0], (byte[])args[1], (int)args[2], (byte[])args[3], (BigInteger)args[4]);
                }

                //激活
                if (method == "activate") //(tokenId, oneLevelInviterPoint, twoLevelInviterPoint)
                {
                    if (args.Length != 3) return false;
                    return Activate((byte[])args[0], (BigInteger)args[1], (BigInteger)args[2]);
                }

                //升级
                if (method == "upgrade") //(assetId, txid, tokenId, receivableValue, needPoint)
                {
                    if (args.Length != 5) return false;
                    return UpgradeNft((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3], (BigInteger)args[4]);
                }

                //加分
                if (method == "addPoint") //(tokenId, pointValue, type)
                {
                    if (args.Length != 3) return false;
                    var nftInfo = GetNftByTokenId((byte[])args[0]);
                    if (nftInfo.Owner.Length != 20) return false;
                    return AddPoint(nftInfo, (BigInteger)args[1], (byte[])args[2]);
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

        private static bool DestroyNft(byte[] address, byte[] tokenId)
        {
            if (!Runtime.CheckWitness(address)) return false;
            if (tokenId.Length != 32) return false;
            NFTInfo nftInfo = GetNftByTokenId(tokenId);
            //不是自己的不能删除
            if (nftInfo.Owner != address) return false;
            //已经激活的不能删除
            if (nftInfo.IsActivated) return false;

            //删除 nftInfo
            Storage.Delete(Context(), NftInfoKey(tokenId));

            BigInteger userNftCount = GetUserNftCount(address);
            if (userNftCount > 0)
                Storage.Put(Context(), UserNftCountKey(address), userNftCount - 1);

            //已发行数量减 1
            BigInteger nftCount = Storage.Get(Context(), "allNftCount").AsBigInteger();
            if (nftCount > 0)
                Storage.Put(Context(), "allNftCount", nftCount - 1);

            Destroyed(address, tokenId);
            return true;
        }

        private static bool Activate(byte[] tokenId, BigInteger oneLevelInviterPoint, BigInteger twoLevelInviterPoint)
        {
            var nftInfo = GetNftByTokenId(tokenId);
            if (nftInfo.Owner.Length != 20) return false;

            //已经激活不能重复激活
            if (nftInfo.IsActivated) return false;

            //获取邀请者证书信息
            NFTInfo inviterNftInfo = GetNftByTokenId(nftInfo.InviterTokenId);
            if (inviterNftInfo.Owner.Length != 20) return false;

            //激活
            nftInfo.IsActivated = true;

            SaveNftInfo(nftInfo);

            BigInteger activatedCount = Storage.Get(Context(), "activatedCount").AsBigInteger();
            Storage.Put(Context(), "activatedCount", activatedCount + 1);

            //上线加分
            AddPoint(inviterNftInfo, oneLevelInviterPoint, "invited".AsByteArray());

            //二级上线加分
            var twoLevelInviterNftInfo = GetNftByTokenId(inviterNftInfo.InviterTokenId);
            if (twoLevelInviterNftInfo.Owner.Length == 20)
                AddPoint(twoLevelInviterNftInfo, twoLevelInviterPoint, "invited".AsByteArray());

            Activated(nftInfo.Owner, tokenId);

            return true;
        }

        private static bool BuyNewNft(byte[] assetId, byte[] txid, int count, byte[] inviterTokenId, BigInteger receivableValue)
        {
            if (assetId.Length != 20 || txid.Length != 32 || inviterTokenId.Length != 32) return false;
            if (count < 1 || receivableValue < 1) return false;

            byte[] v = Storage.Get(Storage.CurrentContext, BctTxidUsedKey(txid));
            if (v.Length != 0) return false;

            //获取邀请者证书信息 未激活不能邀请
            var inviterNftInfo = GetNftByTokenId(inviterTokenId);
            if (inviterNftInfo.Owner.Length != 20) return false;
            if (!inviterNftInfo.IsActivated) return false;

            //判断是否已达数量上限
            BigInteger nftCount = Storage.Get(Context(), "allNftCount").AsBigInteger();
            BigInteger totalCount = Storage.Get(Context(), "totalCount").AsBigInteger();
            if (nftCount + count > totalCount) return false;

            byte[] gatherAddress = Storage.Get(Context(), "gatherAddress");

            //获取 bct 转账信息
            TransferLog tx = NativeAsset.GetTransferLog(assetId, txid);

            //钱没给够或收款地址不对 false
            if (tx.From.Length == 0 || tx.To != gatherAddress || (BigInteger)tx.Value < receivableValue) return false;

            byte[] address = tx.From;

            Map<byte[], int> newNftsMap = new Map<byte[], int>();

            for (int i = 1; i <= count; i++)
            {
                NFTInfo nftInfo = CreateNft(address, inviterTokenId, i);

                SaveNftInfo(nftInfo);

                newNftsMap[nftInfo.TokenId] = 1;
            }

            BigInteger userNftCount = GetUserNftCount(address);
            Storage.Put(Context(), UserNftCountKey(address), userNftCount + count);

            //更新数量
            Storage.Put(Context(), "allNftCount", nftCount + count);

            //notify
            Bought(address, inviterTokenId, count, (BigInteger)tx.Value, newNftsMap);

            SetTxUsed(txid);

            return true;
        }

        private static bool BindNft(byte[] address, byte[] tokenId)
        {
            if (!Runtime.CheckWitness(address)) return false;

            if (address.Length != 20 || tokenId.Length != 32) return false;

            NFTInfo nftInfo = GetNftByTokenId(tokenId);

            //没激活的不能绑定
            if (!nftInfo.IsActivated) return false;

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
            AddPointed(tokenId, nftInfo.Owner, 0 - needPoint, "upgrade".AsByteArray());

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
            if (deploy_data.Length > 0) return false;

            //构建一个证书
            NFTInfo newNftInfo = CreateNft(address, new byte[] { }, 1);

            //创始证书自动激活
            newNftInfo.IsActivated = true;

            Map<byte[], int> nftsMap = new Map<byte[], int>();
            nftsMap[newNftInfo.TokenId] = 1;

            //保存nft信息
            SaveNftInfo(newNftInfo);

            Storage.Put(Context(), UserNftCountKey(address), 1);

            Storage.Put(Context(), "allNftCount", 1);
            Storage.Put(Context(), "activatedCount", 1);

            //notify
            Bought(address, null, 1, 0, nftsMap);

            Storage.Put(Context(), "initDeploy", 1);

            return true;
        }

        private static bool TransferNft(byte[] from, byte[] to, byte[] tokenId)
        {           
            if (from == to) return true;

            var fromNftInfo = GetNftByTokenId(tokenId);

            //from 没有证书、false
            if (fromNftInfo.Owner != from) return false;

            //更换所有者
            fromNftInfo.Owner = to;

            SaveNftInfo(fromNftInfo);

            BigInteger fromUserNftCount = GetUserNftCount(from);
            Storage.Put(Context(), UserNftCountKey(from), fromUserNftCount - 1);
            BigInteger toUserNftCount = GetUserNftCount(to);
            Storage.Put(Context(), UserNftCountKey(to), fromUserNftCount + 1);

            var fromTokenId = GetTokenIdByAddress(from);

            //如果把绑定的卖了、就删除绑定
            if (fromTokenId == tokenId)
                Storage.Delete(Context(), BindNftKey(from));

            SaveExInfo(from, to, tokenId);
            //notify
            Transferred(from, to, tokenId);

            return true;
        }

        private static bool TransferFrom(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from == to) return true;

            var fromNftInfo = GetNftByTokenId(tokenId);

            //from 没有证书、false
            if (fromNftInfo.Owner != from) return false;

            var allowanceKey = AllowanceKey(tokenId);
            var allowanceSpend = Storage.Get(Context(), allowanceKey);
            if (allowanceSpend != from.Concat(to)) return false;

            //更换所有者
            fromNftInfo.Owner = to;

            SaveNftInfo(fromNftInfo);

            Storage.Delete(Context(), allowanceKey);

            BigInteger fromUserNftCount = GetUserNftCount(from);
            Storage.Put(Context(), UserNftCountKey(from), fromUserNftCount - 1);
            BigInteger toUserNftCount = GetUserNftCount(to);
            Storage.Put(Context(), UserNftCountKey(to), fromUserNftCount + 1);

            var fromTokenId = GetTokenIdByAddress(from);

            //如果把绑定的卖了、就删除绑定
            if (fromTokenId == tokenId)
                Storage.Delete(Context(), BindNftKey(from));

            SaveExInfo(from, to, tokenId);
            //notify
            Transferred(from, to, tokenId);

            return true;
        }

        private static bool Approve(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from == to) return true;

            var fromNftInfo = GetNftByTokenId(tokenId);

            //from 没有证书、false
            if (fromNftInfo.Owner != from) return false;

            Storage.Put(Context(), AllowanceKey(tokenId), from.Concat(to));

            //notify
            Approved(from, to, tokenId);
            return true;
        }

        private static bool AddPoint(NFTInfo nftInfo, BigInteger pointValue, byte[] type)
        {
            if (pointValue == 0) return true;

            nftInfo.AvailablePoint += pointValue;
            if (pointValue > 0)
                nftInfo.AllPoint += pointValue;

            SaveNftInfo(nftInfo);
            //notify
            AddPointed(nftInfo.TokenId, nftInfo.Owner, pointValue, type);
            return true;
        }

        private static byte[] GetTokenIdByAddress(byte[] address)
        {
            var key = BindNftKey(address);
            return Storage.Get(Context(), key);
        }

        private static BigInteger GetUserNftCount(byte[] address)
        {
            var key = UserNftCountKey(address);
            return Storage.Get(Context(), key).AsBigInteger();
        }

        public static NFTInfo CreateNft(byte[] owner, byte[] inviterTokenId, int num)
        {
            BigInteger nonce = num;
            byte[] tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash.Concat(nonce.AsByteArray()));

            NFTInfo nftInfo = new NFTInfo()
            {
                TokenId = tokenId,
                AllPoint = 0,
                AvailablePoint = 0,
                Owner = owner,
                Grade = 1,
                InviterTokenId = inviterTokenId,
                IsActivated = false
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

        public static byte[] GetOwnerByTokenId(byte[] tokenId)
        {
            var nft = GetNftByTokenId(tokenId);
            return nft.Owner;
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

        private static byte[] UserNftCountKey(byte[] address) => new byte[] { 0x10 }.Concat(address);
        private static byte[] BindNftKey(byte[] address) => new byte[] { 0x11 }.Concat(address);
        private static byte[] NftInfoKey(byte[] tokenId) => new byte[] { 0x12 }.Concat(tokenId);
        private static byte[] TxInfoKey(byte[] txid) => new byte[] { 0x13 }.Concat(txid);
        private static byte[] BctTxidUsedKey(byte[] txid) => new byte[] { 0x14 }.Concat(txid);
        private static byte[] AllowanceKey(byte[] tokenId) => new byte[] { 0x15 }.Concat(tokenId);

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
        public bool IsActivated = false;//是否激活
    }

}
