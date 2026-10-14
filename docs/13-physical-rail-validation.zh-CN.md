# 0.6.0 实体轨道、自动列车门和乘客显示验证

日期：2026-10-14。针对本机 RimWorld `1.7.4977 rev1248`、Waylith CLR `10.0.10-waylithclr.dev`、.NET SDK `10.0.401`。实机测试只启用 Core、官方 Harmony 1.7 和 LocalRail，不依赖 DLC。需求见 [12](12-physical-rail-requirements.zh-CN.md)，操作说明见 [README](../README.md)。

## 验证记录

| 检查 | 结果 | 证据 |
|---|---|---|
| 生产路径、时刻、缓存、门通行边界 | 59 条通过；构建 0 警告、0 错误 | [构建输出](../Validation/build-0.6.0.log) |
| 实体轨道、原生拖动、真实施工、四名携物乘客、三道自动门 | 39 条通过，图形模式，无 ERROR；提前开门为 true，穿过未开足门为 false | [结果](../Validation/engine-physical-visual-result.json)、[日志](../Validation/engine-physical-visual.log) |
| 门内停车存档在新进程读取 | 6 条通过，无 ERROR | [结果](../Validation/engine-physical-load-result.json)、[日志](../Validation/engine-physical-load.log) |
| 多站台、独立路线、自动停站、真实客货、正反环线 | 51 条通过，无 ERROR | [结果](../Validation/engine-network-trip-result.json)、[日志](../Validation/engine-network-trip.log) |
| 0.6 多站台和环线存档读取 | 7 条通过，无 ERROR | [结果](../Validation/engine-network-load-result.json)、[日志](../Validation/engine-network-load.log) |
| 0.5 非实体轨道存档升级 | 7 条通过，无 ERROR，三辆列车及 29 钢材保留 | [结果](../Validation/engine-upgrade-0.5-result.json)、[日志](../Validation/engine-upgrade-0.5.log) |
| 0.4 三站及独立货运迁移 | 9 条通过，无 ERROR，客货共 48 钢材交付 | [结果](../Validation/engine-load-result.json)、[日志](../Validation/engine-load.log) |
| 0.3 携物乘客迁移 | 6 条通过，无 ERROR，25 钢材交付 | [结果](../Validation/engine-previous-result.json)、[日志](../Validation/engine-previous.log) |
| 0.2 旧站和任务迁移 | 4 条通过，无 ERROR | [结果](../Validation/engine-legacy-result.json)、[日志](../Validation/engine-legacy.log) |
| 四种方向斜向接头画面 | 原生图形场景完成；人工查看连续接缝，不计入功能断言数量 | [截图](../Validation/rail-junctions.png)、[日志](../Validation/engine-junctions.log) |
| 安装、ZIP、XML 与语言 | 15 个文件逐项匹配，10 个 XML 可解析，107 组中英 Keyed 键一致 | [包清单](../Validation/package-manifest.json) |

上述测试在最终 DLL 上运行，不把重复执行同组断言累加为独立覆盖。脚本要求 completed=true 且 stdout 无 ERROR；Waylith 的 stub、无图形测试贴图和退出时分配器 WARN 仍保留原日志，不能据此声称零警告。

安装包：[LocalRail-0.6.0-rw1.7-poc.zip](../dist/LocalRail-0.6.0-rw1.7-poc.zip)，3,555,788 bytes。DLL SHA256：`39D0726B56E39BFA2EFF54AF7A13C2CDC13526AB1D1E36D5153C7C67C6AD398C`。ZIP SHA256：`1C9305B7DFED13D3B52932463874F9F9B31B4BC500C5172BF2F9B6DE6F68920A`。

1000 格固定路径、2000 次查询的微基准：未缓存 390.3236 ms、509,264,040 bytes；缓存 0.2045 ms、40 bytes，仅 1 次搜索。它测量路径缓存，不代表游戏整体帧率。实机门场景每 tick 最多 2 次完整乘客规划，与预算一致。

实际游戏截图：[四人车体与封闭走廊](../Validation/rail-physical-train.png)、[地形边界及三道门](../Validation/rail-terrain-door.png)。

## 游戏行为与实现

轨道、站台和自动列车门采用实体建筑格，不与其他实体建筑叠放。轨道可步行，允许原版地下电缆穿过，但不提供物品存储格；施工先处理树木和物品，低承载地形按原版规则需要桥。免费是指材料、电力、研究和燃料为零，仍保留施工工时。

自动列车门自带轨道，继承原版 `Building_Door` 的房间、气密、温度和殖民者寻路行为。创建门前先拆除该格旧轨道或墙。路线只能直线水平或垂直穿门，不能在门格内转弯或斜穿。开门预警按实际轨段 ticks 计算；未完全打开时列车停车，保留乘客及手持资源。列车车头保护提前三格检查，车体附近的门保持打开到车尾通过。禁止门通行时不强开；已占门的暂停或故障列车继续保护门，避免夹入车体。拆门使路线断开。

