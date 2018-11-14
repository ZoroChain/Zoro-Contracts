using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Configuration;
using System.Numerics;
using System.Security.Policy;
using Helper = Neo.SmartContract.Framework.Helper;

namespace NFT_Token
{
    /// <summary>
    /// BlaCat合伙人证书 NFT
    /// </summary>
    public class PartnerToken : SmartContract
    {
        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AM5ho5nEodQiai1mCTFDV3YUNYApCorMCX");
        //收款地址
        private static readonly byte[] recMoneyAddr = Neo.SmartContract.Framework.Helper.ToScriptHash("AM5ho5nEodQiai1mCTFDV3YUNYApCorMCX");
        //BCT合约hash
        [Appcall("40a80749ef62da6fc3d74dbf6fc7745148922372")]
        static extern object bctCall(string method, object[] arr);

        public delegate void deleExchange(byte[] from, byte[] to, byte[] tokenId);
        [DisplayName("exchange")]
        public static event deleExchange Exchanged;

        public delegate void deleBuy(byte[] address, byte[] tokenId, byte[] lastLine);
        [DisplayName("buy")]
        public static event deleBuy Bought;

        public class ExchangeInfo
        {
            public byte[] from;
            public byte[] to;
            public byte[] tokenId;
        }

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
                if (method == "name")
                    return Name();
                if (method == "symbol")
                    return Symbol();
                if (method == "getnftinfo")
                {
                    byte[] address = (byte[]) args[0];
                    if (address.Length == 0)
                        return false;
                    return GetNFTByAddress(address);
                }

                if (method == "gettxinfo")
                {
                    byte[] txid = (byte[]) args[0];
                    if (txid.Length == 0)
                        return false;
                    return GetTxInfoByTxid(txid);
                }

                //管理员权限

                if (method == "getcount")
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    return GetConfig();
                }

