using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace LockContract
{
    public class LockContract : SmartContract
    {
        //管理员设置解锁时间和比例
        //超级管理员可以提取所有资产、停用合约
        //各类资产通用
        //可以方便查询锁仓数量和解锁时间、数量
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
