# P2 图形深链、宽分支和全展开隔离测量（2026-11-03）

本记录仅为参考机观察值，不设性能阈值，不代表完整 P2 或人工视觉验收。未修改生产渲染或启用按需渲染。

## 环境与样本

- 测量代码基于 `fdd6e0769fb8c586ae7469eecf5a31bb3a924e37`；Windows 10 IoT 企业版 LTSC 10.0.19044，64 位；Intel Core i7-8700 3.20 GHz，6 核 12 线程，物理内存 17,128,722,432 字节；.NET SDK 10.0.403，测试运行时 .NET 10.0.14，Release x64。存储介质、显示缩放及后台负载未记录。
- 每种谱形独立 GUID 临时 SQLite 数据库和独立 STA WPF 子进程，串行运行；不访问正式用户数据库。深链 1,000 人、999 条生物父母关系；宽分支 1 人连接 999 名直属下系；平衡样本与既有图形基线一致，1,000 人、989 条生物父母、30 条养父母、4 条失考，多候选未自动选线。
- `nodes` 为 Canvas 直属人物 Grid 数，`lines` 为直属连线数，`buttons` 为节点 Grid 内直接 Button 数，不是整个窗口的全部可视元素。尺寸与视口为 WPF 设备无关单位。`privateBytes` 为子进程私有字节，`privateDeltaBytes` 以初始化主窗口及选择族谱后、打开图形前的同进程私有字节为基准；包含后续累计分配及 GC 波动，不能直接当作单次操作净内存或泄漏。
- 打开包含取得快照、创建及显示窗口、切换图形并更新布局；不含进程和主窗口初始化。深链前 10 步为真实逐级点击；宽分支和平衡谱为真实点击一次根节点。之后测试通过设置展开状态准备全展开，再重复三次调用现有渲染；产品没有展开全部入口。定位使用人物 ID：深链和宽分支为 999，平衡谱为主树内 500；搜索输入一次性完成，程序选择结果和点击定位。滚动每次先复位至起点，再滚动至末端。
- 各时间以毫秒计至 WPF `ApplicationIdle`，不保证物理帧呈现，不含人工反应或逐字输入。定位校验目标进入视口；全展开校验节点和连线数；测量前后校验人物、关系数和全库代数。每种谱形仅一个进程，3 次重复不是跨机统计。
- 命令：`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Measure_isolated_thousand_person_graph_shapes --logger "trx;LogFileName=graph-shapes-final.trx" --logger "console;verbosity=minimal" --results-directory TestResults`；3/3 通过。首次试测的平衡谱目标为断开人物，故调整为 500 并独立重测；以下仅列最终方法数据。

## 最终方法原始记录

每行 JSON 是一次实际记录，`run` 或 `step` 为执行顺序；`data-unchanged` 为前后核对通过时的数值。

### deep

