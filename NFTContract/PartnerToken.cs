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
        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AM5ho5nEodQiai1mCTFDV3YUNYApCorMCX");

        [Appcall("40a80749ef62da6fc3d74dbf6fc7745148922372")]
        static extern object bctCall(string method, object[] arr);

        public delegate void deleExchange(byte[] from, byte[] to, byte[] tokenId);
        [DisplayName("exchange")]
        public static event deleExchange Exchanged;

        public class ExchangeInfo
        {
            public byte[] from;
            public byte[] to;
            public byte[] tokenId;
        }

        public static string Name() => "BlaCat Partner Certificate Token";//Blacat 合伙人证书 NFT
        
        public static string Symbol() => "BPT";//简称
        
        public static string Version() => "1.0.0"; //版本

        public static byte decimals()
        {
            return 1;
        }

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

                //deploy(to)  发行接口，to:接收者的地址 不公开的接口

                //buy(txid,lastline) 购买接口，txid：购买者的发送BCT转账的txid，lastline:上线地址
                
                //exchange(from,to) 交易接口，from,to
                //upgrade(txid) 升级接口，txid：购买者发送BCT转账的txid,

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

                //管理员权限
                if (method == "setconfig")
                {
                    BigInteger silverInvitePoint = (BigInteger) args[0];
                    BigInteger goldInvitePoint =(BigInteger) args[0];
                    BigInteger platinumInvitePoint = (BigInteger)args[0];
                    BigInteger diamondInvitePoint = (BigInteger)args[0];

                    BigInteger goldUpgradePoint = (BigInteger)args[0];
                    BigInteger platinumUpgradePoint = (BigInteger)args[0];
                    BigInteger diamondUpgradePoint = (BigInteger)args[0];

                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var config = new Config()
                    {
                        SilverInvitePoint = silverInvitePoint, GoldInvitePoint = goldInvitePoint,
                        PlatinumInvitePoint = platinumInvitePoint, DiamondInvitePoint = diamondInvitePoint,
                        GoldUpgradePoint = goldUpgradePoint, PlatinumUpgradePoint = platinumUpgradePoint,
                        DiamondUpgradePoint = diamondUpgradePoint
                    };

                    StorageMap configMap = Storage.CurrentContext.CreateMap("configMap");
                    byte[] configBytes = Helper.Serialize(config);
                    configMap.Put("config", configBytes);

                }

                if (method == "deploy")
                {
                    byte[] tokenId = (byte[]) args[0];
                    byte[] to = (byte[]) args[1];
                    if (tokenId.Length == 0 || to.Length == 0)
                        return false;
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var nftInfo = GetNFTByAddress(to);
                    if (nftInfo.tokenId == null)
                    {
                        nftInfo = CreateNft(to, tokenId);
                        Deploy(nftInfo);
                        return nftInfo;
                    }
                    return false;
                }

                if (method == "buy")
                {
                    byte[] tokenId = (byte[])args[0];
                    byte[] txid = (byte[]) args[1];
                    byte[] lastLine = (byte[])args[2];
                    if (tokenId.Length == 0 || txid.Length == 0 || lastLine.Length == 0)
                        return false;
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    var tx = GetTxInfo(txid);
                    if (tx.@from.Length == 0 || tx.to != superAdmin || tx.value <= 0)
                        return false;
                    var nftInfo = GetNFTByAddress(lastLine);
                    if (nftInfo.tokenId == null)
                        return false;
                    SetTxUsed(txid);
                    return false;
                }
            }

            return false;
        }

        private static NFTInfo CreateNft(byte[] to, byte[] tokenId)
        {
            var nftInfo = new NFTInfo();
            nftInfo.tokenId = tokenId;
            nftInfo.owner = to;
            nftInfo.contributionPoint = 0;
            nftInfo.Rank = 1;
            nftInfo.LastLine = null;
            return nftInfo;
        }

        public static bool Deploy(NFTInfo nftInfo)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] nftInfoBytes = Helper.Serialize(nftInfo);
            userNftInfoMap.Put(nftInfo.owner, nftInfoBytes);
            return true;
        }

        public static NFTInfo GetNFTByAddress(byte[] address)
        {
            StorageMap userNftInfoMap = Storage.CurrentContext.CreateMap("userNftInfoMap");
            byte[] data = userNftInfoMap.Get(address);
            if (data.Length == 0)
                return new NFTInfo(){tokenId = null};
            return data.Deserialize() as NFTInfo;
        }

        public static TransferInfo GetTxInfo(byte[] txid)
        {
            StorageMap txInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
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
            StorageMap txInfoMap = Storage.CurrentContext.CreateMap("txInfoMap");
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
        public byte[] tokenId; //tokenid 证书ID
        public byte[] owner; //所有者 address
        public BigInteger Rank; //等级
        public BigInteger contributionPoint; //贡献值
        public byte[] LastLine; //上线
    }

    public class Config
    {
        public BigInteger SilverInvitePoint; //白银邀请所得贡献值
        public BigInteger GoldInvitePoint; //黄金邀请所得贡献值
        public BigInteger PlatinumInvitePoint; //铂金邀请所得贡献值
        public BigInteger DiamondInvitePoint; //钻石邀请所得贡献值

        public BigInteger GoldUpgradePoint; //升级黄金所需贡献值
        public BigInteger PlatinumUpgradePoint; //升级铂金所需贡献值
        public BigInteger DiamondUpgradePoint; //升级钻石所需贡献值
    }
}