                if (method == "getconfig")
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var nftCount = new NftCount();
                    StorageMap nftCountMap = Storage.CurrentContext.CreateMap("nftCountMap");
                    var data = nftCountMap.Get("nftCount");
                    if (data.Length > 0)
                        nftCount = data.Deserialize() as NftCount;
                    return nftCount;
                }

                if (method == "setconfig")
                {
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    BigInteger SilverPrice = (BigInteger) args[0];
                    BigInteger GoldPrice = (BigInteger) args[1];
                    BigInteger PlatinumPrice = (BigInteger) args[2];
                    BigInteger DiamondPrice = (BigInteger) args[3];
                    
                    BigInteger silverInvitePoint = (BigInteger) args[4];
                    BigInteger goldInvitePoint =(BigInteger) args[5];
                    BigInteger platinumInvitePoint = (BigInteger)args[6];
                    BigInteger diamondInvitePoint = (BigInteger)args[7];

                    BigInteger goldUpgradePoint = (BigInteger)args[8];
                    BigInteger platinumUpgradePoint = (BigInteger)args[9];
                    BigInteger diamondUpgradePoint = (BigInteger)args[10];

                    var config = new Config()
                    {
                        SilverPrice = SilverPrice,GoldPrice = GoldPrice,PlatinumPrice = PlatinumPrice,DiamondPrice = DiamondPrice,
                        SilverInvitePoint = silverInvitePoint, GoldInvitePoint = goldInvitePoint,
                        PlatinumInvitePoint = platinumInvitePoint, DiamondInvitePoint = diamondInvitePoint,
                        GoldUpgradePoint = goldUpgradePoint, PlatinumUpgradePoint = platinumUpgradePoint,
                        DiamondUpgradePoint = diamondUpgradePoint
                    };

                    StorageMap configMap = Storage.CurrentContext.CreateMap("configMap");
                    byte[] configBytes = Helper.Serialize(config);
                    configMap.Put("config", configBytes);

                }

                //内部发行
                if (method == "deploy")
                {
                    byte[] address = (byte[]) args[0];
                    var tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash);
                    if (tokenId.Length == 0 || address.Length == 0)
                        return false;
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    if (IsHaveNft(address))
                        return false;
                    var newNftInfo = CreateNft(address, tokenId, null);
                    if (SaveNftInfo(newNftInfo))
                        return newNftInfo;
                    return false;
                }

                //首次购买
                if (method == "buy")
                {
                    byte[] txid = (byte[]) args[0];
                    byte[] lastLine = (byte[])args[1];
                    var tokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash);
                    if (tokenId.Length == 0 || txid.Length == 0 || lastLine.Length == 0)
                        return false;
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var config = GetConfig();
                    if (config.SilverPrice == 0)
                        return false;
                    var tx = GetBctTxInfo(txid);
                    if (tx.@from.Length == 0 || tx.to != recMoneyAddr || tx.value < config.SilverPrice)
                        return false;
                    if (IsHaveNft(tx.from)|| !IsHaveNft(lastLine)) //购买者已拥有或者上线未拥有证书、均false
                        return false;
                    var newNftInfo = CreateNft(tx.from, tokenId, lastLine);
                    if (SaveNftInfo(newNftInfo))
                    {
                        AddPoint(lastLine);
                        SetTxUsed(txid);
                        SetTxInfo(null, tx.@from, tokenId);
                        Bought(tx.@from, tokenId, lastLine); //notify
                        return newNftInfo;
                    }
                    return false;
                }

                //转手交易
                if (method == "exchange")
                {
                    byte[] from = (byte[]) args[0];
                    byte[] to = (byte[]) args[1];
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    if (from.Length == 0 || to.Length == 0)
                        return false;
                    if (!IsHaveNft(from) || IsHaveNft(to))
                        return false;
                    var nftInfo = GetNFTByAddress(from);
                    nftInfo.owner = to;
                    if(SaveNftInfo(nftInfo))
                    {
                        DeleteNftInfo(from);
                        SetTxInfo(from, to, nftInfo.tokenId);
                        Exchanged(from, to, nftInfo.tokenId);
                        return true;
                    }

                    return false;
                }

                //升级
                if (method == "upgrade")
                {
                    byte[] txid = (byte[]) args[0];
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var tx = GetBctTxInfo(txid);
                    if (tx.@from.Length == 0 || tx.to != recMoneyAddr || tx.value <= 0)
                        return false;
                    if (!IsHaveNft(tx.@from))
                        return false;
                    var nftInfo = GetNFTByAddress(tx.@from);
                    if (CanUpgrade(nftInfo, tx))
                    {
                        nftInfo.Rank += 1;
                        SaveNftInfo(nftInfo);
                        AddNftCount(nftInfo);
                        SetTxUsed(txid);
                        return true;
                    }
                    return false;
                }
            }

            return false;
        }

        public static bool CanUpgrade(NFTInfo nftInfo, TransferInfo tx)
        {
            var config = GetConfig();
            if (config.SilverPrice == 0)
                return false;
            if (nftInfo.Rank == 1 && nftInfo.contributionPoint >= config.GoldUpgradePoint &&
                tx.value >= config.GoldPrice)
                return true;
            if (nftInfo.Rank == 2 && nftInfo.contributionPoint >= config.PlatinumUpgradePoint &&
                tx.value >= config.PlatinumPrice)
                return true;
            if (nftInfo.Rank == 3 && nftInfo.contributionPoint >= config.DiamondUpgradePoint &&
                tx.value >= config.DiamondPrice)
                return true;
            return false;
        }

        public static Config GetConfig()
        {
            StorageMap configMap = Storage.CurrentContext.CreateMap("configMap");
            var configBytes = configMap.Get("config");
            if (configBytes.Length == 0)
                return new Config(){SilverPrice = 0};
            return configBytes.Deserialize() as Config;
        }

        public static bool AddPoint(byte[] address)
        {
            var config = GetConfig();
            if (config.SilverPrice == 0)
                return false;
            var nftInfo = GetNFTByAddress(address);
            if (nftInfo.tokenId == null)
                return false;
            if (nftInfo.Rank == 1)
                nftInfo.contributionPoint += config.SilverInvitePoint;
            if (nftInfo.Rank == 2)
                nftInfo.contributionPoint += config.GoldInvitePoint;
            if (nftInfo.Rank == 3)
                nftInfo.contributionPoint += config.PlatinumInvitePoint;
            if (nftInfo.Rank == 4)
                nftInfo.contributionPoint += config.DiamondInvitePoint;
            return SaveNftInfo(nftInfo); 
        }

        public static bool IsHaveNft(byte[] address)
        {
            var nftInfo = GetNFTByAddress(address);
            if (nftInfo.tokenId == null)
                return false;
            return true;
        }

        public static NFTInfo CreateNft(byte[] owner, byte[] tokenId, byte[] lastLine)
        {
            var nftInfo = new NFTInfo();
            nftInfo.tokenId = tokenId;
            nftInfo.owner = owner;
            nftInfo.contributionPoint = 0;
            nftInfo.Rank = 1;
            nftInfo.LastLine = lastLine;
            AddNftCount(nftInfo);
            return nftInfo;
        }

        public static void AddNftCount(NFTInfo nftInfo)
        {
            var nftCount = new NftCount();
            StorageMap nftCountMap = Storage.CurrentContext.CreateMap("nftCountMap");
            var data = nftCountMap.Get("nftCount");
            if (data.Length > 0)
                nftCount = data.Deserialize() as NftCount;
            if (nftInfo.Rank == 1)
            {
                nftCount.AllCount += 1;
                nftCount.SilverCount += 1;
            }
            if (nftInfo.Rank == 2)
            {
                nftCount.GoldCount += 1;
                nftCount.SilverCount -= 1;
            }
            if (nftInfo.Rank == 3)
            {
                nftCount.PlatinumCount += 1;
                nftCount.GoldCount -= 1;
            }
            if (nftInfo.Rank == 4)
            {
                nftCount.DiamondCount += 1;
                nftCount.PlatinumCount -= 1;
            }
            nftCountMap.Put("nftCount", Helper.Serialize(nftCount));
        }

        public static bool SaveNftInfo(NFTInfo nftInfo)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] nftInfoBytes = Helper.Serialize(nftInfo);
            userNftInfoMap.Put(nftInfo.owner, nftInfoBytes);
            return true;
        }

        public static bool DeleteNftInfo(byte[] address)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            userNftInfoMap.Delete(address);
            return true;
        }

        public static NFTInfo GetNFTByAddress(byte[] address)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] data = userNftInfoMap.Get(address);
            if (data.Length == 0)
                return new NFTInfo();
            return data.Deserialize() as NFTInfo;
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

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }

    //证书信息
    public class NFTInfo
    {
        public byte[] tokenId = null; //tokenid 证书ID
        public byte[] owner; //所有者 address
        public BigInteger Rank; //等级
        public BigInteger contributionPoint; //贡献值
        public byte[] LastLine; //上线
    }

    //配置
    public class Config
    {
        public BigInteger SilverPrice; //白银购买价格
        public BigInteger GoldPrice; //升级黄金价格
        public BigInteger PlatinumPrice; //升级铂金价格
        public BigInteger DiamondPrice; //升级钻石价格

        public BigInteger SilverInvitePoint; //白银邀请所得贡献值
        public BigInteger GoldInvitePoint; //黄金邀请所得贡献值
        public BigInteger PlatinumInvitePoint; //铂金邀请所得贡献值
        public BigInteger DiamondInvitePoint; //钻石邀请所得贡献值

        public BigInteger GoldUpgradePoint; //升级黄金所需贡献值
        public BigInteger PlatinumUpgradePoint; //升级铂金所需贡献值
        public BigInteger DiamondUpgradePoint; //升级钻石所需贡献值
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
