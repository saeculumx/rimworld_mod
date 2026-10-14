# 0.7.0 金属门、双轨与自动仓库货运交付

日期：2026-10-14。实际游戏 `RimWorld 1.7.4977 rev1248`、Waylith CLR `10.0.10-waylithclr.dev`。开发使用 .NET SDK 10.0.401，实机仅加载 Core、官方 Harmony 1.7 和 LocalRail，无 DLC。需求先记录在 [14](14-warehouse-double-rail-requirements.zh-CN.md)，实际操作见 [README](../README.md)。

## 建筑与材料

| 建筑 | 占地与方向 | 材料 | 行为 |
|---|---|---|---|
| 金属自动列车门 | 1×1，原版自动朝向 | 40 任意 Metallic 材料 | 单轨，自带轨道，基准耐久 300 |
| 双轨自动列车门 | 3×1，可旋转；东西线朝东／西 | 80 任意 Metallic 材料 | 两端为独立轨道，中间步行；共享开门和双线车尾保护；基准耐久 600 |
| 双轨模块 | 3×3，可旋转，拖动间距 3 | 免费，施工 180 work | 6 个轨道格，两轨间隔一格；只沿轨道轴连接 |
| 双站台货运仓库 | 5×5 | 免费，施工 1200 work | 两条独立轨道、15 储存格，每格 3 堆 |
| 四站台货运仓库 | 5×9 | 免费，施工 1800 work | 四条独立轨道、25 储存格，每格 3 堆 |

两种门在“结构”，其余新建筑在“杂项”。无电力、燃料或研究要求。双门跨度两端需要墙；使用原版多格门的要求，否则无法正常关闭。门下不能另铺轨道。替换既有双轨模块时，需要拆整个模块，在门两侧补回相应单轨。

耐久按原版 Stuff 系数计算，材料颜色和开门速度同样保留。下面来自本机 Core 的 `Items_Resource_Stuff.xml`，并通过实际金属建门检查确认玻璃钢最大耐久最高：

| 原版材料 | 耐久系数 | 单轨门 | 双轨门 |
|---|---:|---:|---:|
| 金 | 0.6 | 180 | 360 |
| 银 | 0.7 | 210 | 420 |
| 钢材 | 1.0 | 300 | 600 |
| 铀 | 2.5 | 750 | 1500 |
| 玻璃钢 / Plasteel | 2.8 | 840 | 1680 |

原版材质记录见 [vanilla-metal-door-factors.json](../Validation/vanilla-metal-door-factors.json)。兼容其他 Metallic 材料，不人为压低第三方材料系数，因此不保证第三方金属低于玻璃钢。钢材和玻璃钢可能有相同开门速度；材料耐久不同不代表所有材料速度都不同。

## 仓库物流规则

仓库继承车站，同时实现原版 `ISlotGroupParent`、`IHaulDestination`、储存设置和储存标签。只将非轨道行注册为搬运格。实机使用原版 `WorkGiver_HaulGeneral.JobOnThing`，验证它选择仓库，工人实际拾取并搬入 40 钢材。装车从实际库存 SplitOff / ThingOwner 转移，卸车使用原版堆叠；不复制或删除资源来模拟运输。

每仓最多 32 条自动规则，选择路线、本站台、目的站台和单种／全部货物。顺序、启停和目标可编辑。优先级在所有路线间统一：同一物品采用第一个有效、启用、匹配的规则，先到的其他列车不能抢走货物。首选路线暂停或正在朝反方向行驶时，货物等待该路线；禁用规则才允许下一条匹配规则接管。规则找不到其站台或目的地时不会装货。

路线默认客货两用。货运专列参与同样的运营、门、颜色和停站规则，但不接受乘客；改变模式通过路线编辑器完成。每列车八批货物，客货列车最多四人。仓库在停站时先卸后装，每 30 ticks 处理一批。卸货目标固定为站台，多站台按实际访问顺序处理。

目标仓库只向其储存格卸货，遵守过滤器、禁止状态、占用和原版容量。部分卸货保留余量，满仓或拒收不向仓库外溢出。失败批次在本次停站不重复尝试，因此自动停站不会为了它一直等满最长时间；下次访问重试。其他可卸批次和其他目的地仍能交付。普通站台保留原先站外卸货流程。

