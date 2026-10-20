# MajdataPlayAlpha

基于 [TeamMajdata/MajdataPlay 2.0.3](https://github.com/TeamMajdata/MajdataPlay/releases/tag/2.0.3)，支持 MajdataViewAlpha 0.6.0 / 0.6.1 谱面语法。程序版本为 **alpha.0.6.1**，沿用 MajPlay 的输入、判定与计分。

支持 SV / HS、BOUNCE、SPAWN / DESTROY、COLOR / COLORV、SIZE / SIZEV、ALPHA / ALPHAV、ANGLE / ANGLEV、FAKE、图片与轨迹继承、D 区、TouchSlide / P/Q / rp/rq、字幕、显示指令、噪域和滤镜。支持数值与 RGB 渐变；TEXT 支持颜色和 Fonts 内的字体路径。读取 `media_timeline.json` 的音视频剪辑。

噪域的预警、粒子、折射、合并和缩回与 ViewAlpha 对齐。SV / HS 默认包含 blockarea，COLOR / SIZE / ALPHA 默认不包含；零时长仅显示预警。继承载体和 FAKE 不进入判定物量与分数。

音符槽以英文逗号结束。未知或不支持的语法发出警告，跳过对应内容。Play 保留自带判定线，支持开关、染色和拓展；不切换 View 的判定线皮肤，不处理外圈亮度。中央显示对应 Play 已有模式。

默认 Deluxe，附带皮肤包中的四套皮肤；缺少的 Mine 素材使用 MajPlay 原生素材。Windows 使用 Unity 构建；移动端尚未完成本次实机验证。

## Contribute

- [TeamMajdata/MajdataPlay](https://github.com/TeamMajdata/MajdataPlay)：原生播放器、Mine / SlideCode、输入、判定与计分。
- [MajdataViewAlpha](https://github.com/Jian04/MajdataViewAlpha)：Alpha 语法、轨迹与显示算法。
- [Phira-Pro](https://github.com/Phira-Pro/Phira-Pro)：噪域合成算法和参考素材。

遵循 GPL-3.0，保留上游授权与素材来源。
