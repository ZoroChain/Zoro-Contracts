using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace LockContract
{
    public class LockContract : SmartContract
    {
        public static bool Main(string operation, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                // 设置合约状态          
                if (operation == "locktest")
                {
                    Header header = Blockchain.GetHeader(Blockchain.GetHeight());
                    if (header.Timestamp < 1559636423)
                        Runtime.Notify(0, header.Timestamp);
                    else
                        Runtime.Notify(1, header.Timestamp);
                }
                return false;
            }
            return false;
        }
    }
}