```json
{"shape":"deep","stage":"open","value":{"ms":126.6771,"counts":{"nodes":1,"lines":0,"buttons":2,"canvasWidth":234,"canvasHeight":112,"viewportWidth":617.1999999999999,"viewportHeight":347.84,"privateBytes":111771648,"privateDeltaBytes":15982592}}}
{"shape":"deep","stage":"click-expand","value":{"step":1,"ms":4.4981,"counts":{"nodes":2,"lines":1,"buttons":4,"canvasWidth":234,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":347.84,"privateBytes":111443968,"privateDeltaBytes":15654912}}}
{"shape":"deep","stage":"click-expand","value":{"step":2,"ms":7.2217,"counts":{"nodes":3,"lines":2,"buttons":6,"canvasWidth":234,"canvasHeight":366,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110780416,"privateDeltaBytes":14991360}}}
{"shape":"deep","stage":"click-expand","value":{"step":3,"ms":6.2824,"counts":{"nodes":4,"lines":3,"buttons":8,"canvasWidth":234,"canvasHeight":493,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110784512,"privateDeltaBytes":14995456}}}
{"shape":"deep","stage":"click-expand","value":{"step":4,"ms":6.3362,"counts":{"nodes":5,"lines":4,"buttons":10,"canvasWidth":234,"canvasHeight":620,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110796800,"privateDeltaBytes":15007744}}}
{"shape":"deep","stage":"click-expand","value":{"step":5,"ms":7.9275,"counts":{"nodes":6,"lines":5,"buttons":12,"canvasWidth":234,"canvasHeight":747,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110796800,"privateDeltaBytes":15007744}}}
{"shape":"deep","stage":"click-expand","value":{"step":6,"ms":8.7106,"counts":{"nodes":7,"lines":6,"buttons":14,"canvasWidth":234,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110796800,"privateDeltaBytes":15007744}}}
{"shape":"deep","stage":"click-expand","value":{"step":7,"ms":14.4898,"counts":{"nodes":8,"lines":7,"buttons":16,"canvasWidth":234,"canvasHeight":1001,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110755840,"privateDeltaBytes":14966784}}}
{"shape":"deep","stage":"click-expand","value":{"step":8,"ms":10.499,"counts":{"nodes":9,"lines":8,"buttons":18,"canvasWidth":234,"canvasHeight":1128,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110882816,"privateDeltaBytes":15093760}}}
{"shape":"deep","stage":"click-expand","value":{"step":9,"ms":11.5673,"counts":{"nodes":10,"lines":9,"buttons":20,"canvasWidth":234,"canvasHeight":1255,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":110882816,"privateDeltaBytes":15093760}}}
{"shape":"deep","stage":"click-expand","value":{"step":10,"ms":12.4967,"counts":{"nodes":11,"lines":10,"buttons":22,"canvasWidth":234,"canvasHeight":1382,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":111140864,"privateDeltaBytes":15351808}}}
{"shape":"deep","stage":"full-render","value":{"run":1,"ms":873.2889,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":129736704,"privateDeltaBytes":33947648}}}
{"shape":"deep","stage":"full-render","value":{"run":2,"ms":834.2435,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":158064640,"privateDeltaBytes":62275584}}}
{"shape":"deep","stage":"full-render","value":{"run":3,"ms":846.7048,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":176214016,"privateDeltaBytes":80424960}}}
{"shape":"deep","stage":"locate-last","value":{"run":1,"ms":835.298,"x":203.09999999999997,"y":255.83999999999656,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":177438720,"privateDeltaBytes":81649664}}}
{"shape":"deep","stage":"locate-last","value":{"run":2,"ms":728.1608,"x":203.09999999999997,"y":255.83999999999656,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":190021632,"privateDeltaBytes":94232576}}}
{"shape":"deep","stage":"locate-last","value":{"run":3,"ms":735.2551,"x":203.09999999999997,"y":255.83999999999656,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":175767552,"privateDeltaBytes":79978496}}}
{"shape":"deep","stage":"scroll-end","value":{"run":1,"ms":1.2029,"horizontal":0,"vertical":126637.16,"scrollableWidth":0,"scrollableHeight":126637.16,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":179015680,"privateDeltaBytes":83226624}}}
{"shape":"deep","stage":"scroll-end","value":{"run":2,"ms":1.1195,"horizontal":0,"vertical":126637.16,"scrollableWidth":0,"scrollableHeight":126637.16,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":179441664,"privateDeltaBytes":83652608}}}
{"shape":"deep","stage":"scroll-end","value":{"run":3,"ms":1.2551,"horizontal":0,"vertical":126637.16,"scrollableWidth":0,"scrollableHeight":126637.16,"counts":{"nodes":1000,"lines":999,"buttons":1999,"canvasWidth":234,"canvasHeight":126985,"viewportWidth":600.1999999999999,"viewportHeight":347.84,"privateBytes":179445760,"privateDeltaBytes":83656704}}}
{"shape":"deep","stage":"data-unchanged","value":{"UnlinkGeneration":1999,"people":1000,"relationships":999}}
```

### wide

