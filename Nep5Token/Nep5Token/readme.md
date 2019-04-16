# Nep5.z1
Nep.z1 是 Zoro 上发布代币资产的参考标准，其在保留了 NEO Nep5 合约接口的基础上扩展了
 approve，transferFrom，transferApp，getTxInfo 等接口。

该标准有如下接口：
* totalSupply： token 总量
* name：token 名称
* symbol：token 符号，简称
* decimals：精度，8 位
* balanceOf：查询 address 余额
* getTxInfo：根据 txid 查询交易信息
* allowance：查询 approve 的 allowance
* transfer：正常交易，参数：from，to，value，需要 from 签名
* transferApp：合约的转账交易，只允许其他合约调用并且 from 是调用合约的 script hash，参数：from，to，value
* approve：授权可以从 from 转出到 to 的数额，参数：from，to，value，需要 from 签名，授权以后其他合约可以转走该笔资产；
* transferFrom：approve 后的转账交易，参数：from，to，value；
* supportedStandards: 返回合约符合的标准 return "{\"NEP-5\"}"
