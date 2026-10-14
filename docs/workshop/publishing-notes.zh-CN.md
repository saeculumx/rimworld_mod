# Local Rail 0.10.0 · Pre-Alpha 创意工坊发布文案

## 标题与简短介绍

建议统一标题：**Local Rail — 自动客运与货运 / Automatic Passenger & Freight [Pre-Alpha]**

中文标题：**Local Rail — 地图内自动客运与货运 [Pre-Alpha]**

英文标题：**Local Rail — Automatic Passenger & Freight [Pre-Alpha]**

中文简短介绍：

为单张殖民地地图加入实体轨道、常驻列车与自动客货运。殖民者在合适的长途工作中自行乘车，自动列车可在无人乘坐时运送仓库库存。支持多站台、往返／环线、路线颜色与统一管理。RimWorld 1.7，需 Harmony，无 DLC 依赖。当前为 Pre-Alpha。

English short description:

Single-map rail transport with permanent trains, automatic commuting, and unmanned warehouse freight. Includes multiple platforms, shuttles and loops, colored routes, and a Rail management tab. For RimWorld 1.7; requires Harmony, no DLC. Currently Pre-Alpha.

## 直接粘贴的正文

- `description.zh-CN.bbcode.txt`：完整中文版。
- `description.en.bbcode.txt`：完整英文版。
- `description.bilingual.bbcode.txt`：中文在前、英文在后的精简合并版；适合放在同一个介绍页面。

正文中的 `[h1]`、`[h2]`、`[b]`、`[list]`、`[olist]` 和 `[url=…]` 是 Steam 排版标记。粘贴正文原文，保留标记，不要再加 Markdown 代码围栏。格式依据：[Steam 官方文字格式说明](https://steamcommunity.com/comment/ForumTopic/formattinghelp)。

Steam 上传 API 的介绍上限为 8,000 字节，标题上限为 128 字节加终止符，依据：[Steamworks 官方常量](https://partner.steamgames.com/doc/api/ISteamRemoteStorage#Constants)。三份正文均按 UTF-8 字节数核对，合并版使用精简文案，避免把两个完整单语版直接拼接后超限。建议标题也已检查字节长度。

## 上传资料

- 版本标记：**0.10.0 · Pre-Alpha**；测试游戏版本：**1.7.4977 rev1248**。
- 上传 Mod 文件夹：`D:\rimworld_mod\Mod\LocalRail`。这个文件夹含 DLL、Defs、语言和素材；不要上传项目根目录、`.tools`、`.local`、官方程序集或测试存档。
- 备用发行包：`D:\rimworld_mod\dist\LocalRail-0.10.0-rw1.7-poc.zip`。
- 依赖项：[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)，应在 Local Rail 前加载。上传时将 Harmony 添加到创意工坊所需物品。
- 无 DLC 依赖。中英文界面。版本选择为 1.7；若可选内容标签包含建筑、运输或物流，可按实际选项选择。
- 作者、联系信息和项目外链使用你自己的资料。没有在文案中编造作者身份、GitHub 地址或讨论区链接。

## 展示图

已生成一张香草风格宣传封面，包含 Local Rail 标题、中英文客货运副标题、Pre-Alpha 与 RimWorld 1.7 标记，以及小列车、乘客、货物、站台和仓库场景：

- 推荐上传：`D:\rimworld_mod\docs\artwork\local-rail-workshop-cover.jpg`，1254×1254，437,313 字节（约 427 KiB）。
- PNG 原图：`D:\rimworld_mod\docs\artwork\local-rail-workshop-cover.png`，1254×1254，2,820,866 字节。
- [生成提示词与制作记录](../artwork/cover-generation.md)。图片由内置 image_gen 生成；JPEG 只做同尺寸格式导出。

封面是 AI 生成的宣传插画，建筑与人物比例是插画处理，发布时继续保留正文中的 AI 内容披露。可配合下面的实机截图展示当前游戏实现。

建议先用能同时展示车站、轨道和车载货物的画面，再补充路线与界面。当前可选截图：

1. `D:\rimworld_mod\Validation\rail-empty-automatic-cargo.png`：自动列车零乘客，运送钢铁与布料，检视窗显示实际数量。
2. `D:\rimworld_mod\Validation\rail-train-types.png`：路线编辑、颜色、列车类型与地图箭头。
3. `D:\rimworld_mod\Validation\rail-rotated-stations.png`：六种旋转后的客站／仓库及建造预览，来自 0.9 验证，图中建筑功能在 0.10 保留。
4. `D:\rimworld_mod\Validation\rail-travel-diagnostic.png`：自动乘车诊断、接驳与时间估算。

这些是现有实机截图，部分带开发模式图标和测试地图背景，可作为初次发布资料。若重新拍展示图，建议关闭开发模式图标和无关教学提示，用正常殖民地场景展示。

## 首次发布更新说明

中文：

**0.10.0 · 首个 Pre-Alpha 发布**

地图内自动客运与无人货运的早期可玩版本。包含普通／自动列车、多站台客站与货运仓库、2–16 站往返和环线、路线名称与颜色、统一管理、自动停站、列车门及乘车诊断。自动列车支持仓库默认目的站和分类发货规则。当前轨道、车站和仓库免费建造，素材与平衡仍在开发；请查看介绍中的路线占用与兼容性限制。

English:

**0.10.0 · Initial Pre-Alpha release**

An early playable release of single-map commuting and unmanned freight. Includes ordinary and automatic trains, multiple-platform stations and warehouses, 2–16 stop shuttles and loops, named and colored routes, central management, automatic dwell, rail doors, and travel diagnostics. Automatic trains support default warehouse destinations and item-based dispatch rules. Tracks, stations, and warehouses currently have no construction resource cost. Artwork and balance remain in development; see the description for track-sharing and compatibility limitations.

## 文案范围

三份正文均包含独立的 AI 内容披露与存档提示。披露涵盖部分代码／说明的 AI 辅助编写及部分自制占位贴图的 AI 生成，同时注明引用的原版人物和物品图像来自 RimWorld。没有宣称所有内容都是 AI 生成或所有素材均为原创。

存档说明区分已验证的路线／在途货物读写和 0.9→0.10 升级，与未全面验证的任意旧档中途加入、整合包、版本回退和移除。提示首次加入、更新或移除前独立手动备份、避开自动存档覆盖、先用副本验证并保留版本及模组列表；不承诺清理建筑后就一定可以安全卸载。此提示也适合保留在后续更新说明中。

介绍以当前 0.10.0 代码与验收记录为依据，没有承诺所有工作都乘车、共享轨道、跨线换乘、无限运力或全部模组兼容。两种列车均自行运营，只有自动类型收集仓库库存；12 格货仓指十二个实际物品堆栈槽，而非十二件物品。

本轮交付为发布文案。上传和公开发布由你稍后进行；本轮没有变更 Mod 的 packageId、程序集或版本，也没有创建 PublishedFileId。