失败提示使用原版中性消息，并写 `[LocalRail] INFO` 日志，不发暂停信件，不改变时间速度。同一运行会话、路线和目标站台每 60000 ticks 至多一次；限频缓存读档后重建。

收到货物标记为接收库存，默认不再参与自动发货；合并到旧堆时，整个合并堆一起标记。搬出仓库解除标记。明确需要转运时可启用接收货物转运，但不自动计算跨路线多跳，也不验证玩家开启后的复杂循环。接收标记保存。

拆仓库时，原版会将超过普通地面容量的堆移动到附近，可能超过五格；测试按全地图实际数量验收，不能把卸货五格限制套用到原版拆建筑重放规则。存货保留，原生储存目的地注销，相关路线报告站点缺失。

## 兼容和测试

新门使用原版 Stuff。0.6 及以前已建成的无材质门在 PostLoadInit 赋予钢材，保留原对象和已有生命值，不补扣材料。旧门蓝图、框架也赋予钢材，保留已完成工时；未完工计划按新材料成本继续施工。旧路线默认客货两用，旧站、列车、货物和路径保存字段继续兼容。

测试只在显式开关及隔离目录执行，不清理玩家存档。旧计划测试从新保存的专用测试地图中删除蓝图的 stuffToUse 和框架的 stuff 字段，再用新进程加载；这是模拟旧数据形态，不宣称它是玩家的原始 0.6 存档。旧已建门使用保留的实际 0.6 测试存档。

| 检查 | 结果 | 证据 |
|---|---|---|
| 生产路径、时刻、缓存与通行规则 | 59 条通过 | [逻辑输出](../Validation/build-0.7.0.log) |
| 最终二进制构建 | 0 警告、0 错误 | [最终构建](../Validation/build-final-0.7.0.log) |
| 仓库、双轨、材料门、全路线规则优先级、真实自动货运 | 45 条通过，含原生图形界面 | [结果](../Validation/engine-warehouse-visual-result.json)、[日志](../Validation/engine-warehouse-visual.log) |
| 仓库货物读档、余量交付及拆毁 | 12 条通过 | [结果](../Validation/engine-warehouse-load-result.json)、[日志](../Validation/engine-warehouse-load.log) |
| 模拟旧无材质蓝图及框架读取 | 12 条通过，同组加载检查，不累加为独立断言 | [结果](../Validation/engine-warehouse-legacy-plans-result.json)、[日志](../Validation/engine-warehouse-legacy-plans.log) |
| 原始 0.6 无材质建成门升级 | 6 条通过 | [结果](../Validation/engine-upgrade-0.6-result.json)、[日志](../Validation/engine-upgrade-0.6.log) |
| 单轨门、四人携物、物理轨道和斜向拖动 | 39 条通过 | [结果](../Validation/engine-physical-visual-result.json)、[日志](../Validation/engine-physical-visual.log) |
| 单轨门内停车读档 | 6 条通过 | [结果](../Validation/engine-physical-load-result.json) |
| 多站台、自动停站、独立货运与正反环线 | 51 条通过 | [结果](../Validation/engine-network-trip-result.json)、[日志](../Validation/engine-network-trip.log) |
| 本版多站台运输读档 | 7 条通过 | [结果](../Validation/engine-network-load-result.json) |
| 原始 0.5 多站台迁移 | 7 条通过 | [结果](../Validation/engine-upgrade-0.5-result.json) |
| 0.4、0.3、0.2 运输存档迁移 | 9、6、4 条通过 | [0.4](../Validation/engine-load-result.json)、[0.3](../Validation/engine-previous-result.json)、[0.2](../Validation/engine-legacy-result.json) |
| 安装、ZIP、XML、双语 | 18 文件一致、11 XML 可解析、119 组中英 Keyed 键一致 | [最终包清单](../Validation/package-manifest.json) |

所有成功脚本同时要求 completed=true 和 stdout 无 ERROR，原始日志保留引擎 stub、图形／无图形差异与退出分配器 WARN，不宣称引擎零警告。重复运行不累加覆盖数量。

安装包：[LocalRail-0.7.0-rw1.7-poc.zip](../dist/LocalRail-0.7.0-rw1.7-poc.zip)，5,095,461 bytes。DLL SHA256：`307031853D4846EE6D9A2C0194E20C712BAAB6246C46AF4A157D0577CCBB232A`。ZIP SHA256：`A067E020838C86D4E64974445E2354110E2D686C96D7A098111224B2CB578A31`。

