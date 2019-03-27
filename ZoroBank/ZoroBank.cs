using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.Remoting.Messaging;

namespace ZoroBank
{
    public class ZoroBank : SmartContract
    {
        public delegate void deleDeposit(byte[] txid, byte[] who, BigInteger value);
        [DisplayName("deposit")] public static event deleDeposit Deposited;

        public delegate void deleExchange(byte[] txid);
        [DisplayName("exchange")] public static event deleExchange Exchanged;

        public delegate void deleGetMoneyBack(byte[] to, BigInteger value);
        [DisplayName("getmoneyback")] public static event deleGetMoneyBack GetMoneyBacked;

        public delegate void deleCancel(byte[] txid);
        [DisplayName("cancel")] public static event deleCancel CanCelled;

        public delegate void deleResponse(byte[] txid, BigInteger v);
        [DisplayName("response")] public static event deleResponse Responsed;

        public delegate void deleGetReturn(byte[] txid, byte[] who, BigInteger value, BigInteger returnvalue);
        [DisplayName("getreturn")] public static event deleGetReturn GetReturned;

        public delegate void deleSend(byte[] txid, byte[] to, BigInteger value);
        [DisplayName("sendmoney")] public static event deleSend Sended;

        [Appcall("4b2f4015e65d5cc0b6c0167ab11e93e5ef9c4bdd")]
        static extern object bcpCall(string method, object[] arr);

        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AGeYNb4jbyLZ7UmCnzVrbvoyiMYceejkFY");

        public static object Main(string method, object[] args)
        {
            var magicstr = "ZoroBankTest";            
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                //记录存款
                if (method == "deposit")
                {
                    byte[] txid = (byte[])args[0];
                    return Deposit(txid);
                }

                //兑换请求、收到返回前可以撤销
                if (method == "exchange")
                {
                    byte[] who = (byte[])args[0];
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    BigInteger amount = (BigInteger)args[1];
                    byte[] witnessreturn = (byte[])args[2];
                    if (witnessreturn.Length == 0 || amount <= 0) return false;
                    return Exchange(who, amount, witnessreturn);
                }

                //取消交易
                if (method == "cancel")
                {
                    byte[] txid = (byte[]) args[0];
                    return Cancel(txid);
                }

                //处理请求、输出响应
                if (method == "response")
                {
                    byte[] txid = (byte[]) args[0];
                    BigInteger returnvalue = (BigInteger) args[1];
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    return Response(txid, returnvalue);
                }

                //接收返回
                if (method == "getreturn")
                {
                    byte[] txid = (byte[])args[0];
                    BigInteger returnvalue = (BigInteger)args[1];
                    return GetReturn(txid, returnvalue);
                }

                //查存款数
                if (method == "balanceOf")
                {
                    byte[] who = (byte[]) args[0];
                    StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                    return depositBalanceMap.Get(who).AsBigInteger();
                }

                //获取请求状态
                if (method == "getcallstate")
                {
                    byte[] txid = (byte[]) args[0];
                    return GetCallState(txid);
                }

                //兑换请求处理完成，发钱
                if (method == "sendmoney")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] txid = (byte[]) args[0];
                    byte[] who = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    if (who.Length == 0 || value <= 0) return false;
                    return SendMoney(txid, who, value);
                }

