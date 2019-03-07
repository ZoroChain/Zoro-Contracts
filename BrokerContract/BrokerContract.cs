using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace BrokerContract
{
    public class BrokerContract : SmartContract
    {
        public delegate object NEP5Contract(string method, object[] args);

        // Events
        [DisplayName("makeOffer")]
        public static event Action<byte[], byte[], byte[], BigInteger, byte[], BigInteger,byte[], BigInteger> EmitCreated; // (address, offerHash, offerAssetID, offerAmount, wantAssetID, wantAmount, feeAssetId, feeAmount)

        [DisplayName("fillOffer")]
        public static event Action<byte[], byte[], byte[], BigInteger, byte[], BigInteger, byte[], BigInteger> EmitFilled; // (fillerAddress, offerHash, fillAssetID, fillAmount, fillerGetAssetID, fillerGetAmount, fillFeeAssetID, fillFeeAmount)

        [DisplayName("cancelOffer")]
        public static event Action<byte[], byte[], byte[], BigInteger, byte[], BigInteger> EmitCancelled; // (address, offerHash, offerAssetId, returnAmount, feeAssetId, feeReturnAmount)

        //[DisplayName("changeBalance")]
        //public static event Action<byte[], byte[], BigInteger, string> EmitChangedBalance; // (address, assetID, amount, reason)

        [DisplayName("deposit")]
        public static event Action<byte[], byte[], BigInteger> EmitDepositted; // (address, assetID, amount)

        [DisplayName("withdraw")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawn; // (address, assetID, amount)

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

        // superAdmin
        private static readonly byte[] superAdmin = "AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s".ToScriptHash();

        // Contract States
        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        /// <summary>
        ///  BlaCat Token Exchange Contract
        ///  Parameter List: 0710
        ///  Return List: 05
        /// </summary>
        /// <param name="operation">
        /// </param>
        /// <param name="args">
        /// </param>
        public static object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Application)
            {
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
                if (operation == "getBalance") return GetBalance((byte[])args[0], (byte[])args[1]); //address, assetID
                if (operation == "getIsWhitelisted") return GetIsWhitelisted((byte[])args[0]);  // (assetID)
                if (operation == "getFeeAddress") return GetFeeAddress(); //收交易费账户
                if (operation == "getDealerAddress") return GetDealerAddress();

                if (GetState() != Active) return false;

                //存钱 充值
                if (operation == "deposit") // (originator, assetID, value, isGlobal) 存钱，如果不能跳板调用的话需要支持 txid 存钱
                {
                    if (args.Length != 4) return false;
                    return Deposit((byte[])args[0], (byte[])args[1], (BigInteger)args[2],(BigInteger)args[3]);
                }
                //取钱 提现
                if (operation == "withdraw") // originator, withdrawAssetId, withdrawAmount, isGlobal
                {
                    if (args.Length != 4) return false;
                    return Withdrawal((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (BigInteger)args[3]);
                }
                //挂单
                if (operation == "makeOffer") // (makerAddress, offerAssetID, offerAmount, wantAssetID, wantAmount, feeAssetID, feeAmount)
                {
                    if (args.Length != 7) return false;
                    var offer = NewOffer((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3], (BigInteger)args[4], (byte[])args[5], (BigInteger)args[6]);
                    return MakeOffer(offer);
                }
                //撮合成交
                if (operation == "fillOffer") // fillerAddress, offerHash, fillAssetId, fillAmount, fillFeeAssetID, fillFeeAmount)
                {
                    if (args.Length != 6) return false;
                    return FillOffer((byte[])args[0], (byte[])args[1],(byte[])args[2], (BigInteger)args[3], (byte[])args[4], (BigInteger)args[5]);
                }
                //取消挂单
                if (operation == "cancelOffer") // (offerHash)
                {
                    if (args.Length != 1) return false;
                    return CancelOffer((byte[])args[0]);
                }

                // 管理员签名
                if (!Runtime.CheckWitness(superAdmin)) return false;
                
                // == Init == 设置交易费收取地址,设置交易员
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

            return true;
        }

        /***********
         * Trading *
         ***********/

        private static bool MakeOffer(Offer offer)
        {
            var dealerAddress = GetDealerAddress();

            if (!Runtime.CheckWitness(dealerAddress)) return false;
            if (!Runtime.CheckWitness(offer.MakerAddress)) return false;
            if (dealerAddress == offer.MakerAddress) return false;

            // Check that nonce is not repeated
            var offerHash = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            if (Storage.Get(Context(), OfferKey(offerHash)).Length != 0) return false;

            if (!GetIsWhitelisted(offer.OfferAssetID) || !GetIsWhitelisted(offer.WantAssetID)) return false;

            // Check that the amounts > 0
            if (!(offer.OfferAmount > 0 && offer.WantAmount > 0 && offer.FeeAmount >= 0)) return false;

            // Check the trade is across different assets
            if (offer.OfferAssetID == offer.WantAssetID) return false;

            // Check that asset IDs are valid
            if (offer.OfferAssetID.Length != 20 || offer.WantAssetID.Length != 20 || offer.FeeAeestId.Length != 20) return false;

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            // Check that there is enough balance in native fees if using native fees
            if (GetBalance(offer.MakerAddress, offer.FeeAeestId) < offer.FeeAmount) throw new Exception("Invalid available balance!");
            // Reduce fee
            ReduceBalance(offer.MakerAddress, offer.FeeAeestId, offer.FeeAmount);

            if (GetBalance(offer.MakerAddress, offer.OfferAssetID) < offer.OfferAmount) throw new Exception("Invalid available balance!");
            // Reduce available balance for the offered asset and amount
            ReduceBalance(offer.MakerAddress, offer.OfferAssetID, offer.OfferAmount);

            //add fee
            IncreaseBalance(feeAddress, offer.FeeAeestId, offer.FeeAmount);

            // Add the offer to storage
            StoreOffer(offerHash, offer);

            // Notify (address, offerHash, offerAssetID, offerAmount, wantAssetID, wantAmount, feeAssetId, feeAmount)
            EmitCreated(offer.MakerAddress, offerHash, offer.OfferAssetID, offer.OfferAmount, offer.WantAssetID, offer.WantAmount, offer.FeeAeestId, offer.FeeAmount);
            return true;
        }

        // Fills an offer by taking the amount you want
        // => fillAmount's asset type = offer's wantAssetID
        // getAmount's asset type = offerAssetID (taker is taking what is offered)
        private static bool FillOffer(byte[] fillerAddress, byte[] offerHash, byte[] fillAssetId, BigInteger fillAmount, byte[] fillFeeAssetID, BigInteger fillFeeAmount)
        {
            var dealerAddress = GetDealerAddress();

            if (!Runtime.CheckWitness(dealerAddress)) return false;
            if (dealerAddress == fillerAddress) return false;

            // Check fees
            if (fillFeeAssetID.Length != 20) return false;
            if (fillFeeAmount < 0) return false;
            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            Offer offer = GetOffer(offerHash);
            if (offer.MakerAddress.Length == 0) return false;            
            if (fillerAddress == offer.MakerAddress) return false;
            if (offer.WantAssetID != fillAssetId) return false;

            if (fillAmount < 1) return false;              

            //filler 可以买到的数量
            BigInteger amountFillerGet = (fillAmount * offer.OfferAmount) / offer.WantAmount;
            
            if (amountFillerGet < 1) return false;

            // Check that you cannot take more than available
            if (amountFillerGet > offer.AvailableAmount) return false;

            if (GetBalance(fillerAddress, fillFeeAssetID) < fillFeeAmount) throw new Exception("Invalid available balance!");
            // Reduce fees
            ReduceBalance(fillerAddress, fillFeeAssetID, fillFeeAmount);
            //add fee
            IncreaseBalance(feeAddress, fillFeeAssetID, fillFeeAmount);

            var fillerBalance = GetBalance(fillerAddress, offer.WantAssetID);
            if (fillerBalance < fillAmount) throw new Exception("Invalid available balance!");
            // Reduce balance from filler
            ReduceBalance(fillerAddress, offer.WantAssetID, fillAmount);
            //add offer.MakerAddress amount
            IncreaseBalance(offer.MakerAddress, offer.WantAssetID, fillAmount);

            //add fillerAddress amount
            IncreaseBalance(fillerAddress, offer.OfferAssetID, amountFillerGet);

            // Update available amount
            offer.AvailableAmount = offer.AvailableAmount - amountFillerGet;

            // Store updated offer
            StoreOffer(offerHash, offer);

            // (fillerAddress, offerHash, fillAssetID, fillAmount, fillerGetAssetID, fillerGetAmount, fillFeeAssetID, fillFeeAmount)
            EmitFilled(fillerAddress, offerHash, fillAssetId, fillAmount, offer.OfferAssetID, amountFillerGet, fillFeeAssetID, fillFeeAmount);

            return true;
        }

        private static bool CancelOffer(byte[] offerHash)
        {
            // Check that the offer exists
            Offer offer = GetOffer(offerHash);
            if (offer.MakerAddress == new byte[] { }) return false;

            if (!Runtime.CheckWitness(offer.MakerAddress)) return false;

            //按比例计算剩余的交易费
            var feeReturnAmount = (offer.AvailableAmount * offer.FeeAmount) / offer.OfferAmount;

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            var feeAddressBalance = GetBalance(feeAddress, offer.FeeAeestId);

            if (feeAddressBalance < feeReturnAmount) return false;

            //add fee to MakerAddress
            IncreaseBalance(offer.MakerAddress, offer.FeeAeestId, feeReturnAmount);

            //reduce fee from feeAddress
            ReduceBalance(feeAddress, offer.FeeAeestId, feeReturnAmount);

            // Move funds to maker address
            IncreaseBalance(offer.MakerAddress, offer.OfferAssetID, offer.AvailableAmount);

            // Remove offer
            Storage.Delete(Context(), OfferKey(offerHash));

            // Notify (address, offerHash, offerAssetId, returnAmount, feeAssetId, feeReturnAmount)
            EmitCancelled(offer.MakerAddress, offerHash, offer.OfferAssetID, offer.AvailableAmount, offer.FeeAeestId, feeReturnAmount);
            return true;
        }

        private static void StoreOffer(byte[] offerHash, Offer offer)
        {
            if (offer.AvailableAmount < 0)
                throw new Exception("Invalid offer available amount!");
            else
            {
                var offerData = offer.Serialize();
                Storage.Put(Context(), OfferKey(offerHash), offerData);
            }
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

            var originatorBalance = GetBalance(originator, assetId);

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
                EmitWithdrawn(originator, assetId, amount);
                return true;
            }
            else
                throw new Exception("Failed to withdrawal transfer");
        }

        /***********
         * Getters *
         ***********/

        private static BigInteger GetBalance(byte[] address, byte[] assetID)
        {
            if (address.Length != 20 || assetID.Length != 20) return 0;
            return Storage.Get(Context(), AvailableBalanceKey(address, assetID)).AsBigInteger();
        }

        private static Offer GetOffer(byte[] offerHash)
        {
            byte[] offerData = Storage.Get(Context(), OfferKey(offerHash));
            if (offerData.Length == 0) return new Offer();

            return (Offer)offerData.Deserialize();
        }
        private static bool GetIsWhitelisted(byte[] assetID)
        {
            if (assetID.Length != 20) return false;
            if (Storage.Get(Context(), WhitelistKey(assetID)).AsBigInteger() == 1)
                return true;
            return false;
        }

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static byte[] GetFeeAddress() => Storage.Get(Context(), "feeAddress");

        private static byte[] GetDealerAddress() => Storage.Get(Context(), "dealerAddress");

        /***********
         * Control *
         ***********/

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

        private static bool Initialize(byte[] feeAddress, byte[] dealerAddress)
        {
            if (!SetFeeAddress(feeAddress)) throw new Exception("Failed to set fee address");
            if (!SetDealerAddress(dealerAddress)) throw new Exception("Failed to set the dealer address");
            EmitInitialized();
            return true;
        }

        private static bool SetFeeAddress(byte[] feeAddress)
        {
            if (feeAddress.Length != 20) return false;
            Storage.Put(Context(), "feeAddress", feeAddress);
            EmitFeeAddressSet(feeAddress);
            return true;
        }

        private static bool SetDealerAddress(byte[] dealerAddress)
        {
            if (dealerAddress.Length != 20) return false;
            Storage.Put(Context(), "dealerAddress", dealerAddress);
            EmitDealerAddressSet(dealerAddress);
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

        private static StorageContext Context() => Storage.CurrentContext;
        private static BigInteger AmountToOffer(Offer o, BigInteger amount) => (o.OfferAmount * amount) / o.WantAmount;

        // Keys
        private static byte[] OfferKey(byte[] offerHash) => "offers".AsByteArray().Concat(offerHash);
        private static byte[] AvailableBalanceKey(byte[] originator, byte[] assetID) => "availableBalance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] BalanceKey(byte[] originator, byte[] assetID) => "balance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] WhitelistKey(byte[] assetId) => "whiteList".AsByteArray().Concat(assetId);

        private class Offer
        {
            public byte[] MakerAddress;
            public byte[] OfferAssetID;
            public BigInteger OfferAmount;
            public byte[] WantAssetID;
            public BigInteger WantAmount;
            public BigInteger AvailableAmount;
            public byte[] FeeAeestId;
            public BigInteger FeeAmount;
        }

        private static Offer NewOffer(byte[] makerAddress, byte[] offerAssetID, BigInteger offerAmount, byte[] wantAssetID, BigInteger wantAmount, byte[] feeAeestId, BigInteger feeAmount)
        {
            return new Offer
            {
                MakerAddress = makerAddress,
                OfferAssetID = offerAssetID,
                OfferAmount = offerAmount,
                WantAssetID = wantAssetID,
                WantAmount = wantAmount,
                AvailableAmount = offerAmount,
                FeeAeestId = feeAeestId,
                FeeAmount = feeAmount
            };
        }

    }

}