```json
{"shape":"wide","stage":"open","value":{"ms":154.7281,"counts":{"nodes":1,"lines":0,"buttons":2,"canvasWidth":234,"canvasHeight":112,"viewportWidth":617.1999999999999,"viewportHeight":347.84,"privateBytes":112668672,"privateDeltaBytes":15933440}}}
{"shape":"wide","stage":"click-expand-root","value":{"ms":532.8902,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":126533632,"privateDeltaBytes":29798400}}}
{"shape":"wide","stage":"full-render","value":{"run":1,"ms":552.3233,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":143175680,"privateDeltaBytes":46440448}}}
{"shape":"wide","stage":"full-render","value":{"run":2,"ms":452.1005,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":145268736,"privateDeltaBytes":48533504}}}
{"shape":"wide","stage":"full-render","value":{"run":3,"ms":458.8524,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":153579520,"privateDeltaBytes":56844288}}}
{"shape":"wide","stage":"locate-last","value":{"run":1,"ms":490.2402,"x":403.20000000001164,"y":192.92000000000002,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":149708800,"privateDeltaBytes":52973568}}}
{"shape":"wide","stage":"locate-last","value":{"run":2,"ms":480.6073,"x":403.20000000001164,"y":192.92000000000002,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":143708160,"privateDeltaBytes":46972928}}}
{"shape":"wide","stage":"locate-last","value":{"run":3,"ms":463.1792,"x":403.20000000001164,"y":192.92000000000002,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":148946944,"privateDeltaBytes":52211712}}}
{"shape":"wide","stage":"scroll-end","value":{"run":1,"ms":1.6647,"horizontal":193228.8,"vertical":0,"scrollableWidth":193228.8,"scrollableHeight":0,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":151314432,"privateDeltaBytes":54579200}}}
{"shape":"wide","stage":"scroll-end","value":{"run":2,"ms":1.5843,"horizontal":193228.8,"vertical":0,"scrollableWidth":193228.8,"scrollableHeight":0,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":151326720,"privateDeltaBytes":54591488}}}
{"shape":"wide","stage":"scroll-end","value":{"run":3,"ms":1.8354,"horizontal":193228.8,"vertical":0,"scrollableWidth":193228.8,"scrollableHeight":0,"counts":{"nodes":1000,"lines":999,"buttons":1001,"canvasWidth":193846,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":151326720,"privateDeltaBytes":54591488}}}
{"shape":"wide","stage":"data-unchanged","value":{"UnlinkGeneration":1999,"people":1000,"relationships":999}}
```

### balanced

```json
{"shape":"balanced","stage":"open","value":{"ms":223.6231,"counts":{"nodes":41,"lines":0,"buttons":55,"canvasWidth":7994,"canvasHeight":112,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":114192384,"privateDeltaBytes":18845696}}}
{"shape":"balanced","stage":"click-expand-root","value":{"ms":34.4317,"counts":{"nodes":44,"lines":3,"buttons":61,"canvasWidth":8382,"canvasHeight":239,"viewportWidth":617.1999999999999,"viewportHeight":330.84,"privateBytes":114229248,"privateDeltaBytes":18882560}}}
{"shape":"balanced","stage":"full-render","value":{"run":1,"ms":643.2071,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":139915264,"privateDeltaBytes":44568576}}}
{"shape":"balanced","stage":"full-render","value":{"run":2,"ms":634.5821,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":150761472,"privateDeltaBytes":55414784}}}
{"shape":"balanced","stage":"full-render","value":{"run":3,"ms":568.0338,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":159105024,"privateDeltaBytes":63758336}}}
{"shape":"balanced","stage":"locate-last","value":{"run":1,"ms":599.163,"x":0,"y":238.83999999999997,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":156655616,"privateDeltaBytes":61308928}}}
{"shape":"balanced","stage":"locate-last","value":{"run":2,"ms":575.7267,"x":0,"y":238.83999999999997,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":149622784,"privateDeltaBytes":54276096}}}
{"shape":"balanced","stage":"locate-last","value":{"run":3,"ms":565.3019,"x":0,"y":238.83999999999997,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":173907968,"privateDeltaBytes":78561280}}}
{"shape":"balanced","stage":"scroll-end","value":{"run":1,"ms":1.9969,"horizontal":129419.8,"vertical":543.1600000000001,"scrollableWidth":129419.8,"scrollableHeight":543.1600000000001,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":172453888,"privateDeltaBytes":77107200}}}
{"shape":"balanced","stage":"scroll-end","value":{"run":2,"ms":2.7927,"horizontal":129419.8,"vertical":543.1600000000001,"scrollableWidth":129419.8,"scrollableHeight":543.1600000000001,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":172466176,"privateDeltaBytes":77119488}}}
{"shape":"balanced","stage":"scroll-end","value":{"run":3,"ms":1.9116,"horizontal":129419.8,"vertical":543.1600000000001,"scrollableWidth":129419.8,"scrollableHeight":543.1600000000001,"counts":{"nodes":1000,"lines":959,"buttons":1330,"canvasWidth":130020,"canvasHeight":874,"viewportWidth":600.1999999999999,"viewportHeight":330.84,"privateBytes":173121536,"privateDeltaBytes":77774848}}}
{"shape":"balanced","stage":"data-unchanged","value":{"UnlinkGeneration":2023,"people":1000,"relationships":1023}}
```
