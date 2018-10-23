﻿using Neo.SmartContract.Framework;
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

        public delegate void deleGetReturn(byte[] txid,int returnvalue);
        [DisplayName("getreturn")]
        public static event deleGetReturn GetReturned;

        [Appcall("04e31cee0443bb916534dad2adf508458920e66d")]
        static extern object bcpCall(string method, object[] arr);

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

                if (method == "exchange")//兑换请求、收到返回前可以撤销
                { 
                    if (args.Length != 2)
                        return false;
                    byte[] witnesscall = (byte[]) args[0];
                    byte[] witnessreturn = (byte[]) args[1];
                    byte[] who = (byte[])args[2];
                    BigInteger amount = (BigInteger)args[3];
                    return Exchange(witnesscall, witnessreturn, who, amount);
                }

                if (method == "cancel") //取消兑换
                {
                    byte[] txid = (byte[]) args[0];
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

                if (method == "getcallstate")
                {
                    byte[] txid = (byte[])args[0];
                    return GetCallState(txid);
                }

                if (method == "getmoneyback") //取回钱
                {
                    if (args.Length != 2)
                        return false;
                    byte[] who = (byte[])args[0];
                    BigInteger amount = (BigInteger)args[1];
                    return GetMoneyBack(who, amount);
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
            var keytx = new byte[] { 0x12 }.Concat(txid);
            StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
            var v = depositBalanceMap.Get(keytx).AsBigInteger();
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
                    var key = new byte[] { 0x11 }.Concat(tx.@from);
                    var money = depositBalanceMap.Get(key).AsBigInteger();
                    money += tx.value;

                    depositBalanceMap.Put(key, money);
                    depositBalanceMap.Put(keytx, 1);

                    return true;
                }

                return false;
            }
            return false;

        }

        /// <summary>
        /// 兑换请求
        /// </summary>
        /// <param name="witnesscall">请求者见证</param>
        /// <param name="witnessreturn">接收返回见证者</param>
        /// <param name="who">兑换人</param>
        /// <param name="amount">金额</param>
        /// <returns></returns>
        public static bool Exchange(byte[] witnesscall, byte[] witnessreturn, byte[] who, BigInteger amount)
        {
            if (!Runtime.CheckWitness(witnesscall))
                return false;
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            var txidKey = new byte[] {0x11}.Concat(txid);

            var v = new CallState();
            v.state = 1;
            v.witnesscall = witnesscall;
            v.witnessreturn = witnessreturn;
            v.returnvalue = 0;
            v.who = who;
            v.value = amount;
            var data = Neo.SmartContract.Framework.Helper.Serialize(v);

            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            callStateMap.Put(txidKey, data);

            var whoKey = new byte[] { 0x11 }.Concat(who);
            StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
            StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
            var depositAmount = depositBalanceMap.Get(whoKey).AsBigInteger();
            var exchangeAmount = exchangeAmountMap.Get(whoKey).AsBigInteger();
            if (exchangeAmount > depositAmount)
                return false;
            exchangeAmount += amount;
            depositAmount -= amount;
            depositBalanceMap.Put(whoKey, depositAmount);
            exchangeAmountMap.Put(whoKey, exchangeAmount);
            //notify
            Exchanged(txid, who, amount);
            return true;
        }

        /// <summary>
        /// 取消兑换请求
        /// </summary>
        /// <param name="txid">兑换请求的 txid</param>
        /// <returns></returns>
        public static bool Cancel(byte[] txid)
        {
            var key = new byte[] {0x11}.Concat(txid);
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            var data = callStateMap.Get(key);
            if (data.Length == 0)
                return false;
            CallState s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallState;
            if (s.state == 1)
            {
                if (!Runtime.CheckWitness(s.witnesscall))
                    return false;
                var whoKey = new byte[] { 0x11 }.Concat(s.who);
                StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
                var depositAmount = depositBalanceMap.Get(whoKey).AsBigInteger();
                var exchangeAmount = exchangeAmountMap.Get(whoKey).AsBigInteger();
                if (exchangeAmount < s.value)
                    return false;
                exchangeAmount -= s.value;
                depositAmount += s.value;
                depositBalanceMap.Put(whoKey, depositAmount);
                exchangeAmountMap.Put(whoKey, exchangeAmount);
                callStateMap.Delete(key);
                CanCelled(txid);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 接收返回
        /// </summary>
        /// <param name="txid">兑换请求的 txid</param>
        /// <param name="returnvalue">返回值</param>
        /// <returns></returns>
        public static bool GetReturn(byte[] txid, int returnvalue)
        {
            var key = new byte[] { 0x11 }.Concat(txid);
            StorageMap callStateMap = Storage.CurrentContext.CreateMap(nameof(callStateMap));
            var data = callStateMap.Get(key);
            if (data.Length == 0)
                return false;
            CallState s = Neo.SmartContract.Framework.Helper.Deserialize(data) as CallState;
            if (s.state == 1)
            {
                if (!Runtime.CheckWitness(s.witnessreturn))
                    return false;
                if (returnvalue == 0)
                {
                    var whoKey = new byte[] { 0x11 }.Concat(s.who);
                    StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                    StorageMap exchangeAmountMap = Storage.CurrentContext.CreateMap(nameof(exchangeAmountMap));
                    var depositAmount = depositBalanceMap.Get(whoKey).AsBigInteger();
                    var exchangeAmount = exchangeAmountMap.Get(whoKey).AsBigInteger();
                    if (exchangeAmount < s.value)
                        return false;
                    exchangeAmount -= s.value;
                    depositAmount += s.value;
                    depositBalanceMap.Put(whoKey, depositAmount);
                    exchangeAmountMap.Put(whoKey, exchangeAmount);
                    callStateMap.Delete(key);
                    return true;
                }
                s.returnvalue = returnvalue;
                s.state = 2;
                data = Neo.SmartContract.Framework.Helper.Serialize(s);
                callStateMap.Put(key, data);
                //notify
                GetReturned(txid, returnvalue);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 反向操作、取回钱
        /// </summary>
        /// <param name="who"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        private static object GetMoneyBack(byte[] who, BigInteger amount)
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
                //notify
                GetMoneyBack(key, amount);
                return true;
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