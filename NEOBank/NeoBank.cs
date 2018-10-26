using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace NEOBank
{
    public class NeoBank : SmartContract
    {
        public delegate void deleExchange(byte[] txid, byte[] who, BigInteger value);
        [DisplayName("exchange")]
        public static event deleExchange Exchanged;

        public delegate void deleGetMoneyBack(byte[] to, BigInteger value);
        [DisplayName("getmoneyback")]
        public static event deleGetMoneyBack GetMoneyBacked;

        public delegate void deleCancel(byte[] txid);
        [DisplayName("cancel")]
        public static event deleCancel CanCelled;

        public delegate void deleGetReturn(byte[] txid, int returnvalue);
        [DisplayName("getreturn")]
        public static event deleGetReturn GetReturned;

        [Appcall("04e31cee0443bb916534dad2adf508458920e66d")]
        static extern object bcpCall(string method, object[] arr);

        //管理员账户，改成自己测试用的的
        private static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AdsNmzKPPG7HfmQpacZ4ixbv9XJHJs2ACz");

        public static object Main(string method, object[] args)
        {
            var magicstr = "neoBankTest";
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

                if (method == "deposit")//存款记录
                {
                    if (args.Length != 1) return false;
                    byte[] txid = (byte[])args[0];
                    return Deposit(txid);
                }

                if (method == "exchange") //兑换请求、收到返回前可以撤销
                {
                    if (args.Length != 3)
                        return false;
                    byte[] witnessreturn = (byte[]) args[0];
                    byte[] who = (byte[]) args[1];
                    BigInteger amount = (BigInteger) args[2];
                    return Exchange(witnessreturn, who, amount);
                }

                if (method == "cancel") //取消兑换
                {
                    byte[] txid = (byte[])args[0];
                    return Cancel(txid);
                }

                if (method == "getreturn") //接收返回
                {
                    byte[] txid = (byte[])args[0];
                    int returnvalue = (int)args[1];
                    return GetReturn(txid, returnvalue);
                }
                if (method == "balanceOf") //查存款数
                {
                    if (args.Length != 1)
                        return 0;
                    byte[] who = (byte[])args[0];
                    var key = new byte[] { 0x11 }.Concat(who);
                    StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                    return depositBalanceMap.Get(key).AsBigInteger();
                }

                if (method == "getcallstate")//获取调用状态
                {
                    byte[] txid = (byte[])args[0];
                    return GetCallState(txid);
                }

                if (method == "getmoneyback") //兑换完成后发钱
                {
                    if (args.Length != 3)
                        return false;
                    byte[] txid = (byte[]) args[0];
                    byte[] who = (byte[])args[1];
                    BigInteger amount = (BigInteger)args[2];
                    return GetMoneyBack(txid, who, amount);
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
                if (((object[])info).Length == 3)
                    tx = info as TransferInfo;
                if (tx.@from.Length == 0)
                    return false;
                if (tx.to.AsBigInteger() == ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                {
                    var money = depositBalanceMap.Get(tx.@from).AsBigInteger();
                    money += tx.value;
                    depositBalanceMap.Put(tx.@from, money);
                    depositBalanceMap.Put(txid, 1);
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
        public static bool Exchange(byte[] witnessreturn, byte[] who, BigInteger amount)
        {
            if (!Runtime.CheckWitness(who))
                return false;
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            
            var v = new CallState();
            v.state = 1;
            v.witnesscall = who;
            v.witnessreturn = witnessreturn;
            v.returnvalue = 0;
            v.who = who;
            v.value = amount;
            var data = Neo.SmartContract.Framework.Helper.Serialize(v);

            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
            StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
            var depositAmount = depositBalanceMap.Get(who).AsBigInteger();
            var exchangeAmount = exchangeAmountMap.Get(who).AsBigInteger();
            if (exchangeAmount > depositAmount)
                return false;
            exchangeAmount += amount;
            depositAmount -= amount;
            callStateMap.Put(txid, data);
            depositBalanceMap.Put(who, depositAmount);
            exchangeAmountMap.Put(who, exchangeAmount);
            //notify
            Exchanged(txid, who, amount);
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
            CallState s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallState;
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
        /// 接收返回  需要指定返回见证者签名
        /// </summary>
        /// <param name="txid">兑换请求的 txid</param>
        /// <param name="returnvalue">返回值,1 返回ture，0 返回 false，兑换被拒绝了 </param>
        /// <returns></returns>
        public static bool GetReturn(byte[] txid, int returnvalue)
        {
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            var data = callStateMap.Get(txid);
            if (data.Length == 0)
            {
                //notify
                GetReturned(txid, 2);//被取消了
                return true;
            }

            CallState s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallState;
            if (s.state == 1)
            {
                if (!Runtime.CheckWitness(s.witnessreturn))
                    return false;
                if (returnvalue == 0)//请求被拒绝
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
                    //notify
                    GetReturned(txid, returnvalue);
                    return true;
                }
                s.returnvalue = returnvalue;
                s.state = 2;
                data = Neo.SmartContract.Framework.Helper.Serialize(s);
                callStateMap.Put(txid, data);
                //notify
                GetReturned(txid, returnvalue);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 反向操作、取回钱，从跨链请求质押的资产中取回，需要管理员操作
        /// </summary>
        /// <param name="txid">Zoro 兑换请求的 txid</param>
        /// <param name="who"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        private static object GetMoneyBack(byte[] txid, byte[] who, BigInteger amount)
        {
            if (!Runtime.CheckWitness(superAdmin))
                return false;
            var keytx = new byte[] { 0x12 }.Concat(txid);
            StorageMap getMoneyBackMap = Storage.CurrentContext.CreateMap(nameof(getMoneyBackMap));
            var v = getMoneyBackMap.Get(keytx).AsBigInteger();
            if (v == 0)
            {
                var key = new byte[] { 0x11 }.Concat(who);
                StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
                var money = exchangeAmountMap.Get(key).AsBigInteger();
                if (money < amount)
                    return false;
                object[] transArr = new object[3];
                transArr[0] = ExecutionEngine.ExecutingScriptHash;
                transArr[1] = who;
                transArr[2] = amount;

                bool isSuccess = (bool)bcpCall("transfer_app", transArr);
                if (isSuccess)
                {
                    money -= amount;
                    exchangeAmountMap.Put(key, money);
                    getMoneyBackMap.Put(keytx, 1);
                    //notify
                    GetMoneyBacked(who, amount);
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
        public static CallState GetCallState(byte[] txid)
        {
            var key = new byte[] { 0x11 }.Concat(txid);
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            var data = callStateMap.Get(key);
            if (data.Length == 0)
                return null;
            CallState s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallState;
            return s;

        }

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        public class CallState
        {
            public int state;//1 incall 2 havereturn
            public byte[] witnesscall;//调用者
            public byte[] who;//兑换者
            public BigInteger value;//数量
            public byte[] witnessreturn;//返回者
            public int returnvalue;//返回值 0 失败，1 成功
        }
    }
}