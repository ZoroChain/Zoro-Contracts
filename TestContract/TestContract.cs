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
            var magicstr = "Test_Contract_v0.35";

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

                if (method == "set")
                {
                    byte[] key = (byte[])args[0];
                    byte[] data = (byte[])args[1];
                    Storage.Put(Storage.CurrentContext, key, data);
                }
                if (method == "get")
                {
                    byte[] key = (byte[])args[0];
                    return Storage.Get(Storage.CurrentContext, key);
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

                if (method == "balanceOf1")
                {
                    var asset_id = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                    byte[] address = (byte[])args[0];
                    var aa = NativeAsset.Call("BalanceOf", asset_id, address).AsBigInteger();
                    return aa;
                }

                if (method == "transferFrom")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] from = (byte[])args[1];
                    byte[] to = (byte[])args[2];

                    BigInteger value = (BigInteger)args[3];

                    var para = new object[3] { from, to, value };
                    deleCall contract = (deleCall)asset_id.ToDelegate();
                    var aa = (bool)contract("transferFrom", para);

                    Runtime.Notify(1, aa);

                    var par = new object[2] { from, to };
                    BigInteger ba = (BigInteger)contract("allowance", par);
                    
                    Runtime.Notify(1, ba);

                }

                if (method == "transferFrom1")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] from = (byte[])args[1];
                    byte[] to = (byte[])args[2];

                    BigInteger value = (BigInteger)args[3];

                    var success = NativeAsset.Call("TransferFrom", asset_id, from, to, value);
                                      
                    Runtime.Notify(1, success);

                    var par = new object[3] { asset_id, from, to };
                    BigInteger ba = NativeAsset.Call("Allowance", par).AsBigInteger();

                    Runtime.Notify(1, ba);

                }

                if (method == "transferApp")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];

                    byte[] from = ExecutionEngine.ExecutingScriptHash;

                    var para = new object[3] { from, to, value };
                    var contract = (deleCall)asset_id.ToDelegate();
                    var aa = (bool)contract("transferApp", para);
                    Runtime.Notify(from, to, value);
                    Runtime.Notify(1, aa);

                }

                if (method == "GetTransferLog")
                {
                    byte[] asset_id = (byte[])args[0];
                    byte[] txid = (byte[])args[1];

                    var tInfo = new TransferInfo();
                    var info = NativeAsset.Call("GetTransferLog", asset_id, txid);
                    return info;
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
