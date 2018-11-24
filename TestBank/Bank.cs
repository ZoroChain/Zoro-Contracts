using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace TestBank
{
    public class Bank : SmartContract
    {
        public delegate void deleDeposit(byte[] txid);
        [DisplayName("deposit")] public static event deleDeposit Deposited;
        
        public delegate void deleGetMoneyBack(byte[] to, BigInteger value);
        [DisplayName("getmoneyback")] public static event deleGetMoneyBack GetMoneyBacked;
        
        public delegate void deleResponse(byte[] txid, BigInteger v);
        [DisplayName("response")] public static event deleResponse Responsed;

        public delegate void deleGetReturn(byte[] txid, byte[] who, BigInteger value, BigInteger returnvalue);
        [DisplayName("getreturn")] public static event deleGetReturn GetReturned;

        public delegate void deleSend(byte[] txid, byte[] to, BigInteger value);
        [DisplayName("sendmoney")] public static event deleSend Sended;

        [Appcall("04e31cee0443bb916534dad2adf508458920e66d")]
        static extern object bcpCall(string method, object[] arr);

        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin =
            Neo.SmartContract.Framework.Helper.ToScriptHash("AGeYNb4jbyLZ7UmCnzVrbvoyiMYceejkFY");

        public static object Main(string method, object[] args)
        {
            var magicstr = "BankTest";
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

                if (method == "deposit") //存款记录
                {
                    byte[] txid = (byte[]) args[0];
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return Deposit(txid);
                }

                if (method == "response") //处理Zoro侧的兑换请求，输出响应
                {
                    byte[] txid = (byte[])args[0];
                    BigInteger returnvalue = (BigInteger)args[1];
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return Response(txid, returnvalue);
                }

                if (method == "getreturn") //接收返回
                {
                    byte[] txid = (byte[]) args[0];
                    BigInteger returnvalue = (BigInteger) args[1];
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return GetReturn(txid, returnvalue);
                }
                
                if (method == "getstate") //获取调用状态
                {
                    byte[] txid = (byte[]) args[0];
                    return GetCallState(txid);
                }
                
                if (method == "sendmoney") //Zoro侧兑换请求处理完成、发钱
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] txid = (byte[]) args[0];
                    byte[] who = (byte[]) args[1];
                    BigInteger value = (BigInteger) args[2];
                    if (who.Length == 0 || value <= 0) return false;
                    return SendMoney(txid, who, value);
                }
            }

            return false;
        }

        public static bool Deposit(byte[] txid)
        {
            StorageMap txidMap = Storage.CurrentContext.CreateMap(nameof(txidMap));
            var v = txidMap.Get(txid).AsBigInteger();
            //v!=0说明这笔已经记录过了
            if (v == 0)
            {
                var tx = new TransferInfo();
                object[] ob = new object[1];
                ob[0] = txid;
                var info = bcpCall("getTxInfo", ob);
                if (((object[]) info).Length == 3)
                    tx = info as TransferInfo;
                if (tx.@from.Length == 0 || tx.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                    return false;
                ExchangeInfo exchangeInfo = new ExchangeInfo();
                exchangeInfo.who = tx.@from;
                exchangeInfo.returnvalue = 0;
                exchangeInfo.state = 0;
                exchangeInfo.value = tx.value;
                var data = Neo.SmartContract.Framework.Helper.Serialize(exchangeInfo);
                StorageMap exchangeMap = Storage.CurrentContext.CreateMap(nameof(exchangeMap));
                exchangeMap.Put(txid, data);
                txidMap.Put(txid, 1);
                //notify
                Deposited(txid);
                return true;
            }
            return false;
        }
        
        private static bool Response(byte[] txid, BigInteger returnvalue)
        {
            StorageMap responseMap = Storage.CurrentContext.CreateMap(nameof(responseMap));
            var v = responseMap.Get(txid).AsBigInteger();
            //v!=0说明已经处理过该请求
            if (v == 0)
            {
                responseMap.Put(txid, returnvalue);
                Responsed(txid, returnvalue);
                return true;
            }
            return false;
        }

        public static bool GetReturn(byte[] txid, BigInteger returnvalue)
        {
            StorageMap exchangeMap = Storage.CurrentContext.CreateMap(nameof(exchangeMap));
            var data = exchangeMap.Get(txid);
            if (data.Length == 0)
            {
                GetReturned(txid, null, 0, 2); //请求不存在或被取消了，输出2
                return true;
            }
            ExchangeInfo s = Neo.SmartContract.Framework.Helper.Deserialize(data) as ExchangeInfo;
            if (s.state == 0)
            {
                if (returnvalue == 0) //请求被拒绝
                {
                    exchangeMap.Delete(txid);
                    return true;
                }
                s.returnvalue = returnvalue;
                s.state = 1;
                data = Neo.SmartContract.Framework.Helper.Serialize(s);
                exchangeMap.Put(txid, data);
                GetReturned(txid, s.who, s.value, returnvalue);
                return true;
            }
            return false;
        }
       
        private static bool SendMoney(byte[] txid, byte[] who, BigInteger value)
        {
            StorageMap sendMoneyMap = Storage.CurrentContext.CreateMap(nameof(sendMoneyMap));
            var v = sendMoneyMap.Get(txid).AsBigInteger();
            if (v == 0)
            {
                object[] transArr = new object[3];
                transArr[0] = ExecutionEngine.ExecutingScriptHash;
                transArr[1] = who;
                transArr[2] = value;
                bool isSuccess = (bool) bcpCall("transfer_app", transArr);
                if (isSuccess)
                {
                    sendMoneyMap.Put(txid, 1);
                    //notify
                    Sended(txid, who, value);
                    return true;
                }
            }

            return false;
        }

        public static ExchangeInfo GetCallState(byte[] txid)
        {
            StorageMap exchangeMap = Storage.CurrentContext.CreateMap(nameof(exchangeMap));
            var data = exchangeMap.Get(txid);
            if (data.Length == 0)
                return null;
            ExchangeInfo s = Neo.SmartContract.Framework.Helper.Deserialize(data) as ExchangeInfo;
            return s;
        }

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        public class ExchangeInfo
        {
            public int state; //1 incall 2 havereturn
            public byte[] who; //兑换者
            public BigInteger value; //数量
            public BigInteger returnvalue; //返回值 0 失败，1 成功
        }
    }
}