                //取回放进 Bank 中的钱
                if (method == "getmoneyback")
                {
                    byte[] who = (byte[]) args[0];
                    BigInteger amount = (BigInteger) args[1];
                    if (!Runtime.CheckWitness(who)) return false;
                    if (amount == 0) return false;
                    GetMoneyBack(who, amount);
                }

            }

            return false;
        }

        /// <summary>
        /// 记录存款
        /// </summary>
        /// <param name="txid">nep5 transfer 的 txid</param>
        /// <returns></returns>
        public static bool Deposit(byte[] txid)
        {
            var tx = new TransferInfo();
            StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
            var v = depositBalanceMap.Get(txid).AsBigInteger();
            if (v == 0)
            {
                object[] ob = new object[1];
                ob[0] = txid;
                var info = bcpCall("getTxInfo", ob);
                if (((object[]) info).Length == 3)
                    tx = info as TransferInfo;
                if (tx.@from.Length == 0)
                    return false;
                if (tx.to.AsBigInteger() == ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                {
                    var money = depositBalanceMap.Get(tx.@from).AsBigInteger();
                    money += tx.value;
                    depositBalanceMap.Put(tx.@from, money);
                    depositBalanceMap.Put(txid, 1);
                    //notify
                    Deposited(txid, tx.@from, tx.value);
                    return true;
                }

                return false;
            }

            return false;

        }

        /// <summary>
        /// 兑换请求，质押需要跨链的资产
        /// </summary>
        /// <param name="witnessreturn">接收返回见证者</param>
        /// <param name="who">兑换人</param>
        /// <param name="amount">金额</param>
        /// <returns></returns>
        public static bool Exchange(byte[] who, BigInteger amount, byte[] witnessreturn)
        {
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            var v = new CallInfo();
            v.state = 1;
            v.witnesscall = who;
            v.witnessreturn = witnessreturn;
            v.returnvalue = 0;
            v.who = who;
            v.value = amount;
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
            StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
            var depositAmount = depositBalanceMap.Get(who).AsBigInteger();
            var exchangeAmount = exchangeAmountMap.Get(who).AsBigInteger();
            if (amount > depositAmount)
                return false;
            exchangeAmount += amount;
            depositAmount -= amount;
            var data = Neo.SmartContract.Framework.Helper.Serialize(v);
            callStateMap.Put(txid, data);
            depositBalanceMap.Put(who, depositAmount);
            exchangeAmountMap.Put(who, exchangeAmount);
            //notify
            Exchanged(txid);
            return true;
        }

        /// <summary>
        /// 取消兑换请求   需要发起者签名
        /// </summary>
        /// <param name="txid">兑换请求的 txid</param>
        /// <returns></returns>
        public static bool Cancel(byte[] txid)
        {
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            var data = callStateMap.Get(txid);
            if (data.Length == 0)
                return false;
            CallInfo s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallInfo;
            if (s.state == 1)
            {
                if (!Runtime.CheckWitness(s.witnesscall))
                    return false;
                StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
                var depositAmount = depositBalanceMap.Get(s.who).AsBigInteger();
                var exchangeAmount = exchangeAmountMap.Get(s.who).AsBigInteger();
                if (exchangeAmount < s.value)
                    return false;
                exchangeAmount -= s.value;
                depositAmount += s.value;
                depositBalanceMap.Put(s.who, depositAmount);
                exchangeAmountMap.Put(s.who, exchangeAmount);
                callStateMap.Delete(txid);
                //notify
                CanCelled(txid);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 接受请求，输出响应
        /// </summary>
        /// <param name="txid"></param>
        /// <param name="who"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        private static bool Response(byte[] txid, BigInteger returnvalue)
        {
            StorageMap responseMap = Storage.CurrentContext.CreateMap(nameof(responseMap));
            var v = responseMap.Get(txid).AsBigInteger();
            //v!=0说明已经处理过该请求
            if (v == 0)
            {
                responseMap.Put(txid, returnvalue);
                //notify
                Responsed(txid, returnvalue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 接收返回  需要指定返回见证者签名
        /// </summary>
        /// <param name="txid">兑换请求的 txid</param>
        /// <param name="returnvalue">返回值,1 返回ture，0 返回 false，兑换被拒绝了 </param>
        /// <returns></returns>
        public static bool GetReturn(byte[] txid, BigInteger returnvalue)
        {
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            var data = callStateMap.Get(txid);
            if (data.Length == 0)
            {
                //notify
                GetReturned(txid, null, 0, 2); //请求不存在或被取消了
                return true;
            }

            CallInfo s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallInfo;
            if (s.state == 1)
            {
                if (!Runtime.CheckWitness(s.witnessreturn))
                    return false;
                if (returnvalue == 0) //被拒绝了
                {
                    StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                    StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
                    var depositAmount = depositBalanceMap.Get(s.who).AsBigInteger();
                    var exchangeAmount = exchangeAmountMap.Get(s.who).AsBigInteger();
                    if (exchangeAmount < s.value)
                        return false;
                    exchangeAmount -= s.value;
                    depositAmount += s.value;
                    depositBalanceMap.Put(s.who, depositAmount);
                    exchangeAmountMap.Put(s.who, exchangeAmount);
                    callStateMap.Delete(txid);
                }

                s.returnvalue = returnvalue;
                s.state = 2;
                data = Neo.SmartContract.Framework.Helper.Serialize(s);
                callStateMap.Put(txid, data);
                //notify
                GetReturned(txid, s.who, s.value, returnvalue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 取回放进 Bank 中的钱
        /// </summary>
        /// <param name="who">账户</param>
        /// <param name="amount">金额</param>
        /// <returns></returns>
        private static bool GetMoneyBack(byte[] who, BigInteger amount)
        {
            StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
            var money = depositBalanceMap.Get(who).AsBigInteger();
            if (money < amount)
                return false;
            object[] transArr = new object[3];
            transArr[0] = ExecutionEngine.ExecutingScriptHash;
            transArr[1] = who;
            transArr[2] = amount;
            if ((bool)bcpCall("transfer_app", transArr))
            {
                money -= amount;
                if (money == 0)
                {
                    depositBalanceMap.Delete(who);
                    //notify
                    GetMoneyBacked(who, amount);
                    return true;
                }

                depositBalanceMap.Put(who, money);
                //notify
                GetMoneyBacked(who, amount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 发钱，请求完成后转账
        /// </summary>
        /// <param name="txid">Zoro 兑换请求的 txid</param>
        /// <param name="who"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
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
                if ((bool)bcpCall("transfer_app", transArr))
                {
                    sendMoneyMap.Put(txid, 1);
                    //notify
                    Sended(txid, who, value);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 得到调用状态
        /// </summary>
        /// <param name="txid">兑换请求的 txid</param>
        /// <returns></returns>
        public static CallInfo GetCallState(byte[] txid)
        {
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            var data = callStateMap.Get(txid);
            if (data.Length == 0)
                return null;
            CallInfo s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallInfo;
            return s;

        }

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        public class CallInfo
        {
            public int state; //1 incall 2 havereturn
            public byte[] witnesscall; //调用者
            public byte[] who; //兑换者
            public BigInteger value; //数量
            public byte[] witnessreturn; //返回者
            public BigInteger returnvalue; //返回值 0 失败，1 成功
        }
    }
}
