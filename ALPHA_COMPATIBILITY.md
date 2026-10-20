# Alpha 语法迁移对照 — alpha.0.6.0

参照本工作区 `alpha/Assets/MajdataCore/AlphaCommandGrammar.cs`、音符表达式解析器及 View 音符显示代码逐项核对。解析语法沿用 ViewAlpha；显示时间与真实判定时钟分开。未知指令、参数或不支持的单个音符记录带位置的警告，跳过该项，保留同槽其他音符和后续时间。

图片继承的 Tap（含 D 区）保持图片原方向，Star 换图保留星星旋转；图片按 ViewAlpha DX 的头部基准尺寸绘制，不使用 Play 皮肤的旋转留白尺寸。普通换图与轨迹继承使用同一尺寸基准，SIZE / SIZEV 再在此基础上缩放。未换图的 Play 原生皮肤不受此适配影响。

## 指令

| 指令 | PlayAlpha 行为 |
| --- | --- |
| SV | 全局与分类滚动积分、零速、倒流；全局包含 blockarea、默认不改变滑条轨迹，`slide=` 单独改变滑条显示进度。 |
| HS | 全局/分类下落倍率，包含 blockarea，分类 NULL 恢复默认；滑条只读取 `slide=`，不读取全局 HS。 |
| SPAWN | 自定义出生半径，支持正负数和分类。 |
| SPAWNMODE | Rewind/Once 入场与回撤缩放。 |
| BOUNCE | 使用 View 的弹跳时间、方向、短/长 Hold 身体长度计算。 |
| DESTROY | 自定义视觉到达半径；不更改判定时间和真实感应区。 |
| FAKE | 假头与假轨迹可分别设置；无输入、物量、密度、分数和击打音效，独立回收。假 Mine 头在目标拍消失；普通 Fake Tap 沿用 View 的 0.15 秒回收窗口。独立流设置在 Slide 拆段及开启/关闭音符折叠时保留。 |
| COLOR | 按音符所在时间取颜色，分类/each/break/mine；全局与普通分类不染 Mine，`mine=` 单独染色、`mine=NULL` 恢复原生灰色。 |
| COLORV | 实时染色，包含已出现音符。全局 COLOR / COLORV 不包含 blockarea，`blockarea=` 可单独染色。 |
| SIZE | 按音符所在时间取横纵缩放。Slide 每根箭头围绕自身中心缩放，移动星由 `slidestar=` 控制；全局不缩放 Slide 轨迹和 blockarea；`blockarea=` 单独控制噪域。Hold 尾部、Touch 扇叶和 TouchHold 遮罩随整体缩放，TouchHold 中心点保持原大小。噪域预警和持续区域围绕原判定区中心缩放，不改变判定。 |
| SIZEV | 实时横纵缩放；覆盖已有 SIZE，NULL 恢复音符原有设置。 |
| ALPHA | 按音符所在时间取透明度；全局 ALPHA / ALPHAV 不包含 blockarea，`blockarea=` 单独控制。 |
| ALPHAV | 实时透明度。 |
| JLINE | 原生判定线 RGB/RGBA 染色、过渡和 NULL 还原。 |
| TEXT | 多条字幕、位置、大小、已安装字体、Fade/Typewriter；打字按 Unicode 字符显示。 |
| JUDGELINE | 原生线开关：0/False 关闭，1/True 开启；其余 View 样式按用户要求跳过。 |
| JUDGELINEEXPAND | `True[,时间,方向]` 展开附加白线，`False` 移除附加白线；旧版 `(时间,方向)` 保留。 |
| SHOWJUDGEINFO | 判定统计信息显示与过渡。 |
| SHOWCOMBOINFO | 中央统计显示与过渡。 |
| SHOWJUDGETEXT | 音符判定文字显示与过渡，不隐藏判定本身。 |
| OUTERBRIGHTNESS | 警告跳过：Play 没有对应的外圈背景。 |
| INNERBRIGHTNESS | 内圈遮暗与过渡；0 不遮暗、1 全暗，沿用 View 参数含义。 |
| COMBODISPLAY | 按含义对应原生统计，具体映射见下表。无对应算法的类型警告跳过。 |
| Default | INNERBRIGHTNESS、COMBODISPLAY、SHOWJUDGEINFO、SHOWCOMBOINFO、SHOWJUDGETEXT、JUDGELINE 的首参数支持 `Default`，恢复 Play 配置；可附加过渡时间。OUTERBRIGHTNESS 仍按原规则警告跳过。 |
| AUDIO | 加载歌曲文件夹内的 mp3/ogg/wav；沿用 Play 音频后端，跟随时间、速度和暂停；False 停止。 |
| PVOVERLAY | 覆盖谱面区域的 png/jpg/jpeg/mp4、过渡；False 或视频结束恢复原背景，视频静音。 |
| GAUSSIAN | 谱面区域高斯模糊。 |
| NEON | 谱面区域霓虹。 |
| TRAIL | 谱面区域拖尾；换谱/跳转清理历史。 |
| FLASH | 白色闪光。 |
| BRIGHTNESS | 谱面区域亮度滤镜。 |
| SATURATION | 饱和度。 |
| CONTRAST | 对比度。 |
| RAINBOW | 彩虹。 |
| VIGNETTE | 圆形黑角；末尾 True 开启向圆周外侧渐黑，False 保留硬边；省略默认为 True。 |
| GLITCH | 故障效果。 |
| TVNOISE | 电视噪点。 |
| ZOOM | 谱面区域缩放。 |
| HUE | 色相。 |
| ROTATE | 谱面区域旋转；末尾可附加 True/False 控制是否同时旋转 PV 和 PVOVERLAY，省略默认为 False。 |
| FADE | FLASH 的黑色版本。 |
| TINT | 谱面区域染色。 |
| MOVE | 谱面区域平移。 |
| SHAKE | 谱面区域抖动。`<SHAKE*(Instant,1,10,90,0.2)>` 为强度 1、10Hz、垂直震动 0.2 秒；角度 0 为水平、90 为垂直，省略角度则双向震动。 |
| SHATTER | 厚玻璃中心冲击、随机大小碎片、厚边及层叠折射/反射。 |
| RADIALBLUR | 径向模糊。 |
| RIPPLE | 冲击波折射。 |
| PIXELATE | 像素化。 |
| LENSDISTORT | 正负镜头畸变。 |
| SPLIT | 分屏错位。 |
| KALEIDOSCOPE | 六扇区镜像，第 2 参数为角度，过渡旋转；0° 仍开启，False 关闭。 |
| BLOOM | 高亮泛光。 |
| INVERT | 反色。 |
| POSTERIZE | 色阶压缩。 |

