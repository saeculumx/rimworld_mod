# 0.5.0 多站台、环线与管理界面验证

日期：2026-10-13。实际运行游戏 `1.7.4977 rev1248`，CLR `10.0.10-waylithclr.dev`，构建 SDK `10.0.401`。仅启用 Core、Harmony 1.7 和 LocalRail，无 DLC。此文记录 0.5.0 最终 DLL 的验证；01–09 是历史版本记录。

## 交付和安装

- 本轮需求与 Use cases：[10-network-management-requirements.zh-CN.md](10-network-management-requirements.zh-CN.md)。操作和边界：[README](../README.md)。
- 安装包：[LocalRail-0.5.0-rw1.7-poc.zip](../dist/LocalRail-0.5.0-rw1.7-poc.zip)，2,339,642 bytes。
- 已安装到 `D:\SteamLibrary\steamapps\common\RimWorld\Mods\LocalRail`。重启游戏才能加载新 DLL；Harmony 必须在 LocalRail 前加载。
- 最终构建 0 警告、0 错误。原有三张原创 PNG 继续用作占位；多站台按真实轨道行组合绘制，不引入大量新美术。
- DLL SHA256：`750B056DA189D709E3FB4421905CD30535420D37043948CA14C12B0700C44C81`。
- ZIP SHA256：`042E21042DBC164EA0CED2E0AAAD10326A3A22029D5C1A2839C3B7327DCB7E5D`。

## 最终检查记录

| 检查 | 结果 | 证据 |
|---|---|---|
| 生产路径、成本、时刻与缓存逻辑 | 57 条通过 | [构建和测试输出](../Validation/build-0.5.0.log) |
| 0.5 原生引擎功能，含实际图形界面 | 50 条通过，stdout 无 ERROR | [结果](../Validation/engine-network-visual-result.json)、[日志](../Validation/engine-network-visual.log) |
| 新版运输中存档，新进程读取 | 7 条通过，stdout 无 ERROR | [结果](../Validation/engine-network-load-result.json)、[日志](../Validation/engine-network-load.log) |
| 0.4 三站、随身资源与独立货运存档迁移 | 9 条通过，stdout 无 ERROR | [结果](../Validation/engine-load-result.json)、[日志](../Validation/engine-load.log) |
| 0.3 携物乘客与常驻列车存档迁移 | 6 条通过，stdout 无 ERROR | [结果](../Validation/engine-previous-result.json)、[日志](../Validation/engine-previous.log) |
| 0.2 旧站及运输任务迁移 | 4 条通过，stdout 无 ERROR | [结果](../Validation/engine-legacy-result.json)、[日志](../Validation/engine-legacy.log) |
| XML、语言和安装包一致性 | 9 个 XML 可解析，103 组中英 Keyed 键一致，13 个交付文件逐项匹配源目录、已安装目录及 ZIP | [包清单](../Validation/package-manifest.json) |

无图形版 Network 也通过 50 条断言；它与图形版重复同组场景，不相加宣称独立覆盖数量。各脚本同时要求 `completed=true` 和 stdout 无 ERROR。Waylith 的 stub/退出时内存警告仍记录在原始日志，没有把“无 ERROR”写成“零警告”。

## 原生行为验收

- 从实际 `Misc.ResolvedAllowedDesignators` 检查 3×3 单站台、5×5 双站台、5×9 四站台三个建筑入口和零材料成本。实际 `MainButtonDef.TabWindow` 能创建 Rail 管理页。
- 双站台产生两行独立东西轨道；仅属于同一建筑不连通。在建筑外铺联络轨后，连接表能报告同楼和另一车站的真实可达站台。拆轨会使缓存失效。
- 同一连通路网创建两条使用不同轨道行的路线和两辆永久列车。重叠路线被拒绝，并指出占用路线名称。拆除南线轨道，南线暂停而北线继续；修复后两线恢复，不产生额外车辆。
- 停站表单能接受 `1.75` 秒，转换为 105 ticks；非法文本及自动最少大于最长不能提交。字段按模式启用；禁用字段不阻止当前模式保存。
- 真实 `LR_LoadFreight` 原版拾取/移动 Toil 中，自动停站等待附近装货工超过最短停留，19 钢材的实际 Thing 进入正确列车并在目标站台 5 格内卸出。
- 最长设为 90 ticks 时，装货工仍在作业也按时发车；迟到的 11 钢材留待下一班并能装车，没有丢失或复制。
- 原生 `HaulToCell` 的乘客携 25 钢材使用二号站台；在列车中保留同一个 carryTracker 物品和原 Job，最终把 25 钢材送至原储存区。
- 四站环线建立 248 个唯一轨道格，闭合边计入 744 ticks 的加权周长。连续绕行两圈跨首尾而不在末站折返。断开闭合弧会暂停，修复后保留同辆车恢复。不能用往返路径冒充闭合环线。
- 环线末站→首站同时载人和独立货物，23 钢材送至首站，乘客继续原目标。随后将同一辆车的路线切换反向，原生工人装入 29 钢材，乘客和货物进入首站→末站闭合段。
- 保存三条命名和彩色路线、准确站台索引、各站台自动时间和反向环线中的 29 钢材。新进程恢复完全相同的三辆车，乘客回到原 Job/目标；继续模拟后货物在准确目标站台卸出，乘客到达原目标。
- 0.4 的随身 25 钢材与独立货运 23 钢材合计 48 件继续交付，允许原版合法合堆。0.3 保留同一列车和随身钢材。0.2 乘客恢复原工作，不为旧站自动召唤未经配置的列车。

