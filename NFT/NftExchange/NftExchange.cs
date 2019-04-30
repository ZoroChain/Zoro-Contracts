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
        public delegate object NftContract(string method, object[] args);

        [DisplayName("deposit")]
        public static event Action<byte[], byte[], BigInteger> EmitDepositted; // (address, assetID, amount)

        [DisplayName("withdraw")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawn; // (address, assetID, amount)
        // Events
        [DisplayName("makeOffer")]
        public static event Action<byte[], byte[], byte[], byte[], byte[], BigInteger, byte[], BigInteger> EmitCreated; // (address, offerHash, nftContractHash, sellNftId, acceptAssetId, price, feeAssetId, feeAmount)

        [DisplayName("fillOffer")]
        public static event Action<byte[], byte[], byte[], byte[], BigInteger, byte[], byte[], byte[], BigInteger, byte[], BigInteger> EmitFilled; // (fillerAddress, offerHash, offerAddress, fillAssetID, fillAmount, nftContractHash, sellNftId, fillFeeAssetID, fillFeeAmount, offerFeeAssetId, offerFeeAmount)

        [DisplayName("cancelOffer")]
        public static event Action<byte[], byte[], byte[], byte[], byte[], BigInteger, BigInteger> EmitCancelled; // (address, offerHash, nftContractHash, nftId, feeAssetId, feeReturnAmount, deductFee)

        [DisplayName("addedToWhitelist")]
        public static event Action<byte[]> EmitAddedToWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("removedFromWhitelist")]
        public static event Action<byte[]> EmitRemovedFromWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("feeAddressSet")]
        public static event Action<byte[]> EmitFeeAddressSet; // (address)

        [DisplayName("dealerAddressSet")]
        public static event Action<byte[]> EmitDealerAddressSet; // (address)

        [DisplayName("operatorAddressSet")]
        public static event Action<byte[]> EmitOperatorAddressSet; // (address)

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
                if (operation == "getBalance") return GetBalance((byte[])args[0], (byte[])args[1]);
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
                    return MakeOffer((byte[])args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3], (BigInteger)args[4], (byte[])args[5], (BigInteger)args[6], time);

                }

                if (operation == "fillOffer")
                {
                    if (args.Length != 5) return false;

                    BigInteger time = (BigInteger)args[4];
                    BigInteger curTime = Runtime.Time;
                    if (curTime - time > 300) return false;

                    // fillerAddress, offerHash, fillFeeAssetID, fillFeeAmount
                    return FillOffer((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3]);
                }

                //取消挂单
                if (operation == "cancelOffer") // (offerHash, deductFee)
                {
                    if (args.Length != 2) return false;
                    byte[] offerHash = (byte[])args[0];
                    BigInteger deductFee = (BigInteger)args[1];
                    if (offerHash.Length == 0 || deductFee < 0) return false;
                    return CancelOffer(offerHash, deductFee);
                }

                // 设置交易费收取地址,设置交易员,设置操作员
                if (operation == "setOperator")
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    var opera = (byte[])args[0];
                    if (opera.Length != 20) return false;
                    Storage.Put(Context(), "operator", opera);
                    EmitOperatorAddressSet(opera);
                    return true;
                }               

                if (operation == "initialize")
                {
                    if (args.Length != 2) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return Initialize((byte[])args[0], (byte[])args[1]);
                }
                if (operation == "setDealerAddress")
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return SetDealerAddress((byte[])args[0]);
                }

                if (operation == "setFeeAddress")
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return SetFeeAddress((byte[])args[0]);
                }

                var operatorAddr = Storage.Get(Context(), "operator");
                // 操作员签名
                if (!Runtime.CheckWitness(operatorAddr)) return false;
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

        private static bool MakeOffer(byte[] makerAddress, byte[] nftContractHash, byte[] sellNftId, byte[] acceptAssetId, BigInteger price, byte[] feeAeestId, BigInteger feeAmount, BigInteger timeSpan)
        {
            var dealerAddress = GetDealerAddress();

            if (!Runtime.CheckWitness(dealerAddress)) return false;
            if (!Runtime.CheckWitness(makerAddress)) return false;
            if (dealerAddress == makerAddress) return false;

            if (!GetIsWhitelisted(nftContractHash) || !GetIsWhitelisted(feeAeestId)) return false;

            Offer offer = new Offer
            {
                Address = makerAddress,
                NftContract = nftContractHash,
                TokenId = sellNftId,
                AcceptAssetId = acceptAssetId,
                Price = price,
                FeeAeestId = feeAeestId,
                FeeAmount = feeAmount,
                TimeSpan = timeSpan
            };

            // Check that nonce is not repeated
            var offerHash = Hash(offer);
            if (Storage.Get(Context(), OfferKey(offerHash)).Length != 0) return false;                        

            // Check that the amounts > 0
            if (feeAmount < 1) return false;           

            // Check that asset IDs are valid
            if (nftContractHash.Length != 20 ||feeAeestId.Length != 20) return false;

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            // Check that there is enough balance in native fees if using native fees
            if (GetAvailabelBalance(makerAddress, feeAeestId) < feeAmount) return false;
            // Reduce fee
            ReduceAvailabelBalance(makerAddress, feeAeestId, feeAmount);

            var args = new object[] { makerAddress, ExecutionEngine.ExecutingScriptHash, sellNftId };
            var contract = (NftContract)nftContractHash.ToDelegate();
            bool success = (bool)contract("transferFrom", args);

            if(!success) throw new Exception("transferFrom failed!");

            var offerData = offer.Serialize();
            Storage.Put(Context(), OfferKey(offerHash), offerData);

            // Notify (address, offerHash, offerAssetID, offerAmount, wantAssetID, wantAmount, feeAssetId, feeAmount)
            EmitCreated(makerAddress, offerHash, nftContractHash, sellNftId, acceptAssetId, price, feeAeestId, feeAmount);
            return true;
        }

        private static bool FillOffer(byte[] fillerAddress, byte[] offerHash, byte[] fillFeeAssetID, BigInteger fillFeeAmount)
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

            if (GetAvailabelBalance(fillerAddress, fillFeeAssetID) < fillFeeAmount) return false;

            if (GetAvailabelBalance(fillerAddress, offer.AcceptAssetId) < offer.Price) return false;

            // Reduce fees
            ReduceBalance(fillerAddress, fillFeeAssetID, fillFeeAmount);
            ReduceAvailabelBalance(fillerAddress, fillFeeAssetID, fillFeeAmount);
            //add fee
            IncreaseBalance(feeAddress, fillFeeAssetID, fillFeeAmount);

            //reduce offer.MakerAddress fee amount            
            ReduceBalance(offer.Address, offer.FeeAeestId, offer.FeeAmount);
            //add fee
            IncreaseBalance(feeAddress, offer.FeeAeestId, offer.FeeAmount);

            // Reduce balance from filler
            ReduceBalance(fillerAddress, offer.AcceptAssetId, offer.Price);
            ReduceAvailabelBalance(fillerAddress, offer.AcceptAssetId, offer.Price);

            //add offer.MakerAddress amount
            IncreaseBalance(offer.Address, offer.AcceptAssetId, offer.Price);
            IncreaseAvailableBalance(offer.Address, offer.AcceptAssetId, offer.Price);                    

            var args = new object[] { ExecutionEngine.ExecutingScriptHash, fillerAddress, offer.TokenId };
            var contract = (NftContract)offer.NftContract.ToDelegate();
            bool success = (bool)contract("transferApp", args);

            if (!success) throw new Exception("transferApp failed!");

            Storage.Delete(Context(), OfferKey(offerHash));

            // (fillerAddress, offerHash, offerAddress, fillAssetID, fillAmount, nftContractHash, offer.SellNftId, fillFeeAssetID, fillFeeAmount, offer.FeeAeestId, offer.FeeAmount)
            EmitFilled(fillerAddress, offerHash, offer.Address, offer.AcceptAssetId, offer.Price, offer.NftContract, offer.TokenId, fillFeeAssetID, fillFeeAmount, offer.FeeAeestId, offer.FeeAmount);

            return true;
        }

        private static bool CancelOffer(byte[] offerHash, BigInteger deductFee)
        {
            var dealerAddress = GetDealerAddress();
            if (!Runtime.CheckWitness(dealerAddress)) return false;

            // Check that the offer exists
            Offer offer = GetOffer(offerHash);
            if (offer.Address == new byte[] { }) return false;

            if (!Runtime.CheckWitness(offer.Address)) return false;

            if (deductFee > 0)
            {
                if (offer.FeeAmount < deductFee) return false;

                byte[] feeAddress = Storage.Get(Context(), "feeAddress");
                if (feeAddress.Length != 20) return false;

                ReduceBalance(offer.Address, offer.FeeAeestId, deductFee);

                IncreaseBalance(feeAddress, offer.FeeAeestId, deductFee);

                offer.FeeAmount -= deductFee;
            }

            //add fee to MakerAddress
            IncreaseAvailableBalance(offer.Address, offer.FeeAeestId, offer.FeeAmount);

            var args = new object[] { ExecutionEngine.ExecutingScriptHash, offer.Address, offer.TokenId };
            var contract = (NftContract)offer.NftContract.ToDelegate();
            bool success = (bool)contract("transferApp", args);

            if (!success) throw new Exception("transferApp failed!");

            // Remove offer
            Storage.Delete(Context(), OfferKey(offerHash));

            // Notify (address, offerHash, offerAssetId, returnAmount, feeAssetId, feeReturnAmount)
            EmitCancelled(offer.Address, offerHash, offer.NftContract, offer.TokenId, offer.FeeAeestId, offer.FeeAmount, deductFee);
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
                var contract = (NftContract)assetId.ToDelegate();
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
                var contract = (NftContract)assetId.ToDelegate();
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
            Runtime.Notify("setState", setValue);
            return true;
        }

        private static StorageContext Context() => Storage.CurrentContext;

        // Keys
        private static byte[] OfferKey(byte[] offerHash) => "offers".AsByteArray().Concat(offerHash);
        private static byte[] AvailableBalanceKey(byte[] originator, byte[] assetID) => "availableBalance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] BalanceKey(byte[] originator, byte[] assetID) => "balance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] WhitelistKey(byte[] assetId) => "whiteList".AsByteArray().Concat(assetId);

        private static byte[] Hash(Offer offer)
        {
            var bytes = offer.Address.Concat(offer.NftContract).Concat(offer.TokenId).Concat(offer.TimeSpan.AsByteArray());
            return Hash256(bytes);
        }

        private class Offer
        {
            public byte[] Address;
            public byte[] NftContract;
            public byte[] TokenId;
            public byte[] AcceptAssetId;
            public BigInteger Price;
            public byte[] FeeAeestId;
            public BigInteger FeeAmount;
            public BigInteger TimeSpan;
        }
       
    }
}
