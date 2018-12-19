using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
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
        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AM5ho5nEodQiai1mCTFDV3YUNYApCorMCX");
        //BCT合约hash
        [Appcall("40a80749ef62da6fc3d74dbf6fc7745148922372")]
        static extern object bctCall(string method, object[] arr);

        public delegate void deleExchange(byte[] from, byte[] to, byte[] tokenId);
        [DisplayName("exchange")]
        public static event deleExchange Exchanged;

        public delegate void deleUpgrade(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank);
        [DisplayName("upgrade")]
        public static event deleUpgrade Upgraded;

        public static string Name() => "BlaCat Partner Certificate Token";//Blacat 合伙人证书 NFT
        
        public static string Symbol() => "BPT";//简称
        
        public static string Version() => "1.0.0"; //版本

        public static object Main(string method, object[] args)
        {
            var magicstr = "BlaCat Partner Certificate Token";
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return true;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                //invoke
                if (method == "name") return Name();
                if (method == "symbol") return Symbol();

                if (method == "getnftinfo")
                {
                    byte[] address = (byte[]) args[0];
                    if (address.Length != 20) return false;
                    StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
                    byte[] tokenId = addressMap.Get(address);
                    if (tokenId.Length == 0) return false;
                    return GetNftByTokenId(tokenId);
                }

                if (method == "getnftinfobyid")
                {
                    byte[] tokenId = (byte[])args[0];                    
                    if (tokenId.Length == 0) return false;
                    return GetNftByTokenId(tokenId);
                }

                if (method == "gettxinfo")
                {
                    byte[] txid = (byte[]) args[0];
                    if (txid.Length == 0) return false;
                    return GetTxInfoByTxid(txid);
                }  

                if (method == "getcount")
                {
                    var nftCount = new NftCount();
                    StorageMap nftCountMap = Storage.CurrentContext.CreateMap("nftCountMap");
                    var data = nftCountMap.Get("nftCount");
                    if (data.Length > 0)
                        nftCount = data.Deserialize() as NftCount;
                    return nftCount;
                }

                //管理员权限

                //停用合约操作、isStop != 0 表示合约已停用，停用所有 sendraw 和 getconfig 接口
                BigInteger isStop = Storage.Get(Storage.CurrentContext, "isStopped").AsBigInteger();
                if (method == "setpause")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    BigInteger setValue = (BigInteger)args[0];
                    if (isStop == setValue) return true;
                    Storage.Put(Storage.CurrentContext, "isStopped", setValue);
                    return true;
                }
                if (isStop != 0) return false;

                if (method == "getconfig") return GetConfig();

                //设置参数
                if (method == "setconfig")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    BigInteger silverPrice = (BigInteger) args[0];
                    BigInteger goldPrice = (BigInteger) args[1];
                    BigInteger platinumPrice = (BigInteger) args[2];
                    BigInteger diamondPrice = (BigInteger) args[3];

                    BigInteger leaguerInvitePoint = (BigInteger) args[4];
                    BigInteger silverInvitePoint = (BigInteger) args[5];

                    BigInteger goldInvitePoint = (BigInteger) args[6];
                    BigInteger platinumInvitePoint = (BigInteger) args[7];
                    BigInteger diamondInvitePoint = (BigInteger) args[8];

                    BigInteger goldUpgradePoint = (BigInteger) args[9];
                    BigInteger platinumUpgradePoint = (BigInteger) args[10];
                    BigInteger diamondUpgradePoint = (BigInteger) args[11];

                    byte[] gatheringAddress = (byte[])args[12];

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

                    StorageMap configMap = Storage.CurrentContext.CreateMap("configMap");
                    byte[] configBytes = Helper.Serialize(configs);
                    configMap.Put("config", configBytes);
                    return true;
                }

                //第一本证书创世发行
                if (method == "deploy")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] deploy_data = Storage.Get(Storage.CurrentContext, "initDeploy");
                    if (deploy_data.Length != 0) return false;
                    byte[] address = (byte[])args[0];
                    if (address.Length != 20) return false;
                    StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
                    byte[] tokenId = addressMap.Get(address);
                    if (tokenId.Length > 0) return false;
                    tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash);
                    var newNftInfo = CreateNft(address, tokenId, null);
                    SaveNftInfo(newNftInfo);

                    addressMap.Put(address, tokenId);
                    AddNftCount(newNftInfo.Rank);
                    SetTxInfo(null, address, tokenId);
                    Exchanged(null, address, tokenId); //notify
                    Storage.Put(Storage.CurrentContext, "initDeploy", 1);
                    return true;
                }

                //转手交易
                if (method == "exchange")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from.Length != 20 || to.Length != 20) return false;

                    //to已拥有或者from未拥有证书、均false
                    StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
                    byte[] toTokenId = addressMap.Get(to);
                    if (toTokenId.Length > 0) return false;
                    byte[] fromTokenId = addressMap.Get(from);
                    if (fromTokenId.Length == 0) return false;

                    var fromNftInfo = GetNftByTokenId(fromTokenId);
                    fromNftInfo.Owner = to;
                    SaveNftInfo(fromNftInfo);
                    addressMap.Delete(from);
                    addressMap.Put(to, fromTokenId);
                    SetTxInfo(from, to, fromTokenId);
                    Exchanged(from, to, fromTokenId);
                    return true;
                }

                Config config = GetConfig();
                if (config.SilverPrice == 0) return false;
                if (config.GatheringAddress.Length == 0) return false;
                //首次购买
                if (method == "buy")
                {
                    byte[] txid = (byte[])args[0];
                    byte[] inviterAddr = (byte[])args[1];
                    if (txid.Length == 0 || inviterAddr.Length != 20) return false;
                    var tx = GetBctTxInfo(txid);

                    //钱没给够或收款地址对不上均false
                    if (tx.@from.Length == 0 || tx.to.AsBigInteger() != config.GatheringAddress.AsBigInteger() || tx.value < config.SilverPrice)
                        return false;

                    //购买者已拥有或者邀请者未拥有证书、均false
                    StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
                    byte[] tokenId = addressMap.Get(tx.@from);
                    if (tokenId.Length > 0) return false;
                    var inviterTokenId = addressMap.Get(inviterAddr);
                    if (inviterTokenId.Length == 0) return false;

                    tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash);
                    var nftInfo = CreateNft(tx.from, tokenId, inviterTokenId);
                    SaveNftInfo(nftInfo);
                    addressMap.Put(tx.@from, tokenId);
                    AddNftCount(nftInfo.Rank);
                    var inviterNftInfo = GetNftByTokenId(inviterTokenId);
                    //给邀请者证书加分
                    inviterNftInfo.ContributionPoint += config.SilverInvitePoint;
                    SaveNftInfo(inviterNftInfo);
                    SetTxUsed(txid);
                    SetTxInfo(null, tx.@from, tokenId);
                    Exchanged(null, tx.@from, tokenId); //notify
                    return true;
                }

                //升级
                if (method == "upgrade")
                {
                    byte[] txid = (byte[]) args[0];
                    var tx = GetBctTxInfo(txid);
                    if (tx.@from.Length == 0 || tx.to.AsBigInteger() != config.GatheringAddress.AsBigInteger() || tx.value <= 0)
                        return false;
                    StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
                    byte[] tokenId = addressMap.Get(tx.@from);
                    if (tokenId.Length == 0) return false;
                    var nftInfo = GetNftByTokenId(tokenId);
                    if (CanUpgrade(config, nftInfo, tx))
                    {
                        nftInfo.Rank += 1;
                        SaveNftInfo(nftInfo);
                        AddNftCount(nftInfo.Rank);
                        SetTxUsed(txid);
                        //升级给邀请者加分
                        if (nftInfo.InviterTokenId.Length > 0)
                        {
                            BigInteger addPoint = GetAddPoint(config, nftInfo.Rank);
                            var inviterNftInfo = GetNftByTokenId(nftInfo.InviterTokenId);
                            inviterNftInfo.ContributionPoint += addPoint;
                            SaveNftInfo(inviterNftInfo);
                        }
                        Upgraded(tokenId, tx.@from, nftInfo.Rank - 1, nftInfo.Rank);//notify
                        return true;
                    }
                    return false;
                }

                //邀请普通会员加分
                if (method == "addpoint")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] address = (byte[])args[0];
                    if (address.Length != 20) return false;
                    StorageMap addressMap = Storage.CurrentContext.CreateMap("addressMap");
                    byte[] tokenId = addressMap.Get(address);
                    if (tokenId.Length == 0) return false;
                    var nftInfo = GetNftByTokenId(tokenId);
                    if (config.LeaguerInvitePoint == 0) return false;
                    nftInfo.ContributionPoint += config.LeaguerInvitePoint;
                    SaveNftInfo(nftInfo);
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

        public static Config GetConfig()
        {
            StorageMap configMap = Storage.CurrentContext.CreateMap("configMap");
            var configBytes = configMap.Get("config");
            if (configBytes.Length == 0)
                return new Config();
            return configBytes.Deserialize() as Config;
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

        public static NFTInfo CreateNft(byte[] owner, byte[] tokenId, byte[] inviterTokenId)
        {
            var nftInfo = new NFTInfo();
            nftInfo.TokenId = tokenId;
            nftInfo.Owner = owner;
            nftInfo.ContributionPoint = 0;
            nftInfo.Rank = 1;
            nftInfo.InviterTokenId = inviterTokenId;
            return nftInfo;
        }

        public static void AddNftCount(BigInteger rank)
        {
            var nftCount = new NftCount();
            StorageMap nftCountMap = Storage.CurrentContext.CreateMap("nftCountMap");
            var data = nftCountMap.Get("nftCount");
            if (data.Length > 0)
                nftCount = data.Deserialize() as NftCount;
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
            nftCountMap.Put("nftCount", nftCountBytes);
        }

        public static void SaveNftInfo(NFTInfo nftInfo)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] nftInfoBytes = Helper.Serialize(nftInfo);
            userNftInfoMap.Put(nftInfo.TokenId, nftInfoBytes);
        }

        public static NFTInfo GetNftByTokenId(byte[] tokenId)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] data = userNftInfoMap.Get(tokenId);
            var nftInfo = new NFTInfo();
            if (data.Length > 0)
                nftInfo = data.Deserialize() as NFTInfo;
            return nftInfo;
        }

        public static void SetTxInfo(byte[] from, byte[] to, byte[] tokenId)
        {
            ExchangeInfo info = new ExchangeInfo();
            info.@from = from;
            info.to = to;
            info.tokenId = tokenId;
            byte[] exInfo = Neo.SmartContract.Framework.Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            StorageMap ExchangeInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
            ExchangeInfoMap.Put(txid, exInfo);
        }

        public static ExchangeInfo GetTxInfoByTxid(byte[] txid)
        {
            ExchangeInfo info = new ExchangeInfo();
            StorageMap ExchangeInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
            var data = ExchangeInfoMap.Get(txid);
            if (data.Length > 0)
                info = data.Deserialize() as ExchangeInfo;
            return info;
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