滤镜支持 View 的持续、开/关、Instant 和旧包络形式。只处理中央谱面区域，顶部成绩与真实判定坐标保持独立。显示/媒体指令按 View 的括号形式写，例如 `<SHOWJUDGETEXT*(False)>`、`<INNERBRIGHTNESS*(0.5)>`、`<COMBODISPLAY*(DXACC)>`。

## 噪域与渲染同步

`XD1[8:1]` / `XC[#0.12]`：按 Touch 的速度曲线、HS 和 SV 积分计算预警；预警不禁用输入，持续阶段禁用原感应区，结束时恢复。`XC` 和 `XC[1:0]` 只有预警，没有持续区域或输入禁用；`XD1[384:1]` 只按实际时长显示。相邻区域共享外框，支持错时扩张/回缩及孤立区域向中心回缩，短事件保留可见 Glitch。结束判断使用最后噪域的持续终点，不将预警算进结束时间。

DisabledBlock 预警在音符下方；ActiveBlock 使用 Phira-Pro 的 HSV 折射、预乘色、原始粒子 UV 和分辨率适配。噪域先合成、再经过画面滤镜，因此滤镜也作用于噪域。独立 `blockarea=` 的 COLOR / ALPHA / SIZE 同时作用于预警与持续区域，SIZE 不改变禁用感应区。合成仅限中央谱面区域。

## 中央显示对应

