# 0.4.1 斜向轨道连接绘制修复

日期：2026-10-13。游戏：RimWorld 1.7.4977 rev1248 / Waylith。

问题来自地图实际使用的程序化轨道网格：旧绘制把每条连接分别从格子中心向外画，两条偏移后的钢轨在方向改变时没有共用接点。替换建筑菜单中的 Track.png 并不能修复这段绘制。

## 改动

- 两个连接方向使用同一轨距的偏移线交点，将两边的钢轨接到共同转角，覆盖东北、西北、东南、西南斜向与正交连接。
- 相邻格边界及转角端点保留少量重叠，避免栅格化造成细缝。
- 多分支轨道补上中心连接。枕木随各段方向排列，沿用现有钢灰、深色描边和木色。
- 继续使用静态地图网格，只有地图建筑层重建时生成几何；没有新增逐帧轨道绘制。

建筑、轨道邻接规则、自动乘车策略、列车速度、运营路线和货物存档格式保持兼容。原有轨道在加载后由新绘制代码重新生成外观，无需拆除重建。

## 验证

编译通过，0 警告、0 错误。专用 Waylith 图形场景创建四组 3×3 车站→斜轨→直轨，清除测试区域的屋顶和障碍，暂停模拟后截图。图像人工检查四个方向的转接和连续斜向格，双钢轨在接头连续，枕木朝向匹配。截图：`Validation/rail-junctions.png`；日志与完成结果：`engine-junctions.log`、`engine-junctions-result.json`，stdout 无 ERROR。

这是绘制验收，结果 JSON 的 tests=0 表示没有新增行为断言，不能把截图完成算成客货运营回归。本轮最初启动的完整图形回归在初次乘车阶段退出且未产出结果，不计为通过；原 0.4.0 的 64 条完整图形回归记录仍是历史证据。随后使用专用场景完成本次连接绘制检查。

复现：

```powershell
.\build.ps1
.\Validation\run-engine.ps1 -Junctions
.\Validation\audit-package.ps1
```

专用场景有独立配置 `.local/LocalRailTestJunctions`。截图区域重建只在显式测试开关与指定测试目录下执行，普通游戏不执行。

安装包：`dist/LocalRail-0.4.1-rw1.7-poc.zip`。安装目录：`D:\SteamLibrary\steamapps\common\RimWorld\Mods\LocalRail`。包与安装目录校验见 `Validation/package-manifest.json`。

使用时重启游戏重新加载 DLL，然后读取原存档即可看到新接头。
