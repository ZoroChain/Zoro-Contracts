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

        //BCT合约hash
        [Appcall("40a80749ef62da6fc3d74dbf6fc7745148922372")]
        static extern object bctCall(string method, object[] arr);

        [DisplayName("exchange")]
        public static event Action<byte[], byte[], byte[]> Exchanged;//(byte[] from, byte[] to, byte[] tokenId);

        [DisplayName("upgrade")]
        public static event Action<byte[], byte[], BigInteger, BigInteger> Upgraded;//(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank);

        [DisplayName("addpoint")]
        public static event Action<byte[], byte[], BigInteger> AddPointed; //(tokenID, address, point)

        private static readonly byte[] Active = { };          // 所有接口可用
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
            var magicstr = "BlaCat Partner Certificate Token";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                //管理员权限     设置合约状态          
                if (method == "setstate")
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
                if (method == "getstate") return GetState();

                // stop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                if (method == "name") return Name();

                if (method == "symbol") return Symbol();

                if (method == "getnftinfo")
                {
                    var tokenId = GetTokenIdByAddress((byte[])args[0]);
                    if (tokenId.Length != 32) return null;
                    return GetNftByTokenId(tokenId);
                }

                if (method == "getnftinfobyid")
                {
                    byte[] tokenId = (byte[])args[0];
                    if (tokenId.Length != 32) return null;
                    return GetNftByTokenId(tokenId);
                }

                if (method == "gettxinfo")
                {
                    byte[] txid = (byte[])args[0];
                    if (txid.Length != 32) return null;
                    return GetTxInfoByTxid(txid);
                }

                if (method == "getcount") return GetNftCount();

                // 以下接口只有 Active 时可用
                if (GetState() != Active) return false;

                if (method == "getconfig") return GetConfig();

                //设置参数
                if (method == "setconfig")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    BigInteger silverPrice = (BigInteger)args[0];
                    BigInteger goldPrice = (BigInteger)args[1];
                    BigInteger platinumPrice = (BigInteger)args[2];
                    BigInteger diamondPrice = (BigInteger)args[3];

                    BigInteger leaguerInvitePoint = (BigInteger)args[4];
                    BigInteger silverInvitePoint = (BigInteger)args[5];

                    BigInteger goldInvitePoint = (BigInteger)args[6];
                    BigInteger platinumInvitePoint = (BigInteger)args[7];
                    BigInteger diamondInvitePoint = (BigInteger)args[8];

                    BigInteger goldUpgradePoint = (BigInteger)args[9];
                    BigInteger platinumUpgradePoint = (BigInteger)args[10];
                    BigInteger diamondUpgradePoint = (BigInteger)args[11];

                    byte[] gatheringAddress = (byte[])args[12];
                    if (gatheringAddress.Length != 20) return false;

                    var configs = new Config()
                    {
                        SilverPrice = silverPrice,
                        GoldPrice = goldPrice,
                        PlatinumPrice = platinumPrice,
                        DiamondPrice = diamondPrice,
                        LeaguerInvitePoint = leaguerInvitePoint,
                        SilverInvitePoint = silverInvitePoint,
                        GoldInvitePoint = goldInvitePoint,
                        PlatinumInvitePoint = platinumInvitePoint,
                        DiamondInvitePoint = diamondInvitePoint,
                        GoldUpgradePoint = goldUpgradePoint,
                        PlatinumUpgradePoint = platinumUpgradePoint,
                        DiamondUpgradePoint = diamondUpgradePoint,
                        GatheringAddress = gatheringAddress
                    };

                    byte[] configBytes = Helper.Serialize(configs);
                    Storage.Put(Context(), "config", configBytes);
                    return true;
                }

                //第一本证书创世发行
                if (method == "deploy")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    var address = (byte[])args[0];
                    if (address.Length != 20) return false;

                    return DeployFirstNft(address);
                }

                //转手交易
                if (method == "exchange")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from.Length != 20 || to.Length != 20) return false;

                    return ExchangeNft(from, to);
                }

                Config config = GetConfig();
                if (config.SilverPrice == 0 || config.GatheringAddress.Length == 0) throw new Exception("Not set config");

                //首次购买
                if (method == "buy")
                {
                    byte[] txid = (byte[])args[0];
                    byte[] inviterAddr = (byte[])args[1];
                    if (txid.Length != 32 || inviterAddr.Length != 20) return false;

                    return BuyNewNft(config, txid, inviterAddr);
                }

                //升级
                if (method == "upgrade")
                {
                    byte[] txid = (byte[])args[0];
                    if (txid.Length != 32) return false;

                    return UpgradeNft(config, txid);
                }

                //邀请普通会员加分
                if (method == "addpoint")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;

                    byte[] address = (byte[])args[0];
                    if (address.Length != 20) return false;

                    var tokenId = GetTokenIdByAddress(address);

                    AddPoint(tokenId, config.LeaguerInvitePoint);

                    return true;
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

        public static string Name() => "BlaCat Partner Certificate Token";//Blacat 合伙人证书 NFT

        public static string Symbol() => "BPT";//简称

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static StorageContext Context() => Storage.CurrentContext;

        private static bool DeployFirstNft(byte[] address)
        {
            //判断初始发行是否已完成
            if (IsInitted()) return false;

            //判断address是否已拥有证书
            if (IsHaveNft(address)) return false;

            //构建一个证书
            var newNftInfo = CreateNft(address, null);

            //保存address-tokenid对应关系
            SaveAddressMap(address, newNftInfo.TokenId);

            //保存nft信息
            SaveNftInfo(newNftInfo);

            //增加数量
            AddNftCount(newNftInfo.Rank);

            //保存交易信息
            SaveTxInfo(null, address, newNftInfo.TokenId);

            //notify
            Exchanged(null, address, newNftInfo.TokenId);

            //标记初始发行完成
            Storage.Put(Context(), "initDeploy", 1);

            return true;
        }

        private static bool BuyNewNft(Config config, byte[] txid, byte[] inviterAddr)
        {
            var tx = GetBctTxInfo(txid);

            //钱没给够或收款地址对不上均false
            if (tx.@from.Length == 0 || tx.to.AsBigInteger() != config.GatheringAddress.AsBigInteger() || tx.value < config.SilverPrice)
                return false;

            //购买者已拥有或者邀请者未拥有证书、均false
            if (IsHaveNft(tx.from) || !IsHaveNft(inviterAddr)) return false;

            var inviterTokenId = GetTokenIdByAddress(inviterAddr);
            var nftInfo = CreateNft(tx.from, inviterTokenId);

            SaveAddressMap(tx.from, nftInfo.TokenId);

            SaveNftInfo(nftInfo);

            //增加数量
            AddNftCount(nftInfo.Rank);

            //给邀请者证书加分
            AddPoint(inviterTokenId, config.SilverInvitePoint);

            SaveTxInfo(null, tx.@from, nftInfo.TokenId);

            SetTxUsed(txid);

            //notify
            Exchanged(null, tx.@from, nftInfo.TokenId); 
            return true;
        }

        private static bool UpgradeNft(Config config, byte[] txid)
        {
            var tx = GetBctTxInfo(txid);

            if (tx.@from.Length == 0 || tx.to.AsBigInteger() != config.GatheringAddress.AsBigInteger() || tx.value <= 0)
                return false;
            if (!IsHaveNft(tx.from)) return false;

            var tokenId = GetTokenIdByAddress(tx.from);
            var nftInfo = GetNftByTokenId(tokenId);

            if (CanUpgrade(config, nftInfo, tx))
            {
                nftInfo.Rank += 1;

                SaveNftInfo(nftInfo);

                //各等级数量变化
                AddNftCount(nftInfo.Rank);

                //升级给邀请者加分  获取要加的分数
                BigInteger addPoint = GetAddPoint(config, nftInfo.Rank);

                AddPoint(nftInfo.InviterTokenId, addPoint);

                SetTxUsed(txid);

                //notify
                Upgraded(tokenId, tx.@from, nftInfo.Rank - 1, nftInfo.Rank);

                return true;
            }
            return false;
        }

        private static bool ExchangeNft(byte[] from, byte[] to)
        {
            //to 已拥有证书, from 没有证书、false
            if (IsHaveNft(to) || !IsHaveNft(from)) return false;

            var fromTokenId = GetTokenIdByAddress(from);
            var fromNftInfo = GetNftByTokenId(fromTokenId);

            //更换所有者
            fromNftInfo.Owner = to;

            //删除from的证书
            DeleteAddressMap(from);

            SaveAddressMap(to, fromTokenId);

            SaveNftInfo(fromNftInfo);

            SaveTxInfo(from, to, fromTokenId);
            //notify
            Exchanged(from, to, fromTokenId);

            return true;
        }

        private static void AddPoint(byte[] tokenId, BigInteger pointValue)
        {
            var nftInfo = GetNftByTokenId(tokenId);

            nftInfo.ContributionPoint += pointValue;

            SaveNftInfo(nftInfo);
            //notify
            AddPointed(tokenId, nftInfo.Owner, pointValue);
        }

        private static void DeleteAddressMap(byte[] address)
        {
            StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
            addressMap.Delete(address);
        }

        private static bool IsHaveNft(byte[] address)
        {
            var tokenId = GetTokenIdByAddress(address);
            if (tokenId.Length > 0) return true;
            else return false;
        }

        private static void SaveAddressMap(byte[] address, byte[] tokenId)
        {
            StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
            addressMap.Put(address, tokenId);
        }

        private static bool IsInitted()
        {
            byte[] deploy_data = Storage.Get(Context(), "initDeploy");
            if (deploy_data.Length != 0) return true;
            else return false;
        }

        private static byte[] GetTokenIdByAddress(byte[] address)
        {
            StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
            return addressMap.Get(address);
        }

        public static bool CanUpgrade(Config config, NFTInfo nftInfo, TransferInfo tx)
        {
            if (nftInfo.Rank == 1 && nftInfo.ContributionPoint >= config.GoldUpgradePoint &&
                tx.value >= config.GoldPrice)
                return true;
            if (nftInfo.Rank == 2 && nftInfo.ContributionPoint >= config.PlatinumUpgradePoint &&
                tx.value >= config.PlatinumPrice)
                return true;
            if (nftInfo.Rank == 3 && nftInfo.ContributionPoint >= config.DiamondUpgradePoint &&
                tx.value >= config.DiamondPrice)
                return true;
            return false;
        }

        public static BigInteger GetAddPoint(Config config, BigInteger rank)
        {
            if (rank == 2)
                return config.GoldInvitePoint;
            if (rank == 3)
                return config.PlatinumInvitePoint;
            if (rank == 4)
                return config.DiamondInvitePoint;
            return 0;
        }

        public static NFTInfo CreateNft(byte[] owner, byte[] inviterTokenId)
        { 
            var nftInfo = new NFTInfo();
            nftInfo.TokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash);
            nftInfo.Owner = owner;
            nftInfo.ContributionPoint = 0;
            nftInfo.Rank = 1;
            nftInfo.InviterTokenId = inviterTokenId;
            return nftInfo;
        }

        public static void AddNftCount(BigInteger rank)
        {
            var nftCount = GetNftCount();
            if (rank == 1)
            {
                nftCount.AllCount += 1;
                nftCount.SilverCount += 1;
            }
            if (rank == 2)
            {
                nftCount.GoldCount += 1;
                nftCount.SilverCount -= 1;
            }
            if (rank == 3)
            {
                nftCount.PlatinumCount += 1;
                nftCount.GoldCount -= 1;
            }
            if (rank == 4)
            {
                nftCount.DiamondCount += 1;
                nftCount.PlatinumCount -= 1;
            }
            var nftCountBytes = Helper.Serialize(nftCount);
            Storage.Put(Context(), "nftCount", nftCountBytes);
        }

        public static void SaveNftInfo(NFTInfo nftInfo)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] nftInfoBytes = Helper.Serialize(nftInfo);
            userNftInfoMap.Put(nftInfo.TokenId, nftInfoBytes);
        }

        public static void SaveTxInfo(byte[] from, byte[] to, byte[] tokenId)
        {
            ExchangeInfo info = new ExchangeInfo();
            info.@from = from;
            info.to = to;
            info.tokenId = tokenId;
            byte[] exInfo = Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            StorageMap ExchangeInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
            ExchangeInfoMap.Put(txid, exInfo);
        }

        public static Config GetConfig()
        {
            var configBytes = Storage.Get(Context(), "config");
            if (configBytes.Length == 0)
                return new Config();
            return configBytes.Deserialize() as Config;
        }

        private static NftCount GetNftCount()
        {
            var data = Storage.Get(Context(), "nftCount");
            if (data.Length == 0)
                return new NftCount();
            return data.Deserialize() as NftCount;
        }

        public static NFTInfo GetNftByTokenId(byte[] tokenId)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] data = userNftInfoMap.Get(tokenId);
            if (data.Length == 0)
                new NFTInfo();
            return data.Deserialize() as NFTInfo;
        }

        public static ExchangeInfo GetTxInfoByTxid(byte[] txid)
        {
            StorageMap ExchangeInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
            var data = ExchangeInfoMap.Get(txid);
            if (data.Length == 0)
                return null;
            return data.Deserialize() as ExchangeInfo;
        }

        public static TransferInfo GetBctTxInfo(byte[] txid)
        {
            StorageMap txInfoMap = Storage.CurrentContext.CreateMap("bctTxInfoMap");
            var tInfo = new TransferInfo();
            var v = txInfoMap.Get(txid).AsBigInteger();
            if (v == 0)
            {
                object[] _p = new object[1] { txid };
                var info = bctCall("getTxInfo", _p);
                if (((object[])info).Length == 3)
                    tInfo = info as TransferInfo;
            }
            return tInfo;
        }

        public static void SetTxUsed(byte[] txid)
        {
            StorageMap txInfoMap = Storage.CurrentContext.CreateMap("bctTxInfoMap");
            txInfoMap.Put(txid, 1);
        }

    }

    public class ExchangeInfo
    {
        public byte[] from;
        public byte[] to;
        public byte[] tokenId;
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }

    //证书信息
    public class NFTInfo
    {
        public byte[] TokenId; //tokenid 证书ID
        public byte[] Owner; //所有者 address
        public BigInteger Rank; //等级
        public BigInteger ContributionPoint; //贡献值
        public byte[] InviterTokenId; //邀请者证书ID
    }

    //配置
    public class Config
    {
        public BigInteger SilverPrice; //白银购买价格
        public BigInteger GoldPrice; //升级黄金价格
        public BigInteger PlatinumPrice; //升级铂金价格
        public BigInteger DiamondPrice; //升级钻石价格

        public BigInteger LeaguerInvitePoint; //邀请普通会员所得贡献值
        public BigInteger SilverInvitePoint; //邀请白银所得贡献值

        public BigInteger GoldInvitePoint; //被邀请者升级黄金时邀请者所得贡献值
        public BigInteger PlatinumInvitePoint; //被邀请者升级铂金时邀请者所得贡献值
        public BigInteger DiamondInvitePoint; //被邀请者升级钻石时邀请者所得贡献值

        public BigInteger GoldUpgradePoint; //升级黄金所需贡献值
        public BigInteger PlatinumUpgradePoint; //升级铂金所需贡献值
        public BigInteger DiamondUpgradePoint; //升级钻石所需贡献值

        public byte[] GatheringAddress; //收款人地址
    }

    //已发行数量
    public class NftCount
    {
        public BigInteger AllCount; //总数量
        public BigInteger SilverCount; //白银数量
        public BigInteger GoldCount; //黄金数量
        public BigInteger PlatinumCount; //铂金数量
        public BigInteger DiamondCount; //钻石数量
    }
}
