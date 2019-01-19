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
    /// BlaCat合伙人证书 NFT
    /// </summary>
    public class PartnerToken : SmartContract
    {
        //管理员
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AM5ho5nEodQiai1mCTFDV3YUNYApCorMCX");

        public delegate object NEP5Contract(string method, object[] args);

        [DisplayName("exchange")]
        public static event Action<byte[], byte[], byte[]> Exchanged;//(byte[] from, byte[] to, byte[] tokenId);

        [DisplayName("upgrade")]
        public static event Action<byte[], byte[], BigInteger, BigInteger> Upgraded;//(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank);

        [DisplayName("addpoint")]
        public static event Action<byte[], byte[], BigInteger> AddPointed; //(tokenID, address, point)

        [DisplayName("bind")]
        public static event Action<byte[], byte[]> Bound; //(address, tokenId)

        [DisplayName("error")]
        public static event Action<BigInteger, BigInteger> Errored; //(method, type)

        [DisplayName("debug")]
        public static event Action<BigInteger, byte[]> Debuged; //(method, type)

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
            var magicstr = "BlaCat Partner Certificate Token v1.5";
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

                //invoke
                if (method == "getState") return GetState();

                // allstop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                if (method == "getBindNft") return GetTokenIdByAddress((byte[])args[0]);
                if (method == "getNftInfoById") return GetNftByTokenId((byte[])args[0]);
                if (method == "getTxinfo") return GetExInfoByTxid((byte[])args[0]);
                if (method == "getGatherAddress") return Storage.Get(Context(), "gatherAddress");
                if (method == "getUpgradeConfig") return GetUpgradeConfig();
                if (method == "getBuyConfig") return GetBuyConfig();
                if (method == "getMemberPoint") return new BigInteger(Storage.Get(Context(), "memberPoint"));
                if (method == "getTwoLevelPercent") return new BigInteger(Storage.Get(Context(), "twoLevelPercent"));
                if (method == "getThreeLevelPercent") return new BigInteger(Storage.Get(Context(), "threeLevelPercent"));

                // 以下接口只有 Active 时可用
                if (GetState() != Active) return false;

                //设置参数
                if (method == "init")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return Init(args);
                }

                //升级相关配置
                if (method == "setUpgradeConfig")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return SetUpgradeConfig(args);
                }

                //购买相关配置
                if (method == "setBuyConfig")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return SetBuyConfig(args);
                }

                //第一本创世证书发行
                if (method == "deploy") //(address)
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1) return false;
                    return DeployFirstNft((byte[])args[0]);
                }

                //购买
                if (method == "buy") //(assetId, txid, count, inviterTokenId)
                {
                    if (args.Length != 4) return false;
                    return BuyNewNft((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3]);
                }

                //绑定
                if (method == "bind") //(address, tokenId)
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 2) return false;
                    return BindNft((byte[])args[0], (byte[])args[1]);
                }

                //升级
                if (method == "upgrade") //(assetId, txid, tokenId)
                {
                    if (args.Length != 3) return false;
                    return UpgradeNft((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                }

                //交易
                if (method == "exchange") //(byte[] from, byte[] to, byte[] tokenId)
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 3) return false;
                    return ExchangeNft((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                }

                //降级
                if (method == "reduceGrade") //(tokenId)
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1) return false;
                    return ReduceGrade((byte[])args[0]);
                }

                //扣分
                if (method == "reducePoint") //(tokenId, pointValue)
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 2) return false;
                    return ReducePoint((byte[])args[0], (BigInteger)args[1]);
                }

                //邀请普通会员加分
                if (method == "addPoint") //(tokenId)
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1) return false;
                    var nftInfo = GetNftByTokenId((byte[])args[0]);
                    if (nftInfo.Owner.Length != 20) return false;

                    BigInteger memberPoint = new BigInteger(Storage.Get(Context(), "memberPoint"));
                    if (memberPoint == 0) throw new Exception("Not set config");

                    return AddPoint(nftInfo, memberPoint);
                }

                #region 升级合约,耗费490,仅限管理员
                if (method == "updatecontract")
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1 && args.Length != 9) return false;

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script) return false;

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)05;
                    string name = "nft";
                    string version = "1.0";
                    string author = "ZoroChain";
                    string email = "0";
                    string description = "nft";

                    if (args.Length == 9)
                    {
                        parameter_list = (byte[])args[1];
                        return_type = (byte)args[2];
                        need_storage = (bool)args[3];
                        name = (string)args[4];
                        version = (string)args[5];
                        author = (string)args[6];
                        email = (string)args[7];
                        description = (string)args[8];
                    }
                    Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
                    return true;
                }
                #endregion
            }

            return false;
        }

        private static bool Init(object[] args)
        {
            BigInteger silverPoint = (BigInteger)args[0]; //邀请白银所得贡献值
            BigInteger memberPoint = (BigInteger)args[1]; //邀请会员所得贡献值

            BigInteger twoLevelPercent = (BigInteger)args[2]; //二级上线获得贡献值
            BigInteger threeLevelPercent = (BigInteger)args[3]; //三级上线获得贡献值
            byte[] gatherAdress = (byte[])args[4]; //收款地址

            Storage.Put(Context(), "silverPoint", silverPoint);
            Storage.Put(Context(), "memberPoint", memberPoint);
            Storage.Put(Context(), "twoLevelPercent", twoLevelPercent);
            Storage.Put(Context(), "threeLevelPercent", threeLevelPercent);
            Storage.Put(Context(), "gatherAddress", gatherAdress);
            return true;
        }

        private static bool BuyNewNft(byte[] assetId, byte[] txid, BigInteger count, byte[] inviterTokenId)
        {
            if (assetId.Length != 20 || txid.Length != 32 || inviterTokenId.Length != 32) return false;
            if (count < 1) return false;

            byte[] v = Storage.Get(Storage.CurrentContext, BctTxidUsedKey(txid));
            if (v.Length != 0) return false;

            //获取邀请者证书信息
            var inviterNftInfo = GetNftByTokenId(inviterTokenId);
            if (inviterNftInfo.Owner.Length != 20) return false;

            BuyConfig config = GetBuyConfig();
            if (config.SilverPrice == 0 || config.SilverPoint == 0) return false;

            byte[] gatherAddress = Storage.Get(Context(), "gatherAddress");

            //根据买的数量计算金额和打折后金额
            BigInteger allAmount = config.SilverPrice * count;
            if (count > config.OneDiscountCount)
            {
                if (count > config.TwoDiscountCount)
                    allAmount = allAmount * config.TwoDiscountPercent / 100;
                else
                    allAmount = allAmount * config.OneDiscountPercent / 100;
            }

            //获取 bct 转账信息
            TransferLog tx = NativeAsset.GetTransferLog(assetId, txid);

            //钱没给够或收款地址不对 false
            if (tx.From.Length == 0 || tx.To != gatherAddress || (BigInteger)tx.Value < allAmount) return false;

            for (int i = 0; i < count; i++)
            {
                NFTInfo nftInfo = CreateNft(tx.From, inviterTokenId, i);

                SaveNftInfo(nftInfo);

                SaveExInfo(null, tx.From, nftInfo.TokenId);

                //notify
                Exchanged(null, tx.From, nftInfo.TokenId);
            }

            //邀请白银证书获得贡献值
            BigInteger addPoint = config.SilverPoint * count;

            GradedAddPoint(inviterNftInfo, addPoint);

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

        private static bool UpgradeNft(byte[] assetId, byte[] txid, byte[] tokenId)
        {
            if (assetId.Length != 20 || txid.Length != 32 || tokenId.Length != 32) return false;

            var v = new BigInteger(Storage.Get(Context(), BctTxidUsedKey(txid)));
            if (v != 0) return false;

            TransferLog tx = NativeAsset.GetTransferLog(assetId, txid);

            byte[] gatherAddress = Storage.Get(Context(), "gatherAddress");
            if (tx.From.Length != 20 || tx.To != gatherAddress || tx.Value <= 0) return false;

            var nftInfo = GetNftByTokenId(tokenId);
            if (nftInfo.Owner != tx.From) return false;

            UpgradeConfig config = GetUpgradeConfig();
            if (config.GoldPrice == 0) return false;

            //获取升级时要消耗的贡献值
            var reducePoint = UpgradeReducePoint(config, nftInfo, tx);
            if (reducePoint == 0) return false;

            //升级
            nftInfo.Rank += 1;
            //扣除消耗贡献值
            nftInfo.AvailablePoint -= reducePoint;

            SaveNftInfo(nftInfo);

            //升级给邀请者加分  获取要加的分数
            BigInteger addPoint = GetAddPoint(config, nftInfo.Rank);

            var inviterNftInfo = GetNftByTokenId(nftInfo.InviterTokenId);
            if (inviterNftInfo.Owner.Length == 20)
                GradedAddPoint(inviterNftInfo, addPoint);

            SetTxUsed(txid);
            //notify
            Upgraded(tokenId, tx.From, nftInfo.Rank - 1, nftInfo.Rank);
            AddPointed(tokenId, nftInfo.Owner, 0 - reducePoint);
            return true;
        }

        private static bool ReduceGrade(byte[] tokeId)
        {
            if (tokeId.Length != 32) return false;
            var nftInfo = GetNftByTokenId(tokeId);
            if (nftInfo.Owner.Length != 20) return false;
            if (nftInfo.Rank < 2) return false;
            nftInfo.Rank -= 1;
            SaveNftInfo(nftInfo);

            //(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank)
            Upgraded(tokeId, nftInfo.Owner, nftInfo.Rank + 1, nftInfo.Rank);
            return true;
        }

        private static bool ReducePoint(byte[] tokenId, BigInteger pointValue)
        {
            if (tokenId.Length != 32) return false;

            NFTInfo nftInfo = GetNftByTokenId(tokenId);
            nftInfo.AvailablePoint -= pointValue;
            SaveNftInfo(nftInfo);

            //notify
            AddPointed(tokenId, nftInfo.Owner, 0 - pointValue);
            return true;
        }

        private static bool DeployFirstNft(byte[] address)
        {
            if (address.Length != 20) return false;

            //判断初始发行是否已完成
            byte[] deploy_data = Storage.Get(Context(), "initDeploy");
            if (deploy_data.Length != 0) return false;

            //构建一个证书
            var newNftInfo = CreateNft(address,new byte[] { }, 0);

            //保存nft信息
            SaveNftInfo(newNftInfo);

            //保存交易信息
            SaveExInfo(null, address, newNftInfo.TokenId);

            //notify
            Exchanged(null, address, newNftInfo.TokenId);

            Storage.Put(Context(), "initDeploy", 1);

            return true;
        }

        private static void GradedAddPoint(NFTInfo firstLevelNftInfo, BigInteger Point)
        {
            BigInteger twoLevelPercent = new BigInteger(Storage.Get(Context(), "twoLevelPercent"));
            BigInteger threeLevelPercent = new BigInteger(Storage.Get(Context(), "threeLevelPercent"));

            //给一级邀请者加分
            AddPoint(firstLevelNftInfo, Point);

            //给二级邀请者加分
            if (firstLevelNftInfo.InviterTokenId.Length != 32) return;
            var twoLevelNftInfo = GetNftByTokenId(firstLevelNftInfo.InviterTokenId);
            if (twoLevelNftInfo.Owner.Length == 20)
                AddPoint(twoLevelNftInfo, (Point * twoLevelPercent) / 100);

            //给三级邀请者加分
            if (twoLevelNftInfo.InviterTokenId.Length != 32) return;
            var threeLevelNftInfo = GetNftByTokenId(twoLevelNftInfo.InviterTokenId);
            if (threeLevelNftInfo.Owner.Length == 20)
                AddPoint(threeLevelNftInfo, (Point * threeLevelPercent) / 100);
        }

        private static bool ExchangeNft(byte[] from, byte[] to, byte[] tokenId)
        {
            if (from.Length != 20 || to.Length != 20 || tokenId.Length != 32) return false;
            
            var fromNftInfo = GetNftByTokenId(tokenId);

            //from 没有证书、false
            if (fromNftInfo.Owner != from) return false;

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
            nftInfo.AllPoint += pointValue;
            nftInfo.AvailablePoint += pointValue;

            SaveNftInfo(nftInfo);
            //notify
            AddPointed(nftInfo.TokenId, nftInfo.Owner, pointValue);
            return true;
        }

        private static byte[] GetTokenIdByAddress(byte[] address)
        {
            var key = BindNftKey(address);
            return Storage.Get(Context(), key);
        }

        public static BigInteger UpgradeReducePoint(UpgradeConfig config, NFTInfo nftInfo, TransferLog tx)
        {
            if (nftInfo.Rank == 1 && nftInfo.AvailablePoint >= config.GoldUpgradePoint &&
                (BigInteger)tx.Value >= config.GoldPrice)
                return config.GoldUpgradePoint;
            if (nftInfo.Rank == 2 && nftInfo.AvailablePoint >= config.PlatinumUpgradePoint &&
                (BigInteger)tx.Value >= config.PlatinumPrice)
                return config.PlatinumUpgradePoint;
            if (nftInfo.Rank == 3 && nftInfo.AvailablePoint >= config.DiamondUpgradePoint &&
                (BigInteger)tx.Value >= config.DiamondPrice)
                return config.DiamondUpgradePoint;
            return 0;
        }

        public static BigInteger GetAddPoint(UpgradeConfig config, BigInteger rank)
        {
            if (rank == 2)
                return config.GoldInvitePoint;
            if (rank == 3)
                return config.PlatinumInvitePoint;
            if (rank == 4)
                return config.DiamondInvitePoint;
            return 0;
        }

        public static NFTInfo CreateNft(byte[] owner, byte[] inviterTokenId, int num)
        {
            byte[] nonce = Int2ByteArray(num);
            byte[] tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash.Concat(nonce));

            NFTInfo nftInfo = new NFTInfo()
            {
                TokenId = tokenId,
                AllPoint = 0,
                AvailablePoint = 0,
                Owner = owner,
                Rank = 1,
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

        private static bool SetBuyConfig(object[] args)
        {
            BigInteger silverPrice = (BigInteger)args[0];
            BigInteger silverPoint = (BigInteger)args[1];

            BigInteger oneDiscountCount = (BigInteger)args[2];
            BigInteger oneDiscountPercent = (BigInteger)args[3];

            BigInteger twoDiscountCount = (BigInteger)args[4];
            BigInteger twoDiscountPercent = (BigInteger)args[5];

            BigInteger threeDiscountCount = (BigInteger)args[6];
            BigInteger threeDiscountPercent = (BigInteger)args[7];

            var configs = new BuyConfig()
            {
                SilverPrice = silverPrice,
                SilverPoint= silverPoint,
                OneDiscountCount = oneDiscountCount,
                OneDiscountPercent = oneDiscountPercent,

                TwoDiscountCount = twoDiscountCount,
                TwoDiscountPercent = twoDiscountPercent,

                ThreeDiscountCount = threeDiscountCount,
                ThreeDiscountPercent = threeDiscountPercent
            };

            byte[] configBytes = Helper.Serialize(configs);
            Storage.Put(Context(), "buyConfig", configBytes);

            return true;
        }

        private static BuyConfig GetBuyConfig()
        {
            var configBytes = Storage.Get(Context(), "buyConfig");
            if (configBytes.Length == 0)
                return new BuyConfig();
            return configBytes.Deserialize() as BuyConfig;
        }

        private static bool SetUpgradeConfig(object[] args)
        {
            BigInteger goldPrice = (BigInteger)args[0];
            BigInteger platinumPrice = (BigInteger)args[1];
            BigInteger diamondPrice = (BigInteger)args[2];

            BigInteger goldInvitePoint = (BigInteger)args[3];
            BigInteger platinumInvitePoint = (BigInteger)args[4];
            BigInteger diamondInvitePoint = (BigInteger)args[5];

            BigInteger goldUpgradePoint = (BigInteger)args[6];
            BigInteger platinumUpgradePoint = (BigInteger)args[7];
            BigInteger diamondUpgradePoint = (BigInteger)args[8];

            var configs = new UpgradeConfig()
            {
                GoldPrice = goldPrice,
                PlatinumPrice = platinumPrice,
                DiamondPrice = diamondPrice,

                GoldInvitePoint = goldInvitePoint,
                PlatinumInvitePoint = platinumInvitePoint,
                DiamondInvitePoint = diamondInvitePoint,

                GoldUpgradePoint = goldUpgradePoint,
                PlatinumUpgradePoint = platinumUpgradePoint,
                DiamondUpgradePoint = diamondUpgradePoint
            };

            byte[] configBytes = Helper.Serialize(configs);
            Storage.Put(Context(), "upgradeConfig", configBytes);
            return true;
        }

        public static UpgradeConfig GetUpgradeConfig()
        {
            var configBytes = Storage.Get(Context(), "upgradeConfig");
            if (configBytes.Length == 0)
                return new UpgradeConfig();
            return configBytes.Deserialize() as UpgradeConfig;
        }

        public static byte[] Int2ByteArray(int intValue)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)(intValue >> 24);
            bytes[1] = (byte)(intValue >> 16);
            bytes[2] = (byte)(intValue >> 8);
            bytes[3] = (byte)intValue;
            return bytes;
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
        public BigInteger Rank; //等级
        public BigInteger AllPoint; //累积贡献值
        public BigInteger AvailablePoint; //可用贡献值
        public byte[] InviterTokenId; //邀请者证书ID
    }

    //配置
    public class BuyConfig
    {
        public BigInteger SilverPrice; //白银证书购买价格
        public BigInteger SilverPoint; //邀请白银所得贡献值
        public BigInteger OneDiscountCount; //一级打折数量
        public BigInteger OneDiscountPercent; //达到一级数量时的折扣
        public BigInteger TwoDiscountCount; //二级打折数量
        public BigInteger TwoDiscountPercent; //达到二级数量时的折扣
        public BigInteger ThreeDiscountCount; //三级打折数量
        public BigInteger ThreeDiscountPercent; //达到三级数量时的折扣
    }

    public class UpgradeConfig
    {
        public BigInteger GoldPrice; //升级黄金价格
        public BigInteger PlatinumPrice; //升级铂金价格
        public BigInteger DiamondPrice; //升级钻石价格

        public BigInteger GoldInvitePoint; //被邀请者升级黄金时邀请者所得贡献值
        public BigInteger PlatinumInvitePoint; //被邀请者升级铂金时邀请者所得贡献值
        public BigInteger DiamondInvitePoint; //被邀请者升级钻石时邀请者所得贡献值

        public BigInteger GoldUpgradePoint; //升级黄金所需贡献值
        public BigInteger PlatinumUpgradePoint; //升级铂金所需贡献值
        public BigInteger DiamondUpgradePoint; //升级钻石所需贡献值
    }

}