## 实际界面

图像来自本机原生游戏的 ScreenCapture，不是概念图。测试打开原生窗口并检查渲染及运行日志；未声称模拟了每个按钮的人工鼠标操作。截图使用 English，交付包含简体中文 DefInjected 和 Keyed 翻译。

| 界面 | 图像 |
|---|---|
| 统一路线列表、颜色、搜索与操作 | [rail-network-manager.png](../Validation/rail-network-manager.png) |
| 分站台左右端口、可达站台、运营路线和自动时间 | [rail-platform-manager.png](../Validation/rail-platform-manager.png) |
| 自动开关与可输入的手动/最少/最长秒数 | [rail-dwell-editor.png](../Validation/rail-dwell-editor.png) |
| 环线、反向、名称颜色、站序和地图方向箭头 | [rail-loop-editor.png](../Validation/rail-loop-editor.png) |

在 1280×720 下四个窗口检查通过。路线状态和长站序使用省略及悬停提示，站点列表可滚动，地图预览只在配置/拓扑改变时重算。

## 性能和适用边界

连接表和路径按拓扑缓存，乘客复用路线，每图每 tick 最多两次完整规划；图形验收中的实际峰值为 1。自动停站使用登记的乘客和装货工，不逐 tick 遍历全图殖民者。静态轨道继续烘入地图网格。

最终纯逻辑微基准：1,000 格直线，重复查询 2,000 次，重新寻路 448.2748 ms、缓存命中 0.1967 ms。相应托管分配为 509,264,040 / 40 bytes，缓存实际搜索 1 次。见 [topology-benchmark.json](../Validation/topology-benchmark.json)。这是单一拓扑查询的微基准，不是游戏 FPS、TPS 或真实殖民地性能保证。

本版本每条路线一辆车，每格轨道由一条路线占用。多站台使两条并行路线可以服务同一建筑，但没有共享轨道信号/会车、同路线多车或自动换乘。连接表报告轨道拓扑，墙、门等阻塞在运行状态另行报告。站台轨道固定东西向，建筑不旋转。复杂多环路网采用逐段约束路径，可能拒绝存在另一种全局选路解的站序。

自动等候针对已登记且在范围内的接近乘客和装货工；最长时间仍会发车，不会承诺全地图所有人都能赶上本班。独立货运仍由右键订单发起，未实现自动装货 WorkGiver。读档恢复乘客原工作和目标，但不保留原座位及候车队列顺序。

仍需玩家在普通殖民地及常用工作/寻路 Mod 组合中验证；本轮隔离验收未覆盖所有第三方 Mod、易腐货物或每种拆毁/阻塞情况。此前斜轨连续转角绘制保留，最新 50 条场景以多站台和环线为重点，没有重跑 0.4.1 的全部斜轨截图场景。

## 复现

在工作目录逐个运行：

```powershell
.\build.ps1 -Test
.\Validation\run-engine.ps1 -Network -Visual
.\Validation\run-engine.ps1 -NetworkLoad
.\Validation\run-engine.ps1 -LoadSavedTrip
.\Validation\run-engine.ps1 -PreviousLoad
.\Validation\run-engine.ps1 -LegacyLoad
.\Validation\audit-package.ps1
```

Network 使用 `.local/LocalRailTestNetwork`，历史迁移使用 `.local/LocalRailTest`。仅在显式命令行开关且匹配测试配置目录时启用测试夹具；普通游戏不创建或清理测试地图。测试不修改玩家正常存档。ZIP 不包含官方程序集、Harmony DLL、下载的工具或反编译源码。
