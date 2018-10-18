using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;
using Neo.SmartContract.Framework.Services.System;
using System.ComponentModel;

namespace ZoroBankContract
{
    public class ZoroBank : SmartContract
    {
        public delegate void deleDeposit(byte[] from, BigInteger value);
        [DisplayName("deposit")]
        public static event deleDeposit Depositted;

        public delegate void deleGetMoneyBack(byte[] to, BigInteger value);
        [DisplayName("getmoneyback")]
        public static event deleGetMoneyBack GetMoneyBack;

        public delegate void deleSendMoney(byte[] to, BigInteger value);
        [DisplayName("sendmoney")]
        public static event deleSendMoney SendMoney;

        public delegate void deleSetCanBack(byte[] to, BigInteger value);
        [DisplayName("setcanback")]
        public static event deleSetCanBack SetCanBack;

        [Appcall("e30e5f8aa1b5784570ec38dada546536187e0508")]
        static extern object zoroBcpCall(string method, object[] arr);

        static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("AbN2K2trYzgx8WMg2H7U7JHH6RQVzz2fnx");//管理员

        public static object Main(string method,object[] args)
        {
            var magicstr = "zorobank";
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
                if (method == "deposit") //存钱记录
                {
                    if (args.Length != 1) return false;
                    byte[] txid = (byte[])args[0];
                    var tx = new TransferInfo();
                    var keytx = new byte[] { 0x12 }.Concat(txid);
                    StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                    var v = depositBalanceMap.Get(keytx).AsBigInteger();
                    if (v == 0)
                    {
                        object[] ob = new object[1];
                        ob[0] = txid;
                        var info = zoroBcpCall("getTxInfo", ob);
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

                            //notify
                            Depositted(tx.@from, tx.value);
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                if (method == "setcanback")//记录可取钱的数目
                {
                    if (args.Length != 2)
                        return false;
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    byte[] who = (byte[])args[0];
                    BigInteger amount = (BigInteger)args[1];
                    var key = new byte[] { 0x11 }.Concat(who);
                    StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                    StorageMap canBackMoneyMap = Storage.CurrentContext.CreateMap(nameof(canBackMoneyMap));
                    var depositMoney = depositBalanceMap.Get(key).AsBigInteger();
                    var money = canBackMoneyMap.Get(key).AsBigInteger();
                    if (amount > depositMoney)
                        return false;
                    depositMoney -= amount;
                    money += amount;
                    depositBalanceMap.Put(key, depositMoney);
                    canBackMoneyMap.Put(key, money);
                }

                if (method == "balanceOf") //查存款
                {
                    if (args.Length != 1)
                        return 0;
                    byte[] who = (byte[])args[0];
                    var key = new byte[] { 0x11 }.Concat(who);
                    StorageMap depositBalanceMap = Storage.CurrentContext.CreateMap(nameof(depositBalanceMap));
                    return depositBalanceMap.Get(key).AsBigInteger();
                }

                if (method == "getmoneyback") //取回钱
                {
                    if (args.Length != 2)
                        return false;
                    byte[] who = (byte[])args[0];
                    BigInteger amount = (BigInteger)args[1];
                    var key = new byte[] { 0x11 }.Concat(who);
                    StorageMap canBackMoneyMap = Storage.CurrentContext.CreateMap(nameof(canBackMoneyMap));
                    var money = canBackMoneyMap.Get(key).AsBigInteger();
                    if (money < amount)
                        return false;

                    object[] transArr = new object[3];
                    transArr[0] = ExecutionEngine.ExecutingScriptHash;
                    transArr[1] = who;
                    transArr[2] = amount;

                    bool isSuccess = (bool)zoroBcpCall("transfer_app", transArr);
                    if (isSuccess)
                    {
                        money -= amount;
                        canBackMoneyMap.Put(key, money);
                        //notify
                        GetMoneyBack(key, amount);
                        return true;
                    }

                    return false;
                }

                if (method == "sendmoney") //发钱
                {
                    if (args.Length != 3)
                        return false;
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;
                    byte[] txid = (byte[])args[0];
                    byte[] who = (byte[])args[1];
                    var keytx = new byte[] { 0x12 }.Concat(txid);
                    StorageMap sendMoneyMap = Storage.CurrentContext.CreateMap(nameof(sendMoneyMap));
                    var v = sendMoneyMap.Get(keytx).AsBigInteger();
                    if (v == 0)
                    {
                        BigInteger amount = (BigInteger) args[2];
                        object[] transArr = new object[3];
                        transArr[0] = ExecutionEngine.ExecutingScriptHash;
                        transArr[1] = who;
                        transArr[2] = amount;

                        bool isSuccess = (bool) zoroBcpCall("transfer_app", transArr);
                        if (isSuccess)
                        {
                            sendMoneyMap.Put(keytx, 1);
                            //notify
                            SendMoney(who, amount);
                            return true;
                        }
                    }

                    return false;
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
