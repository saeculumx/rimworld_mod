# Local Rail Workshop cover / 封面制作记录

- Date / 日期：2026-10-14。
- Generation / 生成：Codex built-in image generation tool（内置 image_gen）；未使用 CLI/API fallback。
- Purpose / 用途：Steam 创意工坊宣传图及 GitHub README 插图。
- Disclosure / 披露：AI-generated promotional concept artwork；香草风格宣传插画，不是实机截图。README 与创意工坊正文包含 AI 披露，封面实际可见文字为标题、副标题和版本阶段标记。
- Assets / 文件：`local-rail-workshop-cover.png` 为生成原图；`local-rail-workshop-cover.jpg` 为同尺寸 JPEG 导出，质量 88，不裁剪、不修改画面内容。
- Actual output / 实际输出：1254×1254 像素；PNG 2,820,866 字节，JPEG 437,313 字节。提示词请求 2048×2048，但实际文件为上述尺寸，未进行放大。
- Scope / 范围：主标题 Local Rail，中英文客货运副标题，Pre-Alpha 与 RimWorld 1.7 标记；小型橄榄绿列车、乘客、资源、站台和仓库场景。
- Inspection / 检查：检查标题与中文文字、配色、人物／车体、轨道连续性、平方构图和文件大小。宣传场景的建筑与人物比例为插画处理；当前游戏形态以 Validation 中的实机截图为准。

## Final generation prompt / 最终生成提示词

```text
Use case: ads-marketing.
Asset type: square Steam Workshop cover / introduction artwork for the existing RimWorld mod Local Rail.
Create one finished 2048x2048 square illustration, opaque background. RimWorld vanilla-inspired 2D colony-game visual language: flat hand-painted sprites, bold dark outlines, simple readable forms, top-down three-quarter colony view, muted olive, warm soil brown, cool gray steel, natural grass. Do not use an official RimWorld logo.
Main scene: a modest frontier colony rail stop with continuous twin steel rails and wooden sleepers, one small compact olive-green electric-looking autonomous rail shuttle with a warm wooden open deck and metal guard rails. It is a small single vehicle, not a long locomotive train. One train is foreground focal subject. Show two simplified RimWorld-style colonists seated at the front part and clearly recognizable steel ingots, folded cloth stacks and a crate on its rear deck, showing passenger and freight uses. Platform has plain gray slabs, a small utilitarian shelter, and a nearby compact warehouse stocked with resources. Two simple colonist pawns waiting or hauling near the platform. Grass, a few rocks and trees around the settlement. Rails are physically continuous, smoothly connect diagonals, and go across the scene; keep a clear uncluttered silhouette at thumbnail scale.
Composition: editorial illustrated cover, not a screenshot. Large title area in upper quarter on a subtly weathered dark gray panel integrated into the composition. Main railway scene fills the lower three quarters. Keep all essential text and subjects inside safe margins.
Render these exact texts, with excellent spelling, no extra copy:
"LOCAL RAIL" as the biggest bold cream-colored title.
"自动客运 · 无人货运" as a clearly readable Chinese subtitle below.
"PASSENGERS & FREIGHT" as a smaller English subtitle.
"PRE-ALPHA" and "RIMWORLD 1.7" as two small neat corner badges.
"AI-GENERATED COVER ART" as a discreet small footer.
Make the art approachable and genuinely close to vanilla colony sprites, lightly textured, restrained depth/shadows, crisp antialiased edges; no dramatic photorealism, anime, neon, steampunk locomotive, busy menus, weapon focus, DLC symbols, invented performance claims, or fake in-game UI. This is promotional concept artwork rather than proof of a specific in-game screenshot.
```