场景具体检查：40 钢材在满仓时留车，53 布和 17 银到不同路线、不同目标站台交付；拒收的 13 铀留车，修改过滤器后到站交付；25 钢材先卸 15，余下 10 保存到车内，读档、腾出空间后全部交付。原车 ID、货运模式、储存设置、规则、接收库存和受损钢门均保持。

仓库库存只在停站交换及自动停站判断时扫描，不逐 tick 扫全图资源；同次判断按物品类型缓存首选规则，规则数上限 32。轨道与门继续使用缓存拓扑、静态网格和有限预警。完整乘客规划保留每地图每 tick 两次预算；实机记录未超过两次。未进行大型殖民地整体 FPS 基准。

## 美术与界面

使用内置 imagegen，未调用 CLI。两张原创透明素材已复制到发布目录，均为 2172×724，边角 alpha=0。未使用 Python 改图。原图、完整提示词、尺寸和 SHA256 记录见 [warehouse-door-assets.json](../Validation/warehouse-door-assets.json)。

- [WarehouseDeck.png](../Mod/LocalRail/Textures/LocalRail/WarehouseDeck.png)：低饱和木板、钢边及小控制箱，绘制在各储存行；资源仍使用实际游戏物品图。
- [DoubleDoorLeaf.png](../Mod/LocalRail/Textures/LocalRail/DoubleDoorLeaf.png)：中性灰色门片，由原版门移动逻辑驱动，并接受材质颜色。

仓库素材实际提示词：

> Use case: stylized-concept. Asset type: production transparent sprite for a RimWorld vanilla-style rail freight warehouse loading platform. Strict orthographic top-down view, long horizontal rectangle ratio 4:1, centred on a landscape canvas. A low empty freight loading deck: muted brown wooden planks inside a thick grey steel perimeter, olive-green thin safety edges, small dark bolts, one tiny control cabinet at far left edge and one tiny folded loading ramp at far right. Wide completely EMPTY middle area for actual game resource piles, no permanent crates. Clean thick charcoal outlines, flat simple shading, low saturation, readable at 100 pixels. No roof, no walls, no people, no track or train, no floor outside the deck, no lettering, no yellow hazard stripes, no shadow, no perspective or isometric view. Real transparent background and clean alpha. Suitable to repeat on the storage rows beside two independent rail lines. Landscape 1536x1024 canvas.

双门素材实际提示词：

> Use case: stylized-concept. Asset type: transparent neutral metal sliding gate leaf sprite for a RimWorld vanilla-style three-cell-wide double-track automatic door. Strict orthographic top-down overhead view of ONE rectangular low metal sliding panel, long axis horizontal, length to thickness ratio 3:1. Muted neutral grey sheet steel divided into three broad panels, thick dark charcoal edge outline, a simple narrow steel spine, very few black bolts, no colored paint so the game can tint by construction material. Crisp flat vanilla game shading, minimal detail readable at 60 pixels. Panel only: NO doorway, no fixed frame, no tracks, no ground, no walls, no people, no lettering, no hazard stripes, no cast shadow, no perspective or isometric view. Centered landscape canvas, true transparent background and clean alpha. It will be rendered as mirrored sliding half-panels by game code.

提示词请求的画布大小是创作约束，交付按实际生成文件尺寸记录。双轨几何沿用程序化香草枕木、路基和钢轨，未引入另一套轨宽。

实际界面：[自动规则窗口](../Validation/rail-warehouse-rules.png)，支持原版字体、滚动、启停、排序、类型选择和修改目的地；长名称省略并提供完整提示。实际地图：[双线仓库及双门](../Validation/rail-warehouse-network.png)。

## 当前范围

仍是实验版。每路线一辆常驻车、独占路径；不包含共享轨道信号、同线多车、跨路线自动换乘和宽车体完整碰撞。仓库是共享库存，不是每站台独立仓；货物目的站台仍精确保存和验证。双轨模块提供直线，斜轨和换线在模块外铺单轨。玩家开启转运后的物流循环、第三方工作／储存／寻路 Mod、易腐物品及长期大殖民地性能还需更多验证。

