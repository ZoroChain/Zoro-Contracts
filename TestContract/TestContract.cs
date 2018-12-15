using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

namespace TestContract
{
    public class TestContract : SmartContract
    {
        public static object Main(string method, object[] args)
        {
            var magicstr = "Test_Contract";
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }

            if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return true;
            }

            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                if (method == "test")
                    return 1;
                if (method == "call")
                    return "yes";
                if (method == "getheight")
                    return Blockchain.GetHeight();
                if (method == "getheader")
                {
                    var height = (uint)args[0];
                    return Blockchain.GetHeader(height);
                }

                if (method == "name")
                {
                    byte[] asset_id = (byte[])args[0];
                    return NativeAsset.NativeAssetName(asset_id);
                }

                if (method == "symbol")
                {
                    byte[] asset_id = (byte[])args[0];
                    return NativeAsset.NativeAssetSymbol(asset_id);
                }

                if (method == "decimals")
                {
                    byte[] asset_id = (byte[])args[0];
                    return NativeAsset.NativeAssetDecimals(asset_id);
                }

                if (method == "totalSupply")
                {
                    byte[] asset_id = (byte[])args[0];
                    return NativeAsset.NativeAssetTotalSupply(asset_id);
                }

                if (method == "balanceOf")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] address = (byte[])args[1];
                    return NativeAsset.NativeAssetBalanceOf(asset_id, address);
                }

                if (method == "transfer")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] from = (byte[])args[1];
                    byte[] to = (byte[])args[2];
                    BigInteger value = (BigInteger)args[3];
                    return NativeAsset.NativeAssetTransfer(asset_id, from, to, value);
                }

                if (method == "transfer_app")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] from = ExecutionEngine.ExecutingScriptHash;
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return NativeAsset.NativeAssetTransferApp(asset_id, from, to, value);
                }

            }

            return false;
        }

    }
}
