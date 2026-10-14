# 0.2.0：部分产品化与素材记录

日期：2026-10-13。按用户新增要求，当前建筑免费，优化自动乘车性能，加入平滑动画和香草风格占位素材。目标仍为本机 RimWorld 1.7.4977 / Waylith，不扩展为完整列车模拟。

## 玩家可见变化

- 轨道/车站材料成本为空，不消耗钢材；不需要电力、燃料、研究。保留 60 / 300 WorkToBuild。MarketValue 为 0，免费建筑不额外增加建筑价值。
- 轨道用本机 1.7 的 `BuildableDef.drawStyleCategory` 接入原生 `DrawStyle_Line`。拖拽总是水平或垂直直线，转角用两笔连接；没有采用已不存在的 `designationDraggableDimensions` 字段。
- 车站有重命名按钮，名称最多 28 个字符，写入存档；目的地菜单显示名称。未命名站点显示坐标。
- 设置新增平滑动画开关，默认开启。旅客与车辆共享插值位置，每段 3 ticks；车辆随方向旋转。暂停时插值冻结。逻辑位置只在完成安全检查后逐格推进。
- 三张原创占位 PNG：钢木/橄榄绿小车，灰色站台和木凳，钢轨和木枕图标。保持粗暗轮廓、克制配色、轻量着色。

## 性能实现

| 改动 | 具体作用 | 边界 |
|---|---|---|
| Pawn→行程字典 | 寻路 tick 和绘制直接查询行程 | 序列化保留原列表；读档重建索引 |
| 原地倒序遍历活跃行程 | 移除每 tick 的 `trips.ToArray()` | 完成/取消时才修改集合 |
| 24×24 格站点桶 | 只访问附近九个桶，取最近八站 | 两端仍限制 24 格 |
| 每图每 tick 两次完整规划 | 分散同时出发造成的峰值 | 这是次数上限，不是毫秒预算；大网络首次搜索仍可能昂贵 |
| 有界拓扑路径缓存 | 同一站点对复用 BFS 结果 | 最多 128 对/32768 格；超大单路径不保留 |
| 建造/拆除使缓存失效 | 断轨或修复不会使用过期拓扑 | 缓存不包含区域、危险、禁用或通行权限 |
| 每次规划再次验证安全性 | 缓存路径仍检查当前乘客；不可用时搜索绕路 | 动态障碍仍在行程中逐格检查 |
| 接驳 Dijkstra 提前结束 | 所有候选站点确定成本后停止扩张 | 未到达候选站时仍可能搜索完整有界范围 |
| CurToil getter 委托 | 避免每 tick 使用反射 GetValue | 仍只支持现有工作白名单 |
| MapMeshOnly 静态轨道 | 枕木、轮廓和钢轨在地图网格重建时生成；按材质批处理 | 建造/拆除会标记邻格/邻区网格重建 |
| 小车视野裁剪 | 只绘制当前地图视野附近的行程 | Pawn 绘制仍由原版负责 |

离线微基准在同一条 1000 格路径上重复查询 2000 次。结果存于 `Validation/topology-benchmark.json`，由测试程序直接调用生产代码产生。这是暖缓存重复查询与重复 BFS 的比较，不能推导为整局 FPS/TPS 提升。十二名实际引擎乘客的规划统计见 `engine-visual-result.json`。

## 素材来源与使用

使用内置 imagegen 工具直接生成三个透明背景 PNG，没有下载或截取官方美术，也没有用脚本重绘图像。生成后检查透明通道并原样复制到 `Mod/LocalRail/Textures/LocalRail`。原图保留于 Codex 的 generated_images 目录。

| 项目文件 | 原图尺寸 | 用途 |
|---|---|---|
| `Cart.png` | 1536×1024 | 动态小车，渲染尺寸 2.1×1.4 地图单位，乘客覆盖中央 |
| `Station.png` | 1254×1254 | 静态车站、建筑图标和施工预览，渲染尺寸 1.25×1.25 |
| `Track.png` | 1254×1254 | 建筑图标和施工预览；地图连接轨道使用程序生成的静态网格 |

当前保留生成工具输出分辨率，没有制作多方向 Sprite Sheet；车辆四向共用一张旋转贴图。正式美术阶段可在专用绘图工具中手工整理像素、轮廓、透明边缘和分辨率。占位图并非官方素材或官方认可资产。

### Cart 最终生成提示词

```text
Use case: stylized-concept. Asset type: one original 2D game sprite PNG for a RimWorld single-map light rail mod. Create one small single-passenger open rail trolley, facing east/right, viewed from directly overhead with the slight south-facing visibility characteristic of RimWorld vanilla furniture sprites. A compact rectangular steel and muted olive trolley body, four small dark wheels, understated wood seat framing, a completely empty central passenger area (the game draws its pawn over the center). Simple clean thick charcoal outlines, flat restrained grey/olive/brown colors, minimal two-tone shading, readable at 64 pixels, rough utilitarian colony equipment matching RimWorld's vanilla visual language. No rider, no tracks, no lettering, no icons, no scenery, no drop shadow outside the sprite. Square image; centered sprite fills about 80 percent of canvas width and 55 percent height. Truly transparent background and empty center transparent where appropriate. Do not make a sprite sheet; one east-facing vehicle only.
```

### Station 最终生成提示词

```text
Use case: stylized-concept. Asset type: one original 2D game sprite PNG, a compact ONE-TILE ground-level passenger rail station for a RimWorld mod. Orthographic directly overhead with only slight south-facing edge visibility, same visual language as vanilla RimWorld furniture: simple thick dark outlines, restrained flat grey steel, warm wood and dull olive colors, minimal two-tone shading, readable at 64px. Small rectangular utilitarian concrete boarding pad with two short wooden benches along the north and south edges, small olive ticket/control box in one corner and a modest blank signpost; leave the central horizontal east-west path visibly open for a rail trolley and pawn to pass. No roof, no enclosing walls, no train, no people, no written text, no ground scenery, no cast shadow outside the sprite, no realistic perspective. Square canvas, centered sprite covering about 85% of canvas. Truly transparent background. One single station sprite, no sprite sheet.
```

### Track 最终生成提示词

```text
Create ONE original 2D top-down game sprite icon for a RimWorld vanilla-style mod, a single horizontal east-west light railway track tile. Exactly two parallel muted steel rails with charcoal outlines, four simple warm dark wood sleepers underneath, flat two-tone shading, no gradients or realistic textures. Plain utilitarian colony furniture visual language, readable at 64 pixels. Orthographic directly overhead. Track ends left and right at the same positions. Canvas square; horizontal track occupies 90% width and 55% height, centered. Truly transparent background outside the track. No ground, people, vehicle, text, symbols, shadows, or other objects. One tile only, no sprite sheet. The image will be used for the track build menu icon and construction preview; final connected map rails are drawn by the game.
```

三次调用均使用 `transparent_background: true`，不提供参考图、模型或质量覆盖参数。实际检查和路径/哈希见 `Validation/assets.json`。详细提示词留在这里，便于之后重新生成同风格素材。

## 下一阶段

优先完成实际玩家殖民地/常用 Mod 验收，覆盖剩余原版工作、区域动态变化、站点销毁、转角/分支和多地图。之后开发简单运力、排队和会车规则，再考虑货运。当前性能证据不覆盖大型网络或第三方寻路修改。
