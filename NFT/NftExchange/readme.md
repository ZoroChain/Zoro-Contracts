# NFT asset 买卖合约接口整理

## 合约管理部分：
需要管理员签名
* 交易所合约状态
    * 可用：所有接口可用
    * 只读：只有查询接口可用
    * 停用：所有接口停用
* 设置白名单 即支持币种、交易对
    * 增加
    * 删除
* 设置交易费收取地址
* 设置交易员 Address

## 买卖部分：
* 充值：往交易所存钱,先调用代币的 Approve 接口，to address 为交易所合约，然后合约中调用 TransferFrom 接口将要充值的资产转到本合约，需要用户签名。
* 挂单：挂买卖单，需要发起者和交易员签名。调用该接口前要使用 NFT 合约的 Approve 接口将标的 NFT 授权给合约。
需要参数：发起者地址 offerAddress, 要出售的 NFT 的 Contract Hash nftContractHash, 要出售的 NFT TokenId, 接受的目标币种 acceptAssetId, 售卖价格 price, 手续费 feeAssetID，手续费 feeAmount，时间戳 timespan；挂单完成后会产生 notify，输出挂单编号和挂单信息，需要后端保存。挂单后冻结手续费资产，NFT 会使用 transferFrom 转移到合约地址。

* 吃单：(成交单) 用户选择了要成交的对手单，在合约中进行成交验证和加减余额，需要交易员签名，参数： 发起者地址 fillerAddress, 要成交的对手单编号 offerHash, 支付资产 fillAssetId，要支付的币种数量 fillAmount, 手续费 fillFeeAssetID，手续费数量 fillFeeAmount，时间戳 timespan。成交后会扣除/增加用户账户的余额、动态调用 transferApp 接口将标的 NFT 转移到 fillerAddress，成交后产生 notify。

* 撤单：撤销未成交的单子，需要挂单者签名，参数：挂单编号 offerHash，撤单成功后解冻相应资产，资产变化信息都会在 notify 中体现。
* 取钱：用户从交易所提出币，需要用户签名，参数：用户地址 address，资产类型 assetId，提现数量 amount，动态调用 TransferApp 接口实现。

## 查询部分
* 查询合约状态
* 查询币种是否包含在白名单中 参数：assetId，返回 true/false
* 查询已挂单 参数：挂单编号，返回挂单信息
* 查询账户指定资产的余额 参数：address，assetId， 返回余额
* 查询可用余额，参数：address，assetId， 返回余额
* 查询当前收交易费的地址
* 查询交易员地址
