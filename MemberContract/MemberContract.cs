using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System.Numerics;

namespace MemberContract
{
    public class MemberContract : SmartContract
    {
        public static object Main(string operation, object[] args)
        {
            var callScriptHash = ExecutionEngine.CallingScriptHash;
            if (Runtime.Trigger == TriggerType.Application)
            {
                //给一个address发放会员
                if (operation == "Deploy")
                {
                    byte[] address = (byte[])args[0];
                    //取得会员信息的存储区，使用address作为key
                    StorageMap memberInfoMap = Storage.CurrentContext.CreateMap("memberInfoMap");
                    var memberInfo = new MemberInfo { Owner = address, Rank = 1, Point = 0 };
                    byte[] memberInfoBytes = Neo.SmartContract.Framework.Helper.Serialize(memberInfo);
                    memberInfoMap.Put(address, memberInfoBytes);
                    return true;
                }

                //根据address获取会员信息
                if (operation == "GetInfo")
                {
                    byte[] address = (byte[])args[0];
                    //取得会员信息的存储区
                    StorageMap memberInfoMap = Storage.CurrentContext.CreateMap("memberInfoMap");
                    byte[] data = memberInfoMap.Get(address);
                    var memberInfo = new MemberInfo();
                    if (data.Length > 0)
                        memberInfo = data.Deserialize() as MemberInfo;
                    return memberInfo;
                }

                //增加积分
                if (operation == "AddPoint")
                {
                    byte[] address = (byte[])args[0];
                    //取得会员信息的存储区
                    StorageMap memberInfoMap = Storage.CurrentContext.CreateMap("memberInfoMap");
                    BigInteger pointValue = (BigInteger)args[1];
                    byte[] data = memberInfoMap.Get(address);
                    var memberInfo = new MemberInfo();
                    if (data.Length == 0)
                        return false;
                    memberInfo = data.Deserialize() as MemberInfo;
                    memberInfo.Point += pointValue;
                    byte[] memberInfoBytes = Neo.SmartContract.Framework.Helper.Serialize(memberInfo);
                    memberInfoMap.Put(address, memberInfoBytes);
                    return true;
                }

                //升级
                if (operation == "Upgrade")
                {
                    byte[] address = (byte[])args[0];
                    //取得会员信息的存储区
                    StorageMap memberInfoMap = Storage.CurrentContext.CreateMap("memberInfoMap");
                    byte[] data = memberInfoMap.Get(address);
                    var memberInfo = new MemberInfo();
                    if (data.Length == 0)
                        return false;
                    memberInfo = data.Deserialize() as MemberInfo;
                    memberInfo.Rank += 1;
                    byte[] memberInfoBytes = Neo.SmartContract.Framework.Helper.Serialize(memberInfo);
                    memberInfoMap.Put(address, memberInfoBytes);
                    return true;
                }
            }
            return false;
        }
    }
    public class MemberInfo
    {
        public byte[] Owner; //所有者 address
        public BigInteger Rank; //等级
        public BigInteger Point; //积分值
    }
}