开门预警每辆运行中的列车至多检查前方 24 条边，并在中间停站处截止。车体保护格按地图每 tick 计算一次，门查询使用 HashSet，不逐门扫描所有路线。轨道烘入静态网格，使用复用材质；重建大站台时更新全部轨道格及邻接网格。

新的轨道绘制增加低饱和路基、枕木、深色边和浅色钢轨。所有细平面关闭顶点倾斜，避免地形遮挡；地形边界不改变轨道拓扑。保留连续斜向转角。默认铺设样式改为原生斜线 DrawStyle，实际原生 Designator 检查了 11 格连续斜线；原版横竖 Line 仍可选择。

车体使用原创俯视透明占位图。四个乘客座位沿车体旋转和插值，复用原版人物绘制树，世界贴图缩放为 0.55；肖像不缩放，下车恢复原绘制。乘坐时禁用人物贴图缓存，防止原缓存忽略矩阵缩放；移除步行影子与重叠姓名标签，列车检查栏显示乘客名单。真实携物仍留在 carryTracker；乘坐时由列车在对应座位旁画 0.5 比例的物品图标，避免原携物渲染覆盖人物头部。

## 兼容与测试边界

0.5 及以前的普通轨道不是实体建筑，可能和站台占地重叠。读档生成站台前，只清理本 Mod 的旧 `LR_Track`，保留站台、列车与路线引用。自带站台轨道继续提供连通；原先穿过站台步行带的额外轨道需要在站外重铺。迁移记录清理数量，不删除其他 Mod 或原版建筑。升级前应人工处理与其他实体建筑的旧叠放。

测试入口要求显式命令行开关和隔离存档目录。测试地图会清理自己的测试区域，普通存档不会运行这些入口。原生搬运场景使用真实物品、真实 carryTracker、原始 Job 和原版目标堆货格；没有通过传送或复制资源伪造验收。施工检查验证原版蓝图阻挡树木、清树后进入 Frame 并完成实体建筑，没有模拟完整的殖民者砍树工作链。

随机地图中的石块曾使目标堆货格不能存储，原生搬运工作在上车前结束。测试区域改为清理随机物品，并显式验证目标格有效；这属于隔离测试准备。游戏中的轨道不存储杂物，旧地图已有杂物仍需搬走。

## 美术生成记录

使用内置 imagegen，生成图没有用 Python 改绘。部署文件：[CartTop.png](../Mod/LocalRail/Textures/LocalRail/CartTop.png)，1536×1024、RGBA、透明边角，SHA256 `514B82245BA2EF5203386235565525295FC9972379A6375AC6C13A5A2D093948`。原始生成文件：`C:\Users\XHYX0\.codex\generated_images\01a13607-7351-7751-aea7-8143221155b0\exec-b7dede45-e71c-430e-9b61-efa6a9e457ef.png`。机器记录见 [cart-top-asset.json](../Validation/cart-top-asset.json)。

实际提示词如下：

> Generate a production game sprite, use case stylized-concept. A RimWorld vanilla-style small single-map open passenger rail trolley. Strict orthographic straight overhead top-down view, horizontal east-west orientation, long axis horizontal, symmetrical front/back. Clean thick charcoal outlines, flat muted olive painted metal, grey steel fittings, brown wood floor, minimal flat shading, no texture noise. A single rectangular low flatbed vehicle with a small motor compartment at LEFT end and a small buffer at RIGHT end, thin low perimeter rails around an EMPTY spacious flat wooden seating deck; no people, no individual tall seats, no rails or ground. The empty center deck occupies most of length so the game's four vanilla pawn sprites can be drawn on it. Two little visible wheel/bogie edges on each long side. Silhouette clearly readable at 100 pixel game size. Vehicle is centered, fills about 90% image width, actual hull width-to-height ratio around 2.6:1. Absolutely NO side-view/isometric/perspective, NO roof, NO cast shadow, NO glow, NO background, NO text, NO border. Transparent background with clean alpha. Landscape 1536x1024 canvas.

列车门引用玩家已安装游戏内的自动门素材和声音，不把官方图片复制进发布包。ZIP 保留旧图供兼容，共四张原创 PNG。

## 剩余范围

车体仍以中心格运行，未实现三格宽车辆的完整碰撞、多车信号和会车。自动列车门首版为一格直线门；开门时沿用原版温度交换，封闭房间不代表永久隔温。自动乘车仍使用工作白名单，未保证与第三方寻路、工作、人物绘制 Mod 的兼容。普通长期殖民地和复杂拆毁场景仍需要玩家继续检验。