| Alpha 模式 | PlayAlpha 对应 |
| --- | --- |
| None | 不显示。 |
| Combo | 原生连击数。 |
| Score | 原生已获得的 Classic 分数。 |
| ACC | 原生 Classic 达成率，上升显示。 |
| ACCDOWN | 原生 Classic 达成率，扣减显示。 |
| DXACC | 原生 DX 达成率，上升显示。 |
| DXACCDOWN | 原生 DX 达成率，扣减显示。 |
| DXScore | 根据原生已判定物量与丢失 DX 分，显示已获得 DX 分。 |
| CSCORE 系列 | 没有等价的原生归一化分数，警告跳过。 |

别名和数字模式由 View 的同一语法表解析，不要求两个项目的枚举命名相同。切换这些显示不会重算或改变成绩。

## 音符及结构扩展

| 写法 | 行为 |
| --- | --- |
| `||`、`|* ... *|` | 单行/块注释；字幕引号里的字符不作为注释。 |
| `@3/4`、编辑区颜色、练习片段标记 | 识别并跳过编辑器元信息，不生成音符。 |
| `@{分拍}...`、`@* ... *@` | 独立叠加流，不推进主谱时间；独立 BPM/HS/Alpha 状态，按实际时间合并。 |
| 反引号伪双压 | MajPlay 原生分离时点与音符语义，Alpha 不另行改写。 |
| `B1h[8:1]` 等非 C TouchHold | MajPlay 已原生支持；直接交给原生解析、区域与 TouchHold 判定链路。 |
| `A1~[3]` | Touch 自定义绘制半径，判定仍在 A1；支持范围沿用 View。 |
| Break Touch/TouchHold | 原生 Break 判定和分数。 |
| `?`、`!` 无头 | MajPlay 已原生支持；沿用原生无头标记与星星显示，不附加 View 的特殊显示规则。 |
| `f` | Tap/Hold/Touch/TouchHold/星星头烟花；Hold 使用 View 的纯视觉烟花，不额外添加烟花音效。 |
| `$`、`$$` | 原生星形 Tap、不旋转/旋转模式。 |
| Mine | 原生皮肤与判定；不主动模拟击打，碰到判定区由原生 Mine 链路处理。 |
| `1d`、`1dh[8:1]`、D 区星星头 | 显示在 D1；内屏 D1，外键 1/8 任一键。其余位置同理。D 队列和反馈独立，不改普通 A 队列。 |
| D 区轨迹、`1dw5d[4:1]` | 按 View 的实际感应区中心生成区段，纯 D→D 排除 A/B：`1d-5d` 为 D1→E1→C→E5→D5；混合 A/D 保留两套区域，含不同区域的折点。D 扇形使用三条射线、三条判定队列。D 感应区允许对应相邻外键。 |
| `rp/rq`、`P/Q` | 沿用 View 几何和语法，以真实感应区形成原生 Slide 队列。 |
| TouchSlide | 沿用 View 路径与 AstroDX 区段采样；例如 `A1>A4`：A1→D2→A2→D3→A3→D4→A4。 |
| 多圈、V 折线、混合键位/Touch | 与 View 同一表达式/几何规则；不兼容的组合逐个警告跳过。 |
| SlideCode | 使用 MajPlay 原生解析、轨迹、判定表和结束反馈。 |
| `~[图片.png/jpg/jpeg]` | 仅换对应图片，保留原音符判定/物量/分数；歌曲目录内可使用子文件夹。 |
| `1~[5-7[8:1]]` 等轨迹继承 | 纯移动显示：不计物量/密度/分数、不占手、不触发音效，没有可判定头或轨迹。 |

普通 Slide、Tap/Hold 及 Mine 的既有判定方法保持原版；新增区域通过输入适配和原生 SlideArea 队列接入。支持提前触碰/蹭掉区域的 DJAuto，视觉 SV 不改变判定时钟。

## 验证范围

本次 0.6.0 同步覆盖全部命令目录，新增全局/分类 blockarea 的 SV、HS、颜色、透明度、尺寸与万花筒角度过渡回归。Unity 检查实际 URP 合成、预警、滤镜、跳转与输入恢复，并在三组分辨率/长宽比下独立核对 Phira-Pro 的 HSV 混色和粒子采样。构建记录随 Windows 发行包保存；Android 仍需独立构建与实机验证，见 [移动端发布](MOBILE_RELEASE.md)。
