using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

namespace BancorMath
{
    public class BancorMath : SmartContract
    {
        private static readonly BigInteger FIXED_1 = (new byte[] {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 0}).AsBigInteger(); //0x800000000000000000000000000000  
        static readonly BigInteger B_010000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }).AsBigInteger();
        static readonly BigInteger B_020000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2 }).AsBigInteger();
        static readonly BigInteger B_040000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4 }).AsBigInteger();
        static readonly BigInteger B_080000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8 }).AsBigInteger();
        static readonly BigInteger B_100000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 16 }).AsBigInteger();
        static readonly BigInteger B_200000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 32 }).AsBigInteger();
        static readonly BigInteger B_300000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48 }).AsBigInteger();
        static readonly BigInteger B_400000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 64 }).AsBigInteger();
        static readonly BigInteger B_500000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 80 }).AsBigInteger();
        static readonly BigInteger B_600000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 96 }).AsBigInteger();
        static readonly BigInteger B_700000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 112 }).AsBigInteger();
        static readonly BigInteger B_800000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 0 }).AsBigInteger();
        static readonly BigInteger B_d3094c70f034de4b96ff7d5b6f99fc = (new byte[] { 252, 153, 111, 91, 125, 255, 150, 75, 222, 52, 240, 112, 76, 9, 211, 0 }).AsBigInteger();
        static readonly BigInteger B_a45af1e1f40c333b3de1db4dd55f29 = (new byte[] { 41, 95, 213, 77, 219, 225, 61, 59, 51, 12, 244, 225, 241, 90, 164, 0 }).AsBigInteger();
        static readonly BigInteger B_910b022db7ae67ce76b441c27035c6 = (new byte[] { 198, 53, 112, 194, 65, 180, 118, 206, 103, 174, 183, 45, 2, 11, 145, 0 }).AsBigInteger();
        static readonly BigInteger B_88415abbe9a76bead8d00cf112e4d4 = (new byte[] { 212, 228, 18, 241, 12, 208, 216, 234, 107, 167, 233, 187, 90, 65, 136, 0 }).AsBigInteger();
        static readonly BigInteger B_84102b00893f64c705e841d5d4064b = (new byte[] { 75, 6, 212, 213, 65, 232, 5, 199, 100, 63, 137, 0, 43, 16, 132, 0 }).AsBigInteger();
        static readonly BigInteger B_8204055aaef1c8bd5c3259f4822735 = (new byte[] { 53, 39, 130, 244, 89, 50, 92, 189, 200, 241, 174, 90, 5, 4, 130, 0 }).AsBigInteger();
        static readonly BigInteger B_810100ab00222d861931c15e39b44e = (new byte[] { 78, 180, 57, 94, 193, 49, 25, 134, 45, 34, 0, 171, 0, 1, 129, 0 }).AsBigInteger();
        static readonly BigInteger B_808040155aabbbe9451521693554f7 = (new byte[] { 247, 84, 53, 105, 33, 21, 69, 233, 187, 171, 90, 21, 64, 128, 128, 0 }).AsBigInteger();
        static readonly BigInteger B_008000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 0 }).AsBigInteger();
        static readonly BigInteger B_0aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa = (new byte[] { 170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 0 }).AsBigInteger();
        static readonly BigInteger B_0888888888888888888888888888888 = (new byte[] { 136, 136, 136, 136, 136, 136, 136, 136, 136, 136, 136, 136, 136, 136, 136, 0 }).AsBigInteger();
        static readonly BigInteger B_0999999999999999999999999999999 = (new byte[] { 153, 153, 153, 153, 153, 153, 153, 153, 153, 153, 153, 153, 153, 153, 153, 0 }).AsBigInteger();
        static readonly BigInteger B_0924924924924924924924924924924 = (new byte[] { 146, 36, 73, 146, 36, 73, 146, 36, 73, 146, 36, 73, 146, 36, 73, 146, 0 }).AsBigInteger();
        static readonly BigInteger B_08e38e38e38e38e38e38e38e38e38e3 = (new byte[] { 227, 56, 142, 227, 56, 142, 227, 56, 142, 227, 56, 142, 227, 56, 142, 0 }).AsBigInteger();
        static readonly BigInteger B_08ba2e8ba2e8ba2e8ba2e8ba2e8ba2e = (new byte[] { 46, 186, 232, 162, 139, 46, 186, 232, 162, 139, 46, 186, 232, 162, 139, 0 }).AsBigInteger();
        static readonly BigInteger B_089d89d89d89d89d89d89d89d89d89d = (new byte[] { 157, 216, 137, 157, 216, 137, 157, 216, 137, 157, 216, 137, 157, 216, 137, 0 }).AsBigInteger();
        static readonly BigInteger B_1000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }).AsBigInteger();
        static readonly BigInteger B_2000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2 }).AsBigInteger();
        static readonly BigInteger B_3000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3 }).AsBigInteger();
        static readonly BigInteger B_4000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4 }).AsBigInteger();
        static readonly BigInteger B_5000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5 }).AsBigInteger();
        static readonly BigInteger B_6000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6 }).AsBigInteger();
        static readonly BigInteger B_7000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7 }).AsBigInteger();
        static readonly BigInteger B_8000000000000000000000000000000 = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8 }).AsBigInteger();

        //static readonly BigInteger MaxConnectWeight = 100000;

        public static object Main(string method, object[] args)
        {
            string magicStr = "bancorMath";
            if (Runtime.Trigger == TriggerType.Verification)
            {
                //鉴权部分
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //转入一定的抵押币换取智能代币
                if ("purchase" == method)
                {
                    var E = (BigInteger)args[0]; // 转入的准备金的数量

                    var R = (BigInteger)args[1]; //准备金的余额

                    var S = (BigInteger)args[2];//智能代币的余额

                    // connectWeight/maxConnectWeight得到恒定金率  即w
                    var connectWeight = (BigInteger)args[3];

                    var maxConnectWeight = (BigInteger)args[4];

                    var a = (E + R) * FIXED_1 / R;
                    var baseLog = OptimalLog(a);

                    var baseLogTimesExp = baseLog * connectWeight / maxConnectWeight; // 因为没有小数  所以只能用先×10 再/100这种方法来模拟 ×0.1

                    //计算得出可以换取得智能代币的数量
                    var T = S * OptimalExp(baseLogTimesExp) / FIXED_1 - S; //S*(1+E/R)^F -S

                    return T;
                }

                //清算一定的智能代币换取抵押币
                if ("sale" == method)
                {
                    var T = (BigInteger)args[0]; // 转入的智能代币的数量

                    var R = (BigInteger)args[1]; //准备金的余额

                    var S = (BigInteger)args[2];//智能代币的余额

                    // connectWeight/maxConnectWeight得到恒定金率  即w
                    var connectWeight = (BigInteger)args[3];

                    var maxConnectWeight = (BigInteger)args[4];

                    var baseLog = OptimalLog((T + S) * FIXED_1 / S);

                    var baseLogTimesExp = baseLog * maxConnectWeight / connectWeight;

                    var E = R * OptimalExp(baseLogTimesExp) / FIXED_1 - R; //S*(1+E/R)^F

                    return E;
                }
            }
            return true;
        }

     
        //Return e ^ (x / FIXED_1) * FIXED_1
        public static BigInteger OptimalExp(BigInteger x)
        {
            BigInteger res = 0;

            BigInteger z = x % B_100000000000000000000000000000;
            BigInteger y = z;
            z = z * y / FIXED_1; res += z * 0x10e1b3be415a0000; // add y^02 * (20! / 02!)
            z = z * y / FIXED_1; res += z * 0x05a0913f6b1e0000; // add y^03 * (20! / 03!)
            z = z * y / FIXED_1; res += z * 0x0168244fdac78000; // add y^04 * (20! / 04!)
            z = z * y / FIXED_1; res += z * 0x004807432bc18000; // add y^05 * (20! / 05!)
            z = z * y / FIXED_1; res += z * 0x000c0135dca04000; // add y^06 * (20! / 06!)
            z = z * y / FIXED_1; res += z * 0x0001b707b1cdc000; // add y^07 * (20! / 07!)
            z = z * y / FIXED_1; res += z * 0x000036e0f639b800; // add y^08 * (20! / 08!)
            z = z * y / FIXED_1; res += z * 0x00000618fee9f800; // add y^09 * (20! / 09!)
            z = z * y / FIXED_1; res += z * 0x0000009c197dcc00; // add y^10 * (20! / 10!)
            z = z * y / FIXED_1; res += z * 0x0000000e30dce400; // add y^11 * (20! / 11!)
            z = z * y / FIXED_1; res += z * 0x000000012ebd1300; // add y^12 * (20! / 12!)
            z = z * y / FIXED_1; res += z * 0x0000000017499f00; // add y^13 * (20! / 13!)
            z = z * y / FIXED_1; res += z * 0x0000000001a9d480; // add y^14 * (20! / 14!)
            z = z * y / FIXED_1; res += z * 0x00000000001c6380; // add y^15 * (20! / 15!)
            z = z * y / FIXED_1; res += z * 0x000000000001c638; // add y^16 * (20! / 16!)
            z = z * y / FIXED_1; res += z * 0x0000000000001ab8; // add y^17 * (20! / 17!)
            z = z * y / FIXED_1; res += z * 0x000000000000017c; // add y^18 * (20! / 18!)
            z = z * y / FIXED_1; res += z * 0x0000000000000014; // add y^19 * (20! / 19!)
            z = z * y / FIXED_1; res += z * 0x0000000000000001; // add y^20 * (20! / 20!)
            res = res / 0x21c3677c82b40000 + y + FIXED_1; // divide by 20! and then add y^1 / 1! + y^0 / 0!
            return res;
        }

        //Return log(x / FIXED_1) * FIXED_1
        public static BigInteger OptimalLog(BigInteger x)
        {
            BigInteger res = 0;

            if (x >= B_d3094c70f034de4b96ff7d5b6f99fc)
            {
                res += B_400000000000000000000000000000;
                x = x * FIXED_1 / B_d3094c70f034de4b96ff7d5b6f99fc;  //0.606530659712634
            }
            if (x >= B_a45af1e1f40c333b3de1db4dd55f29)
            {
                res += B_200000000000000000000000000000;
                x = x * FIXED_1 / B_a45af1e1f40c333b3de1db4dd55f29;
            }
            if (x >= B_910b022db7ae67ce76b441c27035c6)
            {
                res += B_100000000000000000000000000000;
                x = x * FIXED_1 / B_910b022db7ae67ce76b441c27035c6;
            }
            if (x >= B_88415abbe9a76bead8d00cf112e4d4)
            {
                res += B_080000000000000000000000000000;
                x = x * FIXED_1 / B_88415abbe9a76bead8d00cf112e4d4;
            }
            if (x >= B_84102b00893f64c705e841d5d4064b)
            {
                res += B_040000000000000000000000000000;
                x = x * FIXED_1 / B_84102b00893f64c705e841d5d4064b;
            }
            if (x >= B_8204055aaef1c8bd5c3259f4822735)
            {
                res += B_020000000000000000000000000000;
                x = x * FIXED_1 / B_8204055aaef1c8bd5c3259f4822735;
            }
            if (x >= B_810100ab00222d861931c15e39b44e)
            {
                res += B_010000000000000000000000000000;
                x = x * FIXED_1 / B_810100ab00222d861931c15e39b44e;
            }
            if (x >= B_808040155aabbbe9451521693554f7)
            {
                res += B_008000000000000000000000000000;
                x = x * FIXED_1 / B_808040155aabbbe9451521693554f7;
            }

            BigInteger z = x - FIXED_1;
            BigInteger y = x - FIXED_1;
            BigInteger w = y * y / FIXED_1;
            res += z * (B_1000000000000000000000000000000 - y) / B_1000000000000000000000000000000;
            z = z * w / FIXED_1;
            res += z * (B_0aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa - y) / B_2000000000000000000000000000000;
            z = z * w / FIXED_1;
            res += z * (B_0999999999999999999999999999999 - y) / B_3000000000000000000000000000000;
            z = z * w / FIXED_1;
            res += z * (B_0924924924924924924924924924924 - y) / B_4000000000000000000000000000000;
            z = z * w / FIXED_1;
            res += z * (B_08e38e38e38e38e38e38e38e38e38e3 - y) / B_5000000000000000000000000000000;
            z = z * w / FIXED_1;
            res += z * (B_08ba2e8ba2e8ba2e8ba2e8ba2e8ba2e - y) / B_6000000000000000000000000000000;
            z = z * w / FIXED_1;
            res += z * (B_089d89d89d89d89d89d89d89d89d89d - y) / B_7000000000000000000000000000000;
            z = z * w / FIXED_1;
            res += z * (B_0888888888888888888888888888888 - y) / B_8000000000000000000000000000000;
            return res;
        }
    }
}
