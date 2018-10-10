using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Neo.SmartContract.Framework.Services.System;

namespace NeoBankContract
{
    public class NeoBank : SmartContract
    {
        public delegate void deleCheckTrans(byte[] from, BigInteger value);

        [DisplayName("checktrans")]
        public static event deleCheckTrans CheckTrans;

        [Appcall("04e31cee0443bb916534dad2adf508458920e66d")]
        static extern object bcpCall(string method, object[] arr);

        public static object Main(string method,object[] args)
        {
            var magicstr = "bankTest";
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

                if (method == "checktrans")
                {
                    if (args.Length != 1) return false;
                    byte[] txid = (byte[]) args[0];
                    var tx = new TransferInfo();

                    var keytx = new byte[] {0x12}.Concat(txid);
                    var v = Storage.Get(Storage.CurrentContext, keytx).AsBigInteger();
                    if (v == 0)
                    {
                        object[] o = new object[1];
                        o[0] = txid;
                        var info = bcpCall("getTxInfo", o);
                        if (((object[]) info).Length == 3)
                            tx = info as TransferInfo;
                        if (tx.@from.Length == 0)
                            return false;
                        if (tx.to.AsBigInteger() == ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                        {
                            var key = new byte[] { 0x11 }.Concat(tx.@from);
                            var money = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
                            money += tx.value;
                            Storage.Put(Storage.CurrentContext, key, money);
                            Storage.Put(Storage.CurrentContext, keytx, 1);
                        }

                    }
                    else
                    {
                        return false;
                    }

                    
                }
            }

            return false;
            
        }


        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

    }
}
