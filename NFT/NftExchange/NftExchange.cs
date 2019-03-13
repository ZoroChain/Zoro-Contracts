using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace NftExchange
{
    public class NftExchange : SmartContract
    {
        public delegate object NEP5Contract(string method, object[] args);

        [DisplayName("deposit")]
        public static event Action<byte[], byte[], BigInteger> EmitDepositted; // (address, assetID, amount)

        [DisplayName("withdraw")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawn; // (address, assetID, amount)
        // Events
        [DisplayName("makeOffer")]
        public static event Action<byte[], byte[], byte[], byte[], byte[], BigInteger, byte[], BigInteger> EmitCreated; // (address, offerHash, nftContractHash, sellNftId, acceptAssetId, price, feeAssetId, feeAmount)

        [DisplayName("fillOffer")]
        public static event Action<byte[], byte[], byte[], BigInteger, byte[], byte[], byte[], BigInteger> EmitFilled; // (fillerAddress, offerHash, fillAssetID, fillAmount, nftContractHash, sellNftId, fillFeeAssetID, fillFeeAmount)

        [DisplayName("cancelOffer")]
        public static event Action<byte[], byte[], byte[], byte[], byte[], BigInteger> EmitCancelled; // (address, offerHash, nftContractHash, nftId, feeAssetId, feeReturnAmount)

        [DisplayName("addedToWhitelist")]
        public static event Action<byte[]> EmitAddedToWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("removedFromWhitelist")]
        public static event Action<byte[]> EmitRemovedFromWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("feeAddressSet")]
        public static event Action<byte[]> EmitFeeAddressSet; // (address)

        [DisplayName("dealerAddressSet")]
        public static event Action<byte[]> EmitDealerAddressSet; // (address)

        [DisplayName("initialized")]
        public static event Action EmitInitialized;

        // Contract States
        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        // superAdmin
        private static readonly byte[] superAdmin = "AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s".ToScriptHash();

        public static object Main(string operation, object[] args)
        {
            var magicstr = "NFT Exchange Template v0.1";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var entryscript = ExecutionEngine.EntryScriptHash;
                var callscript = ExecutionEngine.CallingScriptHash;

                // 设置合约状态          
                if (operation == "setState")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1) return false;
                    return SetState((BigInteger)args[0]);
                }

                if (operation == "getState") return GetState();

                if (GetState() == AllStop) return false;

                // == Getters ==
                if (operation == "getOffer") return GetOffer((byte[])args[0]); //offerHash               
                if (operation == "getAvailabelBalance") return GetAvailabelBalance((byte[])args[0], (byte[])args[1]); //address, assetID
                if (operation == "getIsWhitelisted") return GetIsWhitelisted((byte[])args[0]);  // (assetID)
                if (operation == "getFeeAddress") return GetFeeAddress(); //收交易费账户
                if (operation == "getDealerAddress") return GetDealerAddress();

                if (GetState() != Active) return false;

                //存钱 充值
                if (operation == "deposit") // (originator, assetID, value, isGlobal)
                {
                    if (args.Length != 4) return false;
                    return Deposit((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (BigInteger)args[3]);
                }
                //取钱 提现
                if (operation == "withdraw") // originator, withdrawAssetId, withdrawAmount, isGlobal
                {
                    if (args.Length != 4) return false;
                    return Withdrawal((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (BigInteger)args[3]);
                }

                if (operation == "makeOffer")
                {
                    if (args.Length != 8) return false;

                    BigInteger time = (BigInteger)args[7];
                    BigInteger curTime = Runtime.Time;
                    if (curTime - time > 300) return false;

                    //originator  nftContractHash  sellNftId  acceptAssetId  price  feeAssetId  feeAmount                
                    return MakeOffer((byte[])args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3], (BigInteger)args[4], (byte[])args[5], (BigInteger)args[6]);

                }

                if (operation == "fillOffer")
                {
                    if (args.Length != 7) return false;

                    BigInteger time = (BigInteger)args[6];
                    BigInteger curTime = Runtime.Time;
                    if (curTime - time > 300) return false;

                    // fillerAddress, offerHash, fillAssetId, fillAmount, fillFeeAssetID, fillFeeAmount
                    return FillOffer((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3], (byte[])args[4], (BigInteger)args[5]);
                }

                //取消挂单
                if (operation == "cancelOffer") // (offerHash)
                {
                    if (args.Length != 1) return false;
                    return CancelOffer((byte[])args[0]);
                }

                // 管理员签名
                if (!Runtime.CheckWitness(superAdmin)) return false;

                if (operation == "initialize")
                {
                    if (args.Length != 2) return false;
                    return Initialize((byte[])args[0], (byte[])args[1]);
                }
                if (operation == "setDealerAddress")
                {
                    if (args.Length != 1) return false;
                    return SetDealerAddress((byte[])args[0]);
                }

                if (operation == "setFeeAddress")
                {
                    if (args.Length != 1) return false;
                    return SetFeeAddress((byte[])args[0]);
                }
                if (operation == "addToWhitelist")
                {
                    if (args.Length != 1) return false;
                    return AddToWhitelist((byte[])args[0]);
                }
                if (operation == "removeFromWhitelist")
                {
                    if (args.Length != 1) return false;
                    return RemoveFromWhitelist((byte[])args[0]);
                }
            }

            return false;
        }

        private static bool MakeOffer(byte[] makerAddress, byte[] nftContractHash, byte[] sellNftId, byte[] acceptAssetId, BigInteger price, byte[] feeAeestId, BigInteger feeAmount)
        {
            Offer offer = new Offer
            {
                Address = makerAddress,
                NftContract = nftContractHash,
                NftId = sellNftId,
                AcceptAssetId = acceptAssetId,
                Price = price,
                FeeAeestId = feeAeestId,
                FeeAmount = feeAmount
            };

            var dealerAddress = GetDealerAddress();

            if (!Runtime.CheckWitness(dealerAddress)) return false;
            if (!Runtime.CheckWitness(makerAddress)) return false;
            if (dealerAddress == makerAddress) return false;

            // Check that nonce is not repeated
            var offerHash = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            if (Storage.Get(Context(), OfferKey(offerHash)).Length != 0) return false;

            if (!GetIsWhitelisted(nftContractHash) || !GetIsWhitelisted(feeAeestId)) return false;

            // Check that the amounts > 0
            if (feeAmount <= 0) return false;           

            // Check that asset IDs are valid
            if (nftContractHash.Length != 20 ||feeAeestId.Length != 20) return false;

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            // Check that there is enough balance in native fees if using native fees
            if (GetAvailabelBalance(makerAddress, feeAeestId) < feeAmount) throw new Exception("Invalid available balance!");
            // Reduce fee
            ReduceAvailabelBalance(makerAddress, feeAeestId, feeAmount);

            var args = new object[] { makerAddress, ExecutionEngine.ExecutingScriptHash, sellNftId };
            var contract = (NEP5Contract)nftContractHash.ToDelegate();
            bool success = (bool)contract("transferFrom", args);

            if(!success) throw new Exception("transferFrom failed!");

            var offerData = offer.Serialize();
            Storage.Put(Context(), OfferKey(offerHash), offerData);

            // Notify (address, offerHash, offerAssetID, offerAmount, wantAssetID, wantAmount, feeAssetId, feeAmount)
            EmitCreated(makerAddress, offerHash, nftContractHash, sellNftId, acceptAssetId, price, feeAeestId, feeAmount);
            return true;
        }

        private static bool FillOffer(byte[] fillerAddress, byte[] offerHash, byte[] fillAssetId, BigInteger fillAmount, byte[] fillFeeAssetID, BigInteger fillFeeAmount)
        {
            var dealerAddress = GetDealerAddress();

            if (!Runtime.CheckWitness(dealerAddress)) return false;
            if (!Runtime.CheckWitness(fillerAddress)) return false;
            if (dealerAddress == fillerAddress) return false;

            // Check fees
            if (fillFeeAssetID.Length != 20) return false;
            if (fillFeeAmount < 0) return false;
            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            Offer offer = GetOffer(offerHash);
            if (offer.Address.Length == 0) return false;
            if (fillerAddress == offer.Address) return false;
            if (offer.AcceptAssetId != fillAssetId) return false;

            if (fillAmount < 1) return false;

            if (GetAvailabelBalance(fillerAddress, fillFeeAssetID) < fillFeeAmount) throw new Exception("Invalid available balance!");
            // Reduce fees
            ReduceBalance(fillerAddress, fillFeeAssetID, fillFeeAmount);
            ReduceAvailabelBalance(fillerAddress, fillFeeAssetID, fillFeeAmount);
            //add fee
            IncreaseBalance(feeAddress, fillFeeAssetID, fillFeeAmount);

            var fillerBalance = GetAvailabelBalance(fillerAddress, fillAssetId);
            if (fillerBalance < fillAmount) throw new Exception("Invalid available balance!");
            // Reduce balance from filler
            ReduceBalance(fillerAddress, fillAssetId, fillAmount);
            ReduceAvailabelBalance(fillerAddress, fillAssetId, fillAmount);                     

            //add offer.MakerAddress amount
            IncreaseBalance(offer.Address, fillAssetId, fillAmount);
            IncreaseAvailableBalance(offer.Address, fillAssetId, fillAmount);                    

            var args = new object[] { ExecutionEngine.ExecutingScriptHash, fillerAddress, offer.NftId };
            var contract = (NEP5Contract)offer.NftContract.ToDelegate();
            bool success = (bool)contract("transferApp", args);

            if (!success) throw new Exception("transferApp failed!");

            Storage.Delete(Context(), OfferKey(offerHash));

            // (fillerAddress, offerHash, fillAssetID, fillAmount, nftContractHash, offer.SellNftId, fillFeeAssetID, fillFeeAmount)
            EmitFilled(fillerAddress, offerHash, fillAssetId, fillAmount, offer.NftContract, offer.NftId, fillFeeAssetID, fillFeeAmount);

            return true;
        }

        private static bool CancelOffer(byte[] offerHash)
        {
            // Check that the offer exists
            Offer offer = GetOffer(offerHash);
            if (offer.Address == new byte[] { }) return false;

            if (!Runtime.CheckWitness(offer.Address)) return false;
                       
            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            var feeAddressBalance = GetBalance(feeAddress, offer.FeeAeestId);

            if (feeAddressBalance < offer.FeeAmount) return false;

            //add fee to MakerAddress
            IncreaseAvailableBalance(offer.Address, offer.FeeAeestId, offer.FeeAmount);

            var args = new object[] { ExecutionEngine.ExecutingScriptHash, offer.Address, offer.NftId };
            var contract = (NEP5Contract)offer.NftContract.ToDelegate();
            bool success = (bool)contract("transferApp", args);

            if (!success) throw new Exception("transferApp failed!");

            // Remove offer
            Storage.Delete(Context(), OfferKey(offerHash));

            // Notify (address, offerHash, offerAssetId, returnAmount, feeAssetId, feeReturnAmount)
            EmitCancelled(offer.Address, offerHash, offer.NftContract, offer.NftId, offer.FeeAeestId, offer.FeeAmount);
            return true;
        }

        private static Offer GetOffer(byte[] offerHash)
        {
            byte[] offerData = Storage.Get(Context(), OfferKey(offerHash));
            if (offerData.Length == 0) return new Offer();

            return (Offer)offerData.Deserialize();
        }

        /***********
         * Deposit *
         ***********/
        private static bool Deposit(byte[] originator, byte[] assetId, BigInteger value, BigInteger isGlobal)
        {
            if (!Runtime.CheckWitness(originator)) return false;

            // Check that the contract is safe
            if (!GetIsWhitelisted(assetId)) return false;

            byte[] to = ExecutionEngine.ExecutingScriptHash;
            bool success = false;

            //全局资产 native nep5
            if (isGlobal == 1)
            {
                success = NativeAsset.Call("TransferFrom", assetId, originator, to, value);
            }
            if (isGlobal == 0)
            {
                var args = new object[] { originator, to, value };
                var contract = (NEP5Contract)assetId.ToDelegate();
                success = (bool)contract("transferFrom", args);
            }
            if (success)
            {
                IncreaseBalance(originator, assetId, value);

                IncreaseAvailableBalance(originator, assetId, value);

                EmitDepositted(originator, assetId, value);
                return true;
            }
            else
                throw new Exception("Failed to transferFrom");
        }

        /***********
         * Withdrawal *
         ***********/
        private static bool Withdrawal(byte[] originator, byte[] assetId, BigInteger amount, BigInteger isGlobal)
        {
            if (!Runtime.CheckWitness(originator)) return false;

            if (originator.Length != 20) return false;

            var originatorBalance = GetAvailabelBalance(originator, assetId);

            if (originatorBalance < amount) return false;

            bool success = false;
            byte[] from = ExecutionEngine.ExecutingScriptHash;

            if (isGlobal == 1)
            {
                success = NativeAsset.Call("TransferApp", assetId, from, originator, amount);
            }
            if (isGlobal == 0)
            {
                var args = new object[] { from, originator, amount };
                var contract = (NEP5Contract)assetId.ToDelegate();
                success = (bool)contract("transferApp", args);
            }

            if (success)
            {
                ReduceBalance(originator, assetId, amount);

                ReduceAvailabelBalance(originator, assetId, amount);

                EmitWithdrawn(originator, assetId, amount);
                return true;
            }
            else
                throw new Exception("Failed to withdrawal transfer");
        }

        private static bool IncreaseBalance(byte[] originator, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            byte[] key = BalanceKey(originator, assetID);
            BigInteger currentBalance = Storage.Get(Context(), key).AsBigInteger();
            Storage.Put(Context(), key, currentBalance + amount);
            //EmitChangedBalance(originator, assetID, amount, reason);

            return true;
        }

        private static bool IncreaseAvailableBalance(byte[] originator, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            byte[] key = AvailableBalanceKey(originator, assetID);
            BigInteger currentBalance = Storage.Get(Context(), key).AsBigInteger();
            Storage.Put(Context(), key, currentBalance + amount);
            //EmitChangedBalance(originator, assetID, amount, reason);

            return true;
        }

        private static bool ReduceBalance(byte[] address, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            var key = BalanceKey(address, assetID);
            var currentBalance = Storage.Get(Context(), key).AsBigInteger();
            var newBalance = currentBalance - amount;

            if (newBalance < 0) throw new Exception("Invalid available balance!");

            if (newBalance > 0) Storage.Put(Context(), key, newBalance);
            else Storage.Delete(Context(), key);

            //EmitChangedBalance(address, assetID, 0 - amount, reason);
            return true;
        }

        private static bool ReduceAvailabelBalance(byte[] address, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            var key = AvailableBalanceKey(address, assetID);
            var currentBalance = Storage.Get(Context(), key).AsBigInteger();
            var newBalance = currentBalance - amount;

            if (newBalance < 0) throw new Exception("Invalid available balance!");

            if (newBalance > 0) Storage.Put(Context(), key, newBalance);
            else Storage.Delete(Context(), key);

            //EmitChangedBalance(address, assetID, 0 - amount, reason);
            return true;
        }

        private static BigInteger GetBalance(byte[] address, byte[] assetID)
        {
            if (address.Length != 20 || assetID.Length != 20) return 0;
            return Storage.Get(Context(), BalanceKey(address, assetID)).AsBigInteger();
        }

        private static BigInteger GetAvailabelBalance(byte[] address, byte[] assetID)
        {
            if (address.Length != 20 || assetID.Length != 20) return 0;
            return Storage.Get(Context(), AvailableBalanceKey(address, assetID)).AsBigInteger();
        }

        private static bool Initialize(byte[] feeAddress, byte[] dealerAddress)
        {
            if (!SetFeeAddress(feeAddress)) throw new Exception("Failed to set fee address");
            if (!SetDealerAddress(dealerAddress)) throw new Exception("Failed to set the dealer address");
            EmitInitialized();
            return true;
        }

        private static bool SetDealerAddress(byte[] dealerAddress)
        {
            if (dealerAddress.Length != 20) return false;
            Storage.Put(Context(), "dealerAddress", dealerAddress);
            EmitDealerAddressSet(dealerAddress);
            return true;
        }

        private static bool SetFeeAddress(byte[] feeAddress)
        {
            if (feeAddress.Length != 20) return false;
            Storage.Put(Context(), "feeAddress", feeAddress);
            EmitFeeAddressSet(feeAddress);
            return true;
        }

        private static bool AddToWhitelist(byte[] scriptHash)
        {
            if (scriptHash.Length != 20) return false;
            var key = WhitelistKey(scriptHash);
            Storage.Put(Context(), key, 1);
            EmitAddedToWhitelist(scriptHash);
            return true;
        }

        private static bool RemoveFromWhitelist(byte[] scriptHash)
        {
            if (scriptHash.Length != 20) return false;
            var key = WhitelistKey(scriptHash);
            Storage.Delete(Context(), key);
            EmitRemovedFromWhitelist(scriptHash);
            return true;
        }

        private static bool GetIsWhitelisted(byte[] assetID)
        {
            if (assetID.Length != 20) return false;
            if (Storage.Get(Context(), WhitelistKey(assetID)).AsBigInteger() == 1)
                return true;
            return false;
        }

        private static byte[] GetDealerAddress() => Storage.Get(Context(), "dealerAddress");

        private static byte[] GetFeeAddress() => Storage.Get(Context(), "feeAddress");

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static bool SetState(BigInteger setValue)
        {
            if (setValue == 0)
                Storage.Put(Context(), "state", Active);
            if (setValue == 1)
                Storage.Put(Context(), "state", Inactive);
            if (setValue == 2)
                Storage.Put(Context(), "state", AllStop);
            return true;
        }

        private static StorageContext Context() => Storage.CurrentContext;

        // Keys
        private static byte[] OfferKey(byte[] offerHash) => "offers".AsByteArray().Concat(offerHash);
        private static byte[] AvailableBalanceKey(byte[] originator, byte[] assetID) => "availableBalance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] BalanceKey(byte[] originator, byte[] assetID) => "balance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] WhitelistKey(byte[] assetId) => "whiteList".AsByteArray().Concat(assetId);

        private class Offer
        {
            public byte[] Address;
            public byte[] NftContract;
            public byte[] NftId;
            public byte[] AcceptAssetId;
            public BigInteger Price;
            public byte[] FeeAeestId;
            public BigInteger FeeAmount;
        }

        //originator nftContractHash  sellNftId acceptAssetId  price feeAssetId  feeAmount
        private static Offer NewOffer(byte[] makerAddress, byte[] nftContractHash, byte[] sellNftId, byte[] acceptAssetId, BigInteger price, byte[] feeAeestId, BigInteger feeAmount)
        {
            return new Offer
            {
                Address = makerAddress,
                NftContract = nftContractHash,
                NftId = sellNftId,
                AcceptAssetId = acceptAssetId,
                Price = price,
                FeeAeestId = feeAeestId,
                FeeAmount = feeAmount
            };
        }
    }
}
