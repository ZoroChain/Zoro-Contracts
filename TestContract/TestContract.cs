using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using Zoro;

namespace TestContract
{
    public class TestContract : SmartContract
    {
        [DisplayName("notify")]
        public static event Action<byte[], BigInteger> Notify;

        delegate object deleCall(string method, object[] args);

        public static object Main(string method, object[] args)
        {
            var magicstr = "Test_Contract_v0.33";

            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                if (method == "witnessTest")
                {
                    Notify(new byte[] { }, 0);

                    if (!Runtime.CheckWitness((byte[])args[0])) return false;
                    Notify((byte[])args[0], 1);

                    if (!Runtime.CheckWitness((byte[])args[1])) return false;
                    Notify((byte[])args[1], 2);
                }

                if (method == "test")
                    return 1;
                if (method == "call")
                    return "yes";
                if(method=="return")
                {
                    byte[] asset_id = (byte[])args[0];
                    return asset_id;
                }
                if (method == "getheight")
                    return Blockchain.GetHeight();
                if (method == "getheader")
                {
                    var height = (uint)args[0];
                    return Blockchain.GetHeader(height);
                }

                if (method == "strToByte")
                {
                    var result = "hello world".AsByteArray();
                    return result;
                }

                if (method == "balanceOf")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] address = (byte[])args[1];
                    var aa = NativeAsset.Call("BalanceOf", asset_id, address);
                    return aa;
                }

                if (method == "GetTransferLog")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] txid = (byte[])args[1];

                    var tInfo = new TransferInfo();
                    var info = NativeAsset.Call("GetTransferLog", asset_id, txid);
                    return info;
                }

                if (method == "GetTransferLog1")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] txid = (byte[])args[1];

                    var tInfo = new TransferInfo();
                    var info = NativeAsset.GetTransferLog(asset_id, txid);
                    return info.From;
                }


                if (method == "GetTransferLog2")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] txid = (byte[])args[1];

                    var tInfo = new TransferInfo();
                    var info = NativeAsset.GetTransferLog(asset_id, txid);
                    return info.To;
                }

                if (method == "GetTransferLog3")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] txid = (byte[])args[1];

                    var tInfo = new TransferInfo();
                    var info = NativeAsset.GetTransferLog(asset_id, txid);
                    return info.Value;
                }


            }

            return false;
        }

    }

    public class TransferInfo
    {
        public UInt160 from;
        public UInt160 to;
        public Fixed8 value;
    }
}
