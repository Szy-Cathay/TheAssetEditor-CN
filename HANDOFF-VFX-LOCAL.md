# VFX 本地项目交接

> **当前接续入口为下方 R115；用户已反馈“目前基本可用”（2026-10-28）**。已补显式全／半分辨率普通面片的共享七矩透明合成、原生外观表及提交性能修复，正常 Debug 已更新。该反馈只代表用户本次实际测试范围，不扩大为所有特效原生等价；复杂渐变／折射混合通道、局部边缘及窗口长时性能仍有边界。本次按用户新授权更新文档、提交、推送并经检查合并，随后等待下一步指示；覆盖历史“不提交、不推送”限制，不恢复桌面测试，不继续扩展实现，不清理。源码、正常 Debug、原件、备份、失败及现有证据全部保留。

## 本次交付确认（2026-10-28）

- 用户确认“目前基本可用”，授权文档更新后提交、推送、合并；未要求发布、修改版本号或清理。沿用当前 `codex/vfx-preview-native-parity-r97`，交付前 140 个修改／未跟踪路径与 R115 最后快照逐文件散列一致，暂存区为空；全部为累计 VFX、共享 Effect 播放、对应测试与文档。
- 本次文档更正超级视图的全／半分辨率七矩合成准入说明，以及简单速度朝向光照的范围表述。R115 的 Debug 构建、883 项 CPU、21 项渲染协议及 7 项编辑器／中文检查为上一轮实际结果，本次复用，不能记为重新执行；新的提交／远端 CI／合并结果以本次实际交付记录为准。
- 下方各轮中的分支、HEAD、操作限制与待验描述均为当时时点的历史记录；本节及当前用户指令优先。Nurgle 混合路线、完整原生对照及未覆盖组合继续保留为待办，不因本次“基本可用”反馈宣称全部修复。

## 后续 VFX 实机验收授权（2026-10-26）

- **2026-10-28最新覆盖**：用户明确“你就离屏测试改吧，修复完了我人工核验”。当前仅源码、构建与离屏，不恢复下方历史桌面许可；后续实窗和人工观感由用户核验。本轮Debug与Terry均已正常退出，未丢弃未保存内容。
- 用户明确指示“你以后可以自己实机验收”，覆盖后续 VFX 任务所需的正常 Debug、Terry 及必要采样窗口操作；不再因旧阶段写着“用户自行测试”而把可自行执行的验收留给用户。已有测试许可持续有效，不重复索要同一许可。
- 开测范围、目标窗口与用户键鼠安排仍按根 `AGENTS.md` 核对；检测到真实输入或焦点冲突时立即停止本任务桌面输入并报告，不循环抢占。保留未保存内容，不强制结束用户程序。
- 继续仅用官方原版及其原始依赖，核实实际 Pack 来源；Terry 关闭并核实泛光，对齐画质、相机、挂点和播放阶段。自动、离屏、实机及游戏原生对照分别记录。
- 本次只登记授权，没有执行新的窗口测试，也没有新增视觉验收结论；下方历史记录中的用户分工按当时时点保留。

## R115 Trail 共用透明合成、外观表与性能（2026-10-28）

### 现场、交付与本轮边界

- 实际仓库仍为 `D:\Project\Szy-Cathay\TheAssetEditor-CN`，沿用 `codex/vfx-preview-native-parity-r97`，HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存区为空。接手的 128 个修改／未跟踪路径与 R114 最后快照逐文件散列相同；本轮没有切分支、worktree、暂存、收起、提交、推送或清理。所有历史源码、Mod 记录、正常 Debug、唯一备份、采样、原始 RDC 与失败保留。证据根为 `artifacts/vfx-native-halfres-trails-r115/`，接手快照在 `parent-validation/workspace-before.json`。
- 用户本轮明确只做离屏修改、修完自行人工核验，覆盖所有历史桌面授权；本轮没有显示 Debug／Terry 窗口或发送键鼠输入。已有 RDC 只是受控原生记录；本轮重新执行的是离线回读、同输入产品渲染及完整官方资源对照，不记作新 Terry／游戏实机验收。
- 当前正常入口 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe` 已构建。正常输出与测试实际 Core 为 **484C5689750CBC6FC298F79CDDA5E5807EE894F2760232E19941BDA59CD9A1FE**，native 为 **2607DCED27ACB5024DB5577E958871407FAC88B575169691C7549368338FD63A**。中文资源为 **9C8481338217D1F476FBCD3AF71F7C8F8DA28E1F525515F33CE8EFAE0DD17754**。源码、正常 Debug 与独有验证记录全部保留，最终保护盘点见 `parent-validation/workspace-after.json`。

### 原生依据与共享实现

- 原版吹管 q0 的两条启用层实际为 `FULLRES_DEFERRED_ALPHA`，q1／q2 才是 `HALFRES_DEFERRED_ALPHA`；没有把 FULL 降成 HALF，也没有因库中另有未启用模板而删层。新管线保留原始浮点 RGBA 及完整旧覆盖／发光配置，另以 24 字节配置登记原始粒子；旧 ABI 仍为 Layer132／Particle148／Vertex72／Lighting176。整组普通颜色全部准入时，全部实例先累计七矩，半尺寸全部七矩以整数 2 倍坐标加进完整矩池，再以坐标倍率 1／2 分别求全／半尺寸颜色，最后统一重建与显示；未准入混合整组使用原 C/E 层、原倍率及原顺序，模型、贴花、渐变、折射不因删守卫进入新路线。
- 原 134793 回读重新闭合：完整分配 1707×1067、完整视口 360×655；half 分配 853×533、视口 180×327。实际半尺寸硬件 D24S8 与四分之一尺寸 soft SRV 是两份不同资源。两级生产 PS 对 2×2 取 MAX4、尺寸 floor；本轮原 full→half **58,860**、half→quarter **14,670** 深度值逐低24bit相同，原 packed 上传读回也相同。实际 1413 三 MRT 对全部七矩 ONE/ONE 累加，1462 从完整合并矩池以 scaler2 读取；原生 nofilter 最终颜色使用线性上采样，不能改说深度引导过滤。完整证据入口 `native-depth-oracle-v1/native31-oracle-v1.json`。
- 正常窗口真实使用 4 倍抗锯齿，因此完整尺寸矩／颜色保留 4 倍与原硬件深度，逐采样求值；半尺寸读取采样平均的完整矩场，原完整深度的 MSAA 最小值解析独立保留。原生记录只证单采样，4 倍是 AECN 兼容，不冒充游戏 MSAA 实证。新材质保留全部原 mip，使用实证的硬件三线性／Clamp；旧材质已经有手动 mip 混合，不能把 Point 采样器单独当作旧灰边成因。Alpha 采用 AECN 的 MAX(有效覆盖率1−T,场景Alpha)，原参考目标写零的 Alpha 约定不直接照搬。
- 原先普通解析尺寸／透明度仍直接求值。新可选 `ParticleScale`／初始 Alpha／生命 Alpha 表仅限显式全／半通道的普通 File0、已证解析模式及有效上下表相等；生命 Alpha 非零偏移等不准入。两个原层 12 行完整128点RGBA16F的 SHA 全匹配；Scale／lifeAlpha 用 float `age/(life+1e-6)`，初始 Alpha 保持独立出生阶段及原 RNG。模型128float、旧直接曲线、已支持 quadratic、物理时钟、寿命、过期、段距和全局1/120不变。31项原数据重新生产，裁剪决定／UV保持，角点最大误差 **.23117→.00945像素**，Alpha 最大相对误差约 **3.61%→.0416%**；GPU 末位过滤仍有 half 残差，未强加偏置或焊接几何。
- 同一31原输入、实际完整 DSV、同相机与显式实测环境光，经产品 Frame 和新渲染器绘制黑白背景；原根与原 DDS 均鲜核官方 Pack。对原生局部32²的透射 MAE **.006746→.000190**、线性辐射 MAE **.009861→.000891**。这是固定原捕获输入的 DIAG，未把 PostVS 颜色拷进产品，也不代表完整运动；局部最大辐射差仍约 .146，不能用均值盖过边界误差。最终2607与6F同图块逐字节一致，见 `production-native31-v1/final-lookup-pixel-identity-v1.json`。

### 性能、完整原件与真实验证结果

- 独立编辑器仅把可选的复杂取景范围估算移到单独 CPU 任务；不读取实时挂点、不操作GPU、不冻结正常模拟，完成后只在用户未导航／改投影时补自动取景。范围独立工作与旧负开始完整模拟预热分别准入，共享同一个工作许可；取消／重载／异常不发布旧范围，早晚共享副本读同一不可变范围。早期 Bounds-only 原件对照保持范围与8帧数据逐字节相同，Comet 初次范围调用从约385ms移到约3.8ms的调度；这是移走调用阻塞，不是整次冷启动或总工作量减少。
- Native 按完整材质、纹理、光照、分辨率和深度键稳定分组，复用提交／回退向量容量。6F 曾新增空配置逐粒子 checked-iterator 搜索，烟雾提交约多 .77ms；已在2607改为配置时建立原子提交索引、空配置直返与O(1)查找。实际源码抽取CPU **7,603**检查通过；同Core下120相位粒子包、RGBA和绘制次数全部相同，6对PNG相同。烟雾 PreviewDraw 同轮中位约 **2.540→1.790ms**，Comet **2.636→2.281ms**；旧退步和全部候选保留，不用早期时钟漂移伪造全帧收益。
- 完整官方吹管 q0／q1 的弧线、急转、停止／重启，在暗亮背景共36份原PNG及CPU／真实shader包，本轮重新执行。q0分节明显减轻、q1细小半尺寸阶梯仍需用户看实际运动；停止阶段按原寿命归零。不能把捕获中近直线31项或一张缩图当作弯折全部正确。见 `official-moving-trails-v1/`。
- 另以同一最终工具 actual4×、800×600、24°／26°／FOV45及受控挂点，复测完整 Crowman、Bear、Pox、Nurgle、siege 原件共11对阶段图；每根8／9／9／8／5依赖的来源与原字节鲜核一致、0missing、模拟数量全同。Crowman 三阶段 RGBA 完全相同，1.6秒停拖后2.8秒零粒／零投影；siege两阶段 RGBA 相同，已修中央层仍在。Bear冰蓝烟与雪点、Pox纹理转向保留；均已实际看新原PNG，不仅判有像素。Pox／Nurgle部分投影数随正确外观Alpha门槛改变，不伪称所有像素不变。证据 `official-trail-breadth-v1/validation-summary-v1.json`。
- 最终2607的12组正常尺寸压力例及同轮旧版4组控制均实际执行；另四组 Comet 后期2.06～3.043秒、原画质1／4×／876×750、原BoundsFocus且Zoom0.3的近景／转镜对照。相机、阶段和模拟数量双方精确一致，没有改寿命、质量、数量或步长。含模拟、提交及同步读回／等候，12组最高P95 **11.6103ms**、最高单帧 **12.7658ms**；近景最高P95 **9.2643ms**、单帧 **9.8901ms**。近景有蓝烟／石块及约半屏主体，底部裁切已明示，不称完整全屏压力；默认自动烟雾取景太远而0draw的样本单列负对照，另有真正888粒子的近景压力。不是GPU时间戳、普通窗口FPS或所有VFX流畅承诺。见 `performance/complex-effects-ab-v1/final2607-series-v1/summary-v1.json`、`performance/comet-dense-close-v1/summary-v1.json`。
- 最终正常App Debug构建退出0（883条现有警告／0错误），Core／native／中文资源 SHA 与上文一致；受影响Editor测试重建退出0（4条现有警告／0错误）。最终自动批次分别为：50个已审阅纯CPU夹具 **883通过／0失败／0跳过**；渲染协议与原官方DDS的1×／4×、全／半／跨实例／倒序、MAX Alpha、精确旧回退、分数LOD等 **21通过／0失败／0跳过**；延迟相机与实际中文加载 **7通过／0失败／0跳过**。先前局部200项等与这些批次有交集，不相加。没有开启全部旧合成纹理GPU套件，未跑全解决方案、Release、CI或GUI；精确筛选与TRX在 `parent-validation/safe-filter-v1.*`、`final-cpu-regression-v1/`、`final-2607-protocol-v1/`、`final-editor-localization-v1/`。两条旧中文诊断改为实际条件说明，完整资源经真实 LocalizationManager 与 SoftwareOnly 文字离屏验证，并核正常输出同字节。

### 保留的失败与下一步边界

- 4B候选的新增shader实例保护错误地向 `VS/PSGetShader` 传必需的 null 输出指针，导致首个混合用例 native AV、完成测试0项；不是21个断言失败。独立D3D getter以SEH实际复现0xC0000005后改用ComPtr，6F及最终2607均通过原控制；失败DLL／PDB、SDK合同、日志和全部红灯保留，见 `native-half-scene-v1/crash4b-before/`。早期正交夹具误触旧回退、Alpha union破坏既有Alpha等已据真实原因修正，未放宽逆序或像素断言。
- Nurgle 完整原件仍有细紫直线折角与分开的烟团，不能判为已经原生。最强候选 `generic_trail` 原字段为velocity_v2、Alignment1、stretch10、窄尺寸、固定细纹理UV；没有同条件原生／隔层控制，不能仅从字段或名字断言那条线本来正确。该原件还有 GradientIndex3 的 noxious 层及未显式声明通道的颜色层，所以不是只去掉 distortion 整组判断就能让完整原件使用MBOIT；不放开未经证实的渐变／折射组合。具体源、七层及实际包字段见 `official-trail-breadth-v1/nurgle-mixed-route-readonly-v1.json`。
- FULL原生正可见q0捕获、任意min/max表／Bezier Alpha／非零偏移、源颜色表最后精度、复杂渐变／折射混合、全部持续弯曲与普通Debug冷启动／长时帧稳定仍未整体验收。用户随后反馈本次版本“目前基本可用”，仅记录其实际测试范围；不宣称所有trail／烟雾已达游戏级。保留已确认正确的模型、旋转、播放和原始依赖，按上方交付授权完成 Git 交付后等待下一步指示。

## R114 弯曲 Trail 与密集混合特效（2026-10-27，进行中）

- 用户提供 `D:\WH3WOR~2\codex-clipboard-9255e7a9-81ef-4c32-bf53-b2ebf4ea85d5.png`，明确反馈弯曲拖动仍为独立矩形段、急弯裂口／黑边，复杂花哨特效播放和转镜仍卡；登记为本次未通过验收，不用历史单粒原生方片或2499个自动通过解释掉整链问题。用户重新确认现在可用、不使用键鼠；沿当前 `codex/vfx-preview-native-parity-r97`／HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，不提交、推送或清理。
- 本节除明确旧路径外，证据相对 `artifacts/vfx-continuous-trail-dense-effects-r114/`。初始112个已有路径逐SHA对照R113快照无漂移、暂存空；见 `parent-validation/workspace-before.json`，原源码、文档及native文件在 `source-before/` 保留。
- 已确认并修复引用作用域：同一个引用里的两个官方吹管层原分别抽到S=.941983879与.909741044，段长／运动／颜色因此错位；原生两层CB共用S，且两套不同Tint范围反算同一颜色权重。转换保留每次引用出现位置的token，模拟按实例共享六个随机权重，逐层应用各自覆盖范围；不同引用、嵌套分支和每次事件触发隔离，各层仍消费原六次随机数。源件、寿命、数量和1/120不改。新夹具先2通过／5失败后全绿，并新增两层B401实测首粒浮点位守恒；9项全绿。红记录实际保存在旧 `artifacts/vfx-trail-shape-framepacing-r113/trail-continuity-audit-v1/`，不搬动或冒充旧验证；本轮绿记录见 `trajectory-audit/reference-green-summary-v1.json`。
- 共享参数链补齐：旧转换只映射Kind1的 `ParticleCullDistances`，Kind0吹管完全漏读。现在Kind0显式距离按当前发射器正均匀根／继承缩放计算，保留原生近／远端 `1e-6` 和上界；非均匀、反射、剪切回退原显示并诊断，Kind1／2隔离。标准普通面片按原生全局默认 `.01` 实现两个严格 `threshold < Alpha` 关口，先Tint×距离再最终顶点Alpha，不把贴图Alpha提前裁剪；自定义网格材质／模型／贴花不扩大。Bounds明确绕过假相机距离和显示阈值，保护短距离与极淡几何的初始取景。该参数修复阶段Core为 `28AF9D43C95ADC6FCCC848259B9C1658F1D9741246A53D36AC7C66FAF1A20C3F`；后续仅投影缓冲/稳定排序复用后的最终Core见下文。
- 新gate／Bounds21项真实红灯为11过10败，修后与Cull21、Ref9、模型距离9合计单次 **60通过／0失败／0跳过**，官方manifest已提供，见 `production-appearance-v1/gate-bounds-green-summary-v2.json`。首次编译因只改源码未build Core而失败、首轮安全控制误写成模型的未缩放距离结果均保留，不算产品红灯或放宽断言。
- 本次真实producer核对 `production-appearance-v2/current-28AF-appearance-v1.json`：源XML／贴图仍官方原件，仅诊断引用固定为原生捕获的S和Tint。当前根实际512/0/512、scale=.8478706仅乘一次，独立断言通过；实际Frame箭头Alpha旧D449 `.1829174`→最终 `.031103391`，距离因子 `.17004064` 与原生float相同。弱步枪低于阈值不再提交，simulation仍2粒，寿命不变。原生捕获箭头 `.031561363`，剩 **约1.45%** 未修：原RDC最小DebugVS 206步新核生命表 `.7158203125`／初始表 `.39990234375`，最终与捕获只差7.45e-9，定位到普通Alpha i/128表及half采样精度，未猜改产品。见 `production-appearance-v1/native-alpha-debugvs-v1/result.json`。旧用原生postVS Colour喂AE的结果仅证明shader，不能证明旧CPU producer透明度正确。
- 原B401/81AD正常窗口基线 `live/baseline-v1/curved-motion-v1/`：20秒828原PNG，G模态内通过文档API在29个屏幕点移动指针，27次实际黄框位置变化；三处网格／方向球patch全828帧逐字节相同，未见摄像机变化。这次实际形成近两圈弯路，12／20／22／40／44帧亲看仍有分离宽片、裂口／交叉与黑边；56仍有极淡残片，60已消退。不是旧数字三端点，也不是真人连续输入，工具green=0为检测阴性。原始联系图及测量记录同目录；不能判为修好。
- 官方图集只读审计：吹管三档、圆形路径9阶段的400个实际packet均固定原格7，没有原件中的随机换格／动画／UV翻转字段；格7原RGB确有黑区、Alpha大部分255。不能换格或把黑色直接当透明遮掩。详见 `materials/atlas-pixels-v1/`，原格与邻格的字面图保留；整路径裂口没有因此闭环。
- 密集样本已按本轮D449实际CPU活动与正Alpha选择：完整 `wh_main_heavens_comet_of_casandora_impact.xml` q1在.5／1／2秒有600／767／691个正Alpha packet；1秒含658普通面片、110模型、2贴花及事件。玉龙墙只作早期混材控制，4秒只剩6粒，晚期298FPS不算性能通过。死亡之风／传送门本次分别最多15／2粒，不作密集面片主样本。所有原件依赖missing为空，见 `materials/heavy-roots-cpu-v1/`。
- 保序批量绘制已落产品native `1CD92BB9BD8B2AE07788B5A63810DF1C98B6235A320586C65F9F5BA04518B4C9`，正常native Debug构建退出0。原18份材质DXBC／18exports不变，32项独立CPU状态控制全绿；新增两个bulk头、改Bridge／SpriteUploadCache／CompileMaterials／CMake，CoverageMaterial与本轮前备份同SHA。原C/E图元及half RGB写入次序保留，RGB双源批＋Alpha-only MAX批；相邻兼容段合批，模型／贴花／折射及短段走原队列，作用域结束恢复并失效所有实际状态缓存。
- 冻结D449、只切81AD/1CD的完整官方烟雾与彗星q1反事实：**210次配对执行（含smoke静止重复，不是210个唯一状态）、21对PNG** 的包、计数、RGBA全部相同；彗星另3帧全部4×MSAA RGBA half bits逐位相同，真实GPU clear正控非空、NaN0，官方依赖missing空。两进程raw读回各36MiB、未存全raw，全部原件与失败保留。具名汇总 `performance/ordered-sprite-bulk-validation-final-v1.json`。
- 独立timing的完整EndScene＋staging等待两均值之和：smoke876静止14.332→10.812ms、转镜14.629→11.615ms；Comet876转镜11.431→8.259ms、1080静止13.459→11.022ms。EndScene CPU本身约降38–44%；不是纯GPU时间或窗口FPS。实际draw计数是最后非空pass，smoke普通颜色约1776→2，Comet颜色790–914→65–86，晚阶段805–848→112–130；不能写整场总draw。彗星全览主体约140–230px宽，近景填充与最终窗口稳定度仍须另验。
- 最终完整Core VFX首跑为2546通过／4失败／14跳过；4项均是旧测试要求零／极低Alpha继续产生绘制包。三处测试保留原数值和像素严格断言，增加显式负阈值诊断查看裁剪前值及默认门口裁剪控制，零Alpha应0draw；没有放宽误差或删除用例。修改测试重建后Core28AF全套为2550过／0败／14跳；首跑失败/TRX保留。最终投影复用Core25CC又重建并完整复跑 **2555过／0败／14跳**（2569项，2分21秒），见 `parent-validation/full-core-vfx-final-v3/`。共享Effect测试亦重新构建、开启离屏复跑 **55过／0败／0跳**（39秒），见 `parent-validation/effect-headless-final-v2/`。14项缺指定原版导出/旁存来源，不计通过；专项与全套重叠，不相加。没有运行整个解决方案、Release或CI。


- 进一步确认并修复每帧投影/排序大分配：`VfxPreviewParticle`为152B，旧List、LINQ稳定排序和ToArray令官方smoke热帧约401–404KB、Comet约284–360KB分配。Preview改用实例私有List，模拟Fill保留原计算；复用12B排序key，Layer升序、float距离降序、原索引升序，并按置换环一次搬动大struct。公开Frame/ProjectedFrame仍返回独立快照；私有ground-depth筛除保持原顺序。新红门禁1过2败，修后相关30/30绿；真实五阶段每阶段12预热+64调用的packet SHA与旧28AF全部相同，smoke热分配0B、Comet3.24–11.232KB（残余小分配未逐项归因）。源码只两产品文件+新 `VfxProjectedFrameBufferTests.cs`。独立实测为1060中间Core，随后唯一改动是public List预留容量（Fill原已有EnsureCapacity），最终25CC复用该Fill测量理由已记录，没有伪装成25CC重采。见 `performance/projected-frame-reuse-v1/projection-allocation-validation-final-v1.json`。
- 最终正常App已构建退出0／883个既有警告／0错误，Core=`25CCAC1FAD6F94640A4EF128CCA081B5BE8364BF716A6616FC0573342D3E4FF6`，native仍1CD；没有更改画质、粒子数量、寿命、1/120或Kitbash交互。25CC尚未登记普通窗口视觉复测：之前真实窗口是28AF/1CD；相等的粒子数据和自动像素不伪装成新窗口验收。
- 28AF/1CD真实 `live/final-v1/curved-motion-v1/` 20秒824PNG、0错误、27次黄框位移，两圈弯路，三个网格/方向球patch全部帧恒定且与B401基线初始哈希相同。亲看12/24/31/40/45仍有独立宽矩形、直端和裂口；62极淡残尾，65及823已消退。不能因修正透明度或green检测器0就称形状通过。不是真人连续输入；WGC约41.2采样/秒不是窗口FPS。
- 复杂彗星q1普通窗口实际看了暂停.85秒大面积近景、播放3.43/6.58秒及KP3过渡，截图HUD约73–86仅作观察；该首次90秒诊断选项超出有效20–30秒范围，未生成记录，此错误已明确保留。之后30秒host1只覆盖q0全览初始/混合（冷Render最大2034.8ms）；host2覆盖q1暂停1.99秒全览，Render中位5.76ms／p95 6.29ms，冷Seek仍1057.8ms。F4短按未采到、wheel实际拉远，不能把host2写成近景高填充定量通过。见 `live/final-perf-v1/host-frame-statistics-review-v1.json`。冷加载/Seek和全场景稳定帧率尚未闭环，旧HUD或离屏收益不能代替。
- 工具控制的Terry两轴尝试仍未得到有效拐弯视觉：短笔约.09秒且两笔相隔8.7秒，录像只包含第一笔，原件/797PNG保留，见 `native-curved-reference-v1/native-turn-attempt-summary-v1.json`。随后已实际找到Place（平移下拉的箭头+指针）模式，绿框内一笔斜拖使X/Z同时改变，Y仅浮点残差，原件不改。用户已明确同意真人15秒XZ折线，当前正在等待正式开始：RD采样实例PID47420／Main HWND119277448／ident38920，已鲜核原版、q1、Bloom关闭、Place；全窗原PNG在 `native-curved-reference-v2/preparation/terry-place-ready-original-v1.png`。独立60秒WGC工具已编好但未启动；被动RD控制器62656只等capture-ready文件（8秒后取2帧，600秒超时），ready尚未创建、未抓帧。新回合开始前必须鲜核控制器存活/剩余超时，必要时保留旧失败后建新会话，不能写入旧会话覆盖记录。原生FOV为60°，AECN普通窗口45°；拟以实际Native View/Projection60配AECN离屏，尚未执行，不冒充窗口已同视角。

- **用户本次 Terry 人工拖动已完成并重新审片**：`native-curved-reference-v2/manual-live-v1/` 共1152原PNG，开头两张之间缺29.656秒，不能称60秒连续有效录像。已亲看15张原图，确有真实青绿纹理尾迹及自然缩短；有效强阳性主要近直线，部分位置在一帧内跳变，尚不能验证紧弯接头。不是仅绿色选框，也不把该采样现象归咎用户。原生q1／泛光关闭已核，FOV60与AECN普通窗口45不同；原件、失败控制和图像均保留。
- 新 `capture-manual-v2/terry_frame134793.rdc` 实际31个输入、10个有限PostVS及461个改变像素；下一134794实际indirect/bin计数0，不从黑图猜粒子。`replay-v1/positive-134793-exact-input-v1.json` 留31原记录／当前根／S／相机／CB。Core25CC同输入重新计算，31个裁剪决定全同、UV全同、最大世界四角误差.002719m／全视口.2312像素；剩宽度／Alpha约数个百分点仍属于普通曲线half表边界，未盲目缩窄或调暗。该片段近直线，不能借此证明弯折正确。详见 `production-appearance-v3/comparison-summary-v1.json`。
- **转弯的共用发射节奏差异已复现并实施**：原C75 `6c1a60` 算术／控制循环在自有隔离内存执行，外部常量getter／零区域／writer明确为诊断hook，不是完整游戏模拟。原生active分派一次消费当前端点，负start/catch-up另有循环；同节奏调用AECN与原kernel基本一致。生产路径按1/120细分同一输入后，60Hz弧线／急转会改变出生中心、速度方向和时钟；与Pivot补偿无关，原生位置wrapper及31记录均反证半段补偿，不添加美化接缝。证据 `trajectory-audit/native-turn-kernel-v1/`、`native-pivot-compensation-hypothesis-v1.json`。
- 仅交互continuous、非负开始且已准入的原生距离trail改按实际挂点更新端点发射；`VfxAttachmentHistory.NextSampleTime` 将转折端点传给共享模拟，回放／倒放保留记录路径，三参入口转发兼容既有调用。原全局1/120、普通Seek、负start准备、其他发射路径不变。受限无力无阻尼出生按原birth clock与速度求位置，避免回溯重复积分；过Life不显示，不延寿。零端点但非零Bezier阻尼真实红灯后，Force／Deceleration含Bezier时仅新路线回退，未改全局曲线含义。原12门禁4过8败→12全过；补回放／停止／容量后19全过；新增Bezier正负2红后最终742D **新21＋旧continuous13共34通过／0失败／0跳过**，精度未放宽。源码／失败／TRX均保留。
- native剩余MSAA深度CS及WeightedDistortion VS/PS移至构建期，`SceneUtilityShaderSources.h`为唯一源；3份DXBC逐字节相同，原8Float／18材质／3bulk不变、18exports及ABI正负控制通过。native34C正常Debug构建0；Core25CC＋native34C实际加载的14项GPU门禁 **14通过／0失败／0跳过**。原三份运行期编译共约6.7ms，不能解释秒级卡顿。正常App后续v3构建0警告0错误并复制34C；该次窗口仅25CC，尚不是742D实机。
- 正常Debug PID35776的真实q0冷加载EventPipe位于 `performance/cold-reload-eventpipe-v1/cold-comet-q1-v2/`（目录名含q1但实际首载是q0，q1切换已超20秒窗口）；0eventsLost，UI线程命中转换、BC7解码、Bounds及后续Seek，样本数不冒充精确阶段毫秒。Converter局部一次层列表快照，完整官方彗星／烟雾q0/q1四组定义、曲线、事件和原资源字节SHA保持；彗星暖转换每次少约56.3MB分配，部分首次时延未改善，见 `performance/converter-layer-snapshot-v1/comparison-summary-v1.json`。当前还在独立核验死亡粒子的稳定批量压缩，不把这些收益称全部稳定流畅。
- 本次 `live/current-25CC-34C-v1/` 保存q1／暂停2.06秒的普通窗口原图；启动诊断误填60秒，而该入口只接受20～30秒，实际没有帧时文件，不能报告本次FPS／QueryWait／每帧分配对比。Terry人工拖动场景保存前副本保留，已保存人工变换后正常退出；Debug未编辑源件、正常退出。用户随后明确要求先离屏修改、最后统一实机，此边界生效；弯折长方块与复杂近景流畅度在最终实机前仍未验收。

- **最终兼容与完整专项**：整体回归曾在DFD1复现5个本轮兼容失败（4个旧五参Spawn诊断入口、1个无采样回调的连续播放器入口），均在产品层保留转发修复，未改旧断言；失败TRX保留。随后3633完整VFX专项2576通过／0失败／14跳过。最终轻量转换Core **8B1EE84033EF2DC94DEFF979B8D1E5D0E0AAEF70DC1C1CF4423612C6BC330FE3** 已正常App引用构建（887既有警告／0错误）、测试重建（3既有警告／0错误），重新完整VFX专项 **2576通过／0失败／14跳过**；编辑器选择／交互／历史采样 **50通过／0失败／6跳过**。见 `parent-validation/final-lightweight-*`。未跑全解决方案、Release或CI。原14弱Alpha样本与6项线框GPU opt-in跳过不作通过；旧25CC的55项共享Effect结果仅为前阶段复用，不冒称8B1E重跑。
- **最终转换优化与未采用候选**：Converter只读取层标识、类型与原始XElement，不再构造无用途的编辑字段；路径、顺序、缺省id/type、根选择、引用覆盖及原数据保持。完整彗星／烟雾q0/q1四配置的定义、曲线、事件和原模型贴图字节全图SHA完全同，58/58/3/3官方依赖无缺失。相对3633，彗星每次暖转换再少约28.1MB分配（11.46%），烟雾少约10.13%；这是局部转换收益，不等于冷峰或全帧率已解决。见 `performance/converter-lightweight-layers-v1/comparison-summary-v1.json`。过期粒子压缩候选E979→4579的1924帧包／私有粒子／事件Random／Bounds全同，但暖时延没有改善、分配相同，产品保留原RemoveAt；候选源码、DLL及全部对照保留于 `performance/expiry-compaction-v1/`，不计优化收益。
- **完整官方曲线运动离屏与外观限制**：`performance/official-trail-cadence-v1/` 固定q1原根、资源、相机、输入端点、阶段与暗亮背景，25CC→DFD1有18对原PNG／自产CPU包／实际shader包。实际源引用启用2根，库遍历总30／源未启用28，不是自行删除28层，也不能笼统称质量漏层。停止后6对为零；阳性阶段局部排布随正确cadence改变，但弧线晚段仍有短矩形及灰暗边。最终3633抽阳性与停止×两背景4对，包和像素逐字节同DFD1；8B1E只改转换层投影，四完整官方配置全图SHA另核。该离屏不冒充与Terry同seed／同轨迹的整段视觉验收，不能以停止归零或测试数量判外观全通过。
- **最终原生合成原值补证**：134793 moment1337／colour1462实际VS／PS指纹匹配，GS／HS／DS全为ResourceId::0，本次两draw没有额外几何／细分连段，不外推所有原生trail。原31数据对应的halfColour／halfB0、实际final5输入、fullB0及最终前后像素在 `native-curved-reference-v2/replay-v1/raw-composition-crop-v2/`、`raw-composition-crop-v3/` 保留小块raw；最终PS为nofilter、无depth输入，不能笼统归因深度引导上采样。真实fullB0复算T约.801954～1，合成RGB平均残差3.79e-5／最大.0009765625，非bit exact；最终目标A被Zero/Zero混合写0，不是T。最终CB范围守卫失败及v1 API失败保留，复算明确复用同RDC此前已读screen_size1707×1067，未假称1544重新读取成功。
- **最后实窗边界与当前分工**：3633普通Debug PID44252实际播放完整官方彗星，确认q1及暂停2.06秒全览（主体约190px），未构成完整密集近景／持续绕转验收。30秒记录6669回调无丢样，冷峰Render约2.049秒、当次托管分配约689MiB、Query约2.07ms；后半样本为轻负荷，不当FPS或最终8B1E测量。误命名图片 `comet-q1-close-2s-original.png` 实际53.03秒，保留并排除阶段判断；`comet-q1-2p06-overview-confirmed.jpg` 才是已确认2.06秒。CUA显示图与返回JPEG纵向比例不一致，校正后暂停／定位才命中，无用户键鼠冲突。Debug已正常退出，未保存或改原XML。用户此后明确改为只离屏、人工核验；当前8B1E不再自行实窗，自动／离屏与用户验收分开。

- **简单光照的最终共用修复（2026-10-28）**：同原生31输入的初次无天气DIAG，顶点RGB基本等同但最终L内点RGB比约1.57/.60/.31。实际DebugPixel单像素228步证明纹理返回中性灰，偏色来自原PS的simple lighting；nativeAmbientTop=(.54747492075,4.0172457695,9.4761180878)、E=.7001953125的gain=(.65440905094,1.69466257095,3.33125829697)，与调试寄存器逐bit相同。旧大L差多数是天气条件未配齐，不能记为DDS通道或Alpha故障。但产品也有真实漏映射：weatherLighting原只准alignment0/3，带simple标记的alignment1因此选天气也无效。Converter仅在simple=true时补alignment1，复杂法线组合仍受原限制；默认天气、贴图、源XML、数量及寿命均不改。新10项先5过5败，修后10全过，含legacy/v2、0/3控制、alignment1复杂负控及官方吹管三档，见 `production-transmittance-weather-v2/` 与 `performance/simple-velocity-lighting-v1/` 的实际记录。
- 当前正常Core为 **EC3C898C816F04FBBB456ABFC0A6DCA42F6BD8967C2DDD70783696711173CD34**、native仍34C；正常App引用构建0错误（863既有警告），Core测试重建0警告0错误。同环境最终2次GPU严格自动映射Mode1，使用实测原生AmbientTop的显式诊断天气，不覆盖产品默认值；固定332内点的新/原生RGB能量比约1.02457/1.01989/1.02332。31项原有颜色、Alpha、寿命和几何输入不动；旧/新T均差约8.79e-5，half写入及生产曲线精度残差保留。亲看原黑白图仍有矩形分节和部分灰边，2%左右总能量差不代表逐像素或整段形状通过。证据 `production-transmittance-weather-v2/gpu-final-v1/same-environment-absorption-emission-validation-final-v1.json`；完整半分辨率、匹配原DSV、原生曲线half精度及持续弯曲的最终外观仍未全部验收。最新分工继续只离屏，用户人工核验。

- **EC3C最终自动结果与交付状态**：`parent-validation/final-simple-lighting-core-vfx-v1/` 在重建后的真实EC3C／34C执行，完整VFX专项 **2586通过／0失败／14跳过**（2600项）；`final-simple-lighting-editor-tests-v1/` 为 **50通过／0失败／6跳过**。不与前阶段重复结果相加。正常可测试入口仍是 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`；最终版未再实窗，按用户最新分工留待人工核验。源数据／原始寿命／数量／画质／全局1/120保持；当前分支和HEAD不变、暂存空，源码、正常Debug、备份、离屏包、RDC／录像、失败和未采用候选全部保留。不提交、不推送、不清理，完整保护盘点见 `parent-validation/workspace-after-frame-lighting-v1.json`。

## R113 Trail 形状、转镜与绘制性能（2026-10-27）

- 用户明确要求 trail 外观与性能一起修，后续追加“转动视角时尤其卡顿、trail 不连续和长方块”。正常 Debug 与必要 Terry／采样授权持续有效；仅官方完整原件，不改原始寿命、数量、质量、全局 `1/120`，不操作 Git 交付或清理。
- 现场沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空。最初 108 个已有路径逐 SHA 对照 R112 final-v3 无漂移；见 `artifacts/vfx-trail-shape-framepacing-r113/parent-validation/workspace-before-v1.json`。本节以下路径均相对此 R113 目录；原源码／DLL／PDB／失败记录分别备份保留。
- 普通 Kind0 的 InitialScale／InitialAlpha 使用实际出生时刻及独立 `ParticleInitialParamLoopAnim`；原生 writer 的 float 转换、减开始时刻、fmod／除时长已核。Life／Speed／其他 CPU 出生参数、随机消耗、段长、数量不变；Kind1／2 保留候选。新增 33 项含其它 loop 的不扩展控制，最初 29 项旧 Core 为 1 通过／28 失败，最终均绿。完整官方吹管、AmberSpear、Starfire、Balefire、JadeDragon、Pox、Crowman、Bear 的 CPU 范围审计仅证明参数范围和控制边界，不是视觉验收；49 个实际依赖来源与另 48 个只读发现 XML 分开记录。
- 显式 HALFRES 普通面片的最小像素尺寸原误用 full viewport，已按所在通道的 half viewport 计算；仍保留 HalfResolutionUnsupported，因为普通颜色目标及 MBOIT／重建未因此实现。真实原生两粒、四角／UV 与 full／hires／无视口控制 6 项，旧版 5 通过／1 失败，修后全绿。不改已核 .4S／.6S 轨迹段长。
- 普通 legacy mode2 的近乎平行视角原错误借用 cameraRight，造成轴向突变；现直接归一化非零叉积，零或 float 下溢不造面片，mode5／模型／水平约束不动。7 个新输入旧版 1 通过／6 失败，修后全绿，旧 mode5 控制保留。最终托管 Core=`B4012D46F2F081F5F864A270BFC7DB6ABECA56B686D08FD5714BC3AED94481A4`，`near-parallel-green-v1/` 本次 CPU 专项 **120 通过／0 失败／0 跳过**。此前 113 项的 3 个官方 q0/q1/q2 跳过已另补三项通过，不冒称同一次全量。
- 原生桥先实现 actual VS used-byte 上传复用，再在自有 cached-sprite 作用域复用 VS 绑定。2253 Debug 版本实际两帧计数：886 粒、1772 DrawIndexed 不变，已预热 VS 上传 1772→0、VS 绑定 1772→1，PS 上传／绑定仍各 1772；首中末 actual used bytes／PS 全量／shader 均一致。`performance-audit/binding-camera-ab-v2/` 在同 Core8A 下 90 个唯一相同官方状态、9 对 PNG 与绘制计数严格一致。持续转镜总均 17.407→16.690ms，p95 仍 18.287ms，**没有稳定 60 帧**。初始 C97 的 210 状态对照属于更早阶段，不能与此重复相加宣称最终全量。
- 正常 Debug `normal-debug-build-v3.log` 实际退出0／0警告／0错误，加载 CoreB401/native2253 的 PID43616 已实际打开完整官方吹管 q1、G/X 往返及视角预设；20秒 834 帧保存在 `live/aecn-geometry-v2/endpoint-motion-v1/`，随后正常退出。是端点运动，不能冒称持续鼠标拖动；新的最小尺寸与近平行修复不等于长方块已全部解决。前一版 Core8A 的 807 帧明确出现阶梯亮矩形／暗矩形，见 `live/aecn-final-v1/endpoint-motion-v1/endpoint-literal-crops-contact-v1.png`，其消退正常与形状失败必须分开。
- 正常 Core8A/nativeC97 的烟雾 q0、667×631、稳定区总均16.376ms／p95 17.666／最大25.996，实际 Render均15.691；见 `live/aecn-final-v1/normal-host-analysis-v1.json`。不是新 CoreB401／最终 native 的性能，也不是无采样干扰的整机游戏结论。没有再现旧98／150ms峰，不等于找到了其原因。
- 新鲜官方 Terry q1、泛光关闭已在窗口核实，PID1776 自有场景保存后正常退出。两帧55179／55180原件与独立副本保留：55179真实2粒寿命内，角点有效，moment／颜色各贡献27像素；55180两通道间接实例数0，为正常空帧。`native-visible-burnin-v1/replay-v1/` 含实际相机、GPU输入与原像素图；只闭合有效原生参考，**不把不同相机／阶段的窗口截图直接判成功**。Terry仅是原生参考，不等同游戏最终显示。
- MBOIT 实际 shader／CB／混合语义已核，自己的数学移植在55179单个真实 fragment 上以 GPU 校准7矩、T、u及实际 HalfRGBA 存储逐值一致；原 BC7 base payload 与官方 Pack DDS payload SHA一致。见 `mboit-kernel-sameinput-v1/`。只是单像素数学 DIAG，未接入产品，不能宣称完整 MBOIT／half-res 已支持。同输入材质6draw DIAG 的未照明 RGB约偏 .63～.82%、alpha约偏 -.053%，没有出现 alpha 整块读成1的证据；天气／雾与最终颜色不能混算成材质错误。
- 最终 native=`81ADC70AC6F02FC8172E3965A3B0A1AF4D314393912B2D8A4301FDE7D7F40A36`，基于自有进程实际栈定位每批重复材质状态／纹理引用收集，限定 pure cached File0 的单次同步绘制作用域复用原 pinned 入口生成的状态，全部15个材质值和 node/basic 动态值逐批刷新，条件变化走原入口，队列仍引用的状态延迟安全清除。32项实际 pinned 入口独立 oracle 从旧31通过／1失败到新32全绿；固定 CoreB401 的90个官方同状态、9对PNG与180个绘制数逐项相同，先完成画面验证再独立计时。持续相机API转镜的 EndScene均12.2407→10.2982ms，总均16.7797→14.8114、p95 17.6413→15.6515；静态烟雾最大仍21.236ms。见 `performance-audit/collector-state-ab-v1/`，不等同真实鼠标／WPF输入负载或全部稳定60帧。
- 最终正常 Debug `normal-debug-build-v4.log` 为退出0／0警告／0错误，主程序输出确认 CoreB401/native81AD。`parent-validation/core-vfx-final-v1/` 先重建实际测试项目，随后完整 Core VFX 专项本次 **2499通过／0失败／14跳过，共2513项**；14项仍缺官方 poison-trail 原件及来源旁存记录，不计通过。测试前核对两份最终模块散列；首个宽泛文件名后缀 guard 误匹配 Test.GameWorld.Core.dll 的准备错误在GPU前终止并保留，不隐藏。没有重跑整个解决方案、Release或CI，也不把此前UI／META结果相加冒称本次全量。
- 最终普通窗口 PID25976 实际载入 B401/81AD，完整官方烟雾q0、无Terry／录像／GPU helper并发的30秒宿主诊断中，单独取10.001～26.561秒的1085个同667×631且无重建回调：总均14.1576ms、p95 15.1965、最大21.5636；Render均13.4468。此前8A/C97同尺寸稳定区总均16.3764／p95 17.666，实际普通窗口收益约13.55%；随后窗口尺寸变化的样本另列，不混作同尺寸比较。见 `live/aecn-state-final-v3/normal-host-analysis-v1.json`。这仍是回调耗时，不是GPU时间戳、游戏FPS或持续真人绕转的验收。
- 新近景 Terry `native-closeview-v1/` 保留20秒777帧、两个原RDC及副本、泛光关闭的窗口证据和实际q1。27352中只有arrow真正有限可见（age .171051＜life .360121，原quad约.508575×.20802 world，vertex alpha .03156136）；rifle虽寿命未到但实际PostVS NaN被剔除，27353两pass均0实例，均作为负控制。原生真实显示自身存在宽绿色矩形及阶梯边，不能由此推断全部用户异常都属于正常。实际GPU FOV为60°，不是准备scene.user中45°，且shader仍有极微量雾；没有将它与AECN默认视角截图直接判为同条件成功。自有Terry场景恢复原Fog开启、保存后正常退出。
- `material-audit/native-closeview-material-sameinput-v1/` 固定27352实际四角、UV、Colour、相机和180×327目标，以完整官方DDS做黑白6draw：中心真实原生a=.0315613634884，AECN解出的a=.031561375，差约1.15e-8；关联未光照RGB约偏+.20%～.39%，没有整片alpha读成1的证据。原生MBOIT偏置后的u=.0325613617897与a分列。AECN禁用深度的intrinsic DIAG有310像素，原生B0／colour186像素；补读lower(92,168)两pass明确depthTestFailed且非shaderDiscard／scissor／backface，scene depth .793962955＜particle depth .800521851，不能把禁用深度的轮廓差当材质错误，也没有据此盲改虚拟地面。只精确归因这一点，不外推全部差点；新RDC最小读回与6draw虽和正常App并发，但只用独立缓冲的像素，不采用其计时作性能结论。
- 实机工具在PID25976的工具选择器发生AX／截图目标错配，停止了输入，保留原记录；用户确认正常关闭后实际核实进程消失，没有强制结束、丢弃内容或改动源XML。剩余边界仍包括完整MBOIT／half-res重建、同视角同阶段整段连续拖动与真实持续绕转的完整验收；不能宣称全部trail／烟雾或所有长方块均已正常。全部源码、正常Debug、旧DLL／PDB、原件／副本、小工具、备份与失败均保留，不清理或搬盘。
- 随后新开PID7736，实际载入最终B401/81AD，正常窗口打开完整官方吹管q1；`live/aecn-trail-final-v4/endpoint-motion-v1/` 20秒817帧实际录到三簇端点运动的绿色宽面片及自然消退，原检测器green=0为检测阴性，不能冒称无trail。KP3切换与短寿命末段重叠，只能确认有真实相机过渡及少量残留，不能作为活粒子全视角验收；播放器已连续到13.05秒，超过8秒显示区间，不回绕重置。不是持续真人拖动或同阶段Terry窗口对照。录像工具与Debug最终均正常退出，未保存／改写原XML；最终变化与保留盘点见 `parent-validation/workspace-after.json`、`final-audit.json`。

## R112 吹管真实拖动取证、负时间预热与绘制提交性能（2026-10-27）

### 现场与实现

- 沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空；本轮初始 98 个已有改动／未跟踪路径及散列见 `artifacts/vfx-trail-live-framepacing-r112/parent-validation/workspace-before.json`。不创建替代分支／worktree，不暂存、收起、提交、推送、PR、合并或清理。没有改原 XML、DDS、模型、粒子寿命／数量／画质或全局 `1/120`；R111 的距离 trail、铁龙运动和隐藏暂停保留。
- 本轮先测正常 Debug，确认首次 Reload 在 Render 中阻塞约 10.958 秒，下一回调追算又耗约 5.431 秒，普通 8 秒循环还会同步负时间预热约 4.724 秒；该例没有重复呈现或隐藏场景工作。完整官方 `wh_dlc03_enviro_large_siege_smoke.xml` 的 q0 是 `rising_ashes2`，原开始时间为 **-100 秒**，不能混同旧 -25 夹具或将该档位称为完整灰烟层。
- 独立 VFX 显式启用受限后台预热：完整保留范围估算、零时刻模拟、原随机序列和原步长，交接整个模拟实例；默认同步、共享 Effect、模型、事件与运动 trail 路线不启用。首份保持原 `CycleDuration→Seek` 顺序，继任者保持原自然重播顺序；初次等待先初始化真实挂点历史，变化的 key 拒用旧结果。全局最多一份准备任务，四核以上首次两份独立 CPU 工作并行，全部完成才释放；每实例仅预备一份继任者，关闭／重载取消。等待及回交不追算墙钟，多份重资源来不及准备时仍可能暂留末帧。
- 托管提交缓冲按播放实例复用容量，保持独立快照、有效数量及排序。曲线只复用相同上下界通道的计算与真正单关键点值，保留特殊浮点值和原算式。真实单 PID 托管采样又查明预热中的重复 Force 与世界根计算：只缓存严格单关键点且有限的原力取样，World2 复用不变出生根及力基判断；原件的动态阻尼不是单关键点，保持逐步取样和积分，没有按常量缓存。
- 普通面片的原生材质模板仅在一次绘制内按层复用，始终写回原统一 scratch 地址，逐批更新全部动态参数；保持覆盖／发光交错、原 batch 条件、shader、ABI、模型、贴花和扭曲分支。没有把所有覆盖重排到发光之前。888 粒左右的该烟雾仍产生约 1776 次颜色绘制，模板复用没有消除这个后续瓶颈。

- 后半新增普通固定面片的保守视锥剔除：保持原 C／E 交错与整批条件，只在原四角全部位于同一 XY 裁面外时省略无像素贡献的整批原生提交；近面交叉、异常变换及其他未准入类型保持原路径。全览的三原点预判仅作 **keep-only** 快速保留门，不增加剔除。模拟、出生／粒子数量、寿命、步长、排序、量化、材质和 shader 均未改变。

### 本次构建、回归与精确对照

- 后半视锥修复的最终正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` **退出 0／0 警告／0 错误，6.65 秒**，见 `performance/sprite-frustum-v1/normal-debug-build-v1.log`。正常 Core 保持 `83E33682208F149FD24B55AEE777DC13DD10AB911D67B9CDF7A664B1724E14E3`，最终 native=`7E35AD78BCBF8E57EE6B816DB0576390A960A9A8CB10176F7CF1C14F7F2B4114`；普通窗口实际载入路径／散列见 `live/aecn-frustum-final-v1/loaded-modules.json`。R112 前半 `83E…＋A8C…` 的 883 警告构建及窗口证据仍保留；原生构建均为正常 Debug，没有使用 Release 或替代应用运行库。
- 后半最终完整 Core VFX **2453 通过／0 失败／14 跳过，共 2467 项**，显式 DX11 离屏及官方 data 开关，见 `parent-validation/core-vfx-frustum-final-v1/result-summary.json`；14 项仍缺具名官方 poison-trail 原件及来源旁存记录，不计通过。其中 19 个视锥用例在此完整专项实际载入 `7E35…`，全部通过，见同目录 `frustum-in-full-suite.json`。R112 前半的 UI／播放器／选择／隐藏及架构 **128 通过／0 失败／8 跳过**、共享 Effect **55 通过／0 失败**是此前实际结果，本次没有重跑或与新专项相加；8 跳过仍为 6 个 GPU opt-in 未开及 2 个本地样本路径未配置。未跑整个解决方案、Release 或 CI。
- 新行为门禁保留真实红测及失败夹具：曲线重复计算 5 红→7 全绿；初份与继任者顺序 6 红→13 全绿，初次历史交接另 1 红→16 全绿；World2／严格 Force 缓存经纠正新夹具的历史协议后旧版 10 红／10 控制通过→20 全绿。另以完整同步 17 阶段范围对照验证并行调度；调度候选没有冒称新的行为红测。原夹具错误、编译失败和中间日志均保留，没有放宽物理或亮度断言。 普通面片视锥测试在旧 `A8C…` 真实红测为 **11 通过／8 失败**，修后 19 全绿，最终 `7E35…` 已在上述完整专项重新通过全部 19 项；原红测／中间构建和夹具失败仍保留在 `performance/sprite-frustum-culling-v1/`。
- 提交缓冲先在独立冻结的 `5C8B…` 上与初始 `A588…` 实跑 360 帧相同官方根／参数／RGBA 对照；这是本轮早期结果，不伪装成最终 Core 又重跑该套。曲线独立进程比较 4972 行浮点位／异常结果全同。随后材质模板在 `4EC3…` 上用完整吹管、铁龙及 DLC 烟雾、384²／1920×1080、实际 MSAA4 对比旧 `8510…` 与新 `A8C…`：180 个参数／RGBA 对、18 对 PNG、240 次绘制计数比较全同，15 个官方资源同来源同散列、无缺失。
- Force 缓存及最终调度分别用两份完整官方烟雾、两种根条件、首轮及两次回绕的 **48 个相同状态**核对 CPU 出生／随机／位置速度、提交包、RGBA、PNG、诊断和时钟，全部相同；三路执行 144 帧并不等于 144 个独立状态。最终调度另 48 状态与 F090 同步基线全同。显式载入 Core／native、实际 RTX 5070 Laptop／MSAA4 及官方 manifest 均记录，不能用此证明 Terry／游戏视觉已经等价。

- 后半在同一 `83E…` Core、相同官方原件／相位／相机／画质／MSAA4 下，对旧 `A8C…` 与最终 `7E35…` 取得 **270 个唯一同状态对**（正式近景三类 180＋默认全览负控 90），参数包／粒子数／RGBA 全同，**27 对 PNG 精确相同**；不能再把旧 3BC 候选重复相加。来源、缺失为空、实际模块与完整结果见 `performance/sprite-frustum-v1/formal-paired-comparison-7E35-v1.json`、`autoframe-paired-comparison-7E35-v1.json`。

### 普通 Debug 性能：有改善及剩余

- 以下首次准备／循环数据属于 **R112 前半 `83E…＋A8C…` 冻结阶段**：三次中间及前半最终采样均为实际正常 Debug、完整官方 DLC 根 q0，内部稳定目标 876×750，没有并行构建／GPU 测试／WGC 录像干扰该受控采样。空白准备与可见区分别统计，不能用初载空场景约 300FPS 的 HUD 或全部回调平均宣称效果提速。
- 前半首份异步准备的等待由中间 `4EC…` 的 **10.398 秒**，经 `F090…＋A8C…` 降到 **5.616 秒**，前半冻结 `83E…＋A8C…` 为 **3.099 秒**。该阶段可见区 1427 回调 Render 均值 **15.411ms／p95 16.462ms／p99 17.361ms／最大 20.071ms**；完整回调均值 16.409ms／p95 17.692ms，最大回调间隔仍有 **42.067ms**。30 秒内实测三次 8 秒循环，边界 Render 约 15.388／15.071／14.859ms，没有原多秒峰；这些不是后半 `7E35…` 的新复测数字。
- 前半并行首载相较 F090 缩短等待约 44.8%，但稳态均值／p95 略慢约 1.2%／1.9%，不能说并行又提高了稳态 FPS；初次 Reload 仍约 **209ms**。该采样截止时另有 1 个 Render 正在执行，仅观察 9.324ms，最终耗时未知。保留首次准备收益及尾部限制，不宣称所有复杂组合完全稳定。
- 原生分段 A/B 仅模板缓存带来约 7%～10% 绘制提交均值收益，主要在 native EndScene；384² 新版出现一次更慢的最大帧，原始记录未隐藏。单 PID EventPipe 仅有托管栈，External 边界不能归为某个原生函数内部 CPU 时间。全部证据见 `performance/live-host-parallel-decision-v3.json`、`live-host-three-way-comparison-v2.json`、`official-counterfactual-phase-split/`。

- 后半 `7E35…` 近景烟雾的“提交＋同步读回等待”均值由 **384²：15.414→9.465ms（-38.6%）**、**1920×1080：18.946→15.060ms（-20.5%）**，同状态粒子数和像素保持；这是相机条件使整批位于屏幕外时的提交节省，不是所有场景 FPS 结论。默认全览负控保留原绘制数量；随后唯一 876×750、A8→7E35→A8 的 3×30 帧 ABA 对照，候选相对前后基线均值 **慢 0.093ms（+0.572%）**，尾部 p95 更差，见 `performance/sprite-frustum-v1/drift876-aba-comparison-v1.json`。冻结 A8 正式／全览基线早于本次 Terry，新 7E 正式采样时最小化 Terry 仍有 CPU 消耗；不能把时段差异隐藏后宣称全部提速。
- 后半普通窗口 host2 为实际 876×750，Render 均值约 **15.640ms／最大 98.439ms**；该时段有一次 CUA 截图且最小化 Terry 仍耗 CPU，均未证明是长帧原因。正常保存并退出 Terry 后，host3 实际目标变为 **667×631**，Render 均值 **15.697ms／p95 16.466ms／最大 150.528ms**。最大帧不在循环边界，原始记录没有该帧 GC／resize／DPI 重建，原因仍未知；两种目标尺寸不同，不能硬算窗口提速。见 `live/aecn-frustum-final-v1/host2-readonly-analysis-v2.json`、`host3-readonly-analysis-v1.json` 及原始邻域记录。

### 吹管录像、原生参考与待验边界

- **AECN 用户协助的真实连续拖动已经完成并审查，原“待用户拖动”已失效**：`live/aecn-q1-manual-triggered-v3/` 为 **20 秒／392 原 PNG／0 截图错误**，人工录像冻结版本为 Core `83E…`／native `A8C…`。全 392 帧已做颜色测量，亲看 13 张原图及派生导航图；371 帧检测到独立纹理尾迹，113 个按选框屏幕位移定义的转向样本均有尾迹，未见每次转向整体清空。**227～245 帧挂点／尾迹出视野，不能当作已停下清尾或渲染掉帧**；末段仍在移动，没有最后停 5 秒至完全归零的证据。完整边界见 `live/aecn-q1-manual-triggered-v3-visual-review-final-v1.json`。PNG 采样频率不等于渲染 FPS，该人工结果不冒充最终 7E35 又录了一次。
- Terry 的用户真实连续拖动保留在 `live/terry-q1-manual-triggered-v2/`：等真实移动 49.017 秒后开始，完整 **20 秒／739 原 PNG／0 截图错误**。1.784～19.987 秒可见青绿色纹理长尾及反向移动，8.674～8.800 秒短暂停留可见缩短、变暗；末帧仍有尾迹，未录到最终归零。相机中途平移，轨迹、速度、出生相位及背景没有与 AECN 对齐，不能据颜色／宽度差异盲改材质。初次错过输入、早期未触发、等待超时及不完整截图记录全部保留。
- 官方 q1 来源为完整 `wh2_dlc17_weapon_trail_blowpipe_hero.xml` 及原始依赖；按原始 EXE／DLL 字节守卫读取 Terry 当前 raw2→XML q1，已知 loose 路径检查无同名覆盖。当前三维视图泛光复选框实际未勾选，WGC 原图含该设置。新参考场景正常保存后仍把 FOV45／near.01／far25000 回写为 **60／1／12000**；鲜 GPU 实际 VP 再证 FOV60°，AECN 普通相机仍为 45°，没有改全局相机，也不能把准备文件的 45°当实际生效。
- 本轮鲜原生捕获 `native-match/endpoint-v2/` **2 帧**及 `endpoint-v3/` **4 帧**均成功且保留源／校验副本，分别约 202MiB／405MiB，未重启或注入 v3。v2 的 0.1 单位端点在两帧均实际 draw/bin 为零；v3 同一 CUA 批次的三个 1 单位属性端点使前两帧实际选中 **5／3 个粒子状态**，两个 pass 共 **16 条 selected 记录全部回读**，所有 PostVS 顶点被剔除、RGBA 为零、实际目标前后无变化，后两帧归零。鲜测 S≈0.84787，已跨约 0.339／0.509 的原段长，不能继续只归因位移太小；未按旧跨时钟 age 推断剔除原因。这些只有相机／原始参数和阴性结果，**没有可见有效原生 quad，不能称为 Terry 严格对齐验收**。具名结果 `native-match/endpoint-v2/readback-v1/fresh-native-replay-validation-v1.json`、`endpoint-v3/readback-v1/fresh-positive-endpoint-replay-validation-v1.json` 保留完整 VP／视口、预算与原记录，不安排更多盲原生捕获。
- 烟雾前半历史录像 `live/aecn-final-siege-loops-v2/` 为 F090＋A8C 的 20 秒／192 PNG，不能把首图观察扩成完整视觉验收。后半最终 7E35 普通窗口 `live/aecn-frustum-smoke-final-v1/` 为 **20 秒／210 原 PNG／3 次播放器回绕**；已亲看 14 张原图，灰烬云持续运动，未见整片清空、大硬边或回绕平面硬截。该全览不覆盖所有镜头边缘／材质组合，见 `visual-review-final-v1.json`。另最终版 `live/aecn-frustum-smoke-close-v1/` 的 **20 秒／268 PNG／0 错误**已完成，亲看 0／80／160／267 四张原图，未见明显矩形截断、整层缺失或白色实体片，透明细点仍透出网格；见该目录 `visual-review-final-v1.json`。录制前滚轮上 600、期间上 360 是缩放操作，没有中键 orbit／连续绕转，不能扩称全镜头路径验收；捕获节奏均不作为 GUI FPS。
- Terry 当前试验场景已另存至 `E:\Steam\steamapps\common\Total War WARHAMMER III\assembly_kit\working_data\AssetEditor.CN\TerryPreview\r112-native-capture-preserved-20261027`，原 `r112-matched-camera-blowpipe-20261027` 保留；正常保存后退出确切 PID55420，已核进程退出、未改官方 VFX，见 `native-match/terry-preserved-and-normal-close-v1.json`。
- **最终 7E35 吹管窗口补测已完成限定正对照**：选择器筛选未更新的尝试保留，随后通过 `docs/asseteditor-ipc.md` 已有公共 `open` 正常入口请求打开完整官方吹管；客户端 5 秒超时是等待正常工具选择器的阶段，没有把超时写成 API 成功响应。CUA 选 VFX 后正常打开 q1，黄框及原两条引用保留，没有删源层或改原文件，见 `parent-validation/final-native-blowpipe-ipc-open-v1.json`；已找到正常入口，不据先前尝试确诊选择器产品缺陷。
- `live/aecn-frustum-blowpipe-endpoints-final-v1/` 为最终 7E35 的 **20 秒／394 原 PNG／0 错误**：亲看 0／170／172／174／177／180／393，记录中的三次端点跳变约 **8.543／8.656／8.763 秒**，170 及 172～179 有纹理尾迹；174 返向时旧尾仍在，177 已变窄、变淡，180（约 **9.076 秒**）无可见尾迹，393 末帧仍无。最后端点至已无尾迹约 **0.313 秒只是录像观察间隔**，不是新 Life、粒子年龄推算或产品寿命修改。原两条引用及源文件不变。此为端点跳变＋停止，**不是重新录了连续鼠标手拖**；旧 A8 的 392 帧人工记录仍独立，严格 Terry 对照、所有 trail 外观及连续手拖后的最终清尾未因此验收。见同目录 `visual-review-final-v1.json`。
- **补充长帧采样本次确已执行一次**：同 PID19984 的 host5、完整官方 DLC 烟雾 q0、实际 667×631，30 秒记录中可见 1516 回调，Render **均值 15.8003ms／p95 16.7845ms／p99 18.1084ms／最大 26.9601ms**；回调间隔均值 **17.7745ms／p95 19.1784ms／p99 21.0201ms／最大 29.4921ms**，初次 Reload 至完整提交约 **2.71331 秒**。新 EventPipe 实际 **20.00455 秒／20,639,306B／118868 样本／EventsLost=0／exit0**。collector manifest 在 host5＋20.532 秒、且早于连接／会话建立，只约 **9.46886 秒交集**，不能写 20 秒全部逐帧对齐；manifest 起点后的 527 个可见回调最大 21.5506ms。整份 host5 可见段无 >40ms，26.9601ms 最大帧还在 trace 之前，**未捕获旧 98／150ms 孤立峰，不能说已修好或归咎 native／驱动**。原 `host3-spike-path-readonly-v2.json` 的 Render 包含设目标、Update＋Draw 边界仍有效；新采样不拿旧聚合托管 profile 解释未录到的峰，也不重复盲采。具名结果为 `performance/eventpipe-frustum-isolated-v1/host5-overlap-and-no-long-frame-v1.json`。
- 自动／离屏、AECN 普通窗口、Terry 和游戏最终验收分别记录；不宣称全部 trail／烟雾已原生还原或所有场景流畅。全部原件、负例、备份及正常 Debug 保留，没有提交、推送或清理。

## R111 吹管距离轨迹、铁龙模型动画与预览性能（2026-10-27）

### 现场与共享实现

- 沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空。初始 91 个已有改动／未跟踪路径逐项 SHA 与 R110 最终快照一致；基线及原件小备份在 `artifacts/vfx-trail-model-performance-r111/`。没有新分支、worktree、暂存、收起、提交、推送、PR、合并或清理。全部旧源码、Debug、原件、备份、工具、失败与采样保留；全局 `1/120`、官方原 XML、原 Life／Alpha 和画质保持。
- 完整官方 `wh2_dlc17_weapon_trail_blowpipe_hero.xml` 的两层是普通速度朝向面片，不能按 rigid 模型或固定发射方向处理。原生 `6c2330→6c17e0→6c1a60` 距离 trail 分支表明：正 SpawnSpacing 选择入口，实际段长为原始速度×速度拉伸×初始 Y 尺寸首值×Y 尺寸首值×根缩放；方向取当前点到上次已消费点的弦。出生位置／时间插值，余弦及未消费时钟保留；停留后瞬移会产生过期出生，不刷新年龄或延长寿命。共享模拟已按该规则实现，普通非 trail 的距离发射保留原行为。只准入独立 World、Kind0、恒速／恒定 Y 尺寸、无力／阻尼／旋转／事件／地形及正均匀根缩放等已核组合，动态 Y、模型及复杂组合仍沿用旧候选路径，详见 `docs/vfx-editor.md`。
- 完整官方 `wh3_main_cth_iron_dragon_downward_strike_meshes` 的剑是空骨架 RMVv7；缺少的不是骨骼动画，而是动态 EmitterPosition／Orientation 及 OnSpawn 子图。新增 `VfxModelNativeMotion` 按原生 FullLocal 当前／前次发射器变换运输已有粒子位置和速度；128 点位置曲线与既有原生四元数采样并行，子特效挂到实际模型当前位置／姿态。去掉“有事件即禁用原生朝向”的过宽条件。准入严格限制单位根／继承 EventScale，非单位缩放保持旧候选结果并给 `EmitterPosition/RootScale/InheritedScale` 诊断。普通模型自旋、Crowman、Bear、Pox 和既有阻尼入口没有改套面片算法。
- `Bridge.cpp` 只复用批内 MaterialRenderData 容量，并在完整校验后预留各阶段包容量；shader、ABI、顺序、合成及旧错误隔离不变。另查明 CachedTabControl 隐藏场景仍 Update／Draw／Blit／等待 GPU：新增默认关闭的 `D3D11Host.SuspendWhenHidden`，仅 VFX 和 SuperView 显式启用，隐藏时在资源重建和渲染前返回，恢复首帧 elapsed=0，保留资源／选择／时间／文档。Kitbash 没有启用或改动；原 resize／DPI／front-buffer 重建条件保留。绝对 TotalGameTime 材质时钟不冻结，不扩大为全局着色器暂停契约。

### 本次构建、自动与离屏结果

- 正常 native Debug 构建退出 0；正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 最终增量退出 0、98 个现有警告、0 错误（本轮此前首次 App 构建为 1901 警告）。最终正常 Core=`A58831BECA9061660227AB63DD45E7B610676E2B01BE579D251B28EFD3D33BA2`，Meta=`D200A81A72F38AC1AA39646C975C1AF83C6BECC3EAE523B18CF0E29DCD24A78B`，VFX=`BA568671E7A1D8E3CB83DE60AFB44CB3FDEE4441244EC6A61C85E5681FE7CB89`，native=`8510B7D4548CC563F65B05CE51B0D53965B88D64F0AEA5A60DEBC6425A97F126`。
- 模型／trail 冻结 Core `7FE089FCFB81AE0C05C94F2EECECDCF430C8FE642F0ECE2F47ADC629B8385CBC` 上完整 Core VFX **2383 通过／0 失败／14 跳过**，共享 Effect **55 通过／0 失败／0 跳过**，命令退出均 0，见 `parent-validation/frozen-v2-regression-summary.json`。14 项缺指定原版 poison-trail 导出及旁存来源 manifest，不计通过。随后只增加 Host 调度及两处 opt-in，模拟／转换／native 源码不变，复用该套语义结果，不写成 A588 又重跑全量。最终 Host／VFX UI／输入／播放器／Kitbash 隔离 **122 通过／0 失败／8 跳过**（含新 Host 9 项）；Meta 相关 **153 通过／0 失败／0 跳过**。8 跳过为 6 个 GPU marker opt-in 未开及 2 个本地样本路径未配置。各套与早先专项有重叠，不相加；未跑整个解决方案、Release 或 CI。
- 新模型 14 项、距离 trail 16 项均已先红后绿；模型非单位缩放守卫也有 4 项独立红测。trail 覆盖转向、停留、回放、普通距离发射隔离、动态尺寸退回、容量 1 的过期批后再次连续可见及 1.006 秒停止边界，保留严格 Life／Alpha／后续随机字段断言。原生 DLL 新核 SHA=`C75D40A30CE938958F445AE59EB256E4C23F51201984D1F3EA5CB25319F763B8`；24 组 SSE FullLocal 非交换变换控制最大位置误差约 6.9e-6、速度约 1.75e-6，见 `model/native-position-transport-oracle-v1.json`。
- 原生提交旧／新 DLL 对照使用同一完整官方吹管、铁龙和攻城烟雾、q0、384²／1920×1080、实际 4×MSAA：180 帧包／粒子／RGBA 完全一致，18 对 PNG 字节一致，无缺依赖。1920×1080 CPU 提交均值分别 **1.925→1.672、4.276→3.539、22.180→15.753ms**。GPU 加 staging 等待单列，不能当纯 GPU 时间或 GUI FPS。该对照 Core B971 尚是旧 trail producer，不能用它证明旧轨迹分布正确；最终 7FE0 吹管另补 60 帧／6 对 PNG 的旧新 DLL 相等对照。最终语义段长改变造成 166～168 粒，相比旧 222～224 是原生分段修正，不是性能减粒子。见 `performance/counterfactual-comparison-msaa4-v4.json`、`counterfactual-comparison-blowpipe-final-msaa4-v5.json`。零 MSAA 中间版本与失败样本均留存，不并入最终统计。

### 普通窗口与 Terry：已看见和未完成的边界

- 本轮确实操作正常 Debug 与 Terry；没有使用 Mod 特效或依赖。最终普通 Debug PID38792、正常 EXE 路径和载入模块 SHA 已核实，只读 All Game Packs，无 Mod 工程。完整官方铁龙在最终 A588 观察到 0.40 秒横向展开、1.47 秒改变姿态、2.34／2.35 秒转下劈并有跟随子特效、9 秒自然结束；同一默认相机／q0，没有编辑原件。窗口缩小再最大化后保留暂停 1.47 秒场景，最终留在 2.34 秒。早先 B971 的完整播放也观察到 1.51→3.72 秒下降，按中间版本另记，不伪装成最终复测。
- 最终攻城烟雾窗口实际观察灰烟／火星动态和暂停控制，HUD 约 49～56FPS 仅是窗口观察，不是受控帧率测试。保持官方预热与完整依赖，首次解析／上传仍有成本。四个缓存 VFX 标签中，吹管连续时间不再被 8 秒重置；从 168.87 秒截图后切走，隐藏墙钟 24.145 秒再返回为 176.73 秒（前次截图至点击之间还有可见运行时间），没有把隐藏 24 秒追赶进模拟，选择标记和预览变换保留。先前烟雾 2.97→8.00 秒的短周期切页有操作延迟歧义，不作暂停成功／失败证明。
- 最终吹管确实在窗口中选中并多次往返移动，连续播放超过 8 秒、选择／转向操作正常；但 Sky 停止拖动后才返回截图，原 .19／.36 秒寿命内的轨迹动态未被稳定捕获，**不登记持续拖动外观通过**。离屏完整原件运动帧已实际查看：修前鱼骨状分布变为顺运动的连续绿色段；这证明当前渲染结果，不冒充同阶段 Terry 或游戏最终外观。原生长时间静止后的过期出生不意味着永久不可见；后续连续跨段推进时钟，容量 1 专项已证明再次可见。正常鼠标持续拖动的窗口外观仍应补齐。
- Terry 本轮新场景载入未经修改的完整吹管，位置 512/0/512、原旋转和缩放；Bloom 实际未勾选，图形质量只读 raw2→XML q1 连续两次稳定，camera24°/26°／距24，见 `terry-trail/terry-runtime-quality-v1.json`。v1 只框选没有实际位移，明确是失败；v2 actor X512→520 捕获 frame48115 中 38 条实际被选绘制记录，方向、段长与当前原生指令吻合。该批经过长 idle，全部年龄超原寿命并被 PostVS 裁掉，**不能声称可见 trail 同阶段视觉对照通过**。原生场景诊断副本只保存 actor 移动，VFX 原 XML／贴图不改，Terry 正常关闭，无强退或丢弃用户内容。
- 当前只证明上述样本和受限组合；复杂 trail、非单位模型缩放、全部材质／透明合成、天气与完整游戏后处理仍未全覆盖。未做最终 SuperView 普通窗口 Effect 回放、真实 DPI 切换或设备丢失测试；这部分只有对应自动门禁及既有结果，不伪装成新窗口验收。

### 留存、文档与下一步

- 既有 `docs/vfx-editor.md`、`docs/architecture.md`、`docs/superview.md` 已同步稳定契约；本轮不改 AGENTS，不新增流程文件。最终 Host patch 为 `performance/hidden-page-fix/product-diff-v2.patch`，保留 v1 旧证据而不当最终源码。
- 新产物全部保留；盘点时约 0.744GiB，其中四份独立 RDC 的旁存重复副本约 333MiB 仅列为可再生产物，没有删除、搬盘或打包；原件／独有捕获／TRX／运行库备份与 Debug 保留。最终 GUI 与工作区记录为 `parent-validation/normal-debug-gui-final-v1.json`、`workspace-after.json`；原 91 路径均存在，非本轮路径逐项 SHA 核对，分支／HEAD／暂存边界不变。
- 下一步优先补正常连续拖动与同阶段 Terry 可见轨迹对照，再根据实际异常扩展共用参数组合；不要再从“有像素”、HUD、测试数或原生过期 teleport 记录推出全部视觉正确，也不要为得到截图调长寿命。

## R110 首次冷启动卡顿与妙影贴花出生扇区（2026-10-27）

### 现场、原因与共享修复

- 原地沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空；初始87个已有改动／未跟踪路径逐项SHA与R109最终快照一致。基线、受影响原件和正常运行库小备份在 `artifacts/vfx-cold-start-direction-r110/`；没有切分支、worktree、暂存、收起、回滚、提交、推送、PR、合并或清理。保留全部旧成果、失败、工具和正常Debug，全局 `1/120`、原粒子数量／寿命／画质不变。
- 冷启动原3.94秒主要落在 `BeginScene→FloatSceneTarget::Prepare`，该调用同步 `D3DCompile` 八份场景着色器；隔离CPU原源码／原flags编译仍累计约4477ms，证明不能归咎于GPU粒子量。新 `FloatSceneShaderSources.h` 是单一HLSL来源，`CompileSceneShaders.cpp` 在正常native构建时沿用原入口、profile、flags=0编译生成头；CMake显式依赖源码、生成器、生成头和桥。运行时只从内置字节码创建DX11 shader，无预热、外置缓存、减画质或把耗时搬到资源Reload。八份完整bytecode与原运行期编译结果逐字节完全相同，见 `cold/bytecode-equivalence-v1.json`、`cold/normal-build-bytecode-equivalence-v1.json`。
- R109 径向速度规则本身有原生依据，但出生区域仍整圈均匀铺开，令速度跟着错误出生点向四周散射。重新核实官方 `warscape.modder.x64.dll`（SHA仍 `C75D40A30CE938958F445AE59EB256E4C23F51201984D1F3EA5CB25319F763B8`），实际隔离执行原 `6c0380` 出生函数、`6c08f0` false方向函数及 `6ca8d0` 角度函数。`SpawnAngle=a` 为仰角、`SpawnArc=b` 为方位，出生偏移 `r*(cos(a)cos(b),sin(a),-cos(a)sin(b))`，r线性取原上下界；出生位置、速度方向与朝向法线独立。证据 `native/native-spawn-contract-fresh-v1.json`、`native/native-angle-helper-samples-v2.json`、`native/native-birth-position-samples-v1.json`。
- 共享转换／模型／模拟增加独立 `SpawnAreaAngle/SpawnAreaArc`。只在已有受限径向入口且显式SpawnArc，或严格限定的 `continuous_mesh + DISTORTION` 字面false、世界／仅位置局部、恒定发射器位置、零发射器旋转／出生盒、非负半径、无间距／继承速度／表面法线／事件、上轴／缺省出生方向时应用；数字0、无Arc、模型、FullLocal及其他组合不扩展。保留此前随机数调用数量、后续Life／Alpha／尺寸／旋转随机字段；不宣称原生种子或完整随机调用序列已一致。
- 原版arc90 white的Angle缺省0、Arc±5°、半径.5，因此局部+X十度窄扇区；distortion出生(.5,.1,0)，其中Y=.1来自原EmitterPosition，既有+X速度50及Life=.8保持。未按名字打补丁、钳制Y、旋转整组、修改原XML/META、拉长寿命或调亮。

### 本次重新执行的构建、自动与离屏验证

- 正常native Debug增量构建退出0；正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 的最终 `parent-validation/app-build-v2.log` 退出0，1901警告／0错误。CoreTest和MetaTest均按Debug重新构建。最终正常Core=`3EA03CEAFED5694028605B7B474D160A8F3BF9C60AFA9820980414A200FCBD09`，Meta=`6A3BDD7ABD3373CE7007AADDF9CE02486C30CE5A26368A1EF393F23D7277F864`，Editors.Shared.Core=`4FA0EE6877D640A437E46724FA61791BE95A54266E2D8E8E6695BBB69D3992C4`，native=`20BF86970ED0F0F15AF1DB3015613E809DF22B1707E12F894697D970FCF90E7B`，见 `parent-validation/runtime-v2.json`。
- 最终完整Core VFX **2353通过／0失败／14跳过，共2367项**，`core-vfx-final-v2/core-vfx-final.trx`；最终共享Effect **55通过／0失败／0跳过**，`meta-effects-final-v1/meta-effects-final.trx`，命令退出均0。14跳过仍缺指定官方poison-trail导出及来源夹具，不能计通过。使用显式headless与实际官方data路径；没有执行整个解决方案、Release或CI。专项结果是全量子集，不重复相加。
- 新出生区域专项先在旧正常Core跑出11失败／1数字0控制通过；distortion额外两项在中间Core仍失败，修后通过。完整首跑一项失败是新测试误将原EmitterPosition.Y=.1写成0，按官方XML修正预期为(.5,.1,0)，原1e-6容差与速度／寿命严格断言保持；对应红测、首跑TRX、日志均保留，没有为过测放宽断言。
- 同源性能对照 `cold/cold-and-pixel-comparison-v1.json`：两个独立冷进程使用相同完整官方Storm2 META／clip／骨架／依赖、384×384、同画质／视角／30Hz输入，只更换实际载入native模块路径，未交换正常App文件。274帧时钟、粒子数及PNG像素SHA全部相同；首次Begin **5622.3141→1.7865ms**、Draw **5655.1155→32.9724ms**，readback单列。最大Reload **1194.7451→1214.3321ms**、总Reload **1666.5467→1651.7953ms**，没有把编译卡顿转移过去。该对照Core为同轮中间B9FF版本，最终额外修正distortion出生位置不涉及native着色器；不冒称最终Core又完整跑了相同counterfactual。资源首次解析／解码／上传仍可能约.4～1.2秒，不宣称冷启动零延迟或这些毫秒就是GUI实际FPS。
- 最终完整官方妙影human5四圈离屏 `direction/human-direction-final-v2.json`：原META／3.10秒clip／骨架／原件SHA及字段不变，24阶段Birth／Life／数量、Alpha多重集和后续随机字段与R109相等；20独立模拟只用五套资源。white实际出生角[-4.62850°,4.86955°]，速度方向误差<3.81e-8；distortion原位置／速度／寿命严格通过。沿用R109窄相机时部分正确前方运动离开画面，低像素不作视觉成败判据，实际Debug另拉远覆盖完整轨迹。

### 本次普通Debug与实际原生参考

- 最终正常Debug PID12292、正常输出精确路径核实。唯一只读All Game Packs、无Mod工程；正常UI载入原 `cth_storm_dragon_human`、`humanoid01e`、`hu1e_mage_storm_dragon.bin/JUMPING_ATTACK_1` 与原五项Effect。多圈实际播放看到起跳、落地、充能和两道有方向贴花；暂停检查1.61／2.05秒，均为按各自META朝向展开的扇区，未再整圈四散。关闭循环看到3.10秒模型末帧、原短寿命地面效果已消退，未写源文件；相机只拉远，不以不同视角比较像素。见工具窗口截图及 `parent-validation/normal-debug-gui-final-v1.json`。这是普通窗口行为观察，不代替独立帧计时或全部原生外观验收。
- Terry PID25868保留，未强退或丢弃用户内容。先前Prefab没有真实接收地面，不能作为贴花视觉证据；本次通过Terry UI新建1×1 Battle Tile，实际Height全0，原地形文件备份并复制到独立receiver，原VFX资源不改。Bloom实际未勾选，原生当前图形质量只读核实raw2→XML quality1，文件 `native/terry-runtime-quality-read-v1.json`；保存High标签不单独作为实际画质证明。
- Autoplay属性重输不能可靠重播已结束实例，v2/v3捕获实际draw0，均保留为失败；选择后CtrlC/CtrlV新增同位置、原rot0/scale1的完整官方arc90，v4实际捕获成功。`native/capture-v4/terry_frame216063.rdc`、`native/arc90-readback-v4/readback/clock-camera.json` 及 `native/arc90-export-v4/images/event-2108-tone-viewport.png`。实际查看原生白弧在真实地面向前形成窄扇区；GPU出生／速度角[-4.48654°,3.30568°]、white Y=0、distortion沿+X且Y为原.1×根缩放，原寿命范围保持。实际阶段约.1399536135秒、相机24°/26°、距离18、FOV60°、有效viewport360×655均从GPU读回核对，不以触发延迟或预设相机冒充对齐。原生第一大步另有七条预测出生记录，不用于阶段／固定步长粒子数比较。保存诊断场景有两个同位actor重叠提示，属于此次原样重播副本，未部署或构建游戏内容；源场景及失败全部保留。
- 同条件AECN补图已完成：`direction/native-camera-match/q1-stage014-v1/results.json` 和 `aecn-arc90-q1-native-camera-viewport.png`，完整官方arc90、q1、阶段.13995361，复用实际GPU相机与360×655视口，独立投影代数最大误差.000153像素。两图实际查看为同侧窄扇弧，不再整圈四散；AECN白4粒、原生白11粒（含七条预测），原引用随机尺度也不同，原生纹理更密、右端形状和亮度仍有差异，不能写成逐像素或完整原生外观通过。背景／天气未完全配平，故此对照用于形状方向，不能用其亮度指导调色。质量依据明确来自当前引擎只读raw2→XML1，最初误写UI证明的results小备份保留并更正。`Shared.Core.dll` 与 `Editors.Shared.Core.dll` 为两个不同程序集，散列分别记录，不误判漂移。
- 原生根引用自身有.90～1.00随机缩放，本次实际.99810177；不能修改GUI尺度或源字段来凑画面。照明、首次大步、随机种子、预测粒子及完整游戏后处理仍有差异；方向/出生规则的当前证据不等于完整形状／透明合成／游戏最终显示逐像素一致。

### 留存与后续边界

- 实现、自动／离屏及上述窗口验收完成后，最后尝试恢复正常Debug窗口时，Computer Use明确返回 `user input was detected in this window`；立即停止桌面输入，没有按工具提示重试或抢焦点。当前只维护文档及只读Git核对，未关闭用户程序。见 `parent-validation/desktop-input-stop-final-v1.json`；此前实际观察不伪装成冲突后重新验证。随后用户明确恢复桌面授权，重新绑定Debug窗口；缓存坐标导致一次文件树分隔条误拖，已恢复约350px原宽度。最终仍为原件JUMPING_ATTACK_1、暂停0.00秒，定位尝试没有成功，不写成已留在1.6秒或新增视觉验收；原XML/META未保存。续记 `parent-validation/desktop-resume-final-v1.json`。
- 本次证据根 `artifacts/vfx-cold-start-direction-r110/`，包含原件／运行库小备份、源码与函数证据、失败、TRX、工具和有界单帧捕获；没有打包搬盘或删除。长期稳定契约更新既有 `docs/vfx-editor.md`，没有新增设计／流程文件或改AGENTS。最终 `parent-validation/workspace-after.json` 核对初始87项中仅7项本轮相关文件变化、80项SHA不变，新增进入改动清单4个路径（其中CMake为原跟踪文件，另三份为新源码／测试），共91项；分支／HEAD不变、暂存为空。产物盘点见 `parent-validation/artifact-inventory-v1.json`，全部保留，未执行固定范围清理。
- 保留R108/R109已验证的循环资源复用、自然尾段、有限根停止和既有原生旋转。模型FullLocal、材质／透明合成、全部trail／烟雾、无限事件子图和龙形另一owner-stop语义不在本轮修复结论中。首次资源上传成本仍是独立性能边界；不要再把本轮的同步着色器编译停顿与资源加载混为一项。

## R109 妙影人形跳跃贴花与循环加载卡顿（2026-10-27）

### 现场与本轮修复

- 原地沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，接手 84 个已有改动／未跟踪路径，逐项散列核对 R108 `workspace-after.json` 一致，暂存为空。本轮基线、受影响源码及旧正常运行库小备份位于 `artifacts/vfx-superview-jump-perf-r109/parent-validation/`；新增三份测试文件，其余已有成果保留。无分支／worktree、暂存、收起、提交、推送、PR、合并或清理。
- 用户实际场景是 `cth_storm_dragon_human.variantmeshdefinition`、`humanoid01e`、`hu1e_mage_storm_dragon.bin` 的 `JUMPING_ATTACK_1`，完整原动画 3.10 秒及 `hu1e_storm_dragon_jump_attack_01.anm.meta` 原五项 Effect，不能误用龙形同名动作代替。META／clip／骨架来自当前官方 `anim2.pack`，全部 VFX／模型／DDS 按 manifest 核实官方来源，没有 Mod 覆盖。
- 两条 arc90 的 `Arc_white` 是 `continuous_mesh + DEFERRED_DECAL`，原粒子寿命约 0.4～0.5 秒。修前在 1.60／2.05 秒仍有活粒子、有效 Alpha 与绘制包，却因为错误的向上初速度使投影体离开地面。原 `SpreadMethod="false"` 的径向入口只接普通面片，漏掉该贴花和动态速度组合；修复按原生出生点减发射器中心求径向，速度曲线仍每次出生采样，法线独立。不钉死 Y、不改 Alpha／Life／META／原 XML，不把贴花延长到整段动画结束。受限条件与未标定 SpawnArc 分布见 `docs/vfx-editor.md`。
- 原生方向依据是既有 R104 的 false 分支和本轮核对的出生调用链：`6c0b82..6c0c4f` 位移归一化，零保持零；`6c2080` 每次出生取得速度标量并写入独立速度，不能把 Up 法线当速度。当前官方 `warscape.modder.x64.dll` 新核 SHA256 仍为 `C75D40A30CE938958F445AE59EB256E4C23F51201984D1F3EA5CB25319F763B8`。这是当前原生二进制身份与历史指令证据的复核，本轮没有新的 Terry 同条件视觉通过。
- 卡顿实测来自每轮／定位重新转换、DDS 解码和资源上传。`IVfxPreview.CreatePlaybackInstance` 与 `EffekseerVfxPreview.PlaybackResources` 复用完成的只读转换和上传结果；各播放代保持独立模拟、时钟、发射截止、诊断，最后一个资源使用者释放 native handle。`VfxPreviewSceneNode` 自然循环及定位复用资源，源／依赖失效仍明确重载并丢弃过期尾段；保留循环边界待处理的失效，空尾段不能阻止有效实例开启共享合成。没有按特效名建立全局缓存，也没有改全局 `1/120`、画质或粒子数量。
- 对照抓到本轮中间实现的真实问题：新 clone 首次显隐查询调用 `IsDrained`，在真实骨骼挂点和历史传入前以单位矩阵初始化，Storm 第二圈有 46 帧相差 1～2 粒。新增节点门禁先跑出 2 失败、1 通过，再用 `_currentPrepared` 保证首次真实 Seek 后才查询排空；修后两组完整原件每帧粒子与旧基线均完全一致。Persistent 非零 Start 的无限点触发跨轮也不再在 0..Start 闪隐；有限与非 Persistent 开始边界不变。

### 本次最终验证

- 正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 最终 `app-build-v4.log` 退出 0，1901 警告、0 错误。两测试项目重新构建后执行；最终 Core `882F965ECB00DFB24DDF445F886B6690157521D17CF1650BD91F2D9144148B26`、Meta `45D7FF06804E907FEC3A961AA2CF107646ABAEAFE2455F0003801B0CE51FC7E5`、Shared `7E55251CE2AF24133AB43408BADB2355C3EED6D1BA67FA28B74056CD34C396C5`。native `A816B5EDAA773FADB7BB32C38C3BF84464B713FAC11BE4D44C71C2CFD7787E36` 未改；完整散列见 `parent-validation/runtime-final-v4.json`。
- **最终 Core VFX：2339 通过、0 失败、14 跳过／2353 项**，`core-vfx-final-v2/core-vfx-final.trx`；14 项仍缺少既有带来源证明的官方 poison-trail 导出夹具，未计通过。**最终共享 Effect：55 通过、0 失败、0 跳过**，`meta-effects-final-v3/meta-effects-final.trx`。GPU 环境显式启用 headless，只读官方游戏 data；未跑整个解决方案、Release、CI。新资源／径向／首次挂点及六项共享 handle 像素检查均包含其中，不重复相加。R108 其他播放器／动画结果仍属历史复用，不伪装成本次重跑。
- 初次筛选名无匹配、新循环资源测试未提供 owner 真实 clip 时长的夹具错误、独立工具构建把备份 `.cs` 纳入默认编译等失败日志全部保留。资源测试提供真实一秒 clip 后仍严格断言原 `.5` 截止及新旧独立时钟；没有放宽断言。human v1 观察器调用 Frame／IsDrained 也会提前初始化待播放 clone，已在独立工具修正；正式 v3 只观察已初始化实例，并按出生身份／Alpha 多重集核对，不把空间变化导致的深度排序当作 Alpha 改变。
- **同源性能最终对照**：完整 Bear13 的 705 帧、Storm2 的 274 帧，其每个播放时钟和每帧粒子数与旧正常 Debug 基线完全相等。后续循环 Reload 帧分别 24→0、5→0；Prepare 峰值 497.1848→3.0317 ms、775.5544→1.2235 ms；单帧分配峰值 291,188,856→2,114,856 字节、640,987,648→697,232 字节。52／8 个独立播放实例分别只需最多 13／2 套真实只读资源；实际 Reload 总数 39→13、7→2，仅首次加载。相同 384×384、画质、相机、原件、30Hz 播放输入，计时段没有并行 GPU 测试或 GUI 播放；读回等待单列，不把 CPU Prepare 当实际 FPS。见 `performance/after-comparison-v2.json`。初次冷 draw 仍约四秒，尚未标定其具体驱动／材质初始化占比，本轮未消除。
- **完整官方 human5 最终离屏**：`official/human-final-acceptance-v3.json`，24 个同相位阶段覆盖四圈。原 META／clip／骨架／依赖散列、META 字段、Life／Birth／MotionOrigin 与原 Alpha 保留；四圈出生数据完全一致。第一 arc 在 1.45／1.60／1.75 秒分别 4151／21831／22216 像素，第二在 1.90／2.05／2.20 为 5947／6766／0，四圈同阶段完全相同；修前 1.60／2.05 均为零。Y 保留原 META `-.3203122`，XZ 按原径向速度运动；2.20 秒按原曲线淡出，不强行维持可见。20 个独立模拟分属五项 Effect，每项四代只复用一套资源／转换／native handle。完整原始包、参数和阶段图在 `official/human-final-validation/human-jump-final-v3/`。像素数仅是丢失／复现核对，未冒充游戏外观等价。
- **最终普通 Debug 实机**：PID 47372，准确路径为本仓库正常 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。唯一只读 All Game Packs、无 Mod 工程；通过正常 UI 载入上述人形模型／骨架／战斗 bin／JUMPING_ATTACK_1，原五条全部保留，Persistent 原为空。实际多轮播放看到原起跳／落地、烟雾与手部充能，定位 1.60 和 2.05 秒均有贴地扩散；正常播放 1.73 秒仍有第一道贴花，2.75 秒已退场，跨轮恢复新动作与充能。关闭循环后模型保持 3.10 秒末帧、地面效果已自然结束；暂停与定位正常，未见此前周期性加载停顿。显示 FPS 仅作为窗口观察，不代替上述帧计时。只拉远相机覆盖完整轨迹，不拿不同视角作像素成功比较。最终留在暂停约 1.59 秒、循环选项恢复；未保存源文件、强退程序或发生用户输入冲突，Terry 原窗口保留未操作。窗口记录在工具截图及 `parent-validation/normal-debug-gui-final-v1.json`。

### 留存与边界

- 稳定契约已补入既有 `docs/superview.md`、`docs/architecture.md`、`docs/vfx-editor.md`；本轮日志、源件、备份、失败、工具与小型阶段图全部保留，没有清理或搬盘。最终工作区盘点见 `parent-validation/workspace-after.json`；初始 84 项中仅更新本轮相关的 10 项源码／测试／文档，其余 74 项散列不变，另新增三份专项测试，暂存为空。
- 本次修好人形妙影这组贴花离地及重复加载问题，未完成全部贴花、SpawnArc 出生分布、全局透明合成、全部 trail／烟雾或游戏最终视觉验收。首次冷加载约四秒仍待修；R108 无限事件子层停止及 600 秒定位边界保留。
- 另发现龙形官方 storm jumping 的原片段合法引用 iron clip，不能按文件名前缀判错；其另一短范围 rock impact 的迟发根层可能被 META 截止，属于不同的 owner Stop 语义，尚未获得足够原生证据，本轮未改。不要将它当成人形原五条复测已通过，也不要延长 META End 掩盖。

## R108 超级视图 Effect／地面贴花的停止发射与自然尾段（2026-10-27）

### 现场及共享修复

- 原地沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，接手 75 个已有改动／未跟踪路径，暂存为空。基线和受影响源码／正常运行库小备份位于 `artifacts/vfx-meta-tail-fix-r108/parent-validation/`；保留全部旧成果与失败记录，无分支／worktree、暂存、提交、推送、PR、合并或清理。全局 `1/120`、原粒子寿命与原始资源参数不变。
- 前一轮 R107 为只读诊断：普通范围的 META End 隐藏整项，地面贴花随父节点一同消失；主动画循环回零后低时间 Seek 又清掉旧模拟。完整官方 Bear13 的两条贴花在 4.60 秒及 7.49 秒仍能实际绘制，却被父显隐门控隐藏，证据在 `artifacts/vfx-meta-tail-r107/compositing/decal-tail-audit-v1.json`。这些是修前证据，不算 R108 修后通过。
- `IVfxPreview`／模拟新增实例局部 `EmissionEndTimeSeconds` 和真实 `IsDrained`。范围 META 在 End-Start 截止根发射；只裁剪跨 End 的最后部分 Tick，保留完整 Tick 原有累计规则，出生时刻／出生挂点不超 End。既有普通粒子、模型、贴花继续原 Life、Alpha、图集和已触发事件链；根 MeshLoop 在截止后沿原有限停止规则退出，不改 Life。默认 null 仍走原播放路径。
- `AnimationPlayer.PlaybackTime` 提供片段时间、连续时间、修订与循环号。超级视图显式启用末帧后的尾段时钟，`SceneObject` 仅向可选的 `IMetaDataPlaybackInstance` 分发；其他元数据与普通未启用的播放器保留原逻辑。真实共享 VM 自然循环走去重的 `RestartForLoop`，不再用用户定位命令模拟循环。用户定位、回第一帧、停止或换动画明确重置；暂停冻结并可恢复尾段。
- `EffectMetaInstance` 不再按 META End 或 `CycleDuration` 隐藏活粒子。共享节点保存上一轮独立运行时，新一轮重新触发，旧轮在实际排空后释放；范围 End 与轮次 End 取较早截止，不恢复已停止的根发射。出生历史用当前片段姿态恢复根变换，再按各轮原动画采样，避免末帧／回绕时把旧世界空间粒子搬回新起点。Persistent 点触发中实际无限根继续同一实例，不强行每轮重建；历史按实际循环起点换算，不能直接对片段长度取余，因为主模型会丢弃循环越界时间而连续时钟保留该时间。

### 本次重新执行的验证

- 正常主程序 Debug 构建 `app-build-v2.log` 退出 0，1881 警告、0 错误；最终挂点修正后的增量 `app-build-v3.log` 退出 0，96 警告、0 错误。本轮未改原生源，没有无故重编原生桥。受影响测试项目全部重新构建。最终正常 Core 为 `EC7FBC640B072FF7161F590FFB54139E623A8C199B6F2C3D7EFA7C93222056BD`；Shared 为 `7C1881FC48BB21658903FA800E5B9ECDCB9C3FCBB7AF25AC3387A307227FF293`；Meta 为 `7EC68D09ABE69D1A8B4BCE4116E7BD6D7E70E8D4B77A73312A89695AF3A68E2D`。native 仍为 `A816B5EDAA773FADB7BB32C38C3BF84464B713FAC11BE4D44C71C2CFD7787E36`。
- **完整 Core VFX：2315 通过、0 失败、14 跳过／共 2329 项**，`core-vfx-final-v1.trx`。14 项均缺少要求带 manifest 来源证明的既有官方 poison-trail 导出原件，不计为通过。新发射截止专项 24／24、相关既有 CPU 78／78 是全量子集，不相加。**共享 Effect 最终 48／48**，`meta-effects-final-v4/`；**共享播放器 16／16**，`shared-playback-v2/`；**Core 动画／骨架解析 35／35**，`animation-core-v1/`。最终只再改 Meta 的持续历史采样，Core／Shared 散列不变，复用本轮对应通过，不冒称再次全量执行。未执行整个解决方案全量、Release 或 CI。
- 新专项先对旧正常 Core 稳定复现；首个实现的一处完整 Tick 重算舍入导致 End 前少发一粒，已修为仅裁剪最后部分 Tick。两处测试准备错误分别是沿用了已确认归零的 OnDeath 延迟、240/s 样本最后不足一粒却要求出现 End 后子粒子；按原生延迟规则和独立计数公式纠正样本，最终 480/s、End=.253 精确为 122 根粒子，保留严格 Birth／Root／事件 Start 断言。旧四项宿主断言要求八秒预估到点隐藏，与本次需求冲突，改为明确测试运行时仍活应显示、实际排空才隐藏。原失败及原因在 `emission-audit-v1.json`、对应旧 TRX／日志中，没有改原生夹具或放宽像素／时间容差。
- 最终编码检查只为新 Core 测试补齐 `.editorconfig` 要求的 UTF-8 BOM，内容语义与运行源码不变；重新构建 CoreTest 并再次执行 24 项全部通过，见 `emission-tail-final-encoding/`。完整 2315 项结果复用本轮同一 Core／native、同语义测试，不伪装成编码整理后的另一次全量。
- 完整官方离屏：Bear ATTACK4 原 13 条与 StormDragon 攻击原 2 条，均加载原动画和真实骨架，通过实际 `Player.Update → SceneObject.Update`、正常可见场景树和共享合成绘制。各覆盖循环及非循环四跑：META End 后不再出生根粒子、旧尾段仍有实际画面；Bear 跨循环保留旧对象，原件真实首次新出生后与新代并存；关闭循环后模型分别保持 7.5／2.7 秒，特效继续自然排空。Bear／Storm 最终分别 13／2 项全部 `IsDrained=true`；空当前运行时保留到场景释放，不是历史代无限累积。见 `validation/official-lifecycle-summary-v1.json`。资源仅来自当前 manifest 官方 Pack，原 META 字节／字段未变，Missing 为空；没有强制打开隐藏父节点冒充验收。
- 独立工具最初一次 Moq 反射入口失败，第二次在原贴花尚未首发的 .1 秒过早要求新旧粒子并存，均保留并标为工具失败；修正为在原有效发射窗口内观察真实首次出生，不改产品或原件参数。首轮正式 Bear loop-v3／drain-v1、Storm loop-v1／drain-v1 通过；最终 Meta 再对 Bear loop-v4／drain-v2、Storm loop-v2／drain-v2 全部重跑通过，汇总 `validation/final-summary-v2.json`。这四组为非 Persistent 回归；持续分支另用旧 Meta DLL 稳定跑红：新循环真实 clip=0 时旧取模将出生挂点采到 X=2，最终严格映射至 X=0，并检查历史旧轮及越界空隙。该新项包含在最终 48 项中。本次离屏未绘制角色模型，没有匹配天气／Terry，不把非空像素或生命周期通过当作完整原生视觉等价。
- **普通 Debug 本次实际窗口观察**：首轮 PID 50412；最终挂点修正后正常关闭该只读实例，无保存提示，重新启动最终正常 App PID 38300。均为唯一只读 All Game Packs、无 Mod 工程。原 `ksl_elemental_bear.variantmeshdefinition`、`bear01` 骨架、`be1_wh3_elemental_bear.bin` 的 ATTACK_4，同时保留原 Animation／Persistent META。首轮循环实际观察到 6.80→下一轮 2.64→1.06 秒；最终 Meta 再观察到 3.14、跨轮 2.62／.76、4.47 秒，旧冰面仍在，持续烟雪跟随原身体／头部。最终关闭循环后再看到 4.01→7.50 秒保持末帧，冰面继续变为零散残片，再完全消退，Persistent 原身体烟雪继续原行为。暂停／回第一帧操作有效，未保存源文件或检测到输入冲突。仅拉远相机看全轮廓，不做不同视角的像素成功比较。最终实例留在暂停的 Bear 末帧页，没有关闭 Terry 或其他用户程序。Storm 的本次新验证为完整官方离屏，未补其角色窗口；没有本次 Terry／游戏最终画面验收。

### 仍保留的边界

- 未跟踪父寿命的无限事件子层，在 owner Stop 后如何停止发射尚无足够原生证据；现保留其原事件链、如实返回未排空，不按固定秒数或数量强制删尾。上述两个完整官方组合未覆盖该无限事件图，不能据其运行时数证明所有组合长期内存有界。普通 Seek 的 600 秒上限仍保留。
- R107 另外发现的父引用倍率与事件尾段周期估算不一致，本轮宿主已不依赖该估算清尾，但没有修改或宣称完成原生倍率全组合标定。R106 的其他材质、透明合成、ribbon 和性能边界继续保留。
- 本轮产物约 98 MiB，包含必要源码／旧运行库小备份、CPU／GPU 日志、独立工具与阶段图，全部保留，没有打包搬盘或清理。长期播放契约已更新现有 `docs/superview.md`、`docs/architecture.md`，没有新建流程文档。

## R106 共享问题批量修复（2026-10-26）

### 现场与共用实现

- 原地沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空。接手 60 个已有改动／未跟踪路径的散列与 R105 快照一致；本轮前后盘点位于 `artifacts/vfx-shared-batch-r106/parent-validation/`。没有切分支、创建 worktree、暂存、收起、回滚、提交、推送、PR、合并或清理，保留正常 Debug 和全部旧证据；全局 `1/120` 不变。
- `RenderEngineComponent` 的 Normal/Solid 路径将同场景 `VfxPreviewSceneNode` 经显式 `BeginScene/DrawInScene/EndScene` 分组：全组不透明接收体、各实例覆盖／发光、全组折射方向、全组透射率、一次折射解析及显示输出。修复逐实例曝光截断和各自折射复制背景的问题；实例身份、时间、挂点与模拟不合并。独立 `Draw` 保持兼容。入队复制粒子数据并保留运行时／深度资源，拒绝坏提交，未结束的客户端作用域销毁时取消；浮点中间目标捕获／解绑／恢复 RTV、DSV、UAV 及计数器。未宣称 MBOIT、全局透明排序、半分辨率或游戏完整透明合成已经实现。
- 九类使用 `mesh0`／`meshRender` 契约的滚动模型材质补齐绝对 `SV_Position` 整图深度采样，修复偏移／缩小视口的软边差异；保留 R105 投影贴花修复及专用 Tidecall 等独立路径，不全局替换材质 UV。
- 普通粒子的独立 ForceA 不再因未实现的 Wind 参数被整体省略，Wind 仍明确诊断；普通 `Kind=0` 线性负阻尼保留 `exp(-lambda*sourceDt)` 的原始符号，模型与贴花原有规则不变。原始开始时间支持 `[-600,600]`，完整引用净时间用运行时浮点运算检查有限性与预算，只停用超界层并清除事件悬挂引用；原版 Ash 的 -100 秒预热恢复，没有截短原寿命。
- `.wsmodel` 先匹配明确 LOD／部件；仅唯一未索引材质、实际单 LOD／单部件时接受省略索引。原版 `wh_main_ability_melee_slash_circle` 的 `3D_ring` 因此读取真实 `wh_vfx_mesh` 六槽材质，不再误读 rigid 内的 `no_texture.dds`；不补白图、不放宽真实缺纹理守卫。该问题属于 Bear，不是 Gyro。
- 扩大回归定位到 R104 继承问题：径向发射把数字 `SpreadMethod="0"` 与原生已标定字面 `false` 混同，零半径时丢失旧控制的初速度。现将该受限入口收紧到实际字面 `false`；数字 0 保留既有原生轨迹支持路径。新增同源半径 0／1 对照，旧原生夹具、轨迹容差和完整官方 RockImpact 控制均保留；不据此声称已反编译完整 XML Boolean 解析。

### 本次重新执行的验证

- 原生 Debug 构建成功，随后 Core 测试及 Meta 测试项目 Debug 构建成功；最终测试条件为 `ASSETEDITOR_VFX_HEADLESS_TEST=1`、`ASSETEDITOR_VFX_GAME_DATA=E:\Steam\steamapps\common\Total War WARHAMMER III\data`。最后两项仅测试代码修正通过 `BuildProjectReferences=false` 构建，未改写运行中的正常 App。完整命令、退出结果、早期失败均保留在 `parent-validation/*build*.log` 及对应测试日志中。
- **最终完整 `GameWorld.Core.Test.Vfx`：2291 通过、0 失败、14 跳过／共 2305 项**，见 `core-vfx-final-v6/`。14 项因缺少指定的已导出官方 poison-trail 原件及来源记录而跳过，不计像素通过。**最终 `EffectVfxPreviewTests`：43 通过、0 失败、0 跳过**，见 `meta-effects-final-v5/`。未运行整个解决方案全量测试。新增共享深度、跨实例颜色／折射、状态恢复、力、负阻尼、预热与材质选择用例均在完整专项中重跑。
- 先前完整专项有 114 项失败，使用 R105 的 F92 Core／8AC9 native 和同一测试程序集成对核验：旧轨迹、旧 GPU 控制均有相同失败。收紧径向入口恢复旧轨迹及烟雾控制；两条 Wind 断言同步独立 ForceA 契约。剩余测试分别纠正接收面未计已标定软边偏移、线性与显示颜色混淆、不可分辨取景；保留原误差、面积、颜色强度要求并增加正控制，未为通过测试调亮产品或修改原生夹具。早期有一次构建失败后误启旧测试程序集，已停止并标为 `shared-batch-green-v1-invalid.json`，不计入结果。
- 最终 CPU 清单 **40 个完整官方根／50 画质条件、194 项原始资源来源与 SHA、零转换异常**，见 `coverage/coverage-summary-final-v3.json` 与 `matrix-final-v3/`。这是转换覆盖，不是 40 个视觉通过。13 个实际官方 Pack 索引之外，另精确核对完整 manifest 的 271 个 Pack；补入真实 `models_other.pack` 恢复的一层属于验证索引补齐，不能全部算产品修复。
- 最终共享生产绘制离屏使用完整原版 StormDragon 2、Bear 13、Gyro Persistent 3 条 Effect 组合及真实原动画／骨骼，3 组共 10 个阶段／20 张对照图，含 Bear 5.10 秒的冲击环。最终 Core 的粒子数据及 PNG 与已逐图审查的本轮 v3/v4 完全相同，记录 `compositing/official-multi-meta-audit-v5.json`；全部实读依赖没有 Missing。共享与独立合成的对照使用同一当前 Core，只改变作用域，不改原 META 或粒子。此离屏没有绘制角色模型，使用受控参考曝光，不能当作 Terry 或游戏最终画面。
- **普通 Debug 窗口本次实际观察**：Bear ATTACK_4 使用官方原模型、动画、Animation/Persistent META，查看 3.49、5.08、6.10／6.71 秒及连续播放；冰面边缘保持不规则，5.08 秒能见完整冲击环，后期瞬态按寿命消失。该窗口观察在本轮径向入口收紧前进行，最终同源离屏字节完全相同；不写成最终 Core 再开 Bear 的窗口复测。最终 Core 另查看完整 siege smoke 的原始 -100 秒 Ash（q0/q1）、`wh_dlc03_large_smoke_loop`（q0/q1），以及 Amber Spear 全六层 trail（q0/q1）播放拖动；轨迹在 34／59／106 秒后仍可继续生成，停止运动后按原寿命消失。均从唯一只读 All Game Packs 加载，代码与本次日志核实使用实际官方 manifest，不载入 Mod 工程。没有保存或改写原 XML；最终 Debug 留在暂停的原版 Amber 页面。

### 当前边界与下一项共因

- 本轮没有完成新的同画质、同视角、同原生出生阶段的 Terry 对照；R105 首个大步时钟、随机、照明和最终分辨率限制仍有效，不能宣称全部 VFX 原生一致。完整材质／透明通道、深层静态引用、真正 `continuous_ribbon`、模型力及旋转／地形力组合、Wind 等仍有诊断范围。
- 清单仍有 3 个画质条件缺原始依赖：四条纹理原路径经完整 271 包索引确认不存在，其中 Doomdiver 只有其他目录的同名文件。没有猜测替换路径或伪造贴图。剩余 ForceA 的 21 条诊断按缺依赖、模型、发射器方向、地形跟随分类，见 `coverage/remaining-force-clusters-final-v2.json`。
- 实机发现重预热 Ash 页在暂停后仍持续绘制，出现搜索延迟、UIA 读取超时；关闭本任务无编辑页面后搜索立即完成。源码排除“同时间每帧重新预热”，但每帧重算／绘制且 UI 同步等待 GPU；纯加色精度拆批可能放大成本，CPU／GPU 占比及绘制批数尚未采集。没有擅自改共享 Host、全局步长、原透明／亮度或其他编辑器。此性能与 UI 响应问题仍未修，不能写成全部窗口已正常。
- 最终正常输出：Core `95E02D151A6E0F77C22196C32F5C8FCA82E23DC79BDCB0AFE161B0576F425511`，native `A816B5EDAA773FADB7BB32C38C3BF84464B713FAC11BE4D44C71C2CFD7787E36`。相关长期契约已同步现有 `docs/vfx-editor.md`、`docs/architecture.md`、`docs/superview.md`；正常产物、工具、源件、旧备份、失败记录与有效证据全部保留，无清理。

## R105 投影贴花深度坐标与原生对照（2026-10-26）

### 现场与实际修复

- 原地沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空；接手58项路径及SHA与R104 `workspace-after-v2.json` 完全一致。新证据根 `artifacts/vfx-native-reference-r105/`，基线及原A0C1 native小副本在 `parent-validation/`。没有切分支／建worktree、暂存、提交、推送、PR、合并或清理，未改全局 `1/120`。
- 确认共用缺陷：`CompileMaterials.cpp` 的普通覆盖材质识别要求第二uniform为ambientPX，而投影贴花第二项为decalInfo，漏走绝对深度取样修正。`CaptureDepth` 复制整张DSV，旧贴花却用局部视口UV取整图深度；缩小或偏移视口时错误重建接收点。现在单独识别投影贴花，仅将接收距离和柔边的深度输入改为 `SV_Position` 整像素Load／clamp，保留局部UV用于NDC／体积重建。无ABI、CPU参数、颜色、透明度、粒子时序或朝向更改。
- 原生普通贴花另有按G-buffer有方向法线判断U反转的规则；当前屏幕导数法线的符号受投影反射影响，不能直接套该条件，本轮未实现。Tidecall参数104floats／12纹理及其独立绝对深度路径已核，未发现可证明的Alpha乘法bug，不用普通贴花修复冒充泡沫大白片已解决。见 `projected-decal-viewport-facing-contract-v1.json`、`tidecall-contract-readonly-v1.json`。

### 本次构建、自动、官方离屏及正常窗口

- 新 `VfxProjectedDecalViewportTests` 先红2失败／1通过：完整128×96控制通过，256×192目标内较小及偏移(64,32)视口失败；native修正后3全过，`.002`像素断言未放宽。native Debug构建退出0，受影响CoreTest重新构建退出0／0警告0错误，包含正常App输出更新；正常Core仍为R104 `F92ED3C0…F3BC1F4`，新native为 `8AC9C17959384BCB12195B458A402684817FD1D0F02388C9C42B8C2D64FE8407`，nativebin与正常Debug相同。
- 本次相关材质／贴花／深度回归先142通过、9因未设置游戏资源环境跳过；补齐原游戏路径后9项官方柔边测试重新执行全部通过。合计151个不同用例通过、0遗留失败／跳过；新3项已包含在142，不另加。日志、筛选、TRX在 `parent-validation/`。没有全解决方案、Release、CI或输入测试套件。
- 完整官方 `wh3_main_entity_elemental_bear_frost_trail` 与 `wh3_main_entity_bear_ice_breath_impact_decal`，q0各3阶段，原根／库／两DDS共5项来源SHA及manifest重新读取，缺失0，无Mod索引。真实水平面写DSV，受控挂点前1秒移动5米后停止；不改XML。完整128×96与256×192中偏移视口6对PNG，稳定像素最大RGB差约1.27e-5，`.002`以上差异0；已查看中段及消退图。frost本为无限发射，10.5秒是旧粒子老化与持续出生，不能称整根已结束。v1/v2缺terrain高度及静止距离发射、v3构图／时点有限的失败全部保留，只有v4用于有效比较。见 `official-decal-gpu-v4-audit.json`。
- 正常Debug PID46600／窗口12978950，原版冰熊VMD、bear01骨架、`be1_wh3_elemental_bear.bin`、ATTACK_4及原完整META；实际连续播放见2.48秒，暂停查看3.50／5.28／4.08／6.53。冰面为不规则碎状散边，3.50／4.08无此前大方块；5.28／6.53临时冰面与喷射已退，持久烟雪仍有。缩远只为看完整轮廓，不作为同相机前后像素对照。完整组合仍显示335条候选／未支持警告，不称全部字段已支持。无源编辑／保存、无输入冲突，保留干净页暂停6.53。记录 `parent-validation/normal-debug-bear-gui-v1.json`。

### 有效原生帧及严格边界

- 使用R104自建但原资源未改的Monster场景，正常启动Terry PID25868，现场确认Bloom未勾选。仅一次Autoplay关→开未重启特效，捕获frame16829读回两pass均0实例；否定证据保留，未把它当出生期通过。随后仅再试一次实际Open／OK动作，成功返回后才写action-issued信号；新单帧助手不冒称scene-ready，.6秒延迟不当特效年龄。frame49082源件与副本各53,227,689字节，SHA `8c6f2218d96209fddd4fc9af7f145fe4e2fa4552bfcf023017d69cc33bfb16bb`，两pass同28粒子，不相加；原始GPU记录及只读回放完整。
- 本轮新RDC和回放目录因保留工具路径守卫位于 `artifacts/vfx-acceptance-r100/monster-r105-*`，是R105新证据，不能混写成旧R100复测。R104+R105 Monster所有源件／副本合计319,005,934字节，工具固定1帧、复制前核500MB预算；未清理任何源件或失败，所有隐藏采样／回放助手均正常退出。
- 官方两层寿命零bias；所有档位dust上限≤2秒，含life3.489646673的槽因此独立确认是giant_ring。该槽另有1.208563447<q0下限1.5，且3.489646673>q2上限3，唯一符合q1。字段为原生80字节粒子velocity.w的寿命，不靠数量／槽序／纹理顺序猜画质；只确认此帧此根q1，不建立通用HIGH→XML映射。实际相机(512,4.52618265,502.93692017)，RH／反射投影转换用世界轴点证明；AECN完整原根q1图及审计已生成。
- **本帧是加载首大Tick**：previousElapsedRaw29=0、sourceDtRaw7≈.403。6条未钳制birthUnary得到约.402999879阶段，但ring后续born phase已夹1；原生先更新发射、最后推进elapsed的现有指令支持在大dt内越过.1时长。AECN固定步长7个ring与该帧27个的差异不能直接归因Infinite错误。ring源本无InfiniteLife，删除父引用if也无效；保留当前有限及缺省规则，不按27调发射率／寿命／步长。raw29 CPU上传writer本轮未完全重建，后续须用正常小dt阶段进一步比较。见 `monster-reference-startup-delta-readonly-v1.json`。
- 原生内部粒子视口180×327，最终颜色阶段360×655，SwapBuffer1707×1067；受限资源链未闭合，未导出或裁剪SwapBuffer冒充独立最终场景。AECN对齐图只匹配内部视口／q1／相机／阶段；随机Scale／seed、模拟步进历史、背景、照明和最终分辨率不同，**未完成同条件完整原生视觉验收**。见 `monster-q1-frame-proof-v1.json`、`monster-q1-camera-v1.json`、`monster-q1-comparison-audit-v1.json` 与 `monster-final-target-export-v1/native-final-target.json`。
- 下一项仍需共用材质／透明合成与正常小dt原生参考；Kraken泡沫、G-buffer法线／光照、半分辨率、跨实例合成、复杂ForceBasis及专用ribbon边界仍保留。不能回到原生格线反复调参或宣称全部trail／烟雾已正确。源码保护及本轮产物盘点见 `parent-validation/workspace-after.json`／`final-audit.json`；这不是任务清理或全部完成声明。

## R104 四类反馈的共用修复（2026-10-26）

### 现场与实现

- 沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空。接手48项源码／文档散列与R103最终快照相同，基线及原Core/native小副本位于 `artifacts/vfx-four-issues-r104/parent-validation/`。以下证据路径均相对该R104目录。没有创建分支／worktree、暂存、提交、推送、PR、合并或清理，未改全局 `1/120`。
- 用户补充纹理trail与纯粒子区别后，实际按官方modifier／几何分类，贴图、名字和enable_trail_mode均不作为分类依据。相机修复仅限普通Kind0、显式相机对齐Alignment3、反射投影下的Right回退／混合锚点。实际原生相机Right与AECN RH view符号不同，旧混合在近轴半权重时额外相消。世界、水平和刚体模型路径不变，原生ribbon未伪装为已支持，原生混合自身退化也未强行抹平。证据 `native-contract/trail-camera-classification-and-basis-v1.json`、`camera-near-axis-minimal-fix-scope-v1.json`。
- 显式SpreadMethod=false的受限普通世界空间分支，初速度取出生点减发射器位置，非零归一化、零保持零，不再用旧角度公式将整圈变成单侧上升。速度与朝向独立，保留R103接收面Up及Bear/Crowman/模型旋转阻尼。predicate边界见长期VFX文档；真实producer第6/7参数证据为 `native-contract/radial-false-native-caller-v1.json`，没有重演R102手工串线。
- SuperView仅对Persistent文档的点触发且运行时有非事件模板Infinite层的Effect，取消按预览循环时长提前隐藏。混合有限层仍按原寿命退场；普通Animation、有限特效、非点时间范围、显隐和600秒seek上限不扩大。通过共享预览能力传递，不按名字判断。
- 官方Kraken完整升级组合暴露动画资源漏层：独立顶点上限误拒绝原合计预算内的几何，递归引用又重复烘焙占额。现托管/native统一 `72V+4I<=96,000,000` 字节，另保留600万索引和1800姿态校验；单次顶层转换内严格同路径/part/clip/speed/life/pivot几何跨引用及事件复用，通过已有ModelAlias上传，实例与粒子仍独立。层/引用/事件失败同步回滚新增缓存与预算，保留先前成功条目。不裁帧、降采样、改速度或姿态；配额不是进程峰值内存承诺。

### 本次验证与实际范围

- 本次真实红绿及失败日志保留：径向先4失败/2通过，后6全过；原4项相机1失败，补权重控制后6全过；Persistent先2失败/2通过，最终Meta回归57通过/0失败/0跳过；动画预算红9失败/8通过，相关绿50通过；缓存旧实现3失败/7通过，修复后10全过。最新核心相机/运动回归142通过、缓存/模型/Alpha/图集回归142通过，均0失败0跳过。筛选有重叠，不相加冒充一次全量；命令、构建和TRX在 `parent-validation/`。未跑全解决方案、Release、CI或输入测试套件。
- native Debug编译退出0；正常主程序及最新包含App的Meta构建退出0，后者0警告0错误。正常Core为 `F92ED3C0D2D2C45CF9D12229900087AAD07A2E685490A0F07DDB6096CF3BC1F4`、native为 `A0C1DA3341A95AAD157CD0AFDE96185D4A9F3A145B64B59373DBEF66A4F2ED90`。中途发生过运行Debug锁住复制的构建失败，后核对干净源页并正常退出确切实例再构建；旧失败日志未删，无强杀或丢弃。
- 径向：完整原版Monster Dust的q0/1/2、.3/.6/1秒共9对冻结GPU图，只有dust_giant_ring运动改变，另一个dust及全部数量保持；完整Storm Dragon impact亦核对原件。Rockdust原pebbles DDS真实缺失，不能当完整验收。最终正常Debug复看Monster q0的.29/.63/1.33及实际播放淡出，低平软边云团后段消失，原色/Alpha未调。见 `parent-validation/radial-monster-controlled-diff-v1.json`、`radial-normal-debug-gui-v2.json`。
- 动画：完整官方Kraken普通/升级两根，q1各6阶段，前后各12PNG，原依赖SHA一致且无缺失；升级映射26→29，3条动画预算漏层消失，4种原clip只上传4份，重复引用别名复用。普通版4模型的0/中/末姿态SHA保持，另有本轮径向/相机普通面片预期差异。对比只换基线Core、共用新native，不是整套旧runtime。见 `kraken-animation-budget-gpu-v2-audit.json`；初期亮条仍可能出构图，不宣称原生正确。正常Debug连续播放并看7.01/9.94/13.94/21.08/28.50/32.05：触手弯曲、水花运动，28.50水环已退，32.05全退，记录 `parent-validation/kraken-normal-debug-gui-v1.json`。
- META：原版dwf_gyrocopter VMD、wm_gyrocopter01.bin的STAND #01真实31.7秒，gyrocopter01骨架匹配，Persistent原3条Effect为engine、rotor×2，两个根均含Infinite且依赖无缺失。正常Debug实播及8.88/14.87/29.97仍有火花/旋翼特效随模型；原生格线未反复调。源SHA/路径见 `native-contract/persistent-long-official-ui-context-v1.json`，窗口记录 `parent-validation/persistent-normal-debug-gui-v1.json`，不能外推全部META字段。
- trail/Alpha：最终Debug完整原版Amber Spear q1六层，实际两次拖动，时钟连续越过8秒到28.10。短寿命粒子在首张拖后图已结束，使用真实运动历史4.83固定比较透视/侧视/正视，发光/烟/余烬保持可见，5.41衰减、5.88消失。未新改Alpha公式或重复乘Tint；相关原始浮点Alpha/纹理覆盖和图集时钟像素检查本轮已跑。见 `parent-validation/trail-normal-debug-gui-v1.json`。此原件普通纹理面片显式朝向相机，不能按贴图有无覆盖原modifier。

### 原生对照与剩余工作

- Terry自建原版Monster Dust参考场景实际核实Bloom未勾选、autoplay勾选及原actor变换。后续有界补原生采样前，保存三个原场景文件的小副本至 `native-contract/terry-scene-before-capture/`，在GUI正常保存并关闭干净PID55868，无丢弃；用已保留RenderDoc助手启动新的本任务PID42984，场景/原资源未改。因旧助手安全路径守卫，新增工作目录实际在 `artifacts/vfx-acceptance-r100/monster-r104-launch-v1`、`monster-r104-capture-v1/v2` 和 `monster-r104-readback-v1`，它们是本轮新证据，不能混作历史R100复测。首帧1875实际53,149,508字节（源件和独立副本均保留），离线读回成功，真实相机为(512,4.52618265,502.93692017)，但两个VFX绘制实例数均0，有限原件已结束，不能当有粒子的视觉通过。一次工具参数错误错过短时段，修正后重载补采样又在实际就绪前达到300秒超时；失败记录全部保留，未继续盲抓。主Terrain精确阶段和画质映射仍未闭环；原件三档的发射率/寿命/启用层确实不同，不能借同名根混比。见 `parent-validation/terry-reference-boundary-v1.json`（较早未抓帧时点）、`terry-reference-boundary-v2.json`（后续两尝试及当前现场）。**本轮没有完成Terry同条件或游戏最终视觉验收。**
- 保留Kraken泡沫片以及其余朝向、ForceBasis、半分辨率、完整光照/MBOIT/跨实例合成和专用ribbon等剩余差异。只读复核证实早期亮条实际为无StartTime的END_light_beam_down，不能按END名字认定时段错误；泡沫原参数有白色/alpha强度10，当前Tidecall的104floats及12纹理链路已接，PBR/接收面与白片的因果仍未知，未因此改Alpha或调参。证据 `kraken-foam-beam-readonly-facts-v2.json`。原版依赖缺失不伪造兜底；下一项继续在可验证原生条件下查剩余材质/复杂组合，不能回到已确认原生格线反复调参。
- 本轮工具、采样、失败、DLL基线、正常Debug和自建Terry场景仍需继续验证，全部保留；未打包搬盘或清理。最终源码/文档与原48项保护清单差异见 `parent-validation/workspace-after.json`。本节不是任务收尾或全部完成声明。

## R103 冰熊地面方块：修正贴花出生法线串线（2026-10-26）

### 本轮确认的错误与修复

- 用户指出 R102 窗口中明显方块后继续原地修复，沿用 `codex/vfx-preview-native-parity-r97` 与 HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存为空。接手 47 项散列与 R102 最后快照完全一致，基线为 `artifacts/vfx-bear-rectangles-r103/parent-validation/workspace-before.json`；保留原 94DC Core／F83C native 小副本，没有复制整套运行库。
- 先保留原13条 META、真实 bear01 骨架及 `be1_elemental_attack_04.anim`，只改变提交绘制的实例做诊断。3.26／4.10 秒的广阔浅蓝区域明确属于第4项 `wh3_main_entity_elemental_bear_frost_trail` 的唯一 `frost_decal_trail`；第12项 impact_decal 是独立灰白碎状 splat，不是该浅蓝方块主因。原 frost Alpha 是软散边，不能把当前方块称为原生贴图边界。
- **R102 生产端语义推断错误已纠正**：实际官方 producer `6c1ec0` 分别写 Position到stack+60、速度方向到stack+70、朝向法线到stack+50。最后把stack+50作为 writer第7参数传递；普通writer `6c10c0` 的 `[rbp+6f]` 经 Win64 栈偏移核实就是该参数，并复制 XYZ 后送 `6be290`。方向0／1时朝向法线独立初始化为 Up，方向2才查询地形；当前水平接收面仍为 Up。径向向量只进入速度，不能串到出生朝向。此前实际执行两函数得到的数学结果真实，但手工串联不是真实调用链；R102 那24项集成 oracle 及此前审阅的相关语义放行无效，不能拿49绿灯为错误数据流背书。
- `VfxTerrainDecalOrientation.BirthBasis` 现在只接收显式接收面法线；受限世界空间贴地 Kind2 的调用明确传 `Vector3.Up`。出生点／根旋转不再使地面贴花倾斜。`qz*qy*qx`、位置世界XZ／投地Y、META Scale2.5、原粒子数量／寿命／Alpha／贴图、速度、Kind0 Pox及Kind1 Crowman分支保持；不按效果名字调参，不改全局 `1/120` 或 native 材质。只在内存中反向移除这次 Sim单行修改，SHA精确恢复本轮接手值，见 `prior-simulation-protection-v1.json`。
- 生产链的新鲜官方 DLL 散列与指令、实现及红绿记录实际位于第二个保留目录 `artifacts/vfx-superview-projection-r103/`（命名不同，不搬运或删除）。入口为 `native-contract/terrain-orientation-normal-actual-caller-v1.json`、`parent-validation/terrain-orientation-implementation-v1.json`。低级24函数 fixture 原字节不动，只降为显式输入法线的数学控制；24模拟集成改用独立实际生产 Up oracle，`1e-5` 未放宽，新fixture为 `NativeTerrainOrientationProduction.json`。

### 本次构建、测试和同条件画面对照

- 实际先红 **16失败／33通过／0跳过**，修复后49专项全过；随后核心相关回归 **229通过／0失败／0跳过**、Meta相关回归 **73通过／0失败／0跳过**，302个不同用例。49已包含在229，不能另行相加。日志／TRX为主R103目录的 `parent-validation/core-regression-v1/`、`meta-regression-v1/`；测试代码改后均重新构建。
- Core及完整CoreTest Debug构建均退出0，分别保留1018／889条既有跨模块警告；正常主程序和Meta测试项目最后构建均退出0、0警告0错误。正常 Core 为 `00C59F007DB4A80B5F7E6B3EFE9C09B5A8FD84BDAA67BF237F588D79504B083D`，native保持 `F83C4BE9B69DDF36AA7E4C2F1EC444321B48CEC33030CE453750A28E44DC54B4`，本轮未改C++／着色器，不无故重编native。筛选及opt-in沿用R102相关类，仅离屏GPU，未运行输入测试、全解决方案、Release或CI。
- 主R103目录 `effect-isolation-v1/` 与 `effect-isolation-after-v1/` 各28PNG：同一个冻结工具二进制、同原13实例／真实clip／骨架、同384正交相机、同3.26／4.10。另 `frost-perspective-before-v1/` 与 `frost-perspective-after-v1/` 各1张第4项3.26诊断，采用与GUI相同投影合同FOV45／near.01／far25000／X反射，但 view为固定诊断，不能冒充已捕获GUI镜头。合计29对，已实际查看目标、完整组合和透视对照，其余控制按原PNG字节核对，运行均退出0。
- 前后31条真实官方资源来源／SHA、原META／动画／骨架、全部实例时间／骨／Scale／Attachment、层定义／Render及全部live/projected数量一致。所记录各层首3粒子除3×3朝向矩阵外，位置／Colour／UV／时钟／crush等float32位值一致；**这只是已记录首3，不是全粒子Frame摘要**。第4项透视原横条消失并恢复连续不规则散边；其余12项的24张隔离图逐字节相同。原密集浅蓝覆盖仍存在，不据此宣称密度、最终光照或透明度已原生验收。比较入口 `effect-isolation-comparison-v1.json`，没有改亮、延寿或重写原DDS／mip。

### 普通 Debug 与当前边界

- 先实际查看用户留下的正常Debug55420：完整冰熊ATTACK4暂停3.74秒，方块仍明显。源页无dirty，按正常Alt+F4退出，无保存提示；窗口和Win32进程均核实退出，没有强制结束或丢弃。随后启动正常新Debug窗口3082646，原版VMD／骨架已载入；点击动画库时工具明确报告 `user input was detected in this window`，立即停止全部桌面输入并报告，未按工具提示循环重试抢占。
- 用户随后明确答复“现在可以恢复，我暂不使用键鼠”。在同一正常 Debug25068／窗口3082646恢复官方VMD、bear01骨架、`be1_wh3_elemental_bear.bin`的ATTACK4与原完整13条Effect META；实际播放并暂停检查2.47／7.45／0／3.30／3.75／4.10／6.66／2.31／5.27秒。3.30／3.75／4.10的冰面呈不规则散边，所报直边方块和横向网格条纹未再重现；3.75及4.10缩远后完整地面轮廓可见，喷射／冰屑／雪雾与角色动画共同显示，5.27与播放中的6.66临时冰面／吐息已退场，持久雪雾继续。保留用户原相机方向，仅调视距，不能把本轮不同视距GUI截图当严格前后像素对照；同条件证据仍来自冻结相机的离屏29对。恢复后未再检测到输入冲突，没有编辑或保存源文件；正常Debug保持开启、干净源页暂停3.75秒。证据 `parent-validation/normal-debug-gui-v2.json`，此前冲突中断记录v1仍保留。
- 本次恢复只补普通窗口验收及记录，产品源码、运行库和测试未变，复用上一阶段实际执行的302项通过，不冒充本次重跑；48项工作文件与恢复前快照散列核对一致。最终现场见 `parent-validation/workspace-after-gui-v2.json`／`final-audit-gui-v2.json`。未做Terry或游戏验收。
- 同一PS另有反面U翻转和非零子视口深度取样边界，本轮未混入修复；它们未被证明是本图方块主因。完整G-buffer／法线贴图／光照、随机／时钟及跨实例透明合成仍有边界，不能宣称全部VFX正常。现有 `docs/vfx-editor.md` 的生产法线合同已同步纠正，保留R102旧数学证据及全部失败；无提交、推送、PR、合并或清理。

## R102 超级视图 META 漏显与地面贴花（2026-10-26）

> **R103 更正**：本节“出生点径向法线”和手工串联函数的生产语义结论已被实际 caller 数据流否定；旧图与测试仅作历史，不作为当前正确性依据。见上方 R103。

### 现场、官方样本与共用原因

- 沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存区为空。接手 32 项修改／未跟踪文件与 R101 最后快照散列完全一致；证据根为 `artifacts/vfx-superview-completeness-r102/`，下列相对路径均在该目录内。保护旧源码、原贴图、测试、正常 Debug、独有 Core/native 基线及全部失败记录，不提交、推送、创建分支／worktree 或清理。
- 自行从官方 Pack 固定抽取 13 份完整 META、143 条 Effect、46 个不同 VFX 根；逐条资源去重只用于审计，运行时仍按每条 Effect 的身份独立创建。另有界检查 757 份 META，发现版本 11／12 的 Effect，没有据此声称扫完全部 67,524 条候选。原始 META、根／库、模型、材质和 DDS 均按实际 Pack 与 SHA 核对，检查同名非 manifest 资源覆盖；不使用 Mod 或改亮、延寿的资源。首次完全零转换的是官方 `wh3_main_entity_elemental_bear_frost_trail`，不是固定只支持两条或 META 总数上限。
- 外层 `atlas_number_of_cells_x/y` 的原生独立零轴按 1 处理；旧转换器最小值 1 守卫误拒绝官方 0／0，整层缺失。私有映射执行实际原生算术后加入 9 项源合同与严格转换／像素检查；只归一化外层精确零轴，内图集、负数、小数、范围及 cell 越界仍诊断。
- 超级视图此前没有为共享 VFX 提供接收面。共享节点仍默认无地面，超级视图明确使用现有网格世界 Y=0，调用者已有地面优先。显式 SuperView 地面在每个 Effect 绘制前预写深度，避免地下粒子的显隐取决于实例顺序；独立 VFX 编辑器保留原条件式地面深度。普通不透明表面仍参与原有遮挡。
- 世界空间、贴地出生的普通 Kind2 贴花，在受限静止发射器及正等比根变换下，保留出生分布的世界 XZ，再按原生替换为地面 Y；尺寸沿 META Scale。方向缺省经实际全局默认表／typed cache 证实为 1，不能把构造器的 0 当缺省。实际出生函数使用局部出生点及根旋转选择方向，零法线保持原生 +X 回退；原始角构造为 `qz*qy*qx`。允许显式 `SpreadMethod=false` 的方向 0／1，方向 2 保持平面上轴；其他分布、非零发射器偏移／速度／力／旋转、该分支非零 spin、非等比／镜像／剪切等仍受原守卫约束。没有套用模型粒子规则或改全局 `1/120`。

### 普通贴花材质与实机反馈后的追加修复

- 官方常规 `DEFERRED_DECAL` 使用自己的 G-buffer 覆盖输出及 `ONE/INV_SRC_ALPHA` 混合，不拆预乘贴图残余 RGB 发光。原 frost DDS 中大量透明 texel 保留蓝 RGB，旧通用分层会画出矩形发光底图。现保留完整浮点粒子 RGBA、原始 Alpha，接通普通贴花 `ps_alpha_crush`，依原生顺序执行贴图 Alpha 转换、旧覆盖、裁切、预乘修正、输出饱和及等价混合；不拿字节顶点色隐藏 HDR／弱透明误差。发光贴花模式 3／4 的独立加法与 2500 辐射公式保持。
- 第一轮普通输出修复后，真实完整 Bear 3.28／4.10 秒仍有浅蓝直边，未当作通过。复核同一官方 PS 证实漏掉接收面夹角：`u=min(abs(dot(normalize(projectorZ),receiverNormal))*2.5,1)`，`F=u*u*(3-2*u)`，`F<.1` 丢弃，RGB／Alpha 在输出饱和前同乘 F。已有原生出生四元数反例对平地 F≈0，而旧路径仍绘制。补三组世界变换常量，经重建世界位置的 `ddx/ddy` 求几何法线；无 ABI 改动，只用于常规贴花。当前精确控制是平面，深度边界、法线贴图和原生 G-buffer 法线仍不同，不把该修复说成完整延迟光照还原。
- 本轮只改共享转换／模拟、节点地面传递、SuperView 接线和相应 native 材质，没有按特效名字打补丁、改原 XML／纹理、曝光或寿命。原生证据入口为 `native-contract/outer-atlas-zero-native-contract-v1.json`、`default-direction-cache-contract-v1.json`、`default-direction-normal-counterexample-v1.json`、`ordinary-birth-quaternion-order-v1.json`、`regular-decal-coverage-contract-v1.json`、`bear-remaining-pale-edges-readonly-v1.json`。生产函数私有复制执行，不向用户应用注入或改原 DLL。

### 本次构建和自动检查

- 最终 Core **229 通过／0 失败／0 跳过**，Meta **73 通过／0 失败／0 跳过**，共 302 个不同相关用例，见 `parent-validation/core-final-v3/`、`meta-final-v3/` 及 `final-test-build-receipt-v1.json`。覆盖原生 outer-zero、贴地挂点／姿态、显式地面提交顺序、普通贴花输出与角度、旧图集／投影／发光贴花、R101 原始 Alpha33，以及完整官方 Bear13／Phoenix41／Gyro3 的实例、时间、资源、缩放和骨骼历史。受控多骨骼轨迹明确标记诊断；Bear 另使用完整原动画／骨架，原 META 的 bone40、Scale2.5、位置和四元数保持。
- 所有 red、准备错误及中间失败均保留：外层零值、旋转地面和方向、普通贴花 8 项、夹角 6 项均实际抓到缺陷。夹角首绿 15／16 的 nonPM HDR16 误差来自硬件 sRGB 近似被放大；仅将新角度诊断的 nonPM 输入改为精确白，保留 `.00015` 断言，PM 的 RGB192/HDR16 仍覆盖饱和顺序。旧姿态测试的三项过期“必拒绝”改成对应原生投影、缩放及方向正断言；旧 16 实例绘图诊断明确关闭新增地面，因为原相机布局包含地下实例，仍保留全部 16 条和三阶段 `>6` 像素断言。新真实地面遮挡另有独立正反序测试。没有为绿灯放宽断言或取消旧原生控制。
- 固定 Effekseer `170e` 提交仍为 `92fdeb349c0c7eab9dfaf152047f22dec2fa102d`。实际执行正常 `Build.ps1 -EffekseerSource .../artifacts/effekseer-source -VisualStudioDirectory D:/Software/VisualStudio/2026 -Configuration Debug`；本轮三次 native 增量均 5/5、退出 0。正常主程序最终 Debug 构建退出 0、0 警告／0 错误（`normal-debug-final-build-v3.log`）；测试项目均先重新构建，最终 Meta0警告0错误，Core夹角最终构建3条既有警告0错误。此前 nullable 警告和首轮缺 using 的准备失败也保留，没有顺手改范围外代码。
- 使用 `dotnet test <相应csproj> --configuration Debug --no-build --no-restore --filter <最终TRX列出的类>`，显式 `ASSETEDITOR_VFX_HEADLESS_TEST=1`、官方 `ASSETEDITOR_VFX_GAME_DATA`。筛选范围无键鼠输入测试。最终 Core SHA `94DC05671DFEEEE43D7BFCCAC7810A376EFB4D7801AF695DD0E5DC16BB2326A0`，native SHA `F83C4BE9B69DDF36AA7E4C2F1EC444321B48CEC33030CE453750A28E44DC54B4`，正常／Core测试／Meta测试 native 逐字节一致。未运行完整解决方案、Release、CI、游戏或本轮 Terry。
- 同一 Core 的最终 CPU 原件审计为 `final-current-core-meta-audit-v3-summary.json`：13／143／46 无未知 META、无根名缺失、无整根零转换／转换异常，原 META 与共同依赖 SHA 不变。此报告在夹角 native 最后重建前，CPU 逻辑及 Core 未变，复用该 CPU 结论，不冒充新 native 视觉通过。ground_impact3→4 仅是小审计工具补 `models4.pack`／`models4_dds.pack` 原依赖范围，**不是产品新增支持**。

### 实际窗口、保留项与剩余问题

- 实际通过 Computer Use 操作正常 Debug，按完整官方 VMD／骨架／动画库／动作加载。材质第一阶段的 Bear13 检查 2.48／3.52／2.72／3.28／4.10／6.24；Phoenix41（`bi1_eagle.bin`、`REBIRTH`、`bi1_rebirth_01.anm.meta`）检查 .57 的蓝橙火星／黑烟、1.51 的灰烟环／黑色上烟、4.64／4.74 的地面残留及 5.27 的后段爆发，非仅凭加载或像素数判断。记录为 `normal-debug-material-green-angle-red-v1.json`；该阶段不能冒充最终夹角修复后的窗口复测。
- 最终 native 的实际正常 Debug PID55420、窗口7014924，重新加载完整 Bear13，实际播放并暂停回看 2.47／2.29／3.26／4.10／5.26。3.26／4.10 同时有吐息、飞雪、冰片及原地面贴花；近切向覆盖有所消退，5.26 临时吐息／冰面已退场，持久雪雾与动画模型继续。仍有部分浅蓝投影／纹理边界，尚不能逐像素归到 frost、impact_decal 或冰片层，也没有同画质、同视角、同阶段原生对照，**不宣称矩形边缘全部修好或完整外观正确**。
- 最终第二个 Phoenix 窗口复测未补齐：模型浏览器搜索文本改变但资源树未筛选，扩大查询／Enter 仍未生效，取消后关闭空白页，没有丢弃源编辑或持续重启。保留第一阶段的真实 Phoenix 画面结论与最终 73 项自动结果，严格区分版本。最终 Debug 留在干净 Bear 源页、暂停 5.26，输入未检测到用户冲突；之前确切 Debug 均正常退出、无保存提示或强制结束。见 `parent-validation/normal-debug-final-gui-v1.json`。
- 固定官方范围仍有 MaximumParticles 范围拒绝1项、缺资源诊断5项（涉及三条官方 Pack 索引也找不到的旧 DDS）、灯光通道6项、部分省略力的诊断45项；逐源／层详表已留存，不能以46个根非空说成每层都支持。原生完整透明合成、跨实例折射／排序、动态地形、G-buffer／模型照明及其他复杂曲线边界不随本轮消失。后续先补可重复的 Phoenix 入口与 Bear 投影同阶段原生对照，保留 R101／R100 已确认表现。
- 相关稳定契约同步现有 `docs/vfx-editor.md`、`docs/superview.md` 和 `docs/architecture.md`；不新增流程规则，AGENTS／README 及旧 helper／原贴图未改。所有小工具、真实原件、旧 Core／native、失败日志、TRX、原纹理／mip PNG 和 native 缓存保留；本轮证据约 34 MiB，最终盘点以 `parent-validation/final-audit.json`／`workspace-after.json` 为准，不打包搬盘或清理。

## R101 trail／烟雾的非预乘透明链路（2026-10-26）

### 现场、原生合同与共享实现

- 沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存区为空。接手 29 项修改／未跟踪文件与 R100 最后快照 **0 散列差异**，见 `artifacts/vfx-shared-trail-smoke-r101/parent-validation/workspace-before.json`。先备份唯一一份正常 Debug Core `DC7A2264…` 和原生桥 `026F432C…`；未复制整套运行库。下列相对证据路径均以该 R101 目录为根。
- Fresh 官方 `fx/particle_vfx_library.hlsl` 为 57,075 字节、SHA `40070970…`，另有实际原生 PS 的独立指令交叉：`ps_texture` **先过滤／混合原图集帧，再无条件线性化 Alpha**，之后才读取渐变、乘原始粒子透明度并饱和。该 Alpha 转换不由预乘开关决定；CPU 预解码不能代替像素阶段的处理。此前普通非预乘面片既提前将粒子 Alpha 截到 1，又可能走缺少 Alpha 转换的内置材质，属于共用实现缺陷；Pox 原版初始 Alpha100、寿命曲线最高 1.2 的组合直接受影响，不是按该名字调参。
- 普通 `Kind0` 保留非负原始 Alpha；已有模型规则保持。Bridge 对有贴图、无专用材质的普通面片统一加载／使用现有 Coverage 材质，包含原来 Alpha≤1 的路径，避免只在跨过 1 时改变 gamma；沿用指数分组、byte 归一化及 `particleInfo.x` 恢复，保留弱粒子与原提交顺序。没有重写纹理、曝光、寿命、角运动、全局 `1/120` 或 ABI。
- 第二个共因是普通非预乘 `ps_alpha_crush` 被转换守卫跳过，影响 Pox 底层烟轨迹等出现／消失组合。独立原生 PS 重放证实：先得 `O=saturate(A*V)`，再得 `C=saturate(O-P)/(saturate(1-P)+.001)`，sharp 保留 `C>=P`，随后才软深度淡化。非预乘直通 RGB 在 O>0 时等价保持；**O=0、P<0 会恢复黑色覆盖，RGB 仍为零**。因此除了接通字段，还在 mode0 材质补该零旧覆盖的 RGB 守卫；不是仅删除“不支持”判断。模型、专用材质、预乘独立发光和 DISTORTION 各自路径保持。
- 本轮产品仅改 `TotalWarVfxSimulation.cs`、`TotalWarVfxConverter.cs`、`Bridge.cpp`、`CoverageMaterial.h` 四处文件。把这轮增量仅在内存中移除后，两份此前已有大量成果的 C# 文件 SHA **精确恢复接手值**，见 `parent-validation/prior-source-protection-v1.json`。原生合同及独立审阅为 `native-contract/nonpremul-alpha-source-contract-v1.json`、`nonpremul-alpha-crush-contract-v1.json`、`four-file-product-alpha-review-v1.json`；长期行为同步现有 `docs/vfx-editor.md`。

### 本次构建与自动检查，保留全部失败

- 新增 `VfxOrdinaryRawAlphaTests.cs`。最初 21 项的 red-v1 为 20 失败／1 通过，其中四项是测试试图寻找原 DDS 中不存在的 A255 像素，**不是产品红灯**；改为明确标记的单变量上传对照，仅该 texel 的 Alpha 改255，原 DDS、RGB、其他 texel／mip 保持。red-v2 的三个剩余正常控制只差硬件 sRGB 采样近似；改用独立 MonoGame `ColorSRgb`／Opaque／PointClamp 采样作 RGB 参考，Alpha 仍按原生公式独立求值，**RGBA `1e-5` 从未放宽**。旧源码、准备错误及采样误差记录均保留，不能把这些算成修复产品的通过项。
- 准备修正后的正式 red-v3 是 **16 失败／5 通过／0 跳过**，精确抓住 CPU 提前截断及 GPU 的普通／高／弱 Alpha。第一步接通后，追加 crush 参数与零旧覆盖反例，共 33 项，实际为 **5 失败／28 通过／0 跳过**；五项分别抓住未映射字段和负阈值错误着色，见 `parent-validation/crush-red-v1/`。
- 固定 Effekseer `170e` 源码提交 `92fdeb349c0c7eab9dfaf152047f22dec2fa102d` 已由构建脚本核验；实际执行 `GameWorld/Vfx.Effekseer.Native/Build.ps1 -EffekseerSource .../artifacts/effekseer-source -VisualStudioDirectory D:/Software/VisualStudio/2026 -Configuration Debug`。首轮原生构建 85/85、补守卫后的增量 5/5，均退出 0，确实重新生成并嵌入材质；没有拿单纯 .NET 复制当作原生重建。
- 正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 最终退出 0、**0 警告／0 错误**。受影响测试项目重建退出 0，最终该日志记录 1881 条跨模块 nullable 等警告／0 错误；首轮正常构建也保留 1901 条警告日志，没有为此改范围外代码或隐藏日志。最终正常 Core 为 `F25E169BBDA579319E6902B1D209362E36EB231B1D52A991FB0E7CBE88046495`，原生桥为 `9F20AFF714969A22141B7693FD9591CA3B9EEBCEB4F1CC1B8E4D9AB57A007F4E`，测试使用方一致。
- 最终新专项 **33 通过／0 失败／0 跳过**（`parent-validation/green-v1/`）；相关材质、覆盖、图集、柔边、扭曲、模型、尺寸及角运动回归 **260 通过／0 失败／14 跳过**（`regression-v1/`）。14 项仅缺指定官方原贴图目录，从 manifest 的 `vfx.pack` 有界导出所需一张原 DDS，SHA `047a461d…acc6` 一致后补跑为 **14 通过／0 失败／0 跳过**（`faint-v1/`）；三批分别报告，不重复累加或冒充全解决方案检查。没有放宽旧断言或改旧测试。
- 对应命令为 `dotnet build Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj --configuration Debug --no-restore`，以及 `dotnet test ... --configuration Debug --no-build --no-restore --filter <日志中精确类筛选>`。GPU 显式开启 `ASSETEDITOR_VFX_HEADLESS_TEST=1`；真实资产另指定官方 data，faint 补测指定 `ASSETEDITOR_VFX_FAINT_ALPHA_ORIGINALS`。筛选代码核查无键鼠输入用例；未运行完整解决方案、Release、CI 或游戏。

### 完整官方原件前后及正常窗口

分类诊断读取十个完整官方根、按材质／render／参数组合盘点 59 个定义；实际正式 GPU 矩阵为下表七根，每根 .6／1.2 两阶段，14 对共 28 PNG。前后画质、相机、挂点轨迹、阶段及全部依赖来源／SHA 一致，0 缺资源、0 空图；75 条依赖与 26 个非 manifest Pack 另核对无同名覆盖。Aekold 缺少三条尚未纳入小解析器的 terrain 依赖只保留失败诊断，不归咎产品；Nightmaw 库只作参数边界，不冒充完整效果视觉验收。

| 完整官方根 | q | 本次结论 |
| --- | --- | --- |
| `wh3_dlc29_pox_maggoth_trail` | 0 | head_main 恢复原始高 Alpha、渐变环／带状遮罩；base 进入原生 crush。运动／FullLocal 姿态保持。正常 Debug 实际播放、选择、两向拖动，观察黄色绿色环与细碎边缘、原黑色碎块，时间超过 133 秒仍连续；暂停 133.56，未见新增白实体片或矩形贴图边 |
| `wh3_dlc25_ability_baleflame_amulet` | 0 | 非预乘普通层的 Alpha／采样修正，整根变化较细。正常窗口看 .07 初环、1.22 烟柱及 3.09／6.18 消退，47.69 仍有原长寿命地面残留；紫黑烟边和紫色地面材质保留，不夸大为完整材质已还原 |
| `wh3_dlc25_sorcerer_of_nurgle_death_trail` | 0 | 预乘贴图 trail＋扭曲控制，两对完整 Frame／PNG 字节一致 |
| `wh3_dlc24_cth_crowman_trail` | 1 | 模型溶解控制，两对完整 Frame／PNG 字节一致；本轮窗口未重做其 R100 生命周期观察 |
| `wh3_main_entity_bear_ice_breath_trail` | 0 | 动态角阻尼控制，两对完整 Frame／PNG 字节一致；本轮未重新写成人工验收 |
| `wh3_main_seige_smoke_large` | 0 | 尺寸曲线控制，两对完整 Frame／PNG 字节一致；正常窗口明确全选三层，2.67／1.59 秒有连贯的灰色上烟与软黑中央层，暂停 2.95，未重现中央缺层 |
| `wh_dlc03_beasts_amber_spear_trail` | 1 | 预乘贴图轨迹控制，两对完整 Frame／PNG 字节一致 |

- 所有目标及控制的 committed 角／非角状态、emitter 状态、flags，以及排除 Colour.A／AlphaCrush 后的完整 Frame 均逐字节一致。Pox 的旧“仅排除 Alpha”摘要因本轮合法新增 crush 而不同，如实保留；新增摘要先以旧 Core 重建 14 个 CPU 完整 Frame，并全部精确匹配已采 before FrameSHA，才用于更细比较，没有放宽运动断言。probe 仅新增审计输出分支，旧源及 source SHA 差异明确保留。
- 全部 after14 图及 target before4 图已实际查看，control before10 图与 after 字节一致。主任务另复核 Pox .6 和 Baleflame1.2 的前后原图。Pox 原来缺失的头部环状覆盖恢复；Bale 变化较细。形状、方向和软边观察与参数证据分别记录，不用 draw 数或有像素代替正确。入口为 `official-comparison-v1.json`、`official-execution-v1.json`、`parent-validation/normal-debug-gui-v1.json`。
- 既有计数显示 Pox drawcall .6 为206→319、1.2为200→315，来自逐粒子的裁切参数分组；没有新增 GPU 耗时测试或通用性能验收，不能据计数认定卡顿或性能已通过。不为此追加批处理扩展。
- 窗口操作沿用持续授权，没有检测到用户键鼠冲突。构建前核对旧 Debug 的唯一 Crowman 源页无 dirty 后正常退出确切实例，无保存提示或强制结束；最终正常 Debug 保持打开，三份官方源页均无 dirty／未保存源修改。旧 Terry 场景及应用保留，本轮未操作或新捕获 Terry，没有冒称同画质／同阶段原生视觉对照。

### 保留与剩余问题

本轮关闭的是普通非预乘贴图 Alpha／原始粒子 Alpha／透明裁切的共用缺口，复杂组合仍按其余诊断边界处理。独立溶解模型审计证实原生 dissolve 使用 POSITION5.w、noise／emissiveA 使用 POSITION6.w、B／global 使用原生全局时钟且 B 另有实例 UV 偏移；生产这两个实例时钟的规则尚未闭环，**没有把三路统一猜成 age／phase／global 来改**。完整 MBOIT、半分辨率、跨实例排序、其他复杂曲线／力参数、模型光照及精确随机／时钟仍有限制；R100 Crowman 同阶段 Terry 对照继续待验，不宣称全部 trail 或烟雾达到游戏级。

保留所有旧成果、正常 Debug、唯一 Core／native 基线、28 张有界正式图、原生纯函数／指令证据、失败与准备误差记录、小工具及唯一补测原 DDS；新原生构建缓存也保留，不清理或搬盘。最终盘点和源码保护见 `parent-validation/final-audit.json`、`workspace-after.json`；未暂存、提交、推送、创建分支／worktree、PR 或合并。

## R100 Crowman 实机与 Terry 配准（2026-10-26）

### 现场保护与本次正常 Debug 观察

- 沿用 `codex/vfx-preview-native-parity-r97`、HEAD `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存区为空。接手 29 项修改／未跟踪文件与 R99 最后快照散列全部一致，见 `artifacts/vfx-acceptance-r100/parent-validation/workspace-before.json`。本次不改产品、测试、原资源、材质或全局 `1/120`，没有构建或替换正常 Debug；复用的 R99 构建及 28／555 项检查均为历史结果，不写成本次重跑。
- 实际操作正常 Debug 原有 `DC7A2264…` Core，加载完整官方 `vfx/wh3_dlc24_cth_crowman_trail.xml`、q1、唯一 `feathers` 层。八条根／模型／材质／贴图依赖 fresh 来源及 SHA 与 R99 一致；26 个非 manifest Pack 对这些依赖重新检查，0 同名覆盖。没有使用 Mod、资源别名或改亮／延寿样本。
- 原件按距离发射（间距 1），粒子寿命 1 秒，出生约前 .05 淡入、.75 后淡出；源 emitter 的 .25 秒字段不能混作粒子寿命，`InfiniteLife=true` 也不能漏读。普通窗口相机不会跟随发射器，快速移出画幅或停止超过 1 秒会看不见羽毛，不据此判为材质缺失。
- 本次实际看见播放中 291.49／291.58 秒两帧的黑色原模型羽毛，位置及旋转连续变化；暂停回看 291.12 秒约七片清晰羽毛，291.67 秒姿态／位置变化并淡出，291.95 秒只剩小尾片，292.77 秒全部消失。缩远相机后可以看完整羽片，没有改变粒子尺寸或源寿命。GUI 显示秒数是两位小数标签，不冒充精确内部时钟或精确旋转角度。记录为 `parent-validation/crowman-normal-debug-gui-v1.json`。

### Terry 画质、实际相机与阶段边界

- 正常关闭本任务旧 Terry 的清洁场景后，从设备创建前启用现有 RenderDoc 工具，重启确切官方 Terry（PID 2912）；未强制结束、丢弃或保存用户内容。重启后实际在三维设置核实“泛光”未勾选。仅使用本任务完整原名场景，旧场景三份小文件先留副本；官方源 XML、模型、DDS、Pack 和 exe 未修改。全部输入沿用持续授权，未检测到用户键鼠冲突。
- 完整原版 siege 的实际帧 20368 提供独立画质指纹：三个 emitter slice 的 InitialAlpha min／max 为 `.7998046875／1`、InitialScale XYZ 为 1，与 fresh 原 XML 的 **q1** 唯一对应；不是按“高”字样或旧预设猜测。实际 view／projection／eye／viewport 与原生 tone PNG 同帧保存于 `siege-readback-v4/`、`siege-tone-export-v4/`。
- 该烟雾帧的 base／central 原生 emitter 阶段约 52.388508 秒，end_fade 约 52.029507 秒，晚约 .359 秒；原始粒子 birthUnary／age／life 与 emitter 常量交叉一致。减原 StartTime=-25 仅得各自 AECN 根时间候选，**不能把三个时钟强行归为完整特效的同一播放阶段**，更不能用捕获墙钟差代替。
- Crowman 场景保存的 FOV45 没有成为真实镜头：其实际 camera CB 测得 **FOV60、near1／far12000、viewport360×655**，eye 为 `[512,15.5237846,482.9981384]`；以原挂点 `[512,2,512]` 配准后的局部 eye 为 `[0,13.5237846,-29.0018616]`。证据为 `crowman-camera-v3/matched-camera-v1.json`。矩阵、镜头位置和运动挂点必须使用同一坐标平移，不能二次镜像或只改 view 而遗漏眼点。
- 当前正常 Debug Core 另完成完整官方 Crowman q1 的 .6／1.2 秒同相机参考两图，实际看过黑羽毛、弯转轮廓及细小消退碎片；View／Projection／Eye 按 float32 位模式核对与上述实捕相机一致，八条官方依赖 SHA 一致、0 缺失。输出采用原视口 360×655 的 1:1 裁切等价，不冒充 1707×1067 全目标一致。其运动是明确的诊断方形轨迹，**没有匹配 Terry 的出生历史、年龄或材质时钟，不是同阶段原生验收**；见 `official-samples/crowman-same-camera-reference-result-v1.json` 与 `crowman-complete-root-camera-reference-v1/`。受控替换原生最终姿态模式没有运行。

### Crowman 原生对照未通过，保留失败证据

- Terry 确实加载完整原名 Crowman 并勾选 Autoplay。前两次捕获发生在选择工具，辅助轴不是可拖动手柄，挂点未移动，属于无效发射尝试。随后从“工具→基本操作→移动”进入真正移动模式，实际挂点 X 从 512 移至约 519.56，再在 Autoplay 关／开后移回 512；本任务测试场景保留 dirty 状态，没有丢弃或强制退出。
- 四次 Crowman 实际帧为 34128／38096／44679／58082；最后两次已确认真实挂点移动，分别采用 .45／1.25 秒有界取帧。后面三帧的全部实际 draw 各为 39，均没有原模型的 204 索引绘制，也没有溶解模型主材质绘制；原生 PNG 也未看清羽毛。已经排除仅按材质名筛选漏检，**没有据“移动成功”“捕获成功”或黑图宣称原生表现正确**，停止重复同类时序重捕。
- `enable_in_ted=false` 只是定位线索：已实际提交烟雾的 siege 原根同样为 false，不能把它单独定为 Crowman 不发射的根因，也没有删除该标记来制造“原版通过”。Terry 编辑器挂点变化是否向活特效节点提供了原生距离发射所需的运动，仍未闭环。
- 有界原生字段检查只证实该标记的 XML 读取及 `EnableInTED` 属性注册，没有证明运行时创建守卫；两个候选 offset 比较属于其他类型，未当作支持证据。最后一次 1.25 秒延迟也不能单独排除时机问题：粒子只活 1 秒，而真实最后运动与捕获的间隔尚无活节点记录。不能把“换过延迟”写成已排除全部时序原因。
- 模型 208 字节属于 CPU 粒子状态；GPU 只有最终 world／custom／材质时钟时，不能唯一反推 birth／life／播放阶段。原 Alpha 有长平台且淡入、淡出可同值。已准备的小型 current-Core 对照工具要求真实模型输入并校核全部 37 个原顶点、packed alpha 和实际相机；由于上述原生帧无模型提交，没有伪造输入或空跑受控回放。工具编译为本次实际执行，0 警告／0 错误，不等于产品重新构建或原生验收。
- 早期设备创建后注入未取得有效捕获、等待超时及一次打开确认框期间误发 ready 的记录全部保留；误帧 18653 不作为验收。捕获、失败、源件小副本和工具均位于 `artifacts/vfx-acceptance-r100/`，没有清理或搬盘。本轮没有游戏测试，也没有重新验收 Bear／Pox；它们仍引用 R99 当时范围。

本次关闭的是 Crowman 正常 Debug 清晰窗口观察缺口；同画质已有实际指纹，同视角有实际矩阵，但同阶段 Terry 模型对照仍未通过。接续先解决原生测试场景的运动／发射输入，取得真实羽毛模型帧，再执行既有受控对照；不要回头改原资源、模型角运动、寿命或材质来迎合当前空场景。

## R99 攻城烟雾中央层的二次 Bezier 尺寸曲线（2026-10-26）

### 本轮现场与实现

- 沿用 `codex/vfx-preview-native-parity-r97`，HEAD 仍为 `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存区为空；开始时 24 项修改／未跟踪文件与 R98 的 `workspace-after.json` 全部散列一致。接手快照为 `artifacts/vfx-siege-scale-r99/parent-validation/workspace-before.json`。已有成果保留，不切分支、建 worktree、暂存、提交、推送或清理。
- 缺失层是官方 `wh3_main_seige_smoke_large` 的 `smoke_central_pillar`，实际阻断为 `ParticleScale` 中的 `bezier_q`（原生模式 11）；不是删守卫即可处理的通用三次 Bezier。官方原 XML SHA 为 `b5ccb9b9…`，三档尺寸原文一致。源文件关键点存储为 0／1／约 .56，时间排序后的段为 0→11、11→8；原始字节与关键点存储顺序保持。
- 新增 `VfxSpriteNativeScaleCurve`，只在普通平面 ParticleScale 存在实际 q 段时接入。保留旧曲线快照与元数据，其他非 q 路径逐字节保持；从 wrapper 移除本轮五处增量后，文件 SHA 精确等于接手快照 `106500E8…`。模型、初始尺寸、颜色／透明度与其他字段不借此开放 q。
- 原生模式 11 在出端和入端均取左关键点 outgoing，按 float 0.6667 转换成两个三次控制点，再执行原生十次二分及扫描。四种独立执行过的端点组合为 0→11、11→0、11→11、11→8；右 incoming 改动不影响、左 outgoing 改动影响的反例及真实 half 输出一并保存。其他 q 混合、错误绑定、非法控制与溢出继续诊断。
- 普通 GPU 尺寸上传按 float(i)/128 烘焙成 signed-half XYZ 上下两行，允许不同 min/max；每粒子使用同一 unary 在三个轴之间保持一致，采用 normalized linear-clamp 的可移植双线性采样。没有借用模型 i/127 表，也没有硬编码当前 RTX 的过滤舍入。普通绘制 scale.Z=1、R98 自旋／FullLocal、R96 模型、出生随机数消耗、原材质／原生桥／ABI 和全局 1/120 均保持。
- 原生证据入口为 `native-contract/ordinary-quadratic-scale-contract-v2.json`；当前 GPU 87 次不同双表过滤观察与精确 half 表分别记录。未重新标定上游 life_unary 时钟或随机序列，不能据此宣称游戏同帧。以下相对证据路径均以 `artifacts/vfx-siege-scale-r99/` 为根。

### 本次构建、测试与保留的失败

- 修复前新 18 项专项为 **9 失败／9 通过／0 跳过**，确实抓住三档完整原 XML 转换、四组 CPU 尺寸与两组 GPU 映射缺失；见 `parent-validation/red-v1/red.trx`。未通过旧程序集冒充复现。
- 接入初次构建漏掉新字段声明，4 个编译错误已补齐；中间版 `40FE85C4…` 又因接入脚本误漏原 empty-curve fallback，使纳垢组合的 `splash_mesh_base` 在缺省颜色时抛异常。这是**本轮接入错误，不是旧实现缺陷**；现已恢复原行，并补默认颜色回归。失败日志、CPU 定位和 2 张中间图保留，排除正式结果。
- 一次测试构建遇到本任务 Debug PID 61844 的输出锁，退出失败；核对标签无 dirty 后正常退出确切实例，再完成构建。没有强制结束或丢弃内容。该失败保留于 `green-build-v1.log`。
- 最终受影响测试项目 Debug 重建退出 0，2179 警告／0 错误（跨模块原有 nullable 等警告；新 helper／专项文件无警告）；正常主程序 Debug 构建退出 0，**0 警告／0 错误**，见 `green-build-v2.log`、`normal-debug-build-v3.log`。
- 最终新专项 **28 通过／0 失败／0 跳过**，含精确原生 half、控制点反例、三档原件往返、CPU 到矩阵、两组同官方贴图的独立静态尺寸全 RGBA 对照，以及拒绝边界和空曲线默认值；见 `green-v2/green.trx`。另相关曲线、自旋／阻尼、FullLocal、模型与连续 trail 等回归 **555 通过／0 失败／0 跳过**，见 `regression-v1/regression.trx`；两批独立报告，未跑完整解决方案。
- 实际执行 `dotnet build Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj --configuration Debug --no-restore`、`dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 与对应 `dotnet test ... --configuration Debug --no-build --no-restore --filter ...`。GPU 开启 `ASSETEDITOR_VFX_HEADLESS_TEST=1`，官方回归另指定原版 data；没有桌面输入测试夹带。
- 最终正常 Debug Core 为 `DC7A2264F54AB11455AEF7F14705F5A48167C8DE9A1E631FF14BFAF0D9EC6E35`，原生桥仍为 `026F432C…`，未重编 C++ 或材质。最终只读审查未发现越界或额外开放的 q 组合。

### 完整官方原件离屏复测

仅保留一份 R98 Core DLL 基线，未复制整套 runtime 或导出资源库。before／after-v2 和小 probe 构建均退出 0；六个完整原根、每根 .6／1.2 两阶段，共 12 对正式 PNG。画质、视角、投影、挂点轨迹、时段、manifest 与所有已有依赖来源／SHA 一致，无资源缺失。26 个非 manifest Pack 对 53 条实际依赖重新核对，0 同名覆盖。官方原 XML、模型、DDS 与旧 preview Pack 均未修改。

| 完整官方原根 | 本次 q | 本次结果与限制 |
| --- | --- | --- |
| `wh3_main_seige_smoke_large` | 0 | 2→3 层，中央活粒子 36／33；黑色中央烟连接上灰烟，软边保留。原 smoke_base 数据完全一致；恢复中间层使 end_fade 取得原种子序列的下一个索引，不能把其差异误归为尺寸公式影响 |
| `wh3_main_nur_siege_tower_destruct` | 0 | 13→14 层，原库中央层 40／48 粒子；原黄色爆散后出现棕绿烟柱。后续九根种子索引随新增层移动；-25 预热另造成约 1e-14 的累计时钟差，逐层记录，未改步长或种子压平。黄色模型的旧实体材质不能据此验收 |
| `wh3_main_entity_bear_ice_breath_trail` | 0 | 两对 PNG、完整帧、全部角／非角状态逐字节一致 |
| `wh3_dlc29_pox_maggoth_trail` | 0 | 两对完整图像与数据逐字节一致，保留 R98 FullLocal |
| `wh3_dlc24_cth_crowman_trail` | 1 | 两对模型图像与数据逐字节一致，羽毛轮廓实际查看；模型姿态未改 |
| `wh_main_seige_smoke_large` | 0 | 正常旧曲线烟雾控制两对图像与全部数据逐字节一致 |

正式 after 全 12 图、两个目标 before 4 图已实际查看；其余 before 8 图与 after 字节一致。主任务另复核中央烟柱与 Crowman .6 图。结构和字节证据不代替游戏原生视觉验收。入口为 `official-sampling-execution-v1.json`、`official-sampling-comparison-v1.json`，失败 2 图单独保留，正式 24 图没有混入中间版。

### 本次正常 Debug 与 Terry 实际观察

- 最终 `DC7A2264…` 的正常 Debug 实际打开完整原版 siege，勾选全部三根；q0 观察到 7.03／3.07 秒、q1 1.83 秒的连续烟柱，中央软黑烟不再缺失。中间版另做过中央层单独显示诊断，不能把那次记录冒充最终版本。
- Bear q0 实际选择、侧移、转向和停止，蓝白雾与细雪保留；播放至 21.06／34.18 秒仍连续。Pox q0 同样拖动与转向，绿雾环和原碎块可见，19.45／32.90 秒保留连续轨迹和停止后的尾部消退。没有本轮精确旋转角度验收结论。
- Crowman q1 确实进行了选择、拖动、暂停和回看，但本轮正常窗口截图没有分辨出足够清晰的羽毛几何；**不把这一项写成实机视觉通过**。其 .6／1.2 原模型离屏图像及姿态数据已重新确认与 R98 完全一致。不要为截图延长原寿命或改亮度。
- 纳垢完整组合 q0 实际观察 2.61／4.95／5.71 秒的烟柱，11.84 秒已无可见效果，回到 0 保留原预热；没有重现中间版的缺省颜色异常。原黄色模型材质限制继续保留。
- Terry 加载本轮只引用原名的 siege 场景，确见持续变化的蓝色照明烟柱；实际复核泛光未勾选，预设“高”可见，开场前特效质量“高”可见。蓝色照明与 AECN 灰烟的视角／阶段／照明尚未对齐，**不用于调色或宣布同原生最终画面**。未执行游戏实测；未再盲找尚未证实的 Terry 精确 seek 控件。
- 全部窗口操作沿用持续授权，未检测到用户键鼠冲突；所有源页无 dirty 标记，未保存原件。证据为 `parent-validation/normal-debug-gui-v1.json`。Terry 场景在 Assembly Kit 的 `working_data/AssetEditor.CN/TerryPreview/r99-original-siege-smoke-20261026/`，不覆盖旧场景或 Pack。

### 保留与接续边界

本轮只增加小型原生合同、一个原 XML／一张原 DDS 测试件、单 DLL 基线及有限截图；所有旧源码、正常 Debug、独有备份、工具和失败记录保留，不清理或搬盘。稳定尺寸契约已更新现有 `docs/vfx-editor.md`。本轮产物盘点、旧成果保护、最终状态与散列见 `parent-validation/final-audit.json`、`workspace-after.json`。

本次中央烟层的尺寸曲线阻断已修。中央 ForceA／ForceUnsupported、原 mipmap 诊断、Bear 转动挂点的 ForceBasis、复杂 q 组合、全局随机／时钟、完整光照与透明合成仍有边界；Crowman 清晰窗口帧及 Terry 同条件原生对照仍未通过，不宣称所有攻城烟雾或全部 trail 已达到游戏级。


## R98 动态角阻尼与 Pox FullLocal（2026-10-26）

### 现场、来源与实现

- 沿用 `codex/vfx-preview-native-parity-r97`，HEAD 仍为 `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`，暂存区为空；完整接手快照为 `artifacts/vfx-dynamic-local-r98/parent-validation/workspace-before.json`。既有 18 项修改／未跟踪成果全部保留，没有切分支、创建 worktree、暂存、提交、推送、PR、合并或清理。
- 开测前用户明确回复“现在可用，我暂不使用键鼠”。实际操作仅本任务正常 Debug 和 Terry；未检测到键鼠冲突。正常 Debug 仅加载 manifest 原版 Pack，未打开其他工程／Mod；额外索引当前 26 个非 manifest Pack，R97 三根所需 21 项资源无同名覆盖。新离屏解析只使用 manifest 中核对过实际大小的官方 Pack，并记录完整依赖来源／SHA；没有安装或覆盖旧 preview Pack。
- 新增 `VfxSpriteNativeSpinDamping`：严格 raw 0／1 的相同双表、绑定 0／1、已核实的非 Bezier 解析模式，按原生 `i/128` float 烘焙后用原 half 函数量化。可移植采样为 normalized linear-clamp，末端取最后实际 texel，不能补成源曲线的 1 端点。新负值／高值动态分支限独立出生、birth/spin XY 为零及既定普通材质边界；旧 0～5 world／position-only 动态规则保持。
- 普通 GPU controller 的原算术执行 64 组及 CS 后缀独立解释 40 组，闭环 FullLocal 的节点增量：先世界 Z 阻尼／自身四元数步进，再以 `currentNode*conjugate(previousNode)` 同时运输姿态与世界角速度；Pox 恒定 Emitter X90 在原生增量中相消。新增普通 FullLocal 沿向量角状态路径，Tick 同时提交 q／omega／root，Frame 和暂停当前挂点计算副本；dt=0 仍运输节点。限定独立纯 Z 出生、恒定 Emitter，复用已消费的随机通道；不扩大动态 Emitter、relative／倾斜／XYZ 出生或事件。R97 既有常量 complex 与 R96 模型规则保持，全局 `1.0/120` 不变。
- 原生 half 表逐值与过滤误差分开证明。新增 fixture 含 17×128 实际烘焙 half 值及独立 GPU 数据；当前 RTX 的 8-bit 权重／输出 half 舍入只是设备观察，产品没有硬编码，不承诺跨 GPU bit-exact。证据总入口：`native-contract/r98-native-angular-contract-v4.json`；本轮所有相对证据路径均以 `artifacts/vfx-dynamic-local-r98/` 为根。

### 本次实际构建与自动检查

- 新 FullLocal 与采样专项首次严格红灯：59 项中 **9 失败／50 通过／0 跳过**，见 `parent-validation/red/red.trx`。接入后 v2 的 75 项中 14 项只差 Backward 长度 0.5；新测试 Oracle 把普通平面的 Z 也乘了 1.5。当前及 HEAD 原实现均显式令普通面片 scale.Z=1，故只修正新 Oracle 的 Z 轴，没有改产品尺寸、角度公式、时段或 `1e-5` 断言；失败记录保留。
- 最终受影响测试项目 Debug 构建 **3 警告／0 错误**；角运动专项及已有相关回归 **328 通过／0 失败／0 跳过**，见 `parent-validation/angular-v3/angular-v3.trx`。另按 R97 回归类排除本次已测类，补跑 **245 通过／0 失败／0 跳过**，见 `parent-validation/regression-extra/regression-extra.trx`；两批分别记录，不冒充一次全仓测试。
- 两个新测试文件最后只整理为仓库要求的 BOM／CRLF，重新构建测试项目后，新增三类 **79 通过／0 失败／0 跳过**，见 `parent-validation/encoding-v4/encoding-v4.trx`；不与前两批重复累加。产品 Core 未变，其他已通过范围复用本次对应结果。
- 实际命令包括 `dotnet build Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj --configuration Debug --no-restore`，以及 `dotnet test ... --configuration Debug --no-build --no-restore --filter <证据日志中的专项类筛选>`；GPU 项开启 `ASSETEDITOR_VFX_HEADLESS_TEST=1`，采用隐藏设备，不发送桌面输入。
- 正常输出实际执行 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore`，退出 0、**0 警告／0 错误**。正常 Debug Core 为 `EA90458BCDCA9857F7C3D92C53889DA87491AEA550A27B1552B2FDFE7CF8B0FA`；原生桥仍为 `026F432C…`，没有 C++／材质／ABI 修改或原生重建。初次测试构建的跨模块既有 nullable 等警告原样保留于日志；未运行完整解决方案测试。

### 完整官方原件离屏复测

R97 原 Core `97194345…` 仅备份一份 DLL，before／after 两进程使用同一 probe、参数、当前正常 Debug 其他依赖；未复制 runtime 或展开资源库。probe 构建与两次执行均退出 0，14 对共 28 张图全部可见。逐层非角状态、依赖 SHA、采样条件及已有 flags 前后一致；变化仅在 Bear `breath_snow_flecks` 和 Pox `pox_trail_head_main` 的角状态。另五根共 10 对 PNG 与帧数据完全一致。实际查看目标前后全 8 张及控制 after 10 张，主任务另复核两个目标的 1.2 秒前后共 4 张。

| 完整官方根 | 本次 q／源阶段 | 实际覆盖与边界 |
| --- | --- | --- |
| `wh3_main_entity_bear_ice_breath_trail` | q0；.6／1.2；移动与转向 | 雪点负值→正值 exp3 自旋恢复；白雾、发射方向及密度控制保持。旋转挂点下 ForceBasis 两版仍提示，不能称全部力表现原生 |
| `wh3_dlc29_pox_maggoth_trail` | q0；.6／1.2；移动与转向 | FullLocal 主层 50 阻尼、自旋与节点运输生效；整体拖尾保持，细节角姿变化较小 |
| `wh3_dlc25_sorcerer_of_nurgle_death_trail` | q0；.6／1.2；距离发射 | 纹理 trail 的既有正值动态路径，两对完全一致 |
| `wh3_main_seige_smoke_large` | q0；.6／1.2；原始负时间预热 | base／end_fade 两层完全一致；中央 `smoke_central_pillar` 两版都因 ParticleScale 的旧不支持插值被停用，并非完整三层已正确 |
| `wh3_dlc24_cth_crowman_trail` | q1；.6／1.2；移动与转向 | R96 模型姿态与绘制两对完全一致 |
| `wh3_main_abilities_ursuns_roar` | q0；3.80／3.95 | 本次 pulse 实际各有 1 粒子、glitter 各 9 粒子，补上 R97 .35／.8／1.6 未覆盖的 pulse；随机 BurstInterval／Length 仍有原诊断 |
| `wh3_dlc25_ability_baleflame_amulet` | q0；.35／.8 | R97 高 20 阻尼控制两对完全一致 |

执行及逐层／逐图记录：`official-sampling-execution-v1.json`、`official-sampling-comparison-v1.json`。没有以“全部非空”、自动数量或字节一致代替原生视觉验收。

### 本次正常窗口观察与未完成的原生对照

- R98 改产品前，用 R97 正常 Debug 实际打开完整 Ursun、Baleflame、Unclean Weapon，分别观察早晚阶段的冰枝、紫雾及绿雾；见 `parent-validation/r97-normal-debug-gui-observations.json`。这是本次新窗口观察，R97 原离屏记录仍是历史证据。
- 修复后再次启动正常 Debug，官方 Pox 实际选择、连续拖动、发出 R/Y/90/确认的预览操作，播放超过 117 秒仍持续显示绿色云环与原始碎块；暂停后拖动，时间保持 117.02 秒并跟随挂点。GUI 没有独立读回数值角度，不据发键推定已精确转了 90°。官方 Bear 实际播放、选择与拖动，蓝白雾和细雪可见，连续播放至 51.54 秒；暂停后保留状态。两页均无文档 dirty 标记，未保存或修改原件。见 `parent-validation/normal-debug-gui-after.json`。
- Terry 实际从“打开其他工程”加载只引用官方原名的 Ursun 场景，源 XML／贴图未改。三维视图设置中泛光原为勾选，本次实际取消并复核；Warscape 特效质量观察为“高”。一次滚轮误作用于预设，将“高”改为“极高”，已立即从下拉恢复“高”，没有点击应用。现有旧 preview Pack 保持原状。
- **Terry 同相机／同播放阶段验收仍未完成。** 场景能见初始微光，之后并未建立准确播放秒数；未证实主 Terry 提供精确 VFX seek／暂停入口，不能把 Cindy 或通用 widget 的控件当成主 Terry 的能力。文件相机预设不是生效证明，也未将其与 AECN 自动取景冒称对齐。故本轮只能报告代码原生合同、离屏前后和正常窗口观察，不能报告完整 Terry／游戏最终一致。

### 当前保留与接续边界

保留正常 Debug、原 R97 及本轮全部失败／有效证据、工具和单 DLL 基线；本轮证据约 15 MiB，无运行库或资源库副本，不清理、不搬盘。长期技术边界已写回现有 `docs/vfx-editor.md`，未新增长期流程文档。最终保护／源码合同／测试及产物盘点见 `parent-validation/final-audit.json`。仍需解决 Terry 精确阶段与相机对齐、攻城中央烟 ParticleScale 插值、普通不同双表／Bezier／复杂出生及动态 Emitter 的未证路径；Bear 旋转挂点的力与完整材质／透明合成、随机时钟边界继续有效。受限 Bear／Pox 角运动已修，不宣称所有 trail 或全部 VFX 已正常。

## R97 普通面片常量角阻尼：负值、高值与异常层隔离（2026-10-25）

### 现场与最终实现

- 接续用户“好的，继续吧”，保持 `codex/vfx-preview-native-parity-r97`，HEAD 仍为 `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`。开始时已有前置文档与测试候选全部保留，见 `artifacts/vfx-native-parity-r97/parent-validation/resume-before-v1.json`；不新增分支或 worktree，不暂存、收起、回滚、清理、提交、推送、PR 或合并。全局 `1.0 / 120`、材质／C++／ABI／UI／Kitbash 未改；用户继续自行实机测试。
- 普通面片新增受限常量负阻尼与高阻尼，不再一概清零自转。只接受两个实存 raw 通道 `x_min/x_max`、各一个关键点、float 值相同、绑定 0／1、half 可表示的输入；world／position-only、无专用网格材质／MeshLoop／自带事件，保留原方向及出生限制。新 `VfxSpriteConstantSpinDamping` 先投影为原生 signed half 值，经 emitter 字段真正传入模拟；纯 Z 走离散更新，倾斜／相对上轴或多轴走世界 Z 阻尼，不衰减世界 X/Y。旧 0～5 及模型路径保持。
- 新单层范围隔离防止 `exp`／角速度／四元数不可表示时把非有限矩阵送入原生桥。Tick 才提交失败状态；部分帧失败只隔离该次投影，不污染后续提交历史。回退／重播恢复范围后清除本分支追加的警告，不误删原转换或模型警告；不添加任意角速度上限。
- 初期动态正向测试候选已在 `parent-validation/test-candidates-before-constant-scope/` 保留。发现真实普通 GPU 动态表规则与旧连续取样不同后，正式实现收窄为有完整证据的常量；动态、单通道、不同双表、错误绑定、多点、FullLocal 等继续保留明确拒绝断言。没有为通过而放宽容差，也未把少量成功样本扩为所有 trail 已修好。

### 本次重新执行的原生与参数证据

- fresh 官方 DLL／manifest 与 271 个官方 Pack 名称、大小核对一致。原生上传链 `69ce30 -> 586890 -> 585770 -> 804e40 -> 6d3a20` 证明阻尼以倍率 1 写入两个原始通道的 half 行，没有 0～5 或负值 clamp；既有捕获的实际绑定为 `R16G16B16A16_FLOAT`。证据为 `native-contract/ordinary-constant-upload-contract-v5.json` 及其指向的原始指令记录。
- 直接执行官方纯函数取得 7 条诊断曲线、1792 个烘焙样本，原生 half 端口另经 103627 个 float32 输入逐位比较。用于产品回归的独立小 fixture 为 `Testing/GameWorld.Core.Test/Vfx/Fixtures/NativeSpriteHalf.json`，53 个实际原生输入／输出对，含负值、非整数、高值、次正规数、正负中点及溢出边界。原生中点向零：例如 `-1.00146484375 -> -1.0009765625`、`65520 -> 65504`，不能默认使用 `System.Half` 舍入。
- 普通 GPU 动态表实际按 `i/128` 求值后 half 量化，再由纹理采样；模型 CPU 是 `i/127` float 表。缺 raw channel1 的独立执行返回 0，文本导入是否复制单通道仍未闭环；因此本次不猜单通道或任意链接，不复用模型表开放普通动态。相同常量双行不依赖尚未标定的时间／随机行过滤精度。普通 CS 只衰减世界 Z，再归一化离散四元数；模型仍为 XYZ。

### 本次构建、自动测试与失败记录

- 为取得当前 HEAD 的真实基线，先重编未改产品的源码，Core 为 `B7DBF01FDD125A637748C4C6F81A1889C945B43A67A881EF8603EB6F30163722`。它与旧 R96 冻结库的编译散列不同，正式 before 没有冒用 E 盘旧 `3F6A…`；仅额外保留一个约 1MiB 的 `parent-validation/baseline-core-v1/GameWorld.Core.dll`，没有复制整套运行库。
- 初期候选红灯为 42 失败／55 通过／6 跳过，原记录保留；收窄并明确双通道结构后的正式红灯 `red-tests-v3/red.trx` 为 **34 失败／75 通过／8 跳过**。其时 converter 尚未开放新守卫，失败不是缺方法或旧程序集造成。
- 首次绿灯 175 项中仅“复杂负阻尼、引用速度 2”超出 `1e-5`。独立纯 CPU double Rodrigues 诊断确认，原 oracle 忽略原生每步 float 时差、expf 和角速度存回，75 次增长放大误差；参考矩阵仍用独立 double Rodrigues，仅恢复原生标量精度，误差从约 `1.64e-5` 降至 `2.62e-7`。阶段和 `1e-5` 容差不变，未把产品改为 double 迎合测试。见 `parent-validation/complex-oracle-f32-audit-v2.json`；失败结果 `green-tests-v1` 保留。

| 本次最终检查 | 真实结果 | 记录 |
| --- | --- | --- |
| 正常 `AssetEditor/AssetEditor.csproj` Debug 构建，`--no-restore` | 退出 0，0 警告／0 错误 | `parent-validation/normal-debug-build-v1.log` |
| 受影响 Core 测试项目 Debug 重建 | 退出 0，3 警告／0 错误 | `parent-validation/green-build-v2.log` |
| 普通动态／复杂自转与新 half 专项，含隐藏 GPU | 175 通过／0 失败／0 跳过，退出 0 | `parent-validation/green-tests-v2/green.trx` |
| 既有出生角、连续 trail、图集、法线翻转及模型 signed／曲线／局部／动态方向等较广回归 | 319 通过／0 失败／0 跳过，退出 0 | `parent-validation/regression-v1/regression.trx` |

两组最终测试分别执行，不冒充一次全解决方案验证；未运行全量测试、Release、产品窗口、Terry 或游戏。隐藏 GPU 显式设置 `ASSETEDITOR_VFX_HEADLESS_TEST=1`，无真实键鼠／焦点输入。主程序与测试输出 Core 同为 `971943455C27F0336FF821E894672239617EC16E12E82759E802B9E392EE85D7`；原生 DLL 保持 `026F432C…`，未改原生源因此未重编原生桥。

### 本次完整官方原件离屏复测

- 7 个完整根、21 对同条件阶段图，真实资源从 7 个官方 Pack 直接解析，根与依赖 SHA、画质、相机、投影、挂点和阶段全部一致，无 Mod 解析器。正式 before 为 `gpu-before-v1` 的 6 个完整根，加 `gpu-before-v2-crowman`；after 为 `gpu-after-v1`。两处 before Core 均为上述 B7DBF 基线，未混入修复后运行库。
- 第一次 before 漏载官方 `warmachines_dds.pack`，Crowman 缺纹理而无粒子，失败 3 图保留但不计正式覆盖。补齐 Pack 后重读此前 37 个有效依赖，来源／SHA 0 差异、旧路径 0 覆盖，只补跑 Crowman 的 3 图。整个证据目录只生成 45 PNG，其中正式 42、失败 3；不导出资源、不复制运行库、不生成 raw32f。

| 完整官方根 | 画质与阶段（秒） | 本次覆盖 |
| --- | --- | --- |
| `wh3_main_abilities_ursuns_roar` | q0；.35／.8／1.6 | `attack_buff` 负 -1 旋转恢复；.35 仅 1 个变化像素，视觉证据弱；`attack_buff_pulse` 转换诊断已恢复，但三阶段无活粒子，未验收其外观 |
| `wh3_dlc25_ability_baleflame_amulet` | q0；.35／.8／1.6 | `after_smokey` 高 20，旋转恢复；与火焰／烟雾完整组合共同绘制，变化较细 |
| `wh3_main_entity_great_unclean_one_weapon` | q0；.35／.8／1.6 | `weapon_mist_spherical` 高 20，黄绿小雾团旋转恢复；当前视距下细节判断有限 |
| `wh3_dlc29_pox_maggoth_trail` | q0；.6／1.2／2.2，转弯停止 | FullLocal 高 50 **仍未修**，作为明确未改变的剩余问题控制 |
| `wh2_dlc11_deep_spell_smoke_trails` | q0；.6／1.2／2.2，转弯停止 | 移动烟雾控制 |
| `wh_dlc03_beasts_amber_spear_trail` | q1；.6／1.2／2.2，转弯停止 | 带贴图 trail 控制 |
| `wh3_dlc24_cth_crowman_trail` | q1；.6／1.2／2.8，转弯及受控旋转 | 模型 trail 控制；2.8 已为空，不把空阶段计作可见性证明 |

- 三目标共 9 对只改变对应层的角状态及投影／PNG；所有 21 对、所有层的非角状态摘要全部相同。四个控制共 12 对的 PNG 字节和投影 Frame SHA 全部相同；没有新增范围告警。目标仅移除 4 条相应的 `ForceUnsupported` 并增加 4 条 `SpinCandidate`，未改变其他诊断。
- 本次确实查看了现有多阶段图：Ursun .8／1.6 的冰星枝条朝向变化可辨；Baleflame 扩散软烟／地面残留及小雾团保持颜色、密度和透明边缘，没有新增白实体片或矩形硬边。高阻尼烟雾变化细微，主要精确角度证据来自参数／独立姿态像素对照。控制中烟团扩张、Amber 弯折亮带／烟／火星与 Crowman 羽片继续可见。完整比较与逐图查看记录在 `official-final-comparison.json`；未与同条件 Terry 或游戏进行本次对照，不能称原生视觉验收。

### 剩余与接续

本次受限常量路径完成实现、正常 Debug、专项／较广回归和完整官方原件离屏复测；用户实机仍待验。动态负／高阻尼需要专属 `i/128` half 表及纹理过滤，不能继续使用旧连续曲线冒称原生；Pox FullLocal、Bear 动态负阻尼、投影贴花高阻尼等仍未由本轮解决，pulse 的其他阶段也未验收。既有 R96、材质／合成／天气／随机时钟边界继续有效。后续先按未闭环的共享采样／空间路径推进，勿回到已确认原生格线反复调材质。

## R97 前置文档补齐与调查（历史阶段，2026-10-25）

### 当前分支、交付与任务切换

- 本轮按用户“在新的分支继续完善 VFX”指示，从已 fetch 核实的 `origin/master` 创建 `codex/vfx-preview-native-parity-r97`，基点 `9d2ff8caf62d343ace4b0517f7f31646d33cdd6d`。已核实该基点包含前轮 VFX 提交 `50bba3d8e683c28f4438c2fdf870c3822b67993d`；建立后工作区、暂存区均空。分支没有配置推送到 master 的 upstream，未创建 worktree。
- 先开展了普通面片负值／高角阻尼的原生与官方样本调查；随后用户要求“先补上关于 VFX 的各项文档和更新 AGENTS.md，根据 ChatGPT 官方文档指导来写”，因此停止实现及扩样，先完成本节记载的文档工作。没有新的渲染修复可以宣称已交付。
- `Testing/GameWorld.Core.Test/Vfx/VfxDynamicSpinDampingTests.cs` 保留本轮先前写下的 signed／高阻尼测试候选，产品实现尚未对应修改；该候选未构建、未运行，也未形成有效红灯／绿灯结论。复杂旋转、溢出与新 GPU 用例尚未编写。后续实施先核对候选差异，再按原生证据完成实现和实际验证，不能直接把测试改动视为支持能力。
- 产品 C#／C++、材质、全局 `1.0 / 120` 和正常 Debug 未修改；未启动 AECN、Terry、游戏或任何可见测试窗口，无键鼠／焦点操作。未暂存、收起、回滚、清理、提交、推送、创建 PR 或合并。开始快照为 `artifacts/vfx-native-parity-r97/parent-validation/workspace-before.json`。

### 本次补齐的文档及核验来源

- 实际查阅 OpenAI 的 [AGENTS.md 指南](https://learn.chatgpt.com/docs/agent-configuration/agents-md)与 [Codex 工作建议](https://learn.chatgpt.com/docs/learn/best-practices)：根指令保留准确、可执行的长期约束，领域上下文按任务链接读取，阶段结果留在交接。这里引用的是文档组织指导，不是 VFX 着色器或游戏语义来源。
- 新增 `docs/vfx-editor.md`，把原 `docs/architecture.md` 的 VFX 详细契约按文档／保存、共享预览、画质／曲线、引用／事件、播放器／连续 trail、发射／运动、角阻尼、力、图集、材质、深度、DISTORTION、天气分节；补上修改路由、正常 Debug 与分层验证、官方选样及未完成边界。总体架构保留原 VFX 和验证锚点及跨模块入口；迁移来源记录为 `parent-validation/document-migration.json`。
- 同步根 `AGENTS.md`、主 `README.md`、`docs/README.md` 的 VFX 路由；`docs/ui-design-system.md` 明确专属 UI／交互和公共播放器；`docs/superview.md` 明确多实例、参数／资源归属、官方依赖和共享渲染限制。具体 UI 规范仍只在现有设计系统维护。
- 核对当前转换器、模拟、共享预览、ViewModel／XAML 和测试源后修正过时表述：模型 CPU 四元数规则限实际进入对应分支的模型；普通面片仍有负／高／Bezier 守卫；DISTORTION 的 AlphaCrush 已独立接通；两个 VFX 布局用例使用 WPF 宿主离屏渲染，并非调用 `Window.Show`；超级视图已使用受控显示变换，曝光不再笼统称为“仅记录”。本次是源码核对，没有把这些已有用例写成本次执行。

### 本次已执行的调查，尚未成为修复

- **官方源结构**：仅扫描当前官方 manifest 中的 `vfx_desc.pack`，7470 个 VFX XML、14647 个源层；838 个材质 XML 明确排除，解析错误 0。普通 `continuous_mesh` 声明中带 spin 的负阻尼 4 层、超过 5 的 9 层，没有找到多关键点 Bezier 角阻尼段。计数包含库与禁用层，不是有效实例或失败数量，也不覆盖所有可能的描述文件位置。证据：`official-samples/source-coverage.json`。
- **当前转换器实际复现**：10 个未经修改的完整官方根，q0，静止挂点，.25／.5／1／2 秒 CPU 取样；直接读取 manifest 验证的 6 个官方 Pack，逐根／依赖记录 SHA，0 缺失、0 异常。确认下表 8 层因负值／高角阻尼报告 `ForceUnsupported` 并清零旋转。没有 GPU 像素、动态挂点或实机验收。证据：`official-samples/converter-before-verified6packs.json` 与 `investigation-status.json`。

| 完整官方根（均在 `vfx/`） | 层 | 原始角阻尼 |
| --- | --- | --- |
| `wh3_dlc25_ability_baleflame_amulet.xml` | `after_smokey` | 20 |
| `wh3_dlc29_pox_maggoth_trail.xml` | `pox_trail_head_main` | 50 |
| `wh3_main_cth_iron_dragon_breath_01.xml` | `breath_flame_gpu` | -1 |
| `wh3_main_entity_bear_ice_breath_trail.xml` | `breath_snow_flecks` | 动态 -0.001559～2.272023 |
| `wh3_main_entity_great_unclean_one_impact.xml` | `slime_stream` | 400 |
| `wh3_main_entity_great_unclean_one_weapon.xml` | `weapon_mist_spherical` | 20 |
| `wh3_main_abilities_ursuns_roar.xml` | `attack_buff`、`attack_buff_pulse` | 各 -1 |

- `bone_giant_impact`、`cine_impact_sand` 与 `deep_spell_smoke_trails` 是未出现该特定阻尼诊断的控制根；最后一项静止挂点下全部 4 阶段粒子数为 0，需要运动才能调查，不能记为视觉正常。首次仅加载 5 个 Pack 的 `converter-before.json` 遗漏 `commontextures.pack`，保留为失败调查，已由上面的 6-Pack 结果替代，不作为正式依据。
- **本次原生消费端核对**：fresh 官方 DLL、manifest 散列及 271 个官方 Pack 大小复核一致，重新读取的普通 simulation／spawn CS 与既有原始字节一致。普通 CS 本体仅对世界 Z 做 `exp(-lambda*sourceDt)` 再执行离散归一化四元数更新；模型 CPU 是 XYZ 衰减，不能套用。CS 消费端没有负／高 clamp，但 CPU 上传是否保留符号、纹理格式和准确采样坐标尚未闭环；恒定纯 Z 的旧解析积分也不等于该 CS 精确离散语义。证据：`native-contract/fresh-official-resources-v1.json`、`ordinary-cs-contract-v1.json`。未据此放开任何守卫。
- 两个极小调查工具分别成功构建，官方样本工具退出 0、0 警告／0 错误；复用正常 Debug DLL，不复制运行库或展开完整资源。没有构建产品／测试项目，没有运行测试套件，也没有新原生桥构建。ForceA＋风、FullLocal、动态发射器朝向目前只是源参数覆盖统计，尚不能宣称均有实现错误。

### 历史证据清理后的真实入口

- 下方 R96 原文中的“全部保留”“满盘”等均是当时现场，后续用户已经授权清理。不能根据旧路径存在于交接就认定文件今天还在，也不能把被永久删除的整包声称为可恢复。
- `artifacts/ARCHIVE-INDEX-20261025.json` 是清理后总入口；D 盘精确重复文件旧路径与现存相同字节代表文件见 `artifacts/cleanup-second-pass-20261025/duplicate-path-map-assets.json`、`duplicate-path-map-binaries.json`。这些映射只覆盖已核实的重复内容，不表示废弃独有输出或全部截图可恢复。保护清单和最终结果为同目录 `protected-evidence-index.json`、`final-audit.json`。
- E 盘旧大 ZIP 已永久删除；仅保留 fixture 明确引用的原图及索引 `E:\AECN-artifacts-cleanup-20261025\essential-vfx-evidence\index.json`。独立正式 R96 证据 `E:\AECN-VFX-R96-evidence` 仍保留，本次已核对正式总审计及阶段板目录存在；其旧结果仍是历史验证。
- 19 份临时扫描表最后按用户要求送入 D 盘回收站，没有在原位置保留，也没有再永久删除或清空回收站。逐项恢复记录见 `artifacts/cleanup-second-pass-20261025/scan-table-recycle-final.json`。后续取证先查上述小索引，避免重新复制整库、运行库或大规模连续截图。

### 接续条件

文档先行任务只验证文档内容、相对链接／锚点、迁移完整性及差异；未执行的构建、自动测试、离屏和用户验收均不能写成通过。后续 VFX 实施沿当前分支，首先闭环普通角阻尼上传／采样并核对测试候选，再修共享实现，验证异常层隔离及既有模型／面片控制组合；完整官方动画离屏与用户实机仍分别验收。未经新授权不提交、推送、清理或发布。

本次文档验证：`parent-validation/verify-documentation.py` 退出 0，50 处本地链接／锚点无误；95 条原始来源全部有去向（其中 1 条去重），VFX 节之外的总体架构与 R96 及更早交接正文保持一致，9 个关键保留证据入口存在。`git diff --check` 退出 0；另行只读复核未发现迁移丢失或普通负阻尼被误写为已支持。详细结果为 `parent-validation/documentation-audit.json`；这不是产品构建、自动测试或视觉验收。

## R96 模型 trail：负阻尼、原生 Bezier 与动态发射器朝向

日期：2026-10-25。源码仍在 `D:\Project\Szy-Cathay\TheAssetEditor-CN`，分支 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。实机验收由用户负责。

### 现场保护与证据位置

- 开始时 237 个修改／未跟踪文件逐项状态、长度和 SHA 与 R95 `workspace-after.json` 一致，暂存为空、index 不变。R96 的 `workspace-before.json`、全部 `source-before/` 与 8 个正常 Debug 备份保存在仓库 `artifacts/vfx-model-curves-orientation-r96/`。
- 未切换／新建分支或 worktree，未暂存、收起、回滚已有成果、提交、推送、PR、合并或清理。原正常 Debug、备份、工具、采样、失败和 Mod 资料全部保留。全局 `1.0 / 120` 未改。
- 主程序和专项构建／测试完成后，D 盘写满（实际余量 0）。D 上不完整的 after 副本、部分 before 和失败写入均保留且不纳入正式结果。后续只新建本目录，未搬移或清理旧文件；代码及正常 Debug 输出仍在原仓库。
- 新增较广回归、旧运行库红灯、正式画面和最终盘点位于本目录。`docs/architecture.md` 仅原地更新模型段落；满盘时现有交接头先链接本记录；随后只读核查 D 空余回升约 1.77GB（原因未确认、非本任务清理），最终将本轮正文按常规接回现有交接，R95 及更早正文逐字节保留。

### 本轮实现与原生证据

- 模型负阻尼不再被清零。fresh C75d40a3… 官方 DLL 证明，角阻尼只按源寿命阶段取值，再直接 `expf(-lambda*sourceDt)` 乘 XYZ，没有原生 0～5、负值钳制或速度上限。极端 exp／角速度／四元数不可表示时，预览诊断并停用对应层，其余层继续；这不是新增角速度 cap。
- 新 `VfxModelNativeCurve` 独立实现模型标量表，不改普通 `VfxPreviewCurve.Evaluate`。原生按 `float(i)*float(1/127)` 烘焙 128 点；两侧只有单关键点时可压成 1 点。Bezier_c 使用原生十次 double 参数二分及剩余区间 1% 扫描、float Bernstein 组合、关键点切线增量和端点函数混合，解析模式沿原生 float 多项式。运行时按 phase 插 min/max 表，再按固定粒子 unary 混合；不直接借用旧 24 次二分候选。
- 通过隔离映射官方 Utility PE 的代码／常量页直接执行纯求值函数取得独立 fixture，不运行 DllMain，不打开产品／Terry，不触碰用户进程。59 项 sampler 检查包含 5636 个实际原生烘焙 float 和 648 个实际 reader 结果。flag0 指向不同通道时，只有全表 float 严格相同才允许；真正不同双表继续拒绝，不改写官方源件。
- 新 `VfxModelNativeOrientation` 用 XYZ min/max 分别烘焙 `qz*qy*qx`，先时间、后同一控制器 unary 逐组件 LERP，再归一化；不先插 Euler，不做 shortest-path 翻符号。相对上轴出生与后续跟随使用同一控制器权重，保留原随机调用消耗；MathF 三角键不宣称与原生 SSE 逐位一致。
- 动态 FullLocal 在自身 XYZ 阻尼／q 积分后运输姿态及角速度。行矩阵令 A=当前完整节点、B=前次节点、C=当前发射器、D=前次发射器，增量为 `A^T*D^T*C*A*B^T*A`；对应 Hamilton `deltaNode*nodeNow*deltaEmitter*conj(nodeNow)`。100 组原指令非交换控制全部匹配；静态引用必须留在完整 CurrentRoot 的当前节点共轭中。常量发射器走原 R95 分支，避免改动 Crowman 已确认运算。
- 原生 controller+10c 是固定控制器 unary；+11c 是进入更新前累计的发射器时钟，实际先更新后推进。预览明确采用以 Start 为源时零、按发射器 duration／倍率取连续端点的映射；Tick 提交，部分帧和同时间显示仅计算副本。真实原生首帧标签偏移及游戏时钟仍未知，不将预览映射称为游戏同帧同步。
- 出生循环读取 `EmitterOrientationLoopAnim`；跟随循环取源显式 InfiniteLife 与父实例允许循环的交集，任一 reference false 关闭跟随循环，不改出生开关。源缺省 duration 导致的预览 infinite fallback 不冒充原生 InfiniteLife。官方动态朝向目标两 flag 均 false；其他组合另以诊断 fixture 检查预览父实例映射。
- 普通 DynamicSpinAt／ComplexSpinAt、出生随机调用、CurrentRoot／ParticleRoot、普通守卫、原生桥／ABI／材质及既有 R95 0～5 正解析路径保持。负／Bezier 新路径恢复自旋后会恢复原 Spin 随机调用，不能声称其后续随机状态与旧版清零实现一致，也不代表已同步原生全局种子。

原生证据主要在仓库 `artifacts/vfx-model-curves-orientation-r96/native-curves/`、`native-orientation/`；完整最终方向审查为本目录 `native-orientation/final-product-review-v2.json`。D 上失败的审查 partial 不能作为有效 JSON。架构段落已在审查后按实际范围更新，见 `parent-validation/architecture-update-audit.json`。

### 本次构建与自动验证

- 正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore --verbosity quiet` 成功，883 警告／0 错误，退出 0；Core 构建 1018 警告／0 错误，最终测试项目重建 3 警告／0 错误。未发现运行的本仓库 Debug 实例，未关闭任何用户程序。
- 最终正常 Debug Core SHA：`3F6A76EC6018523ACBC7D3CB650B2CFD2750F65219E6A47CBC6B11C919837C8C`。native 仍为 `026F432C194DB589DBD199FB712C1458DB7403D5A24451D5B9D7F14021A2C1C2`，无 native 构建。本轮正常输出、测试及完整正式 after 运行库字节一致。
- 新检查 130 项：17 signed、59 原生 sampler、24 模型表驱动运动、30 动态 EmitterOrientation（26 CPU＋4 隐藏 GPU）。GPU 与独立静态世界姿态比较完整浮点 RGBA，严格 `1e-5`。微型 fixture 均为诊断，不冒充完整官方外观验收。旧 3 个拒绝临时预期被精确正向行为检查替代，没有放宽断言。
- 专项 `artifacts/vfx-model-curves-orientation-r96/special-final-v4/special.trx`：**251 通过／0 失败／0 跳过**。D 满后的较广检查用正常已构建程序集直接 `dotnet vstest`（未重新构建）执行：`parent-validation/core-final-v2/core.trx` **986 通过／0 失败／0 跳过**，退出 0，不是全解决方案验证，不能与专项累加。
- 同一最终 Test.dll（SHA `2B7964C91C3D4E45C516B9384E11BA02981C3DF2FDDF6A645947450C95253A09`）配冻结 R95 CE9E／026F，59 项行为 CPU 为 **58 失败／1 通过**。明确排除直接引用新 helper 的 sampler、GPU 及新溢出属性检查，未以 MissingMethod／TypeLoad 等伪红灯计证。见 `parent-validation/old-r95-red-v2/old-r95.trx`、`red-runtime-scope.json`。
- 保留全部初始失败：signed 首轮旧实现 14／15 失败；首次恢复后两常量参考漏掉 expf／omega 的 float 舍入，独立 UCRT expf 三路控制后，仅修参考精度契约，15 例和 1e-5 不变。新 CPU 首轮 94／98 通过，四个循环 fixture 错设发射 step 端点而无粒子，修正 fixture 并加 late-birth 两例；产品旧曲线未改。专项首次 249／251，通过外新增溢出 fixture 两项因 XML 容器定位为 null 而失败，修测试定位后通过。原失败、脚本、源码备份与原生输入均保留，不作产品失败或未运行成功冒报。

### 本次完整官方原件画面

- fresh manifest 271 官方 Pack，11 个完整根、41 对正式 before／after；94 项根／依赖／原天气 SHA 前后一致，无 Mod 同名覆盖。只用官方 Empire UI 天气，不把它叫战场最终光照。原始启用及父 EnabledLayers 独立核实，完整根不等于所有源层都支持。
- 四目标各 5 对：`wh3_main_abilities_daemon_prince_chaotic_ebb`，.1／.4／.8／1.4／2.3 秒；`wh3_dlc24_entity_be_chest_blast`，.1／.35／.65／1／2.2；`wh3_dlc20_entity_archaon_medium_melee_slash`，.025／.05／.075／.125／.3；`wh3_dlc20_entity_vilitch_melee_arc`，.025／.05／.075／.125／1.2。均 q1、targetY2、受控直行转弯停止、转速40，前后同相机和阶段，距离依次48／64／32／32。
- Ebb 实际是负 lambda 的 mesh_in，Surge 是正 .5 mesh_out 控制，不能混称。Ebb 同 Birth=0 提交角速度模长在 .1／.4／.8／1.4 为 4.116／6.272／8.661／10.541，恢复增长；暗色近轴对称的画面本身不单独证明精确角度。
- 七个 R95 控制各3对，共 **21 对**：Crowman、Tower、Inspired、Locus、Surge、Flower、Amber。完整捕获 Frame JSON（含新增提交模型内态）、1163 个捕获粒子时点以及 PNG 字节完全相同，Crowman 黑色羽毛、姿态与溶解没有回退。
- before2497／after2498 个投影粒子时点，16 个非空目标阶段 RGB 改变。只有 BE rings3／4 存在非角变化，.65 秒455→456；按 birth 匹配仍有 RandomAlpha／Position／Velocity／InitialScale／RandomScale／RandomColour 各25时点、Life22时点变化和一个额外存活 birth。不能把它写成纯旋转变化或所有随机值不变。41 个 ordinal Transform 差异中29个是 BE 复合差异；其余 Ebb／Archaon／Vilitch各4个可分称旋转变化。其他所有层严格相同。
- 11 张无损同裁切阶段板全部实际查看，主要入口 `sample-audit/paired-review-formal-v1/`；Ebb 自转、BE 环的变化、红／紫弧随时间转动及消失可辨。背景并集之外和 sceneAlpha 独立核对一致；不以像素数、可加载或映射层数代替原生视觉正确。最初20帧 pilot、D写满的 partial 均不累计。完整正式结论见 `sample-audit/gpu-final-audit.json`。

### 剩余边界与交付

本轮三条路径完成实现、正常 Debug、自动与正式离屏复测，实机仍由用户验收；没有 AECN／Terry 窗口、键鼠／焦点操作或新增原生捕获。仍不宣称所有 trail 已原生还原：step／其他未核实曲线、flag0 不同表、近反向四元数消去、复杂事件／MeshLoop、非正规挂点、完整物理／PBR／阴影、游戏时钟及全局随机同步均有边界。Dragon 原版缺失 no_texture 依赖继续排除，不通过替换纹理伪验收。

最终保护盘点：`parent-validation/workspace-after.json`、`parent-validation/final-audit.json`。源码及 Debug 仍在 D 原仓库；新增证据在 E。本轮未清理文件；最后只读核查 D 空余约 1.77GB，回升原因未确认，不宣称由本任务修复容量。后续在 D 保存／构建前仍需注意实际容量。

## R95 模型动态角阻尼与完整局部空间旋转（2026-10-25）

### 现场与实现范围

- 原地沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。开始时 236 个修改／未跟踪文件与 R94 最终快照逐项状态和字节一致、暂存为空、index SHA 未变；本轮 `workspace-before.json`、`source-before/` 和 8 个 `runtime-before/` 文件保留全部源码现场及正常 Debug 运行库。未创建分支／worktree，未暂存／收起／回滚已有成果／提交／推送／PR／清理。
- 新模型动态阻尼保留原始自旋，不再因曲线非常量而清零。受限范围为 Kind1、0～5 的常量或非 Bezier 解析曲线、独立或相对上轴出生、无粒子事件／MeshLoop；按当前源年龄／源寿命阶段和同一出生随机权重取样，先 `exp(-lambda*sourceDt)` 同时衰减 XYZ，再执行原生四元数更新。动态纯 Z 模型也走自身规则；恒定纯 Z 的 world／position-only 仍保留旧解析约定。
- 完整局部空间在 `EmitterOrientation` 恒定时，先完成自身自旋，再用前后根旋转差同时运输姿态与角速度。缓存每粒子根四元数，`deltaQ=normalize(nowQ*conjugate(prevQ))`、`qNext=normalize(deltaQ*qSpin)`，omega 由同一 deltaQ 旋转；不重复出生节点绝对旋转，零自旋及恒定纯 Z FullLocal 同样跟随。只有 Tick 提交状态，部分帧和同时间的当前挂点变化计算副本，不污染后续历史。
- 新 FullLocal 已核实正等比缩放后的正规旋转；非等比、镜像、剪切等输入保留候选近似并显示 `ParticleSimulationSpace` 诊断，不隐藏整层。动态发射器朝向仍走原路径且标明未映射；step／Bezier、负值／超过 5 的模型阻尼、事件及 MeshLoop 继续保留原诊断边界，不以删除判断宣称支持。
- 普通面片 `DynamicSpinAt`／`ComplexSpinAt`、其转换守卫、出生 Spin RNG 代码、CurrentRoot／ParticleRoot 和全局 `1.0 / 120` 逐段保持；Crowman world／position-only 原 XYZ 衰减和四元数运算顺序保持。合法动态模型恢复自旋后沿原出生函数取样，不能把旧版清零导致跳过取样的序列当原生随机基准；全局种子仍未同步。本轮不改 UI、Kitbash、材质、C++ 原生桥或 ABI。

### 本次原生证据与失败处理

- 重新读取当前 Assembly Kit DLL（C75d40a3…）、manifest（D60634da…）和 271 个官方 Pack 大小。208 字节 `continuous_rigid` 更新证明：当前 sourceTime 的寿命阶段取角阻尼，全 XYZ 衰减→自身 q 积分→FullLocal 运输；恒定非零发射器朝向可相消，relative 只决定出生姿态，dt0 不跳过根旋转运输。证据 `native-audit/rigid-dynamic-local-contract-v1.json`。
- 独立解释原 861 条后处理指令的 100 组非对称控制，包含任意当前／历史根 q、恒定非零发射器 q、粒子 q、XYZ omega 和零自旋；姿态最大误差 8.54e-7、omega 3.48e-6，错误乘法顺序 100/100 排除。另以独立 double Hamilton 验证上述 q 形式与行矩阵 `Rq*(Rprev^T*Rnow)` 等价，最大误差 8.88e-16。它不证明原生固定 120Hz，也不证明完整 FullLocal 线性物理或游戏最终外观。
- 首轮动态诊断在旧实现上 23 项中 22 失败／1 通过；动态实现后相关 CPU48 通过。局部矩阵版本首轮 CPU64 为 62 通过／2 失败，保留 `source-local-matrix-v1/`、日志和结果。改用等价的归一化四元数 delta 后，专项首轮 121 为 117 通过／4 失败，均在新 FullLocal 角度参考比较中，而非像素断言失败。
- 已查清后四项为测试参考输入错误：真实程序集 RootAt(float) 的 106 个独立时点有最大约 2.998e-7 非正交误差，旧 double oracle 反复将转置当逆矩阵，累积成姿态缩放；.875 秒基轴差约 1.041e-5，scale2 放大。独立 Gram-Schmidt 正规化与 double 作者解析旋转仅差约 2.8e-8～3.7e-8。只修正 oracle 根输入正规化，Rodrigues、全部 44 项及严格 1e-5 容差保留；原失败源码、真实输入、三路量化和两版实现都保留。证据 `test-audit/oracle-root-drift-proof-v1.json`、`final44-rigid-input-correction-v4.json`。

### 构建与自动验证

- 正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore --verbosity quiet` 成功，883 警告／0 错误，退出 0；未发现运行中的本仓库 Debug 实例，未关闭用户程序。单独 Core 两版构建各 1018 警告／0 错误；最终测试项目重新构建成功，3 警告／0 错误。完整命令及原日志保留，本轮不是全解决方案验证。
- 最终正常 Debug Core SHA `CE9EB5283F1C4703F024FDCACB74E095BBB6790E52549495AF0DB21444BB799A`，native 仍为 `026F432C194DB589DBD199FB712C1458DB7403D5A24451D5B9D7F14021A2C1C2`。原生源与 DLL 保持 R94；正常输出、测试及正式采样四个运行库核对一致。只生成正常 Debug，不安装／发布。
- 新 `VfxModelDynamicLocalSpinTests` 为 40 CPU＋4 隐藏离屏 GPU，覆盖解析动态曲线、固定 min/max 随机权重、relative、源时间倍率、纯轴、零自旋、非交换挂点旋转、部分 Tick、同时间拖动副本、回退重播、正等比及非等比诊断。GPU 以独立静态 WORLD 姿态作恒色原 RMV 微型对照，正／反投影均比较完整浮点 RGBA；微型 fixture 不作为完整官方外观验收。
- 专项 `special-final-v2/special.trx` 121 通过／0 失败／0 跳过。最终相关回归 `core-final-v1/core.trx` **856 通过／0 失败／0 跳过**，退出 0；不与专项累加。R94 两个动态“拒绝”临时预期已改为精确保留作者自旋，R94 一项 FullLocal 拒绝被本轮更完整的实际行为检查替代；未放宽断言。
- 同一最终测试程序集在隔离 R94 CoreD172／native026F 上执行新 CPU40，2 通过／38 失败；`old-r94-red-v1/old-r94.trx`、`red-runtime-scope.json`。无 MissingMethod／NullReference 等伪红灯，四个 GPU 明确排除，不将历史测试写成本次重跑。

### 本次完整官方原件画面

- 本轮新采 7 个完整官方根、31 对同条件 before／after，原资源、天气、画质、相机、挂点和播放时点逐项控制；使用官方 Empire UI 天气，不把它称为战场最终光照。来源只从 fresh manifest 官方 Pack 读取，无 Mod 同名覆盖；完整根输入不代表所有原始层都已支持。原始 Enable 与父引用 EnabledLayers 独立核对；全库扫描工具最初 EnabledSource 布尔字段有缺陷，错误输出保留，不用它证明选样是否启用。
- 三个新增目标各 5 对：`wh3_main_abilities_inspired_defence_1`（1.5／1.8／2.2／3／4.5 秒，动态 defence_shield，position-only，独立出生）；`wh3_main_abilities_locus_of_grace`（.3／.6／1.2／2.2／5.5 秒，FullLocal BUFF_glow_sphere，XYZ 常量阻尼）；`wh3_main_abilities_daemon_prince_chaotic_surge`（.2／.6／1.2／2.2／3 秒，FullLocal mesh_out，相对上轴）。沿用受控直行→转弯→停止，前后同画质 q1、同相机、同源时间；分别固定 distance40／40／48、targetY2。
- R94 四根控制共 16 对：Crowman／Tower 各 5、Flower／Amber 各 3。1413 个捕获粒子时点的非角字段本次均相同，只有上述三类目标模型的 12 个矩阵及 12 对 RGB 画面改变；未采集的 MaterialTime、AlphaCrush 及隐藏 RNG 状态不作相同推断。四控制根的 900 个时点、16 对完整帧和 PNG 逐字节相同，Crowman 原黑色羽毛、姿态、溶解和清空没有回退。
- 7 张无损阶段板／31 对已实际查看；Inspired 与 Locus 的旋转变化可辨；Surge 暗色且接近轴对称，差异细微，板图不能单独证明精确原生角度。后期 Inspired4.5、Surge3、Locus5.5 均前后清空。Locus 子层有 InfiniteLife=true 不能据此断言完整根永存，不把空白尾帧写成持续可见性证明。主要入口 `sample-audit/paired-review-candidate-v1/` 和 `gpu-final-audit.json`；正式 after 复用本轮已渲染的 CE9E／026F 候选，依据最终运行库字节一致纳入，candidate 文件名和标题保留，没有虚构重复渲染。初始 12 帧选样探查不累计。

### 剩余与交付状态

- 本轮完成上述模型动态阻尼和恒定发射器朝向下的完整局部旋转实现、构建及自动／离屏复测。step／Bezier、负值／超界阻尼、动态发射器朝向、事件／MeshLoop、非正规挂点、完整线性力／物理、原生随机与游戏时钟仍有限制；R94 材质的完整 PBR／法线／反射／阴影和已有轨迹分布限制保留，不宣称全部模型 trail 原生还原。
- 实机仍由用户负责；本轮没有 AECN／Terry 窗口、键鼠或焦点操作，没有新增 Terry／游戏捕获。正常 Debug、全部源件、备份、工具、采样、失败、诊断及已有 Mod 材料保留，不提交、不推送、不清理。最终保护核对见 `workspace-after.json`／`final-audit.json`；R94 及更早正文逐字节保留。

## R94 Crowman 模型 trail 可见性与独立模型旋转修复（2026-10-25）

### 现场保护及历史纠正

- 原地沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。开始时全部 232 个修改／未跟踪文件与 R93 最终快照逐项状态／字节一致，暂存为空、index SHA 不变；`workspace-before.json`、`source-before/`、`runtime-before/` 保留现场及 7 个正常 Debug 文件。另备份原生编译器、生成头与 native Debug 等 9 文件；没有分支／worktree／暂存／收起／回滚已有成果／提交／推送／PR／清理。
- **纠正 R93 的定位误判**：fresh 当前 DLL 的字符串解析、factory 与实际更新函数证明，96 字节队列属于 `continuous_ribbon`，不是模型；`continuous_rigid` 使用 208 字节 CPU 粒子状态。R93 原文保留为历史，不能继续用它证明模型更新。当前证据 `native-rotation-audit/model-rotation-contract-v1.json`，来源 DLL SHA `c75d40a3…`、271 个 manifest 官方 Pack 已核对。

### 不可见的直接原因及共用材质实现

- fresh 官方 Crowman 原件指定的底图确为 `brt_knight_shield_base_colour.dds`，不是因名字像盾牌就取错资源。该原件 2048² 顶层及全部 12 mip 均为 RGBA 零，产品解码与独立 BC1 检查一致。37 顶点、68 三角全部有效，138 个初始粒子时点都有模型顶点在视锥内；排除纯离屏、退化几何、单纯深度或背面剔除解释。
- 官方原几何／粒子／RGB／相机的单变量诊断中，仅把上传 Alpha 从 0 改 255，就从 0 变成 2880 个黑色羽毛像素；仅设置 Opaque 或关闭 ZTest 仍为 0。该诊断只定位原因，原件与原上传保留，**未把 Alpha255／白图控制当最终修复**。证据 `sample-audit/invisibility-cause-v1.json`、`alpha-cause-v1/results.json` 和无损因果板。
- fresh `vfx_rigid_basic_dither.xml.shader`、四个主 PS／主 VS 及当前 CPU 写入证明：底色取原 RGB，不读取 base.A、不乘粒子 RGB；原溶解 UV 为 `(UV+offset+globalTime*scroll)*tile`，读取 dissolve.R，按阈值硬裁切。原主 PS 是隐式 mip 采样，没有屏幕 Bayer 抖动，不能套其他溶解材质的屏幕 mip0 图。
- 模型提交前把正 Alpha 限制至 0～1 并四舍五入成字节；通用实例编码把字节归一化值乘 1000 后向下取整，再附不影响整数部分的小数字段。新材质重建相同整数 Alpha：`floor((floor(saturate(alpha)*255+.5)/255)*1000)*.001`，噪声小于 `1-effectiveAlpha` 时丢弃。已分别核实 rigid float→record+113 字节及通用 reader→POSITION4.w；未穷举所有 renderer callback／shadow variant 的直接 dispatch，不声称所有实例路径已闭合。精确证据 `material-audit/rigid-basic-dither-contract-v1.json`、`dissolve-sampling-contract-v1.json`。
- 新共享 `VfxRigidBasicDitherMaterial` 保留四张原始资源、六个源参数及两个对齐 padding，以独立原生 index17 材质上传；按原主通道单面剔除、不透明覆盖并写深度。底图仍按 Diffuse 上传，Alpha 和全部 mip 原字节不改，未用 OpaqueDiffuse 偷改数据。材质图／法线资源保留；当前只恢复原始底色与溶解覆盖，完整 PBR、反射、阴影、法线光照和游戏最终明暗仍未还原，新增中文候选诊断准确替换原普通材质回退提示。

### 模型自己的旋转与阻尼

- 模型 CPU 在更新中以 `exp(-lambda*sourceDt)` 同时衰减 XYZ，再执行 `normalize(q+.5*sourceDt*(OmegaWorld*q))`；普通面片 CS 则只衰减世界 Z。原始 91 条 SSE 指令独立解释器的 100 组非对称输入与上述模型顺序一致，最大误差约 1.05e-7，排除相反乘法顺序。
- 新 `ModelQuaternionSpin`／`ModelSpinAt` 独立实现模型规则，不借普通面片函数。当前限 Kind1、恒定阻尼 0～5、配置非零 X／Y 自旋、world／position-only、独立或相对上轴出生，且无粒子事件／MeshLoop。沿旧出生 Spin RNG 取样，再经完整出生旋转转换世界角速度；每个既有 Tick 保存 q／omega，绘制的不足 Tick 只推进副本。Frame 使用完整世界姿态，不再逐轴累加欧拉角或重复附加节点绝对旋转。
- R92 动态面片、R93 多轴面片、模型纯 Z／完整局部空间及未覆盖事件路径保持；出生随机消耗、原始几何／贴图／XML、位置、尺寸、寿命、发射、普通播放和全局 1/120 不改。原生时间差来自当前与前次时刻，不声称原生固定 120Hz。模型出生随机序列仍未与游戏同步。
- R93 两项“原生规则未建立前保留 Euler”临时测试现已改为严格的模型全轴阻尼独立参考；没有放宽阈值，也没有把面片世界 Z 参考套给模型。

### 本轮构建与自动验证

- 固定 Effekseer 源 `92fdeb349c0c7eab9dfaf152047f22dec2fa102d`，相关 tracked source 干净。正常 native Debug 构建成功，18 个材质生成、5/5 链接完成，`native-build-v1.log` 退出 0；正常主程序 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore --verbosity quiet` 成功，0 警告／0 错误。此前 Core 单项目构建 1018 警告／0 错误独立保留；不以警告数证明视觉正确。
- 最终正常 Debug Core SHA `D172647F42E54E2EFAC4663303180BA3E009F981A0CD06221E76FD0B9CF64B0A`，native SHA `026F432C194DB589DBD199FB712C1458DB7403D5A24451D5B9D7F14021A2C1C2`；正常输出、测试和正式采样运行库核对一致。正常 Debug 可运行产物保留，不安装／发布。
- 最终测试项目重新构建成功：3 既有警告／0 错误。新模型 27 项（25 CPU＋2 GPU），新材质 21 项（9 CPU＋12 GPU）；专项 `special-final-v1/special.trx` 78 通过／0 失败／0 跳过，含 R93 类 30 项，不与其累加。最终相关回归 `core-final-v1/core.trx` **813 通过／0 失败／0 跳过**，退出 0；不是全解决方案验证。
- 同一最终测试程序集在隔离 R93 Core5D33／nativeC5 上重跑 36 个 CPU 控制：10 通过／26 失败，退出 1，涉及旧材质未适配、模型矩阵不符和普通 Alpha 截断；`old-r93-red-v1/old-r93.trx`、`red-runtime-scope.json`，无 MissingMethod 等伪红灯。
- 新材质 GPU 检查包括黑色 A0 底图遮背景、base.A／粒子 RGB 无关、红通道硬裁切、字节 Alpha 的 .4995 边界、全局滚动与原参数顺序、隐式 mip、镜像剔除及深度写入。模型参数按独立 double Rodrigues 矩阵及严格完整浮点 RGBA 比较；自动微型固定输入只证明代码／参数，不作为完整官方 VFX 外观验收。
- 首轮 66 项中 2 个模型 GPU 诊断失败已查清：四象限纹理内部仅 11／6 个像素跨过 1/256 插值量化边界，Alpha／覆盖无差，CPU 基轴误差约 3.8e-6；恒色同几何对照全像素差 0。最终用有颜色、有透明通道的恒色纹理隔离矩阵传递，完整 RGBA 容差仍为 1e-5。原失败、逐像素证据及原测试源码均保留；`model-spin-gpu-diagnostic-conclusion-v1.json`。早期补丁匹配错误、资源检查错误与候选日志也保留，不冒充已通过。
- 原 index0～16 共 17 个已编译材质逐字节相同，仅追加 index17；新四纹理／三 uniform 与 Bridge 的“两组参数＋一组渲染参数”一致。旧屏幕 `rigidDitherUV` mip0 分支原样保留，新 `dissolveUV` 不命中它。Bridge.h、C# 互操作与 15 个导出签名不变，证据 `native-material-preservation-review.json`。中文资源只新增一键，旧键值及正常 Debug 资源字节均核对；未打开实际诊断窗口。

### 本次完整官方原件画面

- fresh manifest 官方 271 Pack，4 个完整根、35 项来源／依赖／原天气、缺失 0，无 Mod 同名覆盖。根为 `wh3_dlc24_cth_crowman_trail`、`wh3_main_siege_tze_siege_tower_15m_feathers`、`wh3_dlc24_aekold_flower_trail` 和 `wh_dlc03_beasts_amber_spear_trail`。使用未经修改的官方 Empire UI 天气，它不是战场光照，不调色／泛光迎合结果。
- Crowman q0／1／2 × .1／.25／.6／1.2／2.2／2.8 秒，共 18 对；直行→转弯→1.6 秒停止，固定镜头 distance32／targetY2，并保留受控挂点转动。Tower q1 × .1／.25／.6／1.2／2.2／4 秒，共 6 对，静止挂点、distance48／targetY8。Flower／Amber q1 × .6／1.2／2.2 秒，共 6 对，镜头与移动挂点前后同条件。正式合计 30 对；初始 12 帧不可见复现和 5 个单变量诊断不累计。
- 1005 个粒子时点的非角状态完全相同，仅 Crowman／Tower 羽毛的 215 个旋转矩阵改变（144／71）；原位置、RGBA、UV、年龄、寿命和发射状态不因修复改变。Crowman 15 个有粒子阶段从空画面恢复清楚黑色羽毛，2.8 秒三档均为 0 粒子／原背景；Tower 5 个阶段 RGB 改变，4 秒清空。黑色来自原 base.RGB=0，未提亮；Tower 保留原蓝紫底色。
- 6 张无损阶段板／30 对全部实际查看，能辨认 Crowman 的出生、转动、后期溶解和清空，以及 Tower 的遮罩出现／姿态变化／消失。Flower 普通零自旋模型与 Amber 的 790 个粒子时点、6 对完整帧及 PNG 逐字节相同。全部背景覆盖并集以外和场景 Alpha 另行精确核对；场景 Alpha 恒定不代表特效透明遮罩逐像素相同。
- 正式 after 仍是本轮已渲染并查看的候选 D172／026F 文件；最终确认正常 Debug／测试／候选四个运行库 SHA 一致后纳入正式结果，文件名与 candidate 标题原样保留，没有虚构重复渲染。本轮原底图全部 12 mip 解码字节前后同 SHA、Usage=Diffuse、A0 保持，新增三张依赖均为官方原件。
- 正式证据 `sample-audit/gpu-final-audit.json`、`paired-review-candidate-v1/comparison.json`、原尺寸帧和无损阶段板；只有原件完整输入／实际可见层的本次范围，不把成功加载或像素数量当原生视觉验收。

### 剩余与交付状态

- Crowman 不可见与上述受限模型旋转已完成本轮实现及离屏复测；完整 PBR、法线光照／反射／阴影、ForceA／B 等原生运动、完整局部空间／动态模型阻尼、全部实例 callback 打包路径、原生随机序列和实际游戏时间同步仍未完整还原。既有轨迹分布 ID、半分辨率、天气及透明合成限制保留，不能宣称所有 trail 或整个 Crowman 原生效果已完成。
- 实机仍由用户验收，本轮没有 AECN／Terry 窗口、键鼠／焦点操作或新增游戏／Terry 捕获。正常 Debug、所有源件、备份、生成物、原件、工具、失败、诊断、候选及已有 Mod 资料保留；不提交、不推送、不清理。最终保护核对见 `workspace-after.json`／`final-audit.json`；R93 及更早正文逐字节保留。


## R93 trail 正恒定多轴阻尼修复与轨迹图集分布核查（2026-10-25）

### 现场保护与本轮真实范围

- 原地沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。开始时 231 个已有修改／未跟踪文件与 R92 最终快照逐项状态／字节一致，暂存为空、index SHA 不变；`workspace-before.json`、`source-before/`、`runtime-before/` 保留现场和 7 个正常 Debug 文件。无分支／worktree／暂存／收起／提交／推送／PR／清理。
- 本轮重新读取当前 manifest 的 271 个官方 Pack 并核对大小；全部 7497 个有效 VFX 的 14805 个动画图集声明只有 lifetime／fixed 两种模式，未发现 `trail_distribution`。另 885 个 XML 是材质等非 VFX 文档，不是 885 个 VFX 预览失败。官方库有该模式的公式，但当前 VS 使用打包数据的低 8 位粒子 ID；分配、复用和 ribbon 身份尚未闭合，不能用无限发射序号替代。本轮保留明确拒绝，**没有宣称轨迹分布模式已支持**。证据 `distribution-audit/trail-distribution-contract.json`。
- fresh 官方 Pack 着色器及当前 Assembly Kit DLL 证明：局部出生角速度经最终出生旋转转换到世界；普通面片模拟只衰减世界 Z，随后 `normalize(q + .5*sourceDt*(Omega*q))`。先前正恒定阻尼却把全部轴统一衰减，导致真实 Elspeth 贴图尾迹的多轴转动失真。31 项着色器新读取／反汇编及 DLL 来源、SHA、精确位置在 `rotation-audit/rotation-contract-v1.json`；可读源码、原生参考与游戏最终显示不混同。

### 最终实现及保留边界

- 最终 `ComplexSpinDamping` 仅用于 Kind0 普通面片：恒定正阻尼且范围在 0～5、配置了非零 X／Y 自旋、有效方向、world／position-only、独立或相对上轴出生；排除专用网格材质、MeshLoop、粒子事件和地形相对出生。保留完整出生矩阵，先转换世界角速度，再在既有 1/120 Tick 内先衰减 Z、后更新并归一化四元数；绘制的部分 Tick 只推进状态副本。
- 旧零阻尼、纯 Z 恒定、完整局部空间、R92 受限动态阻尼及旧出生 Spin 随机取样代码保留。没有改变出生随机数消耗、原始贴图／XML／模型、图集、位置、颜色、尺寸、寿命、发射率、连续拖动播放、UI／Kitbash、原生桥或全局步长。全局随机序列仍未与游戏同步；本轮原生方向表调查不被偷换为随机序列已还原。
- 最初模型候选曾覆盖 Kind1，但只证明模型共享出生函数，尚未证实 96 字节模型队列后续更新与普通 80 字节面片 CS 相同。因此最终收回本轮模型路径，模型 Frame 原两行精确恢复 R92；`source-candidate-model-v1/`、`runtime-candidate-model-v1/`、Core `367FAA6A…` 及全部候选结果保留，不作为最终模型修复。最终只读复核 `rotation-audit/final-kind0-review-v1.json` 确认守卫、R92 动态方法及旧出生 RNG 文本不变。

### 本次构建、红绿与自动回归

- 正常主程序 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore --verbosity quiet`：v1 模型候选成功（1901 警告／0 错误）；收窄后的最终 v2 成功（1881 警告／0 错误，39 秒）。未重编 native。测试项目按 `--configuration Debug --no-restore -p:BuildProjectReferences=false` 最终重建成功，3 既有警告／0 错误。
- 最终正常 Debug Core SHA256 为 `5D33AE5AE8444AED1ACD6CF787F84E87CB468E702CBAD57C6647D076D68866F2`，native 仍为 `C5E1898088BD4A54C8B84AB553D22B642544DDA76D2D19E52F104974534B5187`；正常输出、测试及最终官方采样运行库逐项核对。
- 新 `VfxComplexTrailRotationTests` 最终 30 项：28 CPU 和正／反投影 2 GPU。独立双精度 Rodrigues 矩阵每步使用 `2*atan(|omega|*sourceDt/2)`，不复用产品四元数代码；矩阵与完整浮点 RGBA 容差均保持 0.00001，未放宽断言。覆盖倾角／多轴／顺逆、仅世界 Z 衰减、相对出生、源时间倍率、回退／分段／部分帧、有限尾部、非角随机状态，以及真实模型转换保持旧路径。
- 最终同一测试程序集在隔离 R92 Core A472 上重跑 26 个普通面片 CPU 控制（排除模型保留与 GPU）：13 通过／13 失败，退出 1，全部是待修复旋转矩阵不符；`final-red26/old-core.trx`、`final-red26-scope.json`。最初 CPU28 的 13／15 红灯含已撤回模型提案，不冒充最终范围。
- 最终专项 `complex-final-v1/complex.trx` 30 通过／0 失败／0 跳过。最终相关回归 `core-final-v2/core.trx` 765 通过／0 失败／0 跳过，退出 0；含 R92 动态、普通／速度姿态、旧自旋、图集、连续拖动、透明度、光照、折射及模型等。不是全解决方案验证，也不把专项和回归数字相加。
- 首轮候选广回归 `core-v1` 为 764 通过／1 失败：新增模型 GPU 诊断错误沿用了仅面片可用的 restriction=3，转换被正确拒绝。最终 GPU 限已证实的普通面片正／反投影；两个真实模型保留 CPU 用例改用有效 restriction=0，原模型／动态提案源码、测试程序集、配置错误和所有失败日志均保留，没有放宽实现或断言。

### 本次完整官方原件离屏过程

- 最终 5 个完整原版根：`wh3_dlc25_lord_elspeth_trail`、`wh3_dlc25_lord_elspeth_hand_trail`、`wh3_main_leaf_trail`、`wh_dlc03_beasts_amber_spear_trail`、`wh3_dlc24_aekold_local_sword_trail`。fresh 来源／依赖／天气共 21 项、缺失 0、无 Mod 同名覆盖；q1、.25／.6／1.2／2.2 秒，冻结各根镜头、原 Empire UI 天气及同一移动／转弯／转动／停止挂点，最终 20 帧全部重新渲染。该 UI 照明不是战场天气；不调色／泛光或修改原件迎合结果。
- 两个 Elspeth 父根均启用同一 `glow5` 多轴阻尼层，不能说覆盖了 glow2／8／11；但真实引用 scale=.6／rate2 与 scale=.2／rate5 不同，速度均 .8。它们各有 98 源层、原件禁用 93、有效 5 均可转换。Leaf 的 1 层与 Amber 源启用的 3 层保持；Aekold 源启用 7 层仅转换 5 层，eyes 的 curve_colour 歧义和一个 rigid 几何阻断仍存在，完整根输入不等于所有层已支持。
- 1798 粒子时点的非角状态严格相同；仅两个 Elspeth 根的 glow5 共 347 个旋转矩阵改变，8 对 RGB 变化。Leaf／Amber／Aekold 的 12 对控制完整帧 JSON 和 PNG 逐字节相同。实际查看 5 张四阶段板及 2 张同裁剪细节板，紫色贴图轨迹的直行、转弯和停止后稀疏尾部保持，局部贴片朝向调整；控制形状、密度、颜色与运动保持。这些不是同条件 Terry／游戏原生视觉验收。
- Crowman 仍使用未知 dither 材质的候选回退，原件 before／早期模型候选 q0／1／2 共 12 对有粒子却没有可辨识画面，直接原因未定位，明确不算可见性通过。最终仅补同条件 CPU 控制：138 个模型粒子时点的完整矩阵及非角状态精确保留 R92，`ComplexSpinDamping=false`；见 `sample-audit/crowman-final-cpu-control-v1/control.json`。
- 原 PNG 背景另逐像素核查：20 对在实际特效覆盖并集以外精确等于同一棋盘，场景 Alpha 均为 255，PNG 相对棋盘差异计数与 GPU 回读一致；这不等于粒子透明遮罩逐像素不变。旧 JPG 板有损压缩使局部背景差最大 11 字节，不能据其显示观感调产品；已保留旧板，并实际重看 5 张同裁剪、保持比例、相邻前后的无损 PNG 阶段板。没有修改产品、原 PNG、曝光或颜色迎合对照，20 对不重复累加。
- 最终官方证据 `sample-audit/gpu-final-audit-v2.json`、`paired-review-final-v3/comparison.json`、`background-alpha-audit-v1.json` 及 `paired-review-lossless-v4/`；候选模型帧、无可见性及所有先前比较／取景失败独立保留。

### 剩余与交付状态

- 未完成：Crowman 模型材质／可见性及后续原生阻尼规则、复杂动态／完整局部空间角阻尼、打包粒子 ID 与 trail_distribution、Aekold 两个受阻层、部分 UV90／法线组合的完整标定，以及既有天气、半分辨率和完整透明合成。原生 ps_normal 没有额外 90° 切线旋转，不凭推测增加；不宣称所有 trail 已修好。
- 实机测试仍由用户完成；本轮没有启动 AECN／Terry、焦点或键鼠输入，也没有新增 Terry／游戏同条件捕获。正常 Debug、全部源码／备份／官方原件／工具／采样／失败及已有 Mod 资料保留；不提交、不推送、不清理。`workspace-after.json`、`final-audit.json` 记录最终保护核对；R92 及更早正文逐字节保留。


## R92 攻城烟雾受限动态角阻尼修复（2026-10-24）

### 现场、原生规则及实现

- 沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`；230 个已有修改／未跟踪文件与 R91 最终快照逐项状态／字节一致，暂存为空、index SHA 不变。`artifacts/vfx-dynamic-spin-r92/workspace-before.json`、`source-before/`、`runtime-before/` 保留现场与 7 个正常 Debug 文件。没有切分支／worktree／暂存／收起／回滚／提交／推送／PR／清理。
- 从当前 manifest 271 个官方 Pack 精确重读普通 simulation／spawn 两个编译 CS，大小和 SHA 与 R89 原件完全一致；不是只凭历史结论。规则为当前源年龄除以 `(源寿命+0.000001)` 取样阻尼，先衰减世界 Z 角速度，再 `normalize(q + .5*sourceDt*(Omega*q))`。源码证据与 source-time 单位、纯 Z 独立标量公式均在 `native-audit/dynamic-spin-contract.json`。不证明游戏本身固定 120Hz，也不添加未知原生出生首步。
- 原转换器对非恒定 `ParticleSpinDeceleration` 清零整项自旋。现在仅对合法的非恒定／非 Bezier、范围 0～5、普通面片且无专用网格材质／MeshLoop／自带粒子事件、独立出生、world／position-only、出生角与自旋 X/Y 均为零、Z 自旋非零且方向有效的组合启用新状态分支。其余复杂组合保留原诊断与零自旋，未只删除“不支持”判断。模型／贴花、相对出生、完整局部空间、多轴和 Bezier 动态阻尼未扩展。
- 新分支在既有 1/120 Tick 内保存角速度／四元数，先按当前阶段阻尼再转动，更新在运动分支的提前 `continue` 之前；不足 Tick 的 Frame 推进只操作副本，不让绘制次数改变状态。`sourceDt=previewDelta*TimeScale`，阶段分母按源秒保留 epsilon。暂停、回退、重新播放、预热及有限尾部沿用原模拟重置和寿命机制。
- 恒定／零阻尼继续原解析旋转路径，源方向与连续时钟不改；无自旋或未知方向不进入四元数状态，以保持原出生矩阵逐位相同。新增分支只复用已消耗而未用于当前颜色的 `RandomColour.Y/Z` 选择角速度和随机方向，不额外推进发射器 RNG，因此不改变其他出生参数。全局随机种子仍未与游戏同步，不能将这种隔离选择宣称为原生随机序列已还原。
- 只修改托管 converter／model／simulation 及对应测试／现有文档；native 与 R91 材质修复逐字节不变，未重编原生。原 XML、贴图、模型、概率、几何尺寸、透明度、普通播放、trail 连续拖动、UI／Kitbash 和全局步长不改。

### 本次构建与自动验证

- 正常主程序最终 `debug-build-v3.log` Debug 构建成功：1881 警告／0 错误；最终测试项目 `test-build-v2.log` 按 `BuildProjectReferences=false` 重建：3 既有警告／0 错误。首轮局部变量同名编译失败、修正后的候选构建及全部日志保留，不把失败写成成功。
- 最终正常 Debug Core SHA256 为 `A47206A4D6052E8C8642D56E7F2A5B2051B306658B7ADB0F0FFC180C7EF5F55E`，native 仍为 `C5E1898088BD4A54C8B84AB553D22B642544DDA76D2D19E52F104974534B5187`；最终测试与官方 after 工具均核对确切运行库。早期候选 Core `30EFBE25…` 的证据独立保留，没有冒充最终产物。
- 新动态自旋最初 24 项在不可变 R91 Core `2C4775A2…` 上的有效红灯为 11 通过／13 失败，失败均为旋转被清零／角度不符；`test-audit/red-results-v3/dynamic-spin-red.trx`。早期缺 SharpDX 引用的隔离编译失败、有限寿命控制格式错误及修正记录都保留，只有 v3 是有效红灯。另补 2 项无自旋／未知方向的出生矩阵精确不变控制，最终新增共 26 项。
- 独立标量参考每步使用 `omegaAfter=omega*exp(-lambda*sourceDt)`、`angleDelta=2*atan(omegaAfter*sourceDt/2)`，不复用被测四元数实现；角度基轴容差 0.00001 未放宽。覆盖顺逆、升降阻尼、先衰减后旋转、回退／分段 Advance、部分 Tick 多次 Frame 不提交状态、引用时间倍率、world／position-only、有限尾部死亡、随机出生非角状态不变、复杂组合拒绝及旧常量路径。
- 最终 `core-final-v2/core.trx`：735 通过／0 失败／0 跳过，退出 0；含上述 26 项及 R91 法线翻转、R90 静态图集、普通／速度姿态、自旋旧路径、发射／寿命、图集、透明度、折射、贴花和曝光相关回归。不是全解决方案测试。候选 `core-v1` 的 733 通过单独保留，不与最终或专项数量相加。

### 本次完整官方原件画面

- 3 个完整原版根：动态 `wh3_main_seige_smoke_large`、恒定阻尼对照 `wh_main_seige_smoke_large`、Amber 原版 `wh_dlc03_beasts_amber_spear_trail`。从 manifest 官方 Pack fresh 核对原件／依赖／未经修改的官方 Empire UI 照明，缺失 0、无 Mod 覆盖；该照明仍非战场天气，不改曝光／泛光来迎合效果。
- 动态根 q0/q1/q2 × .2/.6/1.2/2.2/4 秒，共 15 对，静止挂点，固定 distance160／targetY20；同根 q1 另查 90 秒。恒定根 q1/q2 × .6/1.2/2.2 秒，固定 distance48／targetY1，共 6 对；Amber q1 匀速→转弯→停止的 .6/1.2/2.2 秒共 3 对，沿用冻结镜头。正式共 25 对。最终 settings／原件／天气／画质／相机／挂点逐项一致。
- 1136 个粒子时点的非角状态严格相同：位置、RGBA、UV、数量、年龄、尺寸等不因恢复自旋而改变；只有 `smoke_end_fade` 的 194 个旋转矩阵改变。16 对 RGB 变化（动态早期 15 与长时 1）；恒定根与 Amber 的 9 对控制完整矩阵及 PNG 均相同，保持 R89／R91 已确认路径。
- 已实际查看本轮候选的 7 张阶段板／25 对：动态烟云的局部纹理方位随阶段变化，整体分布、软边、密度保持；恒定烟云与 Amber 的直行／转弯／停尾同图。最终 Core A472 重新执行 25 帧，PNG／完整帧 JSON 与已查看候选逐字节／逐值相同，7 个完整结果组一致，按该证据复用本轮实际观察，没有伪装成最终重新看图或重新做板。
- fresh 核对后确认该官方根 `InfiniteLife=true`；90 秒仍 33 个粒子，是长时持续发射／状态稳定检查，**不是消失帧**。有限尾部死亡仅由独立 CPU 用例验证，没有改原件造有限诊断效果。
- `smoke_end_fade` 在三个画质均恢复 SpinMax.Z=20 与动态状态；`smoke_base` 的恒定 SpinMax50 保持。`smoke_central_pillar` 仍因 `ParticleScale/InterpolationUnsupported` 整层拒绝，未作为本轮可见恢复。其他诊断集合严格相同；移动自旋验证的位置使诊断顺序变化，首轮错误地按列表顺序比较的失败保留，后按精确多重集合核验，不删诊断。
- 最终证据：`sample-audit/gpu-final-audit.json`、`paired-review-final-v3/comparison.json`、`final-v2-vs-candidate-v1.json`；实际查看板在 `paired-review-v2/`。测试数量、矩阵改变及非空像素不等于游戏原生视觉验收。

### 剩余边界与保护

- 中央烟柱的尺寸插值阻断是独立剩余问题；多轴／完整局部空间的原生角阻尼、常量路径的少量既有原生残差、事件／专用材质和原生随机序列仍未完整标定。R91 法线背面 Z、未映射光照和生成天气等边界继续保留，不能称所有 VFX 已正常。
- 无新 AECN／Terry 窗口、鼠标键盘／焦点操作、Terry 同条件捕获或游戏实机验收，实机仍由用户完成。正常 Debug 保留，所有源码／运行库备份、官方原件、工具、失败／候选／最终结果及已有 Mod 资料全部保留，无提交／推送／清理。精确变更范围和保护核对见 `workspace-after.json`、`final-audit.json`；R91 及更早正文逐字节保留。


## R91 烟雾／带贴图 trail 的法线翻转修复（2026-10-24）

### 现场和共享修复

- 原地沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`；229 个已有修改／未跟踪文件与 R90 最终 `workspace-after.json` 的状态和字节完全一致，暂存为空、index SHA 不变。`artifacts/vfx-smoke-trails-r91/workspace-before.json`、`source-before/`、`runtime-before/` 已备份现场。额外 `native-build-before/` 保留旧材质编译器、生成头、原生 bin 等 9 文件。
- 本轮重新从官方 Pack 精确读取 7 项原件／着色器／法线依赖，与 R89 对应原件逐字节一致；当前编译 PS 与库均证明 `ps_normal` 在两帧 DXT5n 解码／混合／归一化后，按 `frac(r*251)`、`frac(r*149)` 对切线法线 X/Y 分别取正负。R89 修好了纹理翻转及完整姿态传递，但普通覆盖材质仍只做固定坐标符号转换，法线没有跟着纹理翻，导致烟纹与明暗方向不一致。扭曲材质已有独立符号规则，不重复修改。
- 只修改原生 `CoverageMaterial.h` 与 `Bridge.cpp`：新增独立 `normalUv` 材质参数，沿用每粒子 UV 宽／高的实际符号；在固定坐标转换前后按原生规则修正法线 XY。有效天气、法线贴图、普通覆盖光照分支按符号有序分批，避免同层粒子借用别人的符号；不重排透明粒子，不用四元数冒充单轴镜像，保留原着色四元数和尺寸。覆盖／贴花材质参数数量与 CPU 上传同步调整，公共 ABI 不变。
- 没有改转换器的支持判断、粒子几何／出生方向、图集时钟、寿命、运动、发射数量、1/120、UV 概率、原 XML／DDS、既有曝光或泛光参数，也没有改 UI／Kitbash。未选择天气、无法线、无翻转以及未映射光照的分支仍走原路径。

### 本次构建和自动验证

- 固定 Effekseer `170e` 源码 HEAD `92fdeb349c0c7eab9dfaf152047f22dec2fa102d`；正常原生 Debug 重新生成候选材质、编译并链接成功，`native-build-v1.log` 退出 0。正常主程序 Debug `debug-build-v1.log` 成功，0 警告／0 错误；测试项目按 `BuildProjectReferences=false` 重建，3 既有警告／0 错误。没有 GUI 或桌面输入。
- 正常 Debug Core 仍为 `2C4775A2EF5EB7E4AF754E690C23E048BC4250B453BCCC1235893A2823A713A0`；新 native 为 `C5E1898088BD4A54C8B84AB553D22B642544DDA76D2D19E52F104974534B5187`，旧 native `67A224C0…` 独立保留。正常输出、最终测试和完整官方 after 工具均核对同一新运行库。
- 新 `VfxNormalUvFlipPixelTests` 12 项：U／V／双／无翻转 × 正反投影、同层四粒子不同符号、无 normal／无 weather 对照。参考纹理按原生半步解码独立镜像 A/G 为 `254-value`，不是按现场颜色反推预期；完整浮点像素差容差 0.000005 沿用原生光照控制尺度。混合批次只在测试内固定位置与四个随机值，用于分辨参数传递，不代表游戏出生位置或随机序列。
- 旧 native 的有效红灯为 5 通过／7 失败；新 native 专项 `normal-flip-green-v1/normal-flip.trx` 12 全通过。首轮测试漏 `ps_normal` 标志导致假绿，已加实际 `NormalIndex` 断言，首轮记录保留但不作为有效红灯；见 `material-audit/normal-flip-tests/red-green-evidence.json`。
- `core-v1/core.trx` 本轮 709 通过／0 失败／0 跳过，退出 0。包含普通／速度姿态、自旋旧路径、图集与镜像、R90 静态图集、原生烟雾光照、透明度／AlphaCrush、官方折射、发光／普通贴花和显示曝光等相关回归；不是全解决方案测试。不与单独专项或历史数量累加。

### 本次完整官方原件离屏画面

- 6 个完整根：`wh2_dlc11_deep_spell_smoke_trails`、`wh2_dlc11_weapon_flamethrower_impact_smoke`、`wh2_dlc14_battle_volcano_smoke_huge`、`wh3_main_nurgle_miasma_of_pestilence`、`wh_main_seige_smoke_small`、`wh_dlc03_beasts_amber_spear_trail`。当前官方 manifest 271 个 Pack 大小均匹配，实际源／16 个依赖／天气均记录 Pack 与 SHA，缺失 0、没有 Mod 同名覆盖。完整根输入不代表源关闭或未支持层已启用。
- 全部使用未经修改的官方 `ui/3dui/lighting/front_end_lighting_empire.environment`。这是可表达的官方 UI 照明参考，非战场天气；保留源曝光 .3／burn .05，不调泛光、参数或源文档。当前 87 份官方 environment 的小范围盘点中，79 份因生成式 ambient／irradiance 超出现有解析范围被拒绝，8 份可读；没有删判断来放行。未知 post_processing 继续诊断，不能将该照明等同游戏战场最终显示。
- q1、.2/.6/1.2/2.2 秒，沿既有匀速→转弯→停止挂点，冻结每根适合其原尺寸和预热位置的相机。VolcanoHuge 使用 targetY800／distance3000／far10000，覆盖源中约 700～1500 高度的预热烟柱；Flamethrower 最终为 distance95／targetY20，原过近的 distance55 之 8 张图保留并排除正式比较。另 Nurgle 无天气 .6/1.2 秒对照，正式共 26 对。
- 严格比较来源、依赖、天气、画质、相机、挂点、时间以及 1495 个粒子时点的完整 CPU 数据：位置、矩阵、UV、RGBA、完整法线四元数、尺寸、年龄和数量均完全一致，图像 Alpha 也完全相同。11 对 RGB 改变（Volcano4、Nurgle4、Amber3），12 对 Mode0 控制及 2 对无天气控制 PNG 逐字节相同；Amber .2 秒也是同图。Deep／Flamethrower／Siege 的原光照参数仍超出现有映射范围，保持 Mode0，未升级支持。
- 已实际查看全部 7 张四阶段／对照板：Volcano 两个烟柱、Nurgle 聚集扩散气团、Amber 直行／转弯／停后的烟尾均保留形状、软边、密度及运动，改变的是局部烟纹明暗方向；最大 RGB 字节差分别为 16／6／10。不能将像素改变、加载成功或单元用例称为游戏原生视觉通过。最终证据 `sample-audit/gpu-final-audit.json`、`paired-review-final-v2/comparison.json`、`final-matrix.json` 与原尺寸图。

### 已确认但尚未修复的剩余问题

- 只读盘点与当前转换器／原生 CS 核对发现：非恒定 `ParticleSpinDeceleration` 当前会把整层 spin 置零，官方保存原件 `wh3_main_seige_smoke_large` 的 central_pillar／end_fade 因此存在停转问题。原生按当前寿命阶段采样阻尼、先衰减世界 Z 角速度，再更新并归一化 q；完整局部空间还需同步旋转角速度。现有全轴解析阻尼也不能外推成完整原生语义。本轮未修改此路径或重做该根完整画面，见 `spin-audit/remaining-spin-boundary.json`／`.md`；后续可先处理独立 world／position-only、纯 Z 的受限动态分支，再扩展多轴与完整局部空间，不能仅取消拒绝后宣称支持。
- 编译 PS 还包含背面法线 Z 的处理，需独立控制验证；未捆绑进本次 UV 镜像修复。UV 90°、未映射光照参数、生成环境光、半分辨率合成及完整原生透明／天气仍有既有边界，不宣称所有烟雾／trail 已正常。
- 实机测试由用户完成。本轮无新 AECN／Terry 窗口、键鼠／焦点操作、Terry 同阶段捕获或游戏原生人工验收。原正常 Debug、源码／运行库／生成材质备份、工具、失败与早期取景、官方原件和既有 Mod 资料全部保留，未提交、推送、PR、暂存、收起、回滚、切分支、创建 worktree 或清理。最终保护状态与精确差异见 `workspace-after.json`／`final-audit.json`。


## R90 静态图集亮度旧断言修正（2026-10-24）

- 原地沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。229 个已有修改／未跟踪文件的状态、字节与 R89 `workspace-after-v3.json` 完全一致；暂存为空、index SHA 不变。现场与 7 个正常 Debug 文件均已备份至 `artifacts/vfx-static-atlas-r90/`，不修改产品实现、原贴图或原生材质。
- 本轮重新复现修正前检查：当前 R89 Debug 与不可变 R88 Core 各 11 项，均 10 通过／1 失败，退出 1；同为 `StaticAtlasPixels_MatchNativeWrappedCells(1)` 的 239 不满足 >240。证据 `baseline/`、`r88-baseline/`。R88 使用保留的旧测试程序集与 Core `B4D22081…`，没有把历史日志当本轮复测。
- **更正 R89 文字记录：cell1 是红格，239 是红通道，不是蓝通道。** fixture 中 cell0/2 才是蓝格。DDS 为 256×256 BGRA8、实际只有 mip0；当前 SHA 与原 fixture 完全一致，选中的每个 32×32 格均为纯红／纯蓝、Alpha255，中心过滤不混色，未发现缩小层丢失或错格采样。
- 从当前 manifest 的 271 个官方 Pack 核对全部大小，精确重新读取两个 `hdr_to_screen` 编译着色器与 `lut/default.dds`，来源分别为 `shaders.pack`／`data.pack`，与保留原件逐字节一致，没有 Mod 覆盖或 VFX 全库正文扫描。完整验证默认 LUT 的 32³ 像素与 32 个节点；按原生亮度权重、burn .05、gamma 2.2 及原 LUT 独立计算：线性红1 → 238.712721 → 239，线性蓝1 → 251.443089 → 251，中性白1 → 193.219978 → 193。后者也与既有冻结原生控制一致；不是从现场 239 拟合目标。
- 隔离诊断先新增浮点目标检查，三格 3 项全部通过；中心及内侧 3×3 像素的完整线性 RGBA 精确为红 `(1,0,0,1)`／蓝 `(0,0,1,1)`，证明该颜色没有被图集或采样链路压暗。诊断源码／日志保留在 `linear-diagnostic/`，不是新的完整官方特效视觉验收。
- 只修改 `VfxStaticAtlasCellTests.cs`：新增原 DDS SHA、精确 UV 与上述完整线性 RGBA 检查；8 位目标在同一内侧 3×3 区域精确断言红 `(239,0,0,255)`／蓝 `(0,0,251,255)`。旧主色 >240／另一通道 <10 的含混阈值改为完整通道精确值，不下调阈值、不增加误差容限，也不提亮产品来迎合测试。结论是旧测试把图集原始颜色与最终显示转换混在一起，测试预期已过时。
- 本轮最终 Debug 测试项目按 `BuildProjectReferences=false` 重建：3 警告、0 错误；`related-final/related.trx` 为 121 通过／0 失败／0 跳过，退出 0，覆盖静态图集、显示转换／曝光、图集原生相位与 UV 旋转／翻转。静态图集本身 14 项全部通过。本轮未重新执行 R89 的完整 648 项筛选，不把历史其余结果混加为本次测试数量。
- 正常主程序未重建，7 个备份运行文件逐字节不变；正常 Debug Core 仍为 `2C4775A2EF5EB7E4AF754E690C23E048BC4250B453BCCC1235893A2823A713A0`，native 仍为 `67A224C001CF005D0CAE7A7D7A8F990D1831C8554A1A9A9BB5D9940480BDAD56`，最终测试加载同一运行库。没有改变 1/120、原生已确认表现、UI、Kitbash 或普通 VFX 播放。
- 证据入口：`fixture-audit/fixture-audit.json`、`display-audit/display-contract.json`、`display-audit/fresh-official-v1/current-official-verification.json`、`display-audit/fresh-lut-verification.json`。工具首轮缺少 `System.IO` 的编译失败与修正后成功日志均保留；工具构建不等于产品构建。
- 本轮没有 GUI、键鼠、焦点、新 Terry 捕获或完整官方特效视觉验收；实机仍由用户负责。原有正常 Debug、源码／运行库备份、工具、失败与诊断记录、官方及既有 Mod 资料全部保留。当前差异与保护状态见 `workspace-after.json`／`final-audit.json`。无提交、推送、PR、分支切换、worktree、暂存、收起、回滚或清理。


## R89 普通烟雾三轴朝向与带贴图 trail 图集修复（2026-10-24）

### 现场及共用修复

- 原地沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。开始时 227 个已有修改／未跟踪文件的状态和字节与 R88 最终快照完全一致；暂存为空，index SHA 不变。`artifacts/vfx-smoke-textured-trails-r89/workspace-before.json`、`source-before/`、`runtime-before/` 保留全部现场。
- 本轮重新核对当前官方着色器库、主 VS 和原生共享出生／模拟反汇编。普通面片出生角使用原生 `Rx * Ry * Rz`，显式上轴继承以旋转矩阵组合发射器及出生节点，不再相加欧拉角。独立世界／仅位置空间不追加挂点绝对旋转，完整局部空间只追加出生后的旋转差；R88 的速度几何仍保留二维投影，没有启用实验性完整三维 trail。
- 普通 `vs_translation` 的四种限制共用完整旋转：0／1／2 变换后取 XY，再放入相机／相机横轴加世界竖轴／世界 XZ；3 保留完整 XYZ。修复 camera/horizontal 丢弃 X/Y 角及 horizontal 跟挂点倾斜的问题。模型、贴花与事件姿态沿用既有路径。
- 烟雾光照及普通折射另传完整着色基轴，不能从投影后的几何分解重建。沿用现有 ABI：折射 normal 分支为 T.xy／B.xy，无 normal 为 N.xyz／1；既有 lit 分支接收完整旋转四元数与投影前有效 X 尺寸，速度 v2 保留 Y180 修正。没有重编 native、改 DDS 或扩大未支持光照组合。速度为零、没有建立有效速度基轴时保持既有着色回退，另有回归约束。
- 图集寿命模式按寿命阶段插值 `no_loops` 两端，而非出生时随机取循环次数；随机起帧保留连续小数，随机行独立于单行限制，行偏移只加一次并保留原纹理寻址。两轴翻转按当前原生 `frac(r*251)`／`frac(r*149)` 规则，出生 RNG 消耗顺序不变。静态整数格、外层图集、相邻帧偏移以及 `trail_distribution` 的未支持诊断保留。
- 没有改全局 1/120、粒子寿命、发射数量、普通一次性时序、trail 连续拖动预览、UI／选择／Kitbash、原 XML 或原材质参数。自旋改用出生局部轴的连续旋转组合；原生逐步归一化积分、仅特定分量阻尼、动态 EmitterOrientation／复杂事件组合仍不宣称完全对齐。

### 本次重新执行的构建与自动验证

- 正常主程序最终 `debug-build-v3.log`：Debug 成功，1881 警告／0 错误；正常路径 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。最终测试项目 `test-build-v3.log` 按 `BuildProjectReferences=false` 重建，3 警告／0 错误。没有显示程序窗口或键鼠／焦点操作。
- 最终正常 Debug Core SHA256 `2C4775A2EF5EB7E4AF754E690C23E048BC4250B453BCCC1235893A2823A713A0`；native `67A224C001CF005D0CAE7A7D7A8F990D1831C8554A1A9A9BB5D9940480BDAD56` 与 R88 相同。最终测试、两组官方 GPU 工具均核对该运行库。
- 新普通姿态最初 24 项在保留的 R88 Core 上 3 通过／21 失败；新图集 21 项在 R88 上 11 通过／10 失败。红灯、原测试及旧运行库完整保留；补充法线／光照尺寸／零速回退后普通姿态为 37 项，连同图集共 58 项，最终全部通过。
- `core-final-v3/core.trx`：648 项，647 通过／1 失败／0 跳过，命令退出 1。覆盖共享姿态／自旋、R88 出生方向、图集／镜像投影、烟雾原生光照、官方折射参数及像素、地形／贴花、运动／尺寸和事件回归；不是全解决方案测试。初次 `core-v1` 为 628／633 通过，5 失败；四个旧合成姿态预期仍按旧 Euler 算法，已依本轮原生证据修正，没有降低容差。Spin 控制独立重构 `Rz(-45)*Rx(tilt)`，保持可见像素 >100 及最大色差 ≤1；camera 的 X90 本应退化，换为非退化的 45° 控制；空间 X90/Z90 明确为原生 +Z。
- 剩余失败为 `StaticAtlasPixels_MatchNativeWrappedCells(1)`：蓝通道 239，旧断言要求 >240。用完整备份测试程序集和不可变 R88 Core 单独重跑 11 项，10 通过／同一项同数值失败，确认不是本轮引入。证据 `static-cell-diagnostic-v1/classification.json`。未放宽断言或修改亮度，该旧检查仍待按实际采样依据单独处理。

### 本次完整官方原件的离屏画面

- 只读取当前 manifest 所列官方 Pack；开始时重新核对 271 个 Pack 及依赖来源、大小／SHA，没有 Mod 同名覆盖。完整根作为输入不等于其每个引用层都已支持；微型合成测试不作新官方视觉验收。
- 烟雾 6 根：`wh2_pro05_smoke_pit_small`、`wh2_dlc17_battle_khorne_portal`、`wh_main_enviro_smoke_drift`、`wh2_main_entity_magic_aura_cool`、`wh3_main_entity_rotfly_wings`、`wh_dlc06_ability_smoke_bomb`。涵盖 camera／vertical／horizontal／spatial／velocity、显式继承与三种空间。q1 主阶段 .2/.6/1.2 秒，烟雾弹为 .2/.6/8 秒及 14 秒消失；烟坑另查 q0/q2、identity／yaw60，主挂点 pitch45。共 29 对，来源、依赖、质量、相机、挂点及 2356 个粒子时点的数量／位置／RGBA／年龄精确一致；UV 的 1654 项变化单独记录。portal 用 90 单位、rotfly 用 15 单位镜头，纠正前六张过近取景仍保留，不混入正式比较。
- 烟雾 v1 的 29 对及 11 张板已实际查看：烟坑从斜烟块恢复低平软边烟带，q0/q1/q2 的水平法线 |Y| 从 .7071 恢复 1；portal 黑烟／橙核、aura 蓝雾环、rotfly 翼片及烟雾弹出现／扩散／残留保留。v2 与最终 v3 分别重新渲染 29 帧，PNG、完整帧 JSON、矩阵逐字节相同，因此复用本轮 v1 实际观察，不伪装为后续版本再看一次。14 秒完全消失帧与 R88 一致。见 `smoke-audit/gpu-final-audit-v3.json`、`final-v2-vs-v1-comparison.json`／`final-v3-vs-v2-comparison.json`、`paired-review-final-v3/comparison.json`。
- 带贴图 trail 5 根：`wh2_dlc14_stealth_smoke_trail`、`wh2_main_gore_trail`、`wh3_dlc24_aekold_local_sword_trail`、`wh2_dlc10_blast_trail`、`wh3_dlc20_lightning_weapon_trail`。q1，.3/.6/1.2/2.2 秒，20 单位／秒匀速→转弯→1.6 秒停止；前四根固定 32 单位跟随镜头，LightningWeapon 12 单位，均 1024×768／45° FOV／反向投影。共 20 对，逐值核对官方原件／依赖 SHA、画质／相机／挂点／阶段及物理粒子数据。最终工具仅复用本轮已核官方快照，确认 manifest SHA 相同，没有另扫全库替换原件。
- 已实际查看 20 对四阶段板及汇总图：Stealth 闪电图案朝向／帧细节改变，Gore 仍保留细绿色尖尾，Blast 核心和火花运动保持，LightningWeapon 火焰／尾烟图案更新、尖端／尾部方向保留，Aekold 本地竖直紫焰保留。18 对有像素变化；Stealth .3 秒、Gore 2.2 秒同图。没有发现新的线状退化；不以这些变化、粒子数量或非空像素宣称游戏原生外观正确。见 `trail-audit/same-source-final-comparison-v2/comparison.json`、`observations.json` 和全部并排图。

- 最终差异复核保留了内部模型速度分支的旧角度路径，并新增两项严格 CPU 控制；当前转换器仍将模型固定为 alignment 2，这两项只保护内部兼容路径，不宣称新增模型速度朝向能力。最终 trail 再采样 20 帧与已查看前版逐字节相同，复用本轮实际观察，原图板与各版运行库均保留。

### 边界、保留及后续

- 共 11 个完整官方根、49 对最终同条件离屏画面；无新增 AECN／Terry GUI、同阶段 Terry 原生捕获或用户实机验收，实机测试仍由用户负责。隐藏 GPU 不能代替真实游戏绑定骨骼、天气和最终合成。
- Portal 仍有 8 层既有阻断（5 材质、1 pass、2 范围），烟雾弹 1 层动态光源阻断；Aekold 的异值同时间曲线与 sound-only 缺模型、其他动态光源／未知图集模式继续诊断，不因本轮修复放行。任意多轴阻尼、复杂挂点／事件与未覆盖烟雾／trail 类型继续作为候选，不宣称全部 VFX 已正常。
- `workspace-after-v3.json`／`final-audit-v3.json` 记录本轮精确差异、最终运行库、测试失败及覆盖范围。保留正常 Debug、所有源码／运行库备份、工具、官方原件、早期及失败记录、before／v1／v2／v3 画面、全部既有 Mod 资料。没有分支切换、worktree、暂存、收起、回滚、提交、推送、PR 或清理。


## R88 出生朝向字段与独立速度面片修复（2026-10-24）

### 现场、根因与实现

原地接续 R87，226 项已有修改／未跟踪文件的状态与字节全部一致。当前分支仍为 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存为空、index SHA 不变。快照与正常旧 Debug 保存在 `artifacts/vfx-trail-geometry-r88/workspace-before.json`、`source-before/`、`runtime-before/`。没有分支／worktree／暂存／收起／回滚／提交／推送／PR／清理。

- 根因不是纹理、Alpha 或缺少更多拉伸。转换器读取了非原生字段 `ParticleRelativeOrientation`，并给 Camera-aligned stretching 模式错误的 true 缺省；真正字段 `ParticleRelativeSpawnOrientation` 原来没有参与这一布尔判断。完整官方 Amber 主层没有任何相对朝向字段，Emitter X90 因此被误加到粒子，legacy 的合法 XY 投影把主面片压成细线。其独立随机 Z 角度仍有效，不能把完整 Amber 的最终出生四元数全部称为 identity。
- 重新核对当前 `warscape.modder.x64.dll` SHA `c75d40a30ce938958f445ae59eb256e4c23f51201984d1f3ea5cb25319f763b8`。现代注册、配置构造和 legacy 默认均为 0；枚举表精确为 `DISABLED=0`、`RELATIVE_TO_VFX_UP_AXIS=1`、`RELATIVE_TO_TERRAIN_NORMAL=2`。出生函数在 0 时以 identity 作为发射器贡献，在 1 时取节点／发射器四元数，再乘独立粒子朝向。证据及 RVA 在 `native-pose/relative-orientation-native-conclusion.json`、`relative-orientation-minimal-disassembly.json`。
- 现在按现代枚举与关闭缺省读取，显式旧 `RelativeParticleOrientation` 布尔迁移继续生效；已有 `ParticleRelativeOrientation` 别名仅作显式兼容。现代和旧来源冲突仍严格停用并诊断，未知现代枚举继续保守拒绝；原生对未知字符串回退为 1 的策略不外推为已支持输入。现代 DISABLED 不再被误送入地形分支；地形 2 保持原有受限验证。
- 普通速度面片在独立朝向下，WORLD_SPACE 与 LOCAL_SPACE_POSITION_ONLY 不附加绝对挂点旋转；完整 LOCAL_SPACE_POSITION_ROTATION 只附加出生后的旋转差。原发射速度仍按原发射器方向计算。没有转置已有几何矩阵、改原曲线、Alpha、颜色、贴图、寿命或全局 1/120，也没有改 UI／Kitbash／选择／播放器。
- 当前官方主 VS、GS、library 与出生 CS 从 manifest Pack 重新提取，既有 30 项逐项 SHA 相同，另增加一项参数定义。主 VS 直接读取出生四元数，不自动给 WORLD_SPACE 补发射器旋转；LOCAL_POS_ONLY 仅更新位置，完整局部模式才更新朝向。实验三维分支由全局布尔控制，当前原生注册默认 false；AECN 未改该开关。R63 历史 Z90 控制实际显式设置现代 UP_AXIS，不能作为“缺省为开启”的证据。当前对照均未使用旧 Mod 素材。

### 本次重新执行的验证

- 新 `VfxSpawnOrientationTests.cs` 的 20 项 CPU 回归，先在不可变 R87 Core `2558B812…` 上实际跑红：7 通过、13 失败、0 跳过。修后同 20 项全部通过。覆盖缺省／现代关闭、显式上轴、画质覆盖、两种旧布尔、冲突拒绝、发射速度与粒子朝向分离、三种空间的出生与旋转差、字节保留。红灯和日志保留在 `shader-audit/orientation-tests/red-results/`，没有改断言放宽通过。
- 正常主程序 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 成功：1881 条既有警告、0 错误。测试工程以 `BuildProjectReferences=false` 重建，3 条既有警告、0 错误。未重新构建原生桥；native 字节与 R87 完全相同。
- 最终正常 Debug Core SHA256 `B4D220818FD1694C7D51E20824DAFF2AD026FDBF02ADDEB51EFB9ED658D97009`；native `67A224C001CF005D0CAE7A7D7A8F990D1831C8554A1A9A9BB5D9940480BDAD56`。正常 Debug、测试与 after GPU probe 核对一致。`core-v1/core.trx` 本次重新运行 **489 通过、0 失败、0 跳过**，覆盖 R87 的运动／弱 Alpha／像素下限，以及本轮出生朝向、旧式方向、刚体、地形／贴花、引用与事件等。前面的 20 项已包含在 489 内，不相加；历史 R87 结果不算本次复测。
- 完整官方 GPU：Amber q0/q1/q2，各 .1/.3/.6/1.2/2.2 秒，共 15 对；Moonbow/Warp q1 各 .6/1.2/2.2 秒，共 6 对。全 42 张正式矩阵图已实际逐张查看；来源、依赖 SHA、视角、画质、挂点轨迹和阶段全部相同。Amber 金色亮头、连续尾迹、转弯烟边及停止余烟恢复；Moonbow 三张完全一致；Warp 细芯和火星面片恢复，主电弧保持。75 个层／时点的数量、位置、颜色、UV、年龄与寿命进度一致，恢复来自姿态修正，不是增加粒子。见 `sample-audit/gpu-findings.md`、`gpu-comparison-v1.json`、`gpu-layer-geometry-v1.json`。
- 追加完整 Poison weapon q1、8 单位跟随近景、原值 RGBA32F，.6/1.2/2.2 秒；修前前两帧近乎空白，修后直接可辨绿芯和黑绿尾迹，停止后双方消失。flames/drips 的原始 X90 同样被错误继承，本轮修复后恢复面片面积；poison_trail 本来完整的面积保持。显示 PNG 仅钳制原值，没有提亮；原始浮点缓冲、实际背景及元数据均保留。
- 追加完整 impact、arc90、arc360，q1、静止、相同默认相机、.1/.3/.6/1.2 秒：impact 与 arc360 各四张 PNG 完全一致，地面环／裂痕和既有原生格线不变；arc90 前三帧烟环细部变化，主体扇弧／烟柱保持，1.2 秒完全一致。原因是其 smoke_ring 同样缺省关闭而源 Emitter Y220 被旧逻辑误继承。追加共 15 对、30 张图已实际查看，105 个层／时点的数量及位置／颜色／UV／年龄保持。见 `gpu-extra-findings.md`、`gpu-extra-comparison-v1.json`、`gpu-extra-layer-geometry-v1.json`。
- 合计 7 个完整官方根、36 对正式阶段图（另保留首个候选快速帧），全部重新读取当前 manifest 的 271 个官方 Pack，核对包大小、原字节与依赖，无 Mod 覆盖。自动微型门禁与完整原件视觉观察分开报告；没有新增 Terry／AECN 窗口操作或键鼠输入，实机由用户验收。没有同条件游戏或 Terry 动态捕获，不宣称游戏最终外观全面等价。

### 保留项与剩余边界

- R87 的“Amber 主体仍退化”和“Poison 近乎空白”已由本轮同条件完整原件修前后图定位并改善，不能再沿用旧结论当成当前画面。未修改原生格线或矩形折射外观。
- 任意显式继承的多轴粒子／发射器组合、动态 EmitterOrientation、复杂旋转引用、地形和其他渲染类型仍需独立核对，本轮未重写既有多轴旋转约定。Poison 原本弱 Alpha、距离淡出及完整原生强度仍有候选边界；impact 的复杂粉紫模型环片材质仍近似，不因“不回归”称为原生正确。
- 所有源件、旧 Debug、工具、失败、float 数据与历史 Mod 素材保留。辅助逐层比较首次按重名层匹配失败，已改为原唯一 Index 后通过，原脚本错误日志保留；不是产品缺层，也未放宽产品断言。最终范围、运行时和工作区保护见 `workspace-after.json`、`final-audit.json`。


## R87 Trail 运动继承、最小像素尺寸与弱透明度修复（2026-10-24）

### 现场与实现

从 R86 最终现场原地接续，223 项修改／未跟踪源码的状态与字节全部一致；当前分支 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区及 index 散列均保持。`artifacts/vfx-trail-motion-r87/workspace-before.json`、`source-before/`、`runtime-before/` 保留旧源码、正常 Debug 和编译材质头。没有切分支、worktree、暂存、收起、回滚、提交、推送、PR 或清理。

- **运动继承**：只读当前 Assembly Kit 的 `warscape.modder.x64.dll`（SHA256 `c75d40a30ce938958f445ae59eb256e4c23f51201984d1f3ea5cb25319f763b8`），沿 CPU 配置→节点更新→初速度→GPU 出生缓冲追踪，确认 `SpawnInheritedSpeed` 是共同节点世界位置差／未缩放时钟差得到的速度倍率，不是随意解释 spawn-CS 的 delta_pos。节点求速先于源模拟倍率；共同节点不含单个 EmitterPosition 曲线。完整 RVA／反汇编／哈希与限制在 `native-audit/spawn-inherited-speed-native-conclusion.json`。
- 转换器现在保留 `SpawnInheritedSpeed` 和 `SpawnInheritedDirectionBlend`。模拟在每 Tick 更新节点速度，即使当步没有出生；首个节点速度为零，零时差保留速度并更新历史位置。同时间暂停变换更新位置基线，不把传送作为恢复后的速度脉冲；倒放重播重建状态。世界继承向量转换到出生表示空间后只应用一次源时间倍率，避免挂点旋转／尺度重复作用。
- 原生方向混合先混合继承单位方向与原发射方向，再乘继承速度，不再次单位化；零／负倍率保留。当前官方选样未显式非零 blend。非零 blend 的任意旋转、地形法线、相对方向组合、引用子节点及特殊时钟仍保留候选诊断，不把单位挂点测试外推为这些组合的原生验收。
- **最小像素尺寸**：补齐普通面片 `vs_rendersize_limiter`。保留原 `pixelsize_limit` 的半精度值，以原投影、相机距离、视口宽度等比放大 XY，并把放大后的尺寸传给软深度；不改颜色、UV 或寿命。支持有限非负且半精度可表示的范围；非法参数仍诊断，不静默放行。绘制入口为缺少视口尺寸的旧调用补当前设备视口，纯范围估算不套像素下限。
- **弱透明度**：R86 原生桥将 Alpha 小于约 `0.5/255` 的正值归零。Poison 原件的两阶段峰值约 `0.00110/0.00153`，整层因此失去贡献。现在沿用既有浮点倍率通道，对低 Alpha 按指数范围归一化、分组后恢复原值；精确遮盖／发光路径也保留倍率。零值、强弱顺序和大于 1 的覆盖语义保持；未提高原曲线、粒子寿命或 DDS 亮度。
- 全局 `1.0 / 120`、UI、Kitbash、选择交互及普通一次性播放均未修改。未新增材质或更改材质 HLSL；生成的 17 份编译材质头与 R86 原字节完全一致，原生代码只改桥内 Alpha 分组／归一化。

### 本次重新执行的验证

- 原生 `Build.ps1 ... -Configuration Debug` 成功（`build-native-v1.log`）。正常 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 成功：883 条既有警告、0 错误。测试工程通过 `BuildProjectReferences=false` 重建，3 条既有警告、0 错误；没有用旧测试程序集判新代码。
- 正常交付 Core SHA256 `2558B8126B7FCB45F92AD570A52A478B40C5C89FFE4DF6E4CC805AF7BFF32CDC`；native `67A224C001CF005D0CAE7A7D7A8F990D1831C8554A1A9A9BB5D9940480BDAD56`。正常 Debug、测试目录、GPU after-probe 均核对一致。旧 runtime、所有 baseline/after 候选保留。
- 新弱 Alpha 用例使用未修改的官方火焰 DDS 固定原纹素及浮点目标，明确标为 DIAG。隔离旧 native：`alpha-audit/red-baseline-v2` 为 2 通过、12 失败；新 native 的独立同输入为 14 通过、0 失败、0 跳过。原文件 SHA、CPU 各项 Alpha 乘数、原生 float VS 证据及绿灯审计均保留，不用增亮或修改纹理通过测试。
- 当前交付 DLL 的 `core-v1/core.trx`：358 项，357 通过、1 失败、0 跳过。唯一失败是新增暂停瞬移测试以理想整数速度 4 比较 x=1000 的 float 位置差（实际约 4.00049）。修正测试按同一原生有限差公式计算精确预期，主数值容差从 0.0003 改为更严格的既有 0.0001，并增加非 Tick 整点暂停及速度上界；没有改实现或放宽断言。重建后 `motion-final-v1/motion.trx` 的 14 项全部通过、0 失败／跳过。按用例名称合并覆盖共 358 项、无剩余失败；其中 13 项重复复测，不将两批数量相加冒充一次全量。首次失败日志及测试原件备份保留。
- 本轮自动门禁含转换／模拟／微型着色器合成边界测试；它们与官方完整原件视觉对照分开，不将微型用例、测试数量或非空像素视为游戏原生验收。完整官方原件 GPU 均只从当前 manifest 的 271 个官方 Pack 读取，核对大小、依赖与 SHA，无 Mod 覆盖。
- 完整 Moonbow、Amber、Aekold 的 q1 匀速→转弯→停止，.6/1.2/2.2 秒；Warp q1 使用 56 单位跟随镜头，Poison q1 使用 8 单位跟随镜头及实际 RGBA32F 目标，共 13 张基线＋13 张候选。另 Moonbow/Warp q0、q2，.6/1.2 秒，共 8 张基线＋8 张候选。两版来源、依赖、相机、画质、挂点轨迹和时点逐值一致；结果与实际看图记录在 `render-probe/same-source-comparison-v1.json`、`same-source-q02-comparison.json`、`render-observations-v1.json`。
- 已实际查看阶段画面：Moonbow 恢复此前缺失的碎屑及运动，Warp 电弧分支发生变化；完整图同时受运动继承与像素尺寸影响，不能把变化全归因某一个参数。Poison 的浮点弱贡献恢复（.6 秒从 0 变为 788 个变化像素，最大约 0.00406），但 PNG 仍近乎空白。RGBA32F 原始数据、实际 GPU 背景、元数据及显示用 PNG 全部保留；不以浮点贡献代替肉眼正常。
- Moonbow q2 原件主动关闭两层碎屑，前后均 disabled=2、converted=3；缺少碎屑属于源配置，不改原件。没有新 AECN／Terry GUI、键鼠或焦点操作，实机验收仍由用户完成。

### 当前剩余问题

- Amber 两个主层在当前 legacy 速度面片几何路径中仍几乎退化为线，最小像素放大不能修复这一维退化；本轮完整图仍稀薄。没有凭注释把相对朝向缺省改 false，或强行启用实验性完整三维分支。R63 捕获仅作为历史定位，未作为本轮官方原件视觉验收；当前 Amber 的原生出生姿态／运行分支仍需官方同条件证据。
- Poison 的原初始透明度随出生阶段缓慢增加，本轮修复的是“正值被抹零”，尚未确认完整原生强度与形状；普通面片的原生距离淡出等边界也仍未完整覆盖。
- Aekold 仍按原本地空间聚在挂点附近；第七个 sound-only continuous_rigid 仍缺模型定义，不补造资源。引用偏移、事件节点、地形及任意方向混合不宣称全面原生等价。
- R86 已修复的 impact 地面波／arc90 尘环由本轮转换及材质回归继续覆盖，本轮没有新增这两者的完整窗口验收或新 Terry 对照，也未修改 arc360 已确认的原生格线。
- 最终源码范围、保留项、分批测试覆盖及运行时一致性见 `workspace-after.json`、`final-audit.json`。正常 Debug 保留用于用户测试，无提交／推送／清理。


## R86 Trail 参数、砸地波与 arc90 缺层修复（2026-10-24）

### 当前范围与保护

按用户顺序处理 trail → impact → arc90/arc360。原地沿用 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。开始时 220 项已有源码修改／未跟踪文件的状态和字节与 R85 最终快照全部一致；暂存为空，index SHA 未变。`artifacts/vfx-display-coverage-r86/workspace-before.json`、`source-before/`、`runtime-before/` 保留现场。没有切分支、worktree、暂存、收起、回滚、提交、推送、PR 或清理。

### 实现与证据边界

- `enable_trail_mode` 原来只从行为链读取，漏掉官方渲染栈。现在读取对应画质的渲染栈，q0 默认、q1/q2 覆盖；没有时兼容旧行为链。相同画质重复、两处值不同仍严格停用并诊断。当前官方主 VS 反汇编及 `particle_vfx_library.hlsl` 均证明拖尾开关只决定是否加基础长度 1，再应用上限；没有前后粒子拼接。没有新增 ribbon 或扩大几何解释。
- `continuous_rigid` 的正 `SpawnSpacing` 原来被置零，现在与普通面片共用距离发射，保留原模型、材质、空间模式及寿命。独立连续预览也识别模型距离轨迹；原有限根、普通 `Seek` 与事件模板的时序边界不变。Aekold 两层分别保留 1 和 0.5 的间距。原件本地空间的花朵仍聚在当前挂点附近，不能把新增粒子说成已形成世界空间花带。
- impact 的 `impact_ground_wave` 尺寸曲线含同时间同值、不同出入模式的接点，原来按文本不同误拒绝整层。预览快照现在合并第一个入切线和最后一个出切线；原始 XML、关键点顺序与字节不改。异值（包括微小差值）、非法切线、未支持模式继续严格失败，没有放宽数值断言。
- arc90 `smoke_ring` 的 XY 尺寸有效，Z 是空通道。仅最终为普通面片的 ParticleScale/ParticleInitialScale 在不可变快照中将空 Z 按 1 取样；已有有效 Z 保留用于事件继承。模型、投影贴花和其他字段仍严格验证三轴，不以删除空通道掩盖三维缺陷。
- 不改全局 1/120、不改 UI／选择／Kitbash、粒子原寿命、普通一次性播放或原生材质代码。arc360 没有为此次工作另改渲染参数，也未重新修改已确认原生的格线条纹。

### 本次重新执行的验证

- 当前 manifest 271 个官方 Pack 大小全部匹配，仅从这些 Pack 索引／读取原件和依赖，记录 Pack 与 SHA；没有读取 Mod 同名覆盖。CPU 扫描 7,497 份有效 VFX，另 885 份为非 VFX 文档；397 个 trail 候选为启发式范围，不是完整支持率。
- 正常主程序 Debug 构建成功：1879 条既有编译警告、0 错误，日志 `build-main-v1.log`。最终测试项目按 `BuildProjectReferences=false` 重建，3 条既有警告、0 错误；避免再次改变正常主程序交付运行时。正常输出仍为 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。
- `core-final-v1/core.trx`：236 通过、0 失败、0 跳过。覆盖新拖尾栈／画质／拉伸／模型距离与事件时序、曲线接点／空维度、九项完整官方转换与投影，以及既有距离发射、连续预览、解析／混合曲线、朝向和水平几何。初次 `core-v1` 是较早候选 DLL 的 236 项结果，保留但不与最终结果相加。曲线隔离红灯在 `impact-arc-audit/boundary-tests/before/`：旧 Core 的 12 项中 10 失败、2 通过，保留失败证据。
- 最终正常 Debug Core SHA256 为 `C0AF1908DF26170D8BC20B11550DBB40244C82C7B4F381FBAD5E84D5D72894B2`，native 为 `62FB5B63D83070905D1CA644EACDADF394CD0FE7AFA9D88D99B3DFB05C215B8E`。最终测试与隐藏 GPU 均核对确切交付 DLL。早期候选 Core `3215E9F7…` 的证据单独保留，没有冒充最终产物。
- 隐藏 GPU：完整官方 Moonbow、Aekold、Amber 的匀速／转弯／停止连续预览，同路径、同镜头 .6/1.2/2.2 秒；另 Poison weapon、Warp cannon 使用 8 单位跟随近景 .6/1.2 秒。已实际查看帧，Aekold 距离参数与 Warp 拉伸改变确实进入画面；Moonbow／Amber 保持原样，Poison 仍近乎空白。早期普通 `Seek` 目录与后来的显式 `SeekContinuousTrail` 分开记录。
- 隐藏 GPU：完整 storm dragon impact、Neferata arc90、arc360 的 q0/q1/q2、.1/.3/.6/1.2 秒，同镜头同阶段比较。实际查看展开、运动及消失阶段：impact 恢复地面扩散环，arc90 恢复尘环；arc360 保持原路径。最终为 `impact-arc-final-q012-v2/`，逐帧比较见 `impact-arc-final-comparison.json`。v1 保留采样工具重算默认镜头的失败现场；v2 纠正工具，严格复用原 Eye/View，而非放宽相机比较。本轮 impact 完整根覆盖限 storm dragon，不用此样本代表全部砸地类型。
- 无新的 AECN／Terry 窗口操作、键鼠输入、同条件 Terry 捕获或游戏原生人工验收；用户负责实机测试。隐藏 GPU 使用合成挂点轨迹和编辑器背景，不能替代真实绑定骨骼或游戏最终显示。

### 尚未解决／下一轮入口

- Trail 尚不能宣称全部修好：完整 Poison weapon 近景仍几乎空图，原运行时 Alpha 峰值约 0.0015；Amber 仍较稀薄。未为更亮修改原颜色、透明度或原 DDS。
- `SpawnInheritedSpeed` 在 Moonbow 等原件中存在，当前仍未实现。当前官方出生 CS 输入已含初速度，但 CPU 预填字段／单位没有可证映射；`delta_pos` 只证明参与位置处理，不能直接充当速度或随意除 dt。精确静态边界见 `route-audit/spawn-inherited-speed-static-conclusion.json`，后续需原生运动输入证据再实现。
- Aekold 第七层是缺 RigidData 且空行为链、带 SoundRecord 的 continuous_rigid，保持 GeometryUnsupported，不补造模型或把其当作完整支持。
- arc360 引用中启用的 `3D_ring` 在当前官方库原件不存在，已保存原件证据；不能补造该层。其余原生合成、空间运动、贴花接收面与完整外观仍按既有候选边界。
- 最终保留清单、源码变化、运行时一致性和准确图像范围见 `workspace-after.json` 与 `final-audit.json`；保留本轮全部工具、原件、失败、候选、正常 Debug 和历史成果。


## R85 共用顶点动画光束材质与主 Beam 缺层修复（2026-10-24）

### 实现与原生语义

从 R84 最终快照原地接续，217 项已有修改／未跟踪文件的状态与字节一致，分支及 HEAD 不变、暂存为空。源码和旧正常 Debug 备份保存在 `artifacts/vfx-vertex-anim-r85/source-before/`、runtime-before；没有切换分支、提交、推送或清理。

- 补齐 `shaders/wh_vfx_mesh_vertex_anim_battle.xml.shader` 的独立托管解析与原生材质，64 个原始浮点参数（256 字节）、六个逻辑贴图、原几何／索引／UV／顶点色全部保留。完整官方 `wh2_dlc17_entity_beam` 与 `_ror` 不再丢弃主 Beam 模型层，没有按特效名称放行或静态漫反射回退。
- 从当前官方 manifest Pack 重新读取 shader 定义、三份 DXBC、材质、模型及依赖，保存 Pack／SHA 并反汇编。两份 PS 的反汇编相同；主 VS 不使用 UV2、顶点 ID 或 VAT，不需要改变顶点 ABI。原 VS 的正弦标量以顶点红色加权并广播到世界 XYZ，位移取 mip0 蓝通道、世界法线 X 及 `(1-UV.y)`；原图未读取的 Y/Z 控制保留，不凭名称增加额外振荡。PS 保留其在已位移世界位置上再次计算偏移、再求视角淡化的行为。
- 材质底色为 overlay／glow 乘粒子 RGB；模型 RGB 不染底色，模型 Alpha 按原开关平方参与覆盖。三路滚动通道与亮度相乘，global mask 取 R；粒子 Alpha 只按原 mesh-alpha 开关参与。按原单面剔除、深度测试、不写深度及预乘输出绘制。新材质追加为编译索引 16，旧 0～15 全部逐字节不变；扩展常量、位移纹理与采样器沿用作用域捕获／恢复。全局 1/120、出生、寿命、UI、Kitbash 和播放器逻辑未改。
- 官方几何 `heirotitan_laser.rigid_model_v2`（SHA256 ae4621a01b9f1825ffbbd22d2bfd4142e4b037668ac87e78a51249f7b64519db）仅 38 顶点、108 索引，原 RGBA 全零。该材质 vertex_anim=1 但红色权重为零，且 vertex_displacement=0，因此完整 Coatl 原件的几何本来静止；没有改白顶点色制造运动。代码已接通动态分支，另用明确 DIAG 验证其参数通路。
- 完整原生雾、第三路贴图的实例 UV 偏移、精确材质时钟与游戏完整透明合成仍未还原／校准，新增中文诊断保留这些边界。原 `fog=1` 实际选择无雾底色，`fog=0` 才应用场景雾；本轮保留该参数但输出候选底色，不把雾宣称为支持。

### 本次重新执行的验证

- 原生 Debug 最终 v3 构建成功；正常主程序 Debug 构建 0 警告／0 错误。依赖及测试工程重建有既有警告，保留实际日志；不是全程零警告。`compiled-material-audit-v1.json` 验证旧 16 份材质字节一致，仅追加本轮分支。正常输出仍为 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。
- `core-final-v2/core.trx`：**51 通过、0 失败、0 跳过**，包含新增 3 项 CPU／4 项官方 GPU、tidecall、原官方折射及独立软深度回归。两份完整光束分别对比仅省略 Beam 绘制的 DIAG，确认主模型实际贡献画面；仅改 vertex_anim=0 时原零红模型像素保持一致，仅改 vertex_displacement=1 时保留原模型和 DDS、验证位移实际影响。不是只凭有粒子或加载成功判断。
- `meta-final-v1/meta.trx`：**9 通过、0 失败、0 跳过**，重跑 R84 的 16／24 条实例隔离、16 条同名／混合逐实例贡献，以及完整 41／16 条官方 META。新增门禁禁止活动运行时用旁枝闪电掩盖主 Beam 的 MaterialUnsupported。bird04 原件仍有 16 个运行时、所选阶段 8 个活动，两条光束从各 3 个旁枝粒子变为各 4 个（含主模型），相关材质不支持诊断为空。计数本身不作为外观验收。
- `official-before-v1` 与 `official-after-v1` 是本轮同来源、同 AECN 相机／画质／时间／泛光全零环境副本的修前后原件对照；已实际查看两份光束修后 .6 秒画面及对应控制，主光束现已出现。`official-final-q012-v2` 补全两份完整官方原件 q0/q1/q2 的 .1/.3/.6/1.3 秒阶段。全部索引当前 manifest 的官方 Pack，原始来源／依赖／哈希均保留，未使用 Mod。
- 没有启动 AECN／Terry、发送键鼠或切换焦点；完整 META 的测试仍使用静态挂点回退，没有原生同相机／同阶段捕获，也没有连续骨骼动画的新增视觉验收。环境副本只是官方天气派生的泛光关闭对照，工具名 native-match 不代表本轮已匹配 Terry。

### 失败记录与待验收

修前两份完整原件的主 Beam 转换门禁失败，记录在 contract-before-v1。该早期第三项 fog=1 应拒绝的断言源于语义误读，不作为已确认实现缺陷；按实际 PS 改为严格保留 fog 参数，未假装实现雾。material-tests-v1 的两个 CPU 顶点断言误省略既有中性轴心运算顺序，在极小 Z 分量上不同；现按既有 `(材质轴心+.5)-入口轴心` 顺序严格逐值比较，没有放宽容差或修改已校准轴心实现，最终通过。所有早期失败、源码／旧 Debug、官方原件、工具和采样均保留。

交付的是主光束缺层修复候选；用户可在 VFX 编辑器打开两份完整光束，也可在超级视图测试原 `bird04/attacks/bi04_attack_01.anm.meta` 的 1.2～2.59 秒。最终窗口形状、运动、透明边缘、实际骨骼挂点及完整原生外观仍由用户验收，不能宣布所有 Effect 已正常。

## R84 多 Effect 数量核对与官方大组合覆盖（2026-10-24）

### 本轮发现

用户指出此前两个官方示例不能代表十几条 Effect，并明确要求自行寻找游戏原件，不再向用户索取复现样本。已原地核对实际分支、HEAD、完整状态及暂存区；215 项与 R83 最新用户反馈快照一致，源码和正常 Debug 备份在 `artifacts/vfx-many-effects-r84/`。沿用当前分支，未提交、推送或清理。

- 当前实现遍历全部 IEffectMeta，按源对象引用创建独立运行时；没有按两条示例名称筛选，也没有找到全局两条上限。此前真实 GPU 组合只覆盖两个实例，确实存在验证覆盖不足，不能把这一点描述成已支持所有组合。本轮没有凭猜测修改生产实现或增加一个新的 16／24 条上限。
- 自行从当前官方 manifest 指定的动画 Pack 索引 67,524 份 META，读取了排序中 34,692 份，找到完整原件：bear01 攻击 13 条、bird01 飞行重生 37 条、bird01 重生 41 条、bird04 攻击 16 条。不是全游戏扫描完成；扫描工具、原始字节、Pack、SHA 和字段清单保存在 root-meta-scan-v2，初始工具工程路径错误的失败日志保留。
- 实测完整 `animations/battle/bird01/rebirth/bi1_rebirth_01.anm.meta`（anim2.pack，SHA256 f6ac80a2b038bd1567c1f2cb3331c66b16f0839c772bc147da7b15a8c55153f9）：41 条原始 Effect 建立 41 个运行时，约 4.796655 秒有 23 个处于活动状态，其中 21 个当时有粒子。原始时间窗口的峰值计数为 14；运行时还包含瞬时 Effect 的粒子尾段，两者不能混为一个计数。
- 实测完整 `animations/battle/bird04/attacks/bi04_attack_01.anm.meta`（anim2.pack，SHA256 91a20d83a77c9d4aa00d71e60c3303fa53e4feae6d500c943e826664dc9cd584）：16 条原始 Effect 建立 16 个运行时，约 2.4923406 秒有 8 个活动且有粒子。因此新验证未复现“第三条起一律不加载”的全局数量限制，不能把用户的现象直接归因于条数。
- 另找到具体外观缺口：后一文件的 `wh2_dlc17_entity_beam` 与 `_ror` 中，主 `Beam` 模型层使用 `shaders/wh_vfx_mesh_vertex_anim_battle.xml.shader`，当前为 MaterialUnsupported；旁枝闪电粒子仍能显示。实例存在／有粒子不能证明主光束完整，后续应按这条共用顶点动画材质补齐原生语义，而不是增加计数、按特效名字放行或删除不支持诊断。它不能未经复现就当作用户所有组合缺失的唯一原因。

### 验证与实际范围

新增两个测试分文件，只增加数量和隔离门禁：16／24 个同名或异名实例跨两个 owner 逐条入队、Draw，替换中间一项不销毁其他项；一项资源缺失保留其余十五项。CPU 用例中的小型假文件仅用于对象身份／生命周期检查，不作视觉验证。

真实 GPU 使用官方完整 VFX／模型／贴图及依赖：16 条同名与 16 条混合实例，各自单独绘制、全部组合、逆序和逐条移除，对每条验证实际画面贡献。位置和时间为分区诊断安排，明确标为 DIAG，不冒充未经修改的完整官方 META。另一组测试直接加载上述 41／16 条完整原 META，保留原始位置、时间、缩放和所有条目；它们没有骨架／动画姿态，Tracking 使用静态回退，不证明真实骨骼跟随、运动或完整原生外观。已实际查看两个组合分区图和两份完整 META 的产品画面；没有新的 Terry 对照或连续动画验收。

仅索引官方 manifest 列出的 Pack 并核对文件大小，解析到的资源记录实际 Pack 与 SHA；未读取 Mod。LookupMisses 记录查找尝试，不等于必需资源全部缺失，例如模型默认材质候选路径的探测；具体缺层仍以逐实例诊断为准。初始测试 mock 容器返回类型错误已修正，失败日志保留；最终 Debug 测试工程构建和结果见 final-tests-v2，不把旧结果重复相加。

最终测试工程 Debug 构建为 0 警告／0 错误；`final-tests-v2/many-effects.trx` 本次重新执行 **9 通过、0 失败、0 跳过**（5 项 CPU、4 项官方 GPU），不是全解决方案测试。本轮未改运行源码，正常 Debug 沿用 R83 用户已测试候选；未启动窗口、发送键鼠或操作用户程序。数量门禁和原件追踪补齐后，仍需继续处理具体材质、骨骼运动、时钟及多 Effect 整体合成差异，不能宣布全部原版特效正常。

## R83 共用 tidecall 水花／波浪候选材质（2026-10-24）

### 本轮实现与边界

从 R82 用户反馈后的快照原地接续，210 项已有修改／未跟踪文件与 `workspace-after-user-review.json` 的状态及字节完全一致。分支仍为 codex/vfx-effekseer-preview，HEAD 7c1b2a1ab8517e6bdc41efa79de2e9364761814b，暂存区为空。源码和旧正常 Debug 备份、采样及失败记录全部保存在 `artifacts/vfx-tidecall-r83/`，未切分支、提交、推送或清理。

- 原始风暴龙冲击的 `impact_splash_radial_mesh` 与 `impact_splash_centre` 以前因 `wh2_vfx_cst_tidecall.xml.shader` 未支持而缺失。新增共享解析和专用材质，保留原 92 个浮点参数（368 字节）与十二个逻辑纹理绑定；没有按特效名称修改实现。
- 接通原泡沫、噪声、纵向／波浪遮罩、双路消解、粒子透明度阈值及屏幕抖动裁切。按本次官方着色器字节与反汇编核对：泡沫混合权重及额外增亮项使用 foamMask.r，不能误用曾占同寄存器的 noiseAChannel。sea 颜色保留实例／粒子颜色输入，没有改成模型顶点色；实例输入完整业务打包仍需原生实捕确认。
- 位移使用原纹理 mip0 蓝通道、归一化世界法线及 `(1-UV.y)`，保留作者强度。风暴龙两层原值分别为 0 和 .2；第二组完整官方波浪原值为 2.2。位移、wave 与消解遮罩按粒子年龄；foam/noise/y-mask/主消解按材质全局秒时钟。没有修改粒子寿命、播放逻辑或全局 1/120。
- 第二组官方完整引用源暴露六槽上限：仅去重仍装不下原波浪纹理。现在保留前六槽及额外五个 PS 绑定，VS 位移另绑；原贴图、用途、色彩空间与自带缩小层保留，不生成替代图集。专用 b2、VS t0/s0、PS t8～t12/s8 的调用者状态在绘制后恢复；保留原不透明覆盖、单面剔除及写深度。
- 这是内在底色／泡沫候选路径；原 normal 纹理及参数保留，完整延迟 PBR 法线光照、反射、精确材质时钟和抖动相位尚未还原，新增中文诊断明确限制。风暴龙当前紫色水面取自原 sea_colour，没有为截图调色或增益；不能据此宣称颜色达到原生。UI 布局、Kitbash、SuperView 和已确认的格线折射未改。

### 本轮重新执行的验证

- 原生 Debug 最终 v5 构建成功，主程序正常 Debug 构建成功（0 警告／0 错误）。依赖／测试项目重建有既有警告，日志保留。`compiled-material-audit-v1.json` 证明原有 15 份编译材质逐字节不变，仅追加 tidecall。
- `contracts-v1/contracts.trx`：参数保留／无效输入、两份完整原件转换、既有官方折射及独立软深度共 **40 通过、0 失败、0 跳过**。最终 `tidecall-gpu-final-v3/tidecall.trx`：新增 **4 通过、0 失败、0 跳过**，前两次 3 项结果不重复累加。两份完整特效分别与仅省略 tidecall 绘制的 DIAG 对照产生实际像素差异；另两项 DIAG 分别只将风暴龙两份原材质的 total_alpha_strength、波浪两份材质的 displacement_scale 改为 0，原模型／DDS 不变，验证实际 PS 裁切与 VS 位移通路。不是全解决方案测试，也不是视觉验收。
- 当前官方 manifest 的 Pack 名及大小均实际核对，未索引任何 Mod。完整根为 `vfx/wh3_main_entity_storm_dragon_attack_impact.xml`（SHA256 85c78dd85901788f239c1f25f06a7c76247bd1d57246dc7f45b7b9159d1414a7）及 `vfx/wh2_dlc11_deep_tidecall_breath_area.xml`（8c04fe6418fc6452835f47188e0d6e0fad188dff986a0230a8c899b17476ef74）。依赖来源及字节另存；当前官方 shader、主 VS／PS 也重新导出并反汇编，未把历史 R33 的提取当作本次执行。
- `complete-official-final-v1/` 最终离屏输出两份完整原件、q0/q1/q2，各 .25/.5/.9/1.5/1.9/2.3/2.8/3.4 秒，共 48 帧。缺失依赖为空，专用材质纹理失败及 RuntimeUnavailable 均为空。实际查看了风暴龙出现、展开、消隐及大小波浪展开、后段消失的多档位图；这是 AECN 产品画面检查，稀疏取样不冒充连续动画验收。
- 所有画面使用明确的 AECN 相机和同一已保存泛光全零环境副本；复用历史 R68 的渲染尺寸只作为产品检查条件。本轮未打开 Terry，没有新的同相机／同相位原生对照，不能把目录或旧工具的 native-match 名称当作已匹配证据。环境副本是官方天气派生诊断，不是未经修改的原始天气。
- 48 帧采样之后，仅为两个新 C# 文件补上仓库要求的 BOM／CRLF，并新增位移门禁，没有改变运行算法。正常 Debug／测试／Probe 随后重新构建并核对 Core 与原生模块哈希一致；上述 40 项及 48 帧复用本轮相同语义结果，最终 4 项 GPU 门禁使用最终模块重新执行。中文资源已编入 Debug，实际中文界面及连续动画由用户实机验收。

### 保留的失败与待验收内容

修前 CPU 用例稳定失败；早期 .2 位移门控遗漏、原生 opaque 材质门控遗漏均已修正。`storm-impact-after-q0-v1/` 曾在 RuntimeUnavailable 后写出空图并退出 0，**该输出无效**，Probe 已补严格运行时失败门禁，v2 明确报错，v3 后才产生有效画面。早期第二组波浪在六槽限制下被诊断并跳过，不能用它当作已显示；最终额外绑定版本才通过实际贡献检查。native-build-v4 的额外采样函数未区分 VS／PS 编译失败也保留，最终 v5 已修复。旧工具缺相机、错误工程路径等失败日志均保留，没有清理。

正常候选仍在 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。未发送键鼠、切换焦点或启动实机预览；检查时原 Debug 实例已自行退出，未执行关闭操作。用户可优先看风暴龙冲击 .1～.9 秒和潮汐呼唤 1.5～3.4 秒的水面形状、泡沫裁切与消失过程。完整延迟光照、实际动画相位／运动、复杂力场、接收面深度和多 Effect 整体合成仍有既有候选边界；本轮不能宣布全部特效正常或原生级完成。

### 后续用户人工反馈（2026-10-24）

用户测试本轮 Debug 后反馈：“测试完了，没什么问题”。记录为 R83 候选在用户实际测试范围内未发现明显问题。用户未逐项列明实际样本、画质、阶段与场景，不补写上方两组建议样本全部通过，也不扩大为全部特效或原生等效验收；原有光照、反射、时钟与合成边界继续保留。

本次仅补充人工反馈，没有修改实现、重新构建、重跑自动／离屏测试或操作桌面。沿用当前分支，保留正常 Debug、全部已有成果和验证记录；未提交、推送或清理。

## R82 共用独立软深度、普通预乘面片深度偏移（2026-10-24）

### 本轮实现与边界

从 R81 最终快照原地接续，分支仍为 codex/vfx-effekseer-preview，HEAD 7c1b2a1ab8517e6bdc41efa79de2e9364761814b，入口 209 项已有修改／未跟踪文件逐字节一致，暂存区为空。源码及正常 Debug 旧输出保存在 `artifacts/vfx-depth-contract-r82/source-before/` 和 runtime-before，不提交、不推送、不清理。

- 补齐 R71 记录的两个实际接口缺口。调用者可通过 CommonShaderParameters.VfxSoftDepth 提供独立单采样深度 SRV；普通面片颜色及 DISTORTION 按绝对 SV_Position / 实际颜色目标尺寸 × 深度图尺寸 point Load。实际光栅 DSV 保持原状，贴花／专用模型材质仍读取完整接收面深度，切图前刷新延迟批次。未提供独立图时仍复制场景 DSV；**没有自动生成游戏 quarter-depth**。
- 原普通预乘面片漏传 scale_strength、粒子 XY 最小有符号尺度、半精度参数以及淡出分母的 .001。现在用独立 SoftParticleScale 传递，避免与照明 ParticleSize 相互覆盖；同一无 epsilon 偏移只改 clip Z，保留原几何 XY/W。柔边视深度独立透视插值，保留照明 worldPos，并处理原生近裁面限制。
- 仅对已有普通预乘 Kind0、非专用材质、strength∈[0,1]、非负且有限半精度可表示的 soft_factor 开启。非预乘、模型、专用材质、负值和范围外参数保留旧候选路径及相应诊断，不借深度修复扩大材质支持。新增 ABI 字段与托管／原生尺寸门禁同步。
- 深度可用状态按本次实际所需层汇总，恢复后移除过期缺失提示。未调整颜色增益、泛光、格线、自旋、出生或全局 1/120；Kitbash、VFX 交互及 SuperView 元数据编辑源码未改。架构文档同步新增入口与限制。

### 本轮重新执行的检查

- 原生 Debug 构建 v1～v3 成功；测试工程最终 v4 构建成功，最终正常主程序及 Probe 构建均 0 警告／0 错误。中间大依赖重建有既有警告，保留实际日志，不写成全程零警告。正常 Debug 路径不变。
- `final-tests-v2/final.trx`：受影响 CPU 转换／模拟／时序／trail 及官方 GPU、独立深度／偏移专项 **144 通过、0 失败、0 跳过**。不是全解决方案测试。旧 ABI 尺寸断言升级为严格 132 字节并保留既有字段偏移检查；原非预乘诊断断言保留为参数化分支，同时增加支持与范围外门禁。
- 失败／纠正均保留：最初测试误在构建尚未结束时启动，读到旧程序集且两个输出目录不存在，该结果不计入新代码验证；Probe v1 缺 assets，恢复依赖后 v2 成功。shared-regression-v1 中两项旧合同断言失败，按新 ABI／受限语义更新后通过；该早期筛选误含历史合成纹理 GPU 夹具，不作为本轮官方原件验证，最终 GPU 筛选仅官方原件及明确派生诊断。depth-tests-v3 的新 CPU 用例误认 Neferata 有 lit+soft 组合，原件 directionality=.5 实际仍是候选 LightingMode0，已更正覆盖说明，保留尺度与实际贡献断言，未扩展照明实现。
- `exact-{full,opposite}-colour-gpu-v3/` 用最终正常 Debug 匹配模块重新执行官方 Gyro 实捕输入：真实 853×533 光栅 DSV、独立 426×266 quarter-depth、原 q1 XML／DDS、RGBA／UV／几何。独立深度 SHA 与逐 float GPU 回读无差，clip／soft-world／偏移门禁通过。v1～v3 full／opposite 原始像素一致，旧输出均保留。
- `exact-depth-full-review-v3/review.json`：本轮产品像素对照**历史 R71**原生两粒子颜色捕获及已校准 MBOIT。相同显示传递后 max RGB 差 2、>1 共 43 像素，归一化颜色总量约高 .0303%，差异集中于右弧局部；原生场景重建 max .0009765625。已实际查看对照联系图。共同草地／透射率的 CPU 分离诊断不是 AECN 完整场景，不把加权 RT 与普通 blend 直接判为等价，也不以数值门禁宣称视觉验收。
- `official-execution-final.json` 六条产品命令全部退出 0：Gyro 完整 q1 原件在 .998382568／1.413391113／1.862396240 秒，Neferata 与 Malus 完整原件各 .2／.6／1.4 秒。仅索引当前 manifest 指定的官方 Pack，模型／贴图依赖缺失均空；已查看九张视口图的形状、边缘及阶段变化。最后仅补负值保护不改变这些原件的有效正值分支，完整样本复用本轮输出，最终 v3 实捕输入另行重跑确认像素一致。棋盘高对比格线折射保留；这些画面不是新的 Terry 同背景动态验收。

### 桌面边界与剩余工作

实机测试仍由用户负责，未操作 AECN／Terry、发送键鼠或切换焦点。尝试原生离屏工具时 `qrenderdoc --help` 意外产生本任务帮助窗口 PID40352，已只对该确切实例正常 CloseMainWindow、WaitForExit 成功并核实退出；未强制结束，随后停止启动 RenderDoc。本轮没有新的原生 replay；原生 RT 与逐字节校准明确复用历史记录，opposite-only 原生重播未执行，产品 solo 不冒充配对验收。

当前闭合的是受限普通颜色的同实际输入深度链路。游戏 quarter-depth 生成、完整 MBOIT／半分辨率重建、多个独立 Effect 的整体合成、随机粒子和连续动画相位仍未完整还原。Neferata 未覆盖照明参数继续候选；风暴龙 tidecall 水花与地面接收面限制沿用 R81，没有顺手改材质。最终窗口动画观感及完整官方组合仍待用户实机反馈，不能报告全部特效已达原生级。所有源码、旧 Debug 备份、工具、采样、失败和既有 Mod 材料保留。

### 后续用户人工反馈（2026-10-24）

在收到 Gyro、Neferata、Malus 官方样本及播放阶段建议后，用户反馈：“我测试看着没啥问题了”。记录为用户本次实际测试范围内未发现明显问题。用户未逐项列明实际测试的样本、档位、阶段与场景，不补写三个建议样本全部通过，不扩大为全部原版特效或原生等效验收；上方尚待反馈的记录以本条最新反馈为补充。

本次仅补充人工反馈记录，没有修改源码、重建 Debug、重新执行自动／离屏测试或操作桌面。继续保留当前分支、正常 Debug、全部已有成果与验证记录；未提交、推送或清理。

## R81 超级视图多 Effect、瞬时尾段与版本参数（2026-10-24）

### 本轮定位与修复

用户给定 `animations/battle/humanoid01e/storm_dragon/attacks/hu1e_storm_dragon_attack_01.anm.meta` 及 `wh3_main_entity_storm_dragon_attack_impact`、`wh3_main_entity_storm_mage_lightning_charge`。已直接读取官方 anim2.pack / vfx_desc.pack，保留原始压缩数据、解压副本和 SHA256，没有使用 Mod 覆盖。两条均为 EFFECT_v12。

- impact 的 Start=End=1.289613127708435 秒，Scale≈0.2。旧门控仅允许这个瞬间，动画帧跨过后整条粒子被隐藏；`instant-before/` 已稳定复现失败。现在仅对瞬时 Effect 按 VFX 自身 CycleDuration 延续尾段，普通非瞬时时段不改；不改源时间、时间轴或用户整段显示设置。共享粒子节点只新增可空的只读 Duration，没有修改绘制或模拟算法。
- charge 为 0.47260499000549316～1.4103188514709473 秒，Scale≈0.3，Tracking=true，NodeIndex=34。旧实现只读取 v11.Scale，v12 等版本遗漏；现在 v3/v5/v7/v11/v12 的当前与历史缩放统一读取，无该字段的版本仍取 1。位置、朝向及有效骨骼跟随链路原本存在，本轮按版本补充参数传递检查。
- 另查到重复粘贴复用同一个 META 引用的缺陷，会使字段串联和按 Source 的单项刷新出错。`paste-before/` 留存修前失败；现在每次粘贴用既有复制 API 创建独立对象，不改剪贴板服务或格式。直接 Meta Editor 和超级视图两个子编辑器共用此命令，均纳入元数据编辑与保存回归。
- EffectType 非普通行为、TerrainMutable、DistanceCulled、旧 BoolUnknown 和 v12.ScalesWithParent 尚未完整还原；增加中文预览警告，不猜原生语义，不改源值、dirty 或保存门禁。v12 明确提示父级缩放选项目前未区分。复用现有问题列表，未增加 UI 控件。
- Kitbash 与共享 Gizmo 221 个源码文件逐字节不变；独立 VFX 编辑器 R80 源码保留。没有改全局 1/120、原生桥、共享模型选择、双 owner 历史或游戏源件。架构和 SuperView 文档同步边界。

### 本轮重新执行的验证

证据在 `artifacts/superview-effects-r81/`。入口 206 项现场与 R80 快照一致，仍为 codex/vfx-effekseer-preview / HEAD 7c1b2a1ab8517e6bdc41efa79de2e9364761814b，暂存区为空。源码及正常 Debug 旧输出均有具名备份。

- Debug 构建成功，正常主程序输出与被测 AnimationMeta/Core 程序集一致。测试构建 v1 曾因新 partial 缺少命名空间而失败，修正后 v2～v6 均通过；失败日志保留。最终 v6 构建 0 警告、0 错误。
- `meta-v1/`：受影响 AnimationMeta 过滤 182 通过、0 失败、0 跳过，包含构建、资源隔离、时间、空间编辑、检查索引、字段、选择、多选、保存、新参数、粘贴与官方 GPU。修复前粘贴和瞬时门控的失败单独保留，未放宽断言。
- 首次原版组合发现公共白贴图依赖缺失；补入明确官方 commontextures.pack 后重建测试，仅复跑受影响的 4 项官方 GPU，全部通过，见 `official-final/`。其余 178 项复用本轮未改范围结果。`core-final/` 的转换、模拟、引用、原时序及距离发射 53 通过；`editor-final/` 的 View3D 隔离及 VFX 播放适配 14 通过。独立覆盖 249 项、0 失败、0 跳过，不是一次全解决方案测试，GPU 重跑不重复相加。
- 三组多实例原版对照：同一 pinning_icicles 双实例、pinning_icicles 与 cinematic_smoke_blast、用户的风暴龙组合；均比较各自单独、共同与反序绘制。最终风暴龙采用原 META 时间、Scale 和其余字段，主时间 1.35 秒；仅位置另加 ±20X 作分区诊断。VFX 及模型贴图保持原件，所有解析仅来自 6 个明确官方 Pack；最终该组 24 个依赖均有来源、Missing 为空，见 official-render-v2 的来源清单与散列。
- 实际查看最终风暴龙单独 impact、单独 charge、共同与反序四张 GPU 图，确认诊断时段两项各有贡献。未提供真实骨骼 34 动画，因此距离发射拖尾未由静态图验收；该分区场景不是未经修改的完整游戏场景，也没有 Terry / 游戏原生对照。实机动画和交互仍由用户负责。

### 剩余限制与保留

impact 的两个水花刚体层使用尚未适配的 wh2_vfx_cst_tidecall.xml.shader；crack_decal 仍受超级视图接收面限制。它们不是多实例覆盖问题，本次未盲改共享材质，不宣称全部层、全部设置已正常。调查原件、启用层和材质证据见 investigation。

本轮没有启动或操作 AECN/Terry 实机窗口，没有系统键鼠或焦点输入。离屏设备使用隐藏 HWND；会打开窗口的主题画廊未运行。中文键及 Debug 语言文件已核对，实际警告显示交由用户验收。

保留正常 Debug、源码、备份、官方原件、调查失败记录及所有日志/TRX/图片，已有 Mod 素材不动。没有清理、暂存、收起、换分支、创建 worktree、提交、推送或 PR。R80 及更早正文按历史保留。

## R80 VFX 场景变换撤销与重做（2026-10-24）

### 原因与本次实际变更

- 本轮核实属性编辑原有 Undo/Redo 和根 PreviewKeyDown 路由已存在，共享视口未吞 Ctrl+Z/Y。实际缺口是挂点拖动／G/R/S 确认只提交临时姿态，从未记录撤销项；原快捷键因此只能操作 XML 文档历史，且不识别 Ctrl+Shift+Z。
- `VfxEmitterInteractionComponent` 在一次成功且有变化的确认后发送初始／最终矩阵，取消、捕获中断、无变化和历史恢复不记录；非空重置也记录一次。`VfxScenePreview` 仅转发该事件、恢复姿态和取消活动交互，不新增共享 Gizmo 或模型编辑能力。
- VFX ViewModel 复用当前页的 `_undo/_redo` 先后顺序，Edit 用 `ChangesDocument` 区分文档与预览项。预览项前后文档版本相同，不递增 revision，不调用 Changed／重建参数层／发布快照／安排预览重载，不修改 XML、保存点或 dirty。文档项保留原保存点、验证错误和字段恢复流程；两类操作的新分支均清 redo。
- Ctrl+Z 撤销，Ctrl+Y 或 Ctrl+Shift+Z 重做；Ctrl+S 原逻辑保留。正在拖动或框选时，Ctrl+Z 只取消当前未确认操作，下一次才消费上一条已确认记录；活动期间不重做。菜单仍使用同一命令，未新增 UI 控件或本地化文本。
- 恢复姿态在当前播放时刻写入原挂点历史的右侧值，并同步历史缓存；保留该时刻及之前的出生左极限，不 Seek／Restart／Reload，不修改 IsPlaying、时钟、粒子寿命或既有世界空间粒子位置。撤销恢复的是挂点，不倒放整条 trail。用户回看后恢复时，沿原 Record 规则在该时刻分支、截去未来路径，避免下一帧旧历史把姿态覆盖。
- 成功加载另一文档先取消活动交互并清本页撤销栈，关闭解除事件订阅。框选、灰／黄色小线框、直接拖动、公共播放器保持。Kitbash 与共享 Gizmo 221 个源码文件、核心及原生运行时逐字节不变；固定 `1.0 / 120` 不变。

### 本次重新执行的验证

证据目录 `artifacts/vfx-undo-redo-r80/`。入口 205 项现场与 R79 最终快照逐字节一致，仍为 `codex/vfx-effekseer-preview` / HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存区为空。源码和正常 Debug 旧输出保留在 `source-before/`、`runtime-before/`；`VfxAttachmentHistory.cs` 仅备份和核对，原文件未修改。入口曾读到正常 Debug PID 34572，但构建前再次核对时已无实例；本轮没有请求关闭或结束程序。

- `dotnet build Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Debug --no-restore --verbosity minimal`：退出 0，82 个警告、0 个错误，同时更新正常主程序 Debug 输出；交付 VFX 程序集与测试使用文件逐字节一致，见 `build-tests-debug-v1.log`。
- `AssetEditorTests` 定向执行 `VfxEditorTests|VfxEmitterInteractionTests|VfxPlaybackAdapterTests|View3DComponentIsolationTests`：119 通过、0 失败、2 跳过（未配置真实样本目录的两项 opt-in）。新增 11 项编辑器集成／快捷键检查和 8 项交互 CPU 检查，覆盖确认／取消、属性与预览交错顺序、保存点、redo 分支、重置、活动操作先取消、关闭解绑、重载清栈和历史出生边界。快捷键经真实视图 handler 直接调用的私有 helper 传入 Key/Modifiers，未发送系统键盘，不当作真实焦点操作验收。见 `editor-v1/`。
- `GameWorld.Core.Test` 定向执行 `VfxContinuousTrailPreviewTests|VfxPlaybackClockTests|VfxDistanceEmissionTests`：55 通过、0 失败、0 跳过，见 `trail-final/`。本轮合计 174 通过、0 失败、2 跳过；不是全解决方案测试，未加算历史数量。
- 现有 6 组标记 GPU 离屏检查继续显式启用，重跑深浅背景 × 100%／125%／150% 的选中、未选中及框选状态；未改绘制源码。WPF 视图也重新离屏渲染 10 主题 × 3 缩放，实际查看深浅主题 100% 两张；这些使用假视口，不代表真实 3D 画面。真实样本目录保持空，未加载 Mod 或官方特效，不开展新材质、Terry 或特效保真验收。
- 所有窗口设施均不显示、不激活，未 Show／Present、不移动鼠标、不发键盘、不切换焦点。本轮没有启动 AECN/Terry 实机测试；实际 Ctrl+Z/Y/Shift+Z 焦点与拖动手感、完整官方 trail 外观仍由用户验收。

### 保留项与边界

保留所有源码、正常 Debug、备份、快照、脚本、日志、TRX、离屏 PNG 及既有官方／Mod 素材。未清理、暂存、收起、切换分支、创建 worktree、提交、推送或创建 PR。R79 及更早正文保留为历史；其他原生表现差异不在本次范围内。

## R79 VFX 框选与未选中方框（2026-10-24）

### 本次实际变更

- 按用户补充要求，VFX 共同发射器挂点在已加载时始终显示原小型空心线框：未选中为灰色，选中为黄色。保留 R78 的固定投影尺寸；点击空白取消选择后保留灰框，不再隐藏。卸载、关闭或无效投影仍不显示标记。
- 从空白处按住左键拖出空心矩形，与小方块的实际投影边界相交时，在松开左键后选中；支持四个方向及部分相交。低于 4×4 输入像素的拖动按空白点击处理，不误选。框选仍作用于当前预览范围的共同挂点，没有新增多发射器的独立变换集合。
- 框选暂时持有本组件鼠标所有权，普通鼠标捕获复用既有输入层，不开启连续变换或光标环绕。Esc、右键或捕获中断取消框选并恢复原先选择；其他组件拥有鼠标时不抢占，卸载取消待定矩形并清除选择。已选效果的直接拖动及原 G/R/S、确认／取消、挂点轨迹和连续 trail 播放保持。
- 矩形复用本组件原 BasicEffect 以屏幕正交投影绘制四条细边，内部透明，随后恢复相机参数与三项图形状态。仍不绘制变换文字、轴柄、引导线或“发射器”菜单；未新增公共控件、共享输入开关、源 XML 编辑或保存语义。
- 仅修改 VFX 交互组件、对应测试、现有架构/UI 说明及本交接。Kitbash 与共享 Gizmo 221 个源码文件逐字节一致，材质、原生桥、模拟及全局 `1.0 / 120` 未改。

### 本次重新执行的验证

证据目录 `artifacts/vfx-box-selection-r79/`。入口 205 项现场与 R78 最终快照逐字节一致，仍为 `codex/vfx-effekseer-preview` / HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存区为空；源码与正常 Debug 旧二进制均有具名备份。构建前核实同仓库正常 Debug 实例 PID 11624，按项目规则仅请求一次 CloseMainWindow，正常退出；没有强制结束、丢弃保存提示或关闭其他程序，见 `debug-close-11624.json`。

- `dotnet build Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Debug --no-restore --verbosity minimal`：退出 0，82 个警告、0 个错误，同时更新正常主程序 Debug 输出；测试使用的 VFX 程序集与正常输出逐字节一致。见 `build-tests-debug-v1.log`。
- `AssetEditorTests` 定向执行 `VfxEmitterInteractionTests|VfxPlaybackAdapterTests|View3DComponentIsolationTests`：60 通过、0 失败、0 跳过（交互 46、播放器适配 8、隔离 6），见 `editor-final/`。新增 14 项 CPU 框选检查覆盖四方向、无交集／部分交集、取消／捕获／鼠标所有权、Alt 导航、卸载和框选后 G 操作；已有 G/R/S、直接拖动、轨迹和连续播放器检查保留。
- 在原 6 组 GPU 标记离屏用例中增加灰框和拖选矩形实际绘制检查：深浅背景 × 100%／125%／150%，每组输出 selected／unselected／marquee 三状态，共 18 张 PNG。验证未选中仍有同尺寸同位置灰色边线、选中高亮、矩形四边、内部透明、取消／卸载、BasicEffect 相机参数及图形状态恢复。本轮实际查看深色 100% 和浅色 150% 的三状态共 6 张，见 `marker-render/`。
- GPU 检查仍通过显式 `ASSETEDITOR_VFX_SELECTION_HEADLESS_TEST=1` 启用，使用单一 WPF STA 宿主内不显示、不激活的 HWND；没有 Show／Present、键鼠输入或焦点切换，未打开 AECN/Terry 实机测试窗口。未加载任何 VFX、官方或 Mod 资源；这是选择控件的合成几何验证，不是特效保真验收，缩放测试也不等同真实 Windows DPI。
- R78 的 55 项核心 trail／时钟／距离发射结果仅作为历史保留，本轮没有重跑或加算。共享模拟及播放器产品源码未改；实际拖框手感、特效拖动与完整官方 trail 窗口观感仍由用户实机验收。本轮不是完整解决方案测试。

### 保留项

保留源码、当前正常 Debug、`source-before/`、`runtime-before/`、所有快照／脚本／日志／TRX／PNG 及既有官方／Mod 资料；未清理、暂存、收起、切换分支、创建 worktree、提交、推送或创建 PR。R78 及更早正文保留为历史；本次不恢复未完成的材质保真工作。

## R78 VFX 选中状态的小型方块线框（2026-10-24）

### 本次实际变更

- 用户实机反馈无法辨认选择状态，本次仅在 `VfxEmitterInteractionComponent` 恢复独立的选中标记绘制。复用现有 `LineHelper.CreateCube` 生成 12 条边，在当前共同发射器挂点显示黄色空心小方块；按相机投影将边长控制为 14 个输入像素对应的世界长度，透视、正交及缩放下保持小型，不随粒子边界、拖尾长度或挂点缩放扩大。无选中、预览未加载或投影无效时不绘制，点击空白依原选择逻辑取消并隐藏。
- 使用本组件专有、按需创建并释放的 `BasicEffect`，在场景渲染后绘制细线，不填充表面、不写深度；绘制后恢复原混合、深度和光栅状态。没有调用共享 Gizmo.Draw，变换文字、引导线、轴柄和“发射器”菜单继续隐藏。
- 选择、自由拖动、G/R/S、约束、确认／取消、鼠标所有权和 R76 连续 trail 产品代码保持原样；不写 XML、撤销或 dirty。仅更新 VFX 组件、对应测试、现有架构/UI 说明及本交接，不新增公共 UI 控件或配置。
- Kitbash 和共享 Gizmo 221 个文件与入口/R77 记录逐字节一致。本次未改共享渲染器、材质、原生桥、模拟、播放器、版本或游戏资源；全局 `1.0 / 120` 保持。

### 本次重新执行的验证

证据目录 `artifacts/vfx-selection-marker-r78/`。入口 205 项现场与 R77 最终快照一致，仍为 `codex/vfx-effekseer-preview` / HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存区为空。具名源码及原正常 Debug 二进制保留在 `source-before/`、`runtime-before/`。

- 正常 `Debug` 主程序构建成功；调整投影尺寸计算后，再执行 `dotnet build Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Debug --no-restore --verbosity minimal`，退出 0、82 个警告、0 个错误，更新正常主程序输出及测试程序集；不是隔离交付目录。日志分别为 `build-app-debug-v1.log` 和 `build-tests-debug-v1.log`。
- `AssetEditorTests` 定向执行 `VfxEmitterInteractionTests|VfxPlaybackAdapterTests|View3DComponentIsolationTests`：46 通过、0 失败、0 跳过。包括已有选择、G/R/S、直接拖动、空白取消、历史轨迹、播放器与编辑器隔离，以及新线框尺寸、投影、选中／卸载、状态恢复和实际绘制；见 `editor-final/`。
- `GameWorld.Core.Test` 定向执行 `VfxContinuousTrailPreviewTests|VfxPlaybackClockTests|VfxDistanceEmissionTests`：55 通过、0 失败、0 跳过，见 `trail-final/`。本轮合计 101 通过、0 失败、0 跳过；不是完整解决方案测试，不加算历史通过数。
- 线框 GPU 离屏用例显式启用 `ASSETEDITOR_VFX_SELECTION_HEADLESS_TEST=1`，通过统一 WPF STA 宿主创建不显示、不激活的 HWND，未 Show／Present，不移动鼠标、发送按键或切换焦点。深浅背景各 100%／125%／150% 共 6 组，实际验证空心、小尺寸、位置、BasicEffect 的 World/View/Projection 参数、取消／卸载后隐藏、绘制不改变活跃变换和三项 GPU 状态恢复。图片在 `marker-render/`，本轮实际查看其中深浅背景的 100% 和 150% 四张。
- 这些是选择标记的合成几何检查，未加载任何 VFX、Mod、模型或贴图，不作为官方特效保真证据；缩放测试不等同真实 Windows DPI 验收。本轮未操作 AECN/Terry 实机窗口，真实选取、拖动观感及官方 trail 的人工验收由用户负责。

### 保留项与尚未验证

保留当前可运行 Debug、全部源码、备份、快照、TRX、日志、渲染图、工具及所有既有官方／Mod 资料；没有清理、暂存、收起、切换分支、创建 worktree、提交、推送或 PR。R77 及更早记录保留为历史，不写成本轮复测。既有原生渲染差异不在本次范围内。

## R77 仅 VFX 隐藏变换叠加层并移除发射器菜单（2026-10-24）

### 本次实际变更

- 按用户实机反馈，删除 `VfxEmitterInteractionComponent.Draw` 覆盖及其本地 marker 资源，继承空的 `BaseComponent.Draw`。VFX 不再调用共享 Gizmo.Draw，因此黄色移动／旋转／缩放数值、模态引导线、轴柄／轴字及根菱形均不绘制；内部共享 Gizmo 继续负责原交互数学、事务和捕获，未添加全局显示开关。
- 删除 VFX 自己的隐藏轴柄命中分支。原点／效果估算边界首次命中只选择，已选后再次命中可直接自由平移并在松开左键时确认；空白点击清除选择，不会受旧轴状态影响。保留 G/R/S、轴／平面约束、数值输入、吸附、Enter／左键确认、Esc／右键取消及捕获失败恢复。输入不依赖 Draw 更新轴心。
- 删除场景顶部“发射器”菜单，保留“文件／编辑／视图”和预览画质；只复用既有 AeMenu 等公共样式，未新增公共 UI 组件、模板或资源键。编辑字段、保存／撤销、播放器及 R76 连续 trail 产品源码不变。现有架构和 UI 规范同步这一 VFX 专属可视边界。
- **没有修改共享 Gizmo 或任何 Kitbash 文件**。入口保存 `Editors/Kitbashing` 与共享 Gizmo 221 个源码文件散列，交付逐文件核对全部一致，见 `kitbash-and-shared-gizmo-before.json` / `kitbash-and-shared-gizmo-after.json`。本次不修改材质、着色器、原生 DLL、模拟步长或游戏源 XML。

### 本次重新执行的验证

证据目录 `artifacts/vfx-hidden-controls-r77/`。入口 205 项现场与 R76 最终快照逐字节一致，仍为 `codex/vfx-effekseer-preview` / HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存区为空。具名源码与正常 Debug 备份位于 `source-before/`、`runtime-before/`；正常输出原本没有独立 AssetEditor.CN.pdb，保留了实际存在的主程序／程序集及 VFX PDB，不是删除 PDB。

- 完整正常 Debug 构建 `dotnet build AssetEditor.CN.sln --configuration Debug --no-restore --verbosity minimal`：**exit0／175 警告／0 错误**，见 `build-debug-v1.log`。
- 编辑器、交互、播放器、隔离与 UI 架构定向自动验证：**79 通过／0 失败／2 跳过**，见 `editor-v1/`。其中 VFX 交互 **19 通过**（保留原 16 断言，新增 3 条：活动变换 Draw 零图形访问、效果本体自由拖动及松键确认、旧轴状态下空白不误命中）；编辑器 **40 通过／2 跳过**，播放器 **8 通过**，隔离及 UI 架构各 **6 通过**。顶部菜单断言精确要求只剩文件／编辑／视图；来源字节与 dirty 不变。
- R76 连续 trail、预览时钟及距离出生回归：**55 通过／0 失败／0 跳过**，见 `trail-final/`；Kitbash 变换手势／上传：**283 通过／0 失败／0 跳过**，见 `kitbash-final/`。均使用对应项目 `dotnet test --configuration Debug --no-build --no-restore --filter ...`，只运行已核对无需实机窗口或真实键鼠的类。三个独立程序集合计 **417 通过／0 失败／2 跳过**，不与历史结果重复相加，不是未过滤全套；详情见 `test-results-final-summary.json`。
- VFX 界面十套主题与 100%／125%／150% 渲染缩放的离屏检查通过，已查看深／浅和两套高对比当前截图，菜单布局正常，见 `ui-render/`。这些是 UI 控件截图，没有真实三维场景，不能当作 VFX GPU 或 Windows 实机 DPI 验收；变换覆盖层不绘制由源码与 Draw 零图形访问回归守护，实际画面仍由用户确认。

### 保留及待验收

- 正常运行入口仍为 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`，产物已更新；散列见 `runtime-final-hashes.json`。共享 Core／native 与此前实现保留，R76 的原粒子寿命、普通一次性逻辑及连续拖尾未改。
- **本轮没有启动 AECN／Terry 窗口，没有真实鼠标／键盘输入或新增 VFX GPU 视觉验证**。新 CPU 输入仅为诊断，未使用 Mod 特效作验证；请用户用完整官方原版检查隐藏效果、选择／拖动及连续尾迹。旧 Gyro／arc360／合成等剩余视觉问题没有在本轮复验，不扩大任何历史验收范围。
- 源码、正常 Debug、具名备份、PDB、工具、采样、初始化失败记录和所有历史官方／Mod 素材均保留；R76 及更早交接正文逐字节保留。没有暂存、提交、推送、PR、切分支、worktree 或清理。

## R76 运动拖尾连续预览与短周期断流修复（2026-10-24）

### 用户反馈、实际原因与修复

用户实机反馈：拖动 trail 时每到短播放周期末尾都会回零重播，形成一顿一顿的断流。R75 的自动检查没有证明这一实际行为已正确，本轮据此追加回归。

- 当前源码确认两层边界：`VfxPreviewComponent` 到 `CycleDuration` 直接 Seek(0)，清掉模拟／挂点历史；即使取消回零，`TotalWarVfxSimulation` 仍按有限发射器原 Duration 停止出生。两处均需处理，不能只拉长播放器或粒子寿命。
- 仅独立 VFX 场景显式启用新入口；循环开启、加载后确实观察到挂点变换，且有效根中有普通面片的正 SpawnSpacing 或 TrailMode 时进入连续运动预览。不依赖特效名称；未移动、无拖尾标记、未显式启用或关闭循环时保留原播放分支。TrailMode 自身仍是拉伸标记，不能据此宣称游戏原生 InfiniteLife。
- 连续预览保持同一模拟与挂点轨迹，越过原短周期继续出生。只延续匹配的根发射器，有限根的发射相位重复原时长；粒子原寿命、随机序列、容量／速率限制及普通非 trail 根、有限事件子层时序保持。停止移动后距离发射不补时间粒子，旧尾迹按原寿命消散；源 XML、InfiniteLife、ParticleLifeTime 均不改。
- 播放范围保留本轮已模拟峰值；回看与手动重新播放归零仍能回到已记录尾部。暂停、拖动进度与收起不推进；关闭循环按明确定位返回原末端，避免清空 R75 轨迹。重载／停止清除运动与范围状态。旧两／三／四参 Seek 默认保持，独立连续入口可越过 600 秒且不回零，地形内部重建保留此选择；全局 `1.0 / 120` 不变。
- 没有改布局、公共播放器模板、Kitbash 交互、转换器、材质、着色器或原生 DLL。稳定播放契约已更新现有 `docs/architecture.md`。长时挂点历史仍保留用于回放，数小时内存／精度／性能未做压力验收。

### 本次重新执行的验证

证据目录 `artifacts/vfx-trail-continuity-r76/`。入口与 R75 最终 204 项源码逐字节一致；沿用 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存区为空。修改前源码与正常 Debug 关键程序集／符号具名保存在 `source-before/`、`runtime-before/`。

- 修复前先构建新 CPU 诊断并真实复现：**4 通过／6 失败／0 跳过**，`build-red.log` 与 `red/` 保留；短 .125 秒发射器、.5 秒粒子寿命、.1 距离间隔在第 2 秒应有最近半秒的 9～11 个粒子，原实现实际为 0。
- 产品完整正常 Debug 构建 `dotnet build AssetEditor.CN.sln --configuration Debug --no-restore`，**exit0／1997 警告／0 错误**，见 `build-green-v1.log`。此后产品源码未变；新诊断 XML 的 velocity modifier 补齐必需 orientation，保留原粒子数量／寿命断言，并新增必须成功转换的前置断言；没有放宽转换器。修正测试后重建受影响 `Test.GameWorld.Core.csproj`，**exit0／3 警告／0 错误**，见 `build-core-v2.log`。首轮两项诊断输入失败 `core-v1/` 保留。
- 最终核心 VFX 命名空间：**798 通过／0 失败／498 跳过**，`core-final/`；其中新连续模拟 **13 通过**，时钟 **28 通过**（保留原 19，新增 9）。覆盖短周期续发、有限相位末端为零、停止后过期、混合普通层、真实事件子层、旧入口隔离、600.25 秒边界，以及通过真实托管 Effekseer 接口转发到 CPU 模拟（不创建 GPU）。
- 编辑器／播放器／交互／隔离／UI 架构定向检查：**76 通过／0 失败／2 跳过**，`editor-v1/`；公共播放器 **8 通过**（保留原 5，新增 3），R75 Gizmo 交互 **16 通过**，编辑器 **40 通过／2 跳过**，场景隔离及 UI 架构各 **6 通过**。SuperView Effect 另 **2 通过／0 失败／1 跳过**，`effect-final/`。
- 上述三个独立程序集共 **876 通过／0 失败／501 跳过**，不与红例或首轮重复相加，不是未过滤完整解决方案测试。命令为正常 Debug 的 `dotnet test <对应项目> --configuration Debug --no-build --no-restore --filter <上述类或命名空间>`；实际完整命令在对话，计数与类清单见 `test-results-final-summary.json`。VFX GPU opt-in 为 0，真实窗口／输入用例未运行。

### 交付及待实机确认

- 正常 Debug 主程序仍为 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。最终 Core SHA256 `6A73884CA61D451C73AE34C9E8353C7EF280FB0F5386881856C1C3660BF9EC4B`，与 Core 项目输出一致；原生 DLL 仍为 `2B0ECA0125C03A033BEB0E86DA014FBBF6B07070F33D68DB9C9F875FBC2A9276`，见 `runtime-final-hashes.json`。
- **没有启动 AECN／Terry 实机窗口，没有真实键鼠输入，也没有新增 VFX GPU 或官方视觉验收**。合成 XML 只用于 CPU 诊断，不作原生外观证据；未使用任何 Mod 特效作新验证。请用户在正常 Debug 中以完整官方原版与原始依赖测试实际拖动、连续尾迹、停止消散和普通一次性效果。旧 Gyro／arc360／合成等剩余视觉问题本轮没有复验，不宣称全部特效正常。
- 源件、正常 Debug、具名备份与符号、工具、测试采样、红／绿记录及所有历史官方与 Mod 素材均保留。R75 及更早交接正文逐字节保留；只修改本轮必要文件，没有暂存、提交、推送、PR、切分支、worktree 或清理。

## R75 工作区布局与发射器 Blender 交互（2026-10-24）

### 本次实际变更与边界

- 按用户五点反馈修改：移除场景中央欢迎文字；复用应用左侧资源树，中间为常驻三维场景和公共秒数播放器，右侧固定“属性／效果层／诊断”分类。参数页直接选择当前效果层，原单层与组合勾选预览继续保留。
- 新建、打开、保存、撤销等进入场景顶部紧凑“文件／编辑／视图／发射器”菜单，预览画质与菜单保持同一紧凑行。参照重定向 `RetargetWorkspaceView` 与 Kitbash，直接复用 `AeMenu.*`、`AeTab.*`、表单和分隔条、`AnimationPlayerView`，未新增公共 UI 组件或复制模板。
- 参数名称改为简洁中文术语，原始字段名、用途、调整方法和限制放入悬停提示；曲线、颜色、原始只读值、搜索、验证错误和保存／撤销语义保留。未知字段显示原始名，悬停明确未确认语义，不提供猜测性调法。
- VFX 专属交互复用共享 `Gizmo`，支持点击根标记或效果估算边界选择、拖轴／平面／根标记、G/R/S、轴约束、数字输入、吸附、确认和取消。操纵的是**当前勾选组合的共同预览挂点**；只勾选一层即可独立拖动该层。它不写 XML、不改变文档 dirty 或属性撤销；不启用模型编辑、不修改 Kitbash 交互或其他编辑器的选择能力。
- 记录播放中挂点轨迹，交给已有距离发射与固定步长模拟；WORLD 粒子保留出生位置，LOCAL 按原模拟空间跟随。暂停移动区分历史左侧锚与当前显示位置；显式 Seek 回放轨迹，回退后修改截断未来，自然循环与新快照开始新周期。捕获中断或失败立即取消手势并释放所有权。
- 新行为通过明确的四参预览调用启用，旧两参、三参及超级视图历史取样默认行为保留；地形触发内部重建也保留该选择。没有修改材质、原生 DLL、R74 竖直运动识别或全局 `1.0 / 120`。

### 本次重新执行的验证

证据根目录 `artifacts/vfx-editor-layout-r75/`。入口完整只读盘点与 R74 最终 199 项现场逐字节一致，沿用 `codex/vfx-effekseer-preview` / HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。修改前具名源码、正常 Debug 关键程序集与符号均保存在 `source-before/`、`runtime-before/`；历史源码、Mod 文件、工具、采样与失败记录保留。

- 新 CPU 距离发射／暂停回放用例先真实复现失败：**10 通过／1 失败／0 跳过**，见 `interaction-red/`。没有放宽原粒子位置或数量断言；修复后并增加捕获失败、地形重建、端点回放与真实 Gizmo.Update 输入状态检查，最终 **16 通过／0 失败／0 跳过**。
- 最终完整解决方案正常 Debug 构建：`dotnet build AssetEditor.CN.sln --configuration Debug --no-restore`，**exit0／175 警告／0 错误**，见 `build-solution-v4.log`。没有改配置、依赖或分析器来掩盖警告。
- 最终解决方案测试：`dotnet test AssetEditor.CN.sln --configuration Debug --no-build --no-restore`，附 `solution-test-scope.json` 的保守无窗口／无真实输入筛选，**exit0／4100 通过／0 失败／525 跳过**，15 份 TRX。额外排除了 52 个含窗口或输入站点的类及 25 个具名方法；排除项不算通过，也不并入 525 个运行后跳过。该结果不是未过滤全套，也不与早期定向结果相加。
- 最终包含：编辑器 **40 通过／2 跳过**，交互 **16 通过**，公共播放器适配 **5 通过**，原预览时钟 **19 通过**，原距离发射 **14 通过**，场景隔离 **6 通过**，共享 UI 架构 **6 通过**，Kitbash 变换手势 **239 通过**、上传 **44 通过**，SuperView Effect **2 通过／1 跳过**。分隔条样式元数据另在 v3 定向检查 **2 通过**；真实分隔拖动未执行。
- UI 纯离屏验证十套主题与 100%／125%／150% 渲染缩放，保留菜单高度、控件像素、字段／来源不变断言；人工查看了深／浅与两套高对比离屏布局。`ui-render-final/` 是 UI 控件截图，不含真实 3D 渲染，不能当 Windows 实机 DPI、场景视觉或官方 VFX 验收。v1 移除原测试隐藏原生窗口后暴露同一未挂窗视图的主题资源失效，已改为逐主题新建纯离屏实例，原颜色像素断言保留；v1 失败及全部构建日志保留。

### 保留与未验收范围

- 用户负责实机。**本轮没有启动 AECN／Terry 窗口，没有真实鼠标或键盘输入，没有新增官方 VFX／GPU 画面验证**。合成 XML 仅用于 CPU 语义诊断，UI 离屏截图不渲染特效；没有使用 Mod 作为新视觉样本。真实拾取、轴柄、拖动手感、焦点切换以及不同完整官方 trail 的出现／运动／消失，待用户在正常 Debug 实测。
- Gyro、arc360、完整加权透明与深度合成的原生差异没有在本轮修复或复验，仍按历史记录的验证边界保留；不宣称全部官方特效已经正常。
- 盘点只发现本轮明确授权的文件变化；`SharedUiArchitectureTests.cs` 的 VFX 分隔条清单从两条调整为一条后恰好与 HEAD 相同，因此它离开 dirty 清单，但文件和修改前备份均存在，不是删除。Git 索引文件散列有缓存字节变化，已逐项核对 **2532 个路径、模式和 blob 与 HEAD 完全一致**，暂存区仍空；没有恢复或改写索引，证据见 `index-content-audit.json`。
- 全部本轮源码、正常 Debug、具名备份、临时测试输出、UI 截图、日志、红例与复测证据均保留；没有提交、推送、创建 PR 或清理。既有 R74 及之前交接正文逐字节保留。架构与 UI 规范已同步稳定布局／交互契约。

## R74 八项旧失败分类修复与受限竖直运动恢复（2026-10-24）

**八项已处理：两项测试过时，六项由共用实现的候选识别回归造成。** 本轮修正前者的预期并加强契约，修复后者的受限识别；没有放宽轨迹阈值、改动冻结原生数据、删除歧义拒绝或修改全局步长。正常 Debug 构建及对应自动回归通过，实机仍由用户负责，不能据此宣布完整 VFX 或 Gyro 白弧已恢复原生外观。

### 分类、原因和最小修复

- **PBR 一项为过时测试**：MetalRoughness 已在明暗映射前合入重建的 `emissiveColour`，SpecGloss 仍是无此发光项的表达式；旧测试对两者硬编码同一旧字符串。本次分别匹配各自完整精确的 tone-map 语句，原灯光和执行顺序断言保留，并新增 Metal 发光重建、G-buffer、灯光倍率、tone-map、gamma 的顺序及错误组合禁止断言。产品着色器保持原字节。
- **法线一项为过时测试**：`ps_texture/normal` 与旧 `ps_normal/normal` 都是真正合法的纹理槽，不能把其中一处叫作无关字段、同时定义后要求猜选另一处。本次分别验证两个单槽及真正无关的 `vs_colour` 同名字段，要求实际法线字节、用途和绑定索引正确、无关资源不解析；另验证双槽同路径／异路径均诊断并拒绝该层、撤回纹理。转换器原歧义判断保持，未删除不支持条件。
- **运动六项为实现回归**：原生受控输入明确 `SpawnAngle=0/0`、`SpawnSpread=90/90`、相对 UP，历史 R7 原件和 R8 受限契约保留。后续旧式方向解析保留整圈方位 `0/360`，但固定 90° 仰角实际仍为竖直；原候选识别却要求解析后方位也为 `0/0`，使受限 1/60 候选静默落入普通 1/120 曲线运动。修复只在**旧式路径、未显式设置 SpreadArc、原 SpawnAngle 两端为零、仰角固定 90°**时恢复原候选识别，所有原材质、初速度／阻尼离散值、力限值、寿命、位置、挂点、风、网格、地形和事件限制共同保留。发射方位、随机取样、积分公式和全局 `1.0 / 120` 均未修改。
- 新回归要求恢复候选后仍保留 `SpawnAngle=0/360` 与原 XML 字节，显式非零扇区不得扩大受限范围。原六项运动断言、三份冻结 fixture、3px 阈值及模拟器逐字节保留；仅新增四个识别／边界用例。已确认的普通水平环、定向扇区、arc360 零弧度组合、缺省上轴及动态力曲线另行回归通过。稳定范围说明同步到 `docs/architecture.md`。

### 本次重新执行的验证

证据根目录 `artifacts/vfx-old-failures-r74/`。入口 198 项源码状态／散列与 R73 最终现场一致，空暂存区、分支及 HEAD 不变；修改前七份具名源码／文档另存 `source-before/`。原正常 Core2F68 的 DLL／PDB 仍保存在 R73 自动对照目录，本轮没有删除或覆盖历史证据。

- 修正过时预期、增加回归但尚未修复产品时，以旧 Core 重建并运行三个相关测试类：**156 通过／7 失败／16 跳过**（exit1）。失败为原六项运动加新的候选识别断言，证明测试更正未掩盖实现问题。`red-original-core/` 与失败日志保留。
- 产品修复后扩大到方向、曲线力、播放器和范围选择：**236 通过／0 失败／18 跳过**（exit0），见 `green-targeted-v1/`；不与下一次重复相加。
- 最终完整解决方案正常 Debug 构建 **exit0／3 个既有警告／0 错误**，见 `debug-solution-build-final.log`。本轮未修改原生代码或着色器。最终正常输出 Core SHA256 `89BAE1A43A64335B3AB4BA579AFC7739183FFE826B304130FF9B5D2DD2AF22C9`；native 仍为 `2B0ECA0125C03A033BEB0E86DA014FBBF6B07070F33D68DB9C9F875FBC2A9276`。
- 最终 VFX 命名空间及 PBR 源码／CPU→编译 Effect 光色传递：**785 通过／0 失败／498 跳过**（exit0），见 `results-core-final/`。其中原六个运动失败以原名称、原断言全部通过；过时两项改为更严格的正确契约。该范围不是未过滤的完整解决方案测试。
- 共享场景隔离及播放器适配：**11 通过／0 失败／0 跳过**；SuperView Effect 对应检查另一次为 **2 通过／0 失败／1 跳过**，均 exit0。CPU／源码、参数上传和视觉验收分别记录，详见 `classification-and-validation-final.json`。

本次没有启动 AECN／Terry 实机窗口，VFX GPU opt-in 保持关闭；498／1 项跳过及历史原生录制不算本次画面通过。没有使用 Mod 作新视觉样本，没有新官方原版动画／像素验收；正常 Debug 可交用户检查官方特效。R71 仍记录未闭合的完整深度／合成接口。只修改一处产品识别条件、三个测试文件及对应现有文档；保留所有源码、Debug、备份、工具、采样和失败记录，不提交、不推送、不清理。

## R73 UI 改版剩余八项失败的前后基线复跑（2026-10-24）

**结论已实际复跑确认：R72 剩余八项失败全部在 UI 修改前存在，没有一项由本轮 UI 改版引入。** 两侧相同的 175 项 CPU／源码用例，结果均为 **147 通过／8 失败／20 跳过**，全部用例结果无差异，八项失败的错误消息也逐字相同。旧问题仍未解决；本记录只补齐因果证据，不把八项失败改写为通过，不扩展到材质或运动修复。实机由用户负责，本轮未操作 AECN／Terry 窗口。

### 本次重新执行的证据

- 入口在原目录核对原分支、HEAD、空暂存区和全部 198 项源码状态／散列，与 R72 最终快照完全相同，无外部变化。证据目录为 `artifacts/vfx-ui-failure-audit-r73/`，包含 `workspace-before.json`、`entry-vs-r72.json`、最终快照及交接原件。
- 使用 R71 已保留、SHA256 与 R72 入口一致的 **UI 前 Core376**（`376ACD0CA7B1548981130217B5CFF7D758602CA15AB99755F8EFA9A8ADBB1A03`）和当前 **UI 后 Core2F68**（`2F68ECBCD3D26247CECDE09621642B182C12D483D759D05352E3AB2B3B15FEDA`），分别在证据目录内建立只引用现有 DLL 的 Debug 自动测试小项目。不是替代工作区，未切分支或替换原源码／正常 Debug 文件。两侧编译均 exit0／0 警告／0 错误，测试均 exit1（相同八项断言失败）。测试启动检查实际加载的 DLL 路径和 SHA，不能仅以待加载文件散列代替运行身份。
- 两侧逐字节复制相同的三份原测试源码和三份运动 fixture，未删断言或放宽阈值；范围包含相应测试类的正常控制例。PBR 用例仍读取原路径着色器，该源码与 R72 UI 修改前快照逐字节相同，PBR 测试源码当前 Git blob 亦与入口时干净的 HEAD 一致。所有 GPU opt-in 关闭，20 项实际跳过，未以合成白纹理或旧 Mod 运行新视觉验证。
- 实际 TRX 分别为 `results-before-ui/before-ui.trx`、`results-after-ui/after-ui.trx`；逐项结果、八项错误消息及源码对账见 `paired-result-and-source-proof.json`。175 项结果零变化，八项错误消息完全相等。首次诊断项目准备／构建失败日志保留，修正自动测试项目依赖和隐式 `System.IO` 引入后才取得上述有效结果。

### 八项的已有原因与 UI 调用边界

| 类别 | 项数 | 已核实的原有冲突 |
| --- | ---: | --- |
| PBR 光照源码断言 | 1 | 测试仍匹配 `Uncharted2ToneMapping(hdr_linear_col)`；着色器在 UI 改版前已将 `emissiveColour` 一起送入明暗映射。 |
| 恒力／阻尼诊断、轨迹及缩放 | 6 | 原转换器的 legacy 发射方向分支把这些测试的方向组合变为 0/360，使受限恒力阻尼候选不成立；一项缺候选诊断、四条轨迹超 3px 阈值、一个缩放组合进入不同分支。UI 前实跑出现同样消息和相同数值，未据此盲改方向或全局步长。 |
| 法线片段字段选择 | 1 | 测试要求 `ps_normal` 优先；原转换器已对两处同时定义 normal 的歧义作拒绝，已有文档也保留此边界。 |

八项均未调用 VFX 编辑器、公共播放器或新的预览时钟组件；构造的快照使用默认完整范围。转换器去掉 R72 四处顶层范围增补后，与 UI 前备份相等，其余材质／模拟路径、测试及 fixture 保持原样。此源码证据与上述两侧实跑相互印证，不再只依据“本轮未修改该文件”推断历史归属。

**本轮真正影响过的两项 Terry 进度宿主清单已在 R72 修复，本次又独立复测 2 通过／0 失败／0 跳过，exit0**，见 `results-terry-inventory/terry-inventory-current.trx`。复用的 R72 UI 定向 102 通过／2 跳过仍标为旧记录，没有冒充本次重跑。此次无需修改产品实现，正常 Debug 二进制、全局步长、Git index、源码和既有失败证据保留；仅更新本交接并增加具名自动诊断证据，不提交、不推送、不清理。八项旧问题后续仍须按其真实材质／运动语义处理，不能删判断或改阈值使报表变绿。

## R72 VFX 工作台、公共播放器及局部预览范围（2026-10-24）

**当前状态**：本轮 UI 与选择功能已实现，正常 Debug 构建及相应自动回归已完成；**实机显示与操作由用户验收**。用户在本轮明确接管实机测试，未由 Agent 启动或操作 AECN／Terry 进行本轮验收。本轮不处理材质、透明合成或 Gyro 白弧，也不把 UI 测试计为 VFX 视觉通过。R71 的原生对照、已确认格线及剩余深度接口边界继续有效。

### 现场保护与界面行为

- 证据目录 `artifacts/vfx-editor-ui-r72/`。入口重新核实实际原目录、当前 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区与 index 散列；190 项源状态／路径／字节与 R71 最终快照一致。修改前具名源码、交接原件与失败记录均保留。最终逐项对账见 `workspace-after-final.json`；无暂存、收起、分支／worktree 切换、提交、推送或清理，全局 `1.0 / 120` 与原生 DLL 保持不变。
- 工作台对齐现有编辑器：左侧效果层、中央直接显示的 3D 场景、右侧参数，使用既有公共分隔条和语义样式。打开编辑器即初始化场景，不再需要“预览特效”开关；移除 Terry 预览按钮、结束预览、进度宿主和 VM 命令／运行时注册。历史 Terry 导出及采样辅助代码保留，新增共享项目引用引出的命名空间歧义仅以 `global::Shared.Core` 限定，未删除旧证据或工具。
- 场景下方直接复用已有 `AnimationPlayerView`，未新增公共组件或复制播放器模板。新增默认值为 true 的 `ShowFrameInformation`；VFX 关闭帧信息，只显示实际秒数、起止定位、播放／暂停、循环和进度条，其他动画调用方保持原帧数／FPS／逐帧行为。VFX 适配器与场景时钟负责延迟 seek、拖动保留播放意图、自然播完才循环／暂停、收起暂停但保留画面，以及无效输入停止后恢复原播放意图。预览内容居中取景保留为显式按钮，初始视角约定不变。
- 每个可预览根层有勾选和“仅预览此层”，可单独或组合查看，并有全部／清空。编辑层选择、字段画质筛选不暗改预览范围；画质仍独立选择。范围只传给当前场景，不写入源 XML、保存状态、撤销栈或共享实时快照；供 SuperView 等消费者读取的快照仍是原始完整范围。
- **初始范围是明确的界面默认，不是特效库自动识别**：`construction_type=2` 组合与单根文件默认完整预览，其余多根文件先提示勾选，也可一键全部。官方 Gyro 与 Neferata 库的根属性不能可靠区分“单个效果”和“库”，因此未按文件名或某个效果名称打补丁。根筛选分开发射器 ID 和引用实例名，仅作用于顶层；选中根的递归引用与事件依赖保留。未选层不读取资源、不参与取景，也不再误计为“原文件停用”。

### 本次自动验证与限制

- 最终完整解决方案 Debug 构建 `debug-solution-build-final-v2.log`：**exit0／97 警告／0 错误**；此前 restore 成功。中间构建遇到用户打开的正常 Debug PID16656 锁占用，已停止本任务构建，未自行关闭该实例；用户确认已关闭后才恢复。最终正常输出 Core SHA256 `2F68ECBCD3D26247CECDE09621642B182C12D483D759D05352E3AB2B3B15FEDA`，VFX 编辑器 `53CB6377C58810116DBEAA383EE1E57F988447AF71A591C19793E55EEB77B956`，原生 DLL 仍为 R71 的 `2B0ECA0125C03A033BEB0E86DA014FBBF6B07070F33D68DB9C9F875FBC2A9276`。后续仅更新测试清单及文档，受影响测试项目重新构建 exit0／4 既有警告／0 错误。
- 新范围计数红测为 **6 通过／2 失败**，无效输入期间收起的播放意图红测为 **1 失败**；修复后在方案回归中根范围 8 项与时钟 19 项全部通过，相关 VM／适配器也通过。最终定向 `ui-targeted-final` 为 **102 通过／0 失败／2 跳过**，包括共享架构、分隔条、播放器默认调用方、VFX 布局／绑定、保存与撤销隔离，以及移除 Terry 后的进度宿主清单。两项跳过需要另行指定真实样本目录，本轮未把历史样本扫描当复测。
- 扩大到方案的自动回归关闭实机／GPU opt-in，并过滤桌面输入、可见画廊及模态窗口用例；过滤清单见 `solution-test-scope-v2.json`，**不是未过滤全量验收**。15 份 TRX 原始合计 **4521 通过／10 失败／525 跳过**。其中两项为仍要求 VFX 承载 Terry 进度窗口的旧清单，已同步移除该条并在最终定向回归中通过，不能改写原失败记录。其余 **8 项仍失败**：PBR 光照源码断言 1 项、恒力／阻尼诊断与轨迹／缩放组合 6 项、法线片段字段选择 1 项；这些不属本轮 UI 修复范围，未扩大修改或放宽阈值，也未声称整套通过。逐项名称、错误和 R72 重点用例见 `test-results-final-summary.json`。较早一次过滤运行因补充排除项已中止，日志保留，不算额外通过次数。
- 布局离屏测试检查枚举主题和 100%／125%／150% 缩放，导出 Dark、Light、两种高对比度图片；另以假的 CPU 预览器呈现已加载的公共秒数播放器，检查实际绑定、起止按钮与进度值。已查看深／浅及高对比度布局图片，原始输出分别保留于 `ui-theme-renders*`。这些是 **UI／CPU 诊断样本**，中央假的场景不是游戏 VFX 画面，不算官方原版效果或原生视觉验收。
- 测试源码整理中出现一次全 NUL 写入，构建失败后保留 `VfxEditorTests.nul-write-failure.bin`，依据本轮逐字节备份恢复原测试并补入新行为断言，随后重建、运行通过；原备份、恢复候选和失败日志均保留，未用旧测试程序集冒充修复后通过。

### 用户实机验收与后续边界

可使用正常 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。优先检查打开即有 3D 场景、公共播放器的暂停／定位／循环，以及多根官方源的单选、组合、全部和清空；范围勾选不应产生未保存标记，实际改参和撤销应实时刷新。建议覆盖官方 Gyro、Neferata 引用源及完整消失／重现组合，最终显示仍只用官方 Pack 的原 XML、模型、贴图和依赖，避免同名 Mod 覆盖。**本轮没有新官方动画录制、GPU 离屏特效复测、Terry 对照或用户验收结论**；上述范围全部待用户实机确认。继续渲染工作时回到 R71 已明确的共用深度／完整合成缺口，不围绕已确认原生格线反复修改。

## R71 原生颜色合成、天气对比度及受控 main 对照（2026-10-23）

**当前状态**：已修复共用天气显示链路遗漏的统一对比度，并完成构建、红绿像素验证、完整官方样本复测和普通 Debug 动画复看。**Gyro 剩余白弧／完整原生合成尚未修好，不宣称全部 VFX 正常。** 本次同输入 main 颜色已基本吻合，不能继续凭完整画面印象降低材质增益；原生确认的格线折射继续保留。沿用原分支和 HEAD，未暂存、收起、切分支／worktree、提交、推送、清理；全局 `1.0 / 120` 不变，全部正常 Debug、源码、备份、工具、旧 Mod 和失败记录保留。

### 本次现场与原生证据

- 证据根目录 `artifacts/vfx-colour-composite-r71/`。入口重新核实实际原路径、`codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区与 index 散列。189项源状态／路径／字节散列与 R70 最新快照全部相同，无缺失；本轮修改前源件和正常 DLL/PDB另存 `before-uniform-contrast/`。正常输出没有 native PDB，初始缺失记录保留，修正 manifest 明确取已配对 F96 原生构建 PDB，不冒充正常目录原有文件。
- 本次重播 **历史 R70** 的27103／27104／27105原生捕获，未重新启动或采集 Terry；q1／Bloom四项0／60度／共同阶段约 `.998382568 / 1.413391113 / 1.862396240` 的来源仍是 R70 同进程实捕，不能称本次新采集。27103原捕获 SHA256 `344c8fb4e9ff821f92a20f13216e0cbdfc61cfd6acb7327c12c254c480505fd9`。本次实际导出并核对1331 moments、1407重建、1456加权颜色、1538颜色合成、1586后续场景及1982最终显示的原始 shader／常量／深度／RT／混合状态，资源号不照搬旧记录。
- 原生最终 `hdr_to_screen` PS23458 在 gamma 后、默认 LUT 前实际执行统一 `1.1 * RGB - .05`；对应无泛光天气原字节明确 contrast RGB1.1、其他亮度／饱和度1、hue0、两LUT default。此前产品既不读取也不执行该矩阵。本次CPU只移除该矩阵的单变量诊断增加淡弧可见范围，证明它解释一部分显示条件差异，**不证明它是全部白弧根因**。1538的实际加权重建最大线性误差 `.0009765625`、无像素超过 `.001`，1586未改变此画面RGB；不凭“存在 MBOIT”就改粒子材质。
- 原生 opposite 的有效 GS depth bias为 `.300048828125` 米，保留几何 clip XY/W，仅偏移 clip Z、柔边 world 和 moments深度；main为0。v2 GetUsage误报Unused的失败保留，v3改用真实action树扫描VS使用范围；同位烘焙VS后，1331三个RT及1456颜色RT **逐字节等于原捕获**，再只禁用偏移。禁用后右白弧明显变暗／残缺，原始halfRT颜色亮度和下降58.69%，零bias小弧保留。因此缺少bias不能解释AECN偏亮的方向，本轮不据此降亮或盲改产品。详见 `native-depth-bias-review-v3/`；这是原生单变量DIAG，不是完整最终显示。

### 共用实现与自动验证

- `GameEnvironmentLighting.cs` 仅接收显式同值且 **C≥1** 的 contrast RGB，并要求其他亮度／饱和度1、hue0、两LUT default；缺失或其他组合仍保留未校准提示。`EffekseerVfxPreview.cs` 以既有 `Display.W=C-1` 传入，默认0；`FloatSceneTarget.h` 在原顺序加入矩阵，背景逆算前成对撤销，未受影响像素保留原RGBA；`EffekseerNative.cs` 同步字段含义。未改材质亮度、透明度、粒子源数据、ABI或泛光能力。C<1域在审阅时发现不能稳定逆算任意既有背景；极小C还会经float32打包成为分母0，已收窄并保留诊断，不称完整天气调色支持。已经截断的HDR信息不能逆恢复。
- 有效旧库红测 `contrast-red-v2` 为1通过／2失败：元数据缺失，及低增益像素旧32、原生公式期望约21.47。首版不支持的Blend组合失败保留并排除；另一高增益控制曾处于gamma中点附近，改为3后再跑绿测，原低增益输入和断言阈值未放宽。新库初次3项通过；另外一次官方原DDS／天气回归 **55通过、0失败、0跳过**（34GPU及21CPU），不合写成同一次58项。
- 最后参数域收紧后，正常主程序 Debug构建 exit0／1877既有警告／0错误；测试项目重新构建 exit0／3既有警告／0错误。新元数据 **3通过、0失败、0跳过**，另一次官方原DDS像素 **2通过、0失败、0跳过**。最终Core SHA256 `376ACD0CA7B1548981130217B5CFF7D758602CA15AB99755F8EFA9A8ADBB1A03`，native `2B0ECA0125C03A033BEB0E86DA014FBBF6B07070F33D68DB9C9F875FBC2A9276`；native本轮正常Debug构建成功。早先Core3236及对应证据均保留，最后仅parser拒绝C<1，C1.1着色路径不变。见 `contrast-final-build-test-validation-v3.json`；未运行完整解决方案、Release、CI或用户整体验收。

### 完整官方样本与普通窗口（独立于自动测试）

- Core3236/native2B0E实际新渲染Gyro三个上述阶段，另有完整Neferata消失重现及Malus月牙斩 `.6` 秒，共5帧、4命令均exit0。逐项核对当前271官方Pack manifest、源／模型／DDS来源、质量1、相机／投影／实际阶段、粒子数、天气；无draw suppression或额外DIAG floor。旧图是R70保留结果，不称本次重渲旧版本。三Gyro仅352／812／698像素改变、maxRGB2／3／3，实看变化很小，**白弧仍未解决**；Neferata暗部更深、Malus轻微颜色变化，位置及透明轮廓保留。两张前后联系图已实际查看，见 `uniform-contrast-five-official-review-v1/`，不能把5帧算5种视觉通过。
- 普通正常Debug109736实载Core3236/native2B0E，只加载All Game Packs和唯一具名 **weather-only DIAG** 参考Pack，后者仅含已核SHA的无泛光天气、没有VFX／模型／DDS条目及同名覆盖，也未安装进游戏。界面实选此天气（C1.1／四Bloom0）与预览画质1，完整原版Gyro录1176帧／实际末帧27.968秒，Neferata录1217帧／27.992秒。已看Gyro弧线转动与淡出，Neferata红黑烟团出现、展开、消散的连续序列；默认相机与录制时钟没有冒充Terry同视图／同阶段验收。无键鼠冲突；两个原版标签均无未保存标记后正常关闭以重建，没有强制结束或丢弃。见 `debug-two-official-animation-review-v1.json`。最终正常Debug116764已从新Core376启动并重新打开官方Gyro，另核最终模块身份，不把先前C1.1动画标成在Core376录制。

### 同输入 main 结论与实际剩余接口边界

本次另做严格标记的 **官方派生 main-only DIAG**：27103仅保留1个无bias、SoftDistance0的实际main粒子；先经原生4RT逐字节校准，再在两个pass同时剔除opposite，其余原PS／MBOIT／草地／DDS保持。AECN用最终Core376/native2B0E、真实halfRT853×533／viewport180×327、实际RGBA／UV／几何和D24；角点与58860深度像素回读误差0。实际AECN颜色与原生加权RT max线性差 `.00030518`，原生归一化源与AECN重采样源在相同显示传递后maxRGB1、>1像素0；AECN颜色总量约低0.523%。两张图已查看，只排除该main无柔边分支的明显增益错误；共同草地/T合成是明确的CPU分离诊断，不能冒充AECN完整原场景。曾误标通用RT格式的旧metadata保留，`execution-and-format-correction.json`明确实际为HalfVector4，不拿错误标签作证。

完整opposite同输入对照仍受两项真实接口缺口限制：当前单DSV入口不能同时绑定原生独立426×266 quarter-depth与853×533光栅深度，也不能分别传递未偏移几何、偏移clip Z及softWorld。已准备真实捕获深度、SHA及严格失败门禁于 `exact-colour-captured-depth27103-v1/`，修正artifact工具halfRT选择但未伪改 `g_screen_size` 或用clear1冒充原生深度。本轮没有把这些不等输入的残差归因材质或MBOIT。后续优先补齐这一共用深度合同、在同实际opposite输入上闭合对照，再回完整Gyro及其他官方组合复测；不要再重复无变化截图、降亮度、删原生格线，或把渐变×DISTORTION判定移除当作实现完成。更多质量／渐变alpha映射／模型生命周期与复杂背景仍未全面验收。

## R70 官方 Gyro 多阶段、固定负半径及颜色柔边深度（2026-10-23）

**当前状态**：本次实现了两项共用修复：固定相等负出生半径不再被截成零；普通颜色覆盖／发光材质的柔边按绝对光栅位置点读场景深度。构建、对应 CPU／离屏回归和普通 Debug 动画复看已完成。**完整 Gyro 的白弧强度和原生颜色合成仍未一致，不能宣称 Gyro 或全部 VFX 已修好。** R69 已由原生同输入复现的格线尖折不再作为删除条纹的目标。原分支、HEAD、空暂存区与全局 `1.0 / 120` 保持，不提交、推送、切分支／worktree或清理；全部原件、正常 Debug、备份、工具、失败及旧 Mod 保留。

### 本次重新核实及实现

- 证据根目录 `artifacts/vfx-gyro-native-r70/`。入口 `workspace-before.json` 的188项源路径／状态／散列与 R69 最新快照逐项相同；沿用已有186源件／278运行文件备份，本轮另保留实际修改前的具名源码及 DLL/PDB。未用旧记录替代现场核对，最终盘点见 `workspace-after-final.json`。
- 新 Terry PID20656 在同进程 marker 控制中实际确认 q1，四项 Bloom strength 均0；完整官方 Gyro 的27103／27104／27105分别对应共同阶段约 `.998382568 / 1.413391113 / 1.862396240` 秒。实际 delta 为 `.109008789 / .415008545 / .449005127`，不是60fps；阶段由未钳制 birthUnary 与时钟互证，不取工具等待时间。配平离屏 View/Projection 逐 float32 相同，FOV60、near1/far12000、RT1707×1067、viewport360×655。steady16711 的 birthUnary 已钳制，未编造完整阶段。两段完整原版 Terry 动画实际末帧约27.95秒，已复看；新三阶段原生最终图也已查看。
- 所有新完整原版离屏仅解析当前 manifest 的271个官方 Pack：XML来自 `vfx_desc.pack`，normal／glow来自 `vfx.pack`，三帧两 DDS 的GPU绑定压缩mip0逐字节匹配官方原件。Terry沿用唯一传输别名，原活动参数／模型／DDS保留，不能据此声称整个Terry没有加载其他Pack。普通 Debug仅加载All Game Packs容器、无Mod工程；manifest存在，当前加载代码按manifest取包。见各原版输出的 `official-packs.json`／`dependency-origins.json`。
- 独立官方派生三体 **DIAG** 在同一4940帧比较：原 inner=outer=-.04 为约.04米出生环；只改半径0后为0；仅关闭模型发射仍为约.04环，包含age0粒子。已排除mesh缺省因素。`TotalWarVfxSimulation.Spawn` 只将采样后相等且为负的内外半径取共同幅值，再走原环采样；原正环、正环带、零半径及全局步长保留，不改源字节。不等负值／混合符号／随机种子及signed角度未校准。新PID114852的DIAG帧已核Bloom0／相机／DDS，但其q0和q1无法仅本帧区分；两档该曲线／PS相同，不能把该帧当完整原版q1验收。
- 普通颜色 `ps_soft_edge` 的实际PS用当前pass绝对光栅坐标读取降采样深度；Coverage旧代码却将视口相对UV线性采样整张DSV。新 `CompileMaterials.cpp` 仅对非贴花Coverage材质的 `CalcDepthFade` 传 `SV_Position.xy`，按整张实际DSV point Load；保留已有Distortion路径、距离公式、ABI、scale和epsilon边界，未修改颜色增益或强制写平地。原生colour pass实际853×533／viewport180×327／quarterDepth426×266，不能写成1707×1067颜色通道。该缺口仅解释存在深度的采样错位，不解释main无柔边白弧的全部强度差。
- 单帧显式height0地面深度DIAG只改变101像素／maxRGB8，主要为小段竖痕；左白弧未明显改变，没有据此修改共享Draw强制平地。四组同粒子跨帧quat确实转动；大delta下归一化一阶候选误差很小，仍不是任意多轴／阻尼积分的通用合同，没有据此改自旋或固定步长。详见 `coverage-soft-depth-readonly-review-v1.md`、`gyro-mid-integration-model-comparison.json`。

### 本次自动验证与视觉复测（分开计）

- CPU：旧Core83上新半径类为5通过／1失败（负环全落中心）；修复后新6项加既有24项为 **30通过、0失败、0跳过**。正常依赖Debug构建0错误／1877既有警告；随后主程序正常Debug构建0警告／0错误。新Core SHA256为 `0E70C3F6FE9016FA7E2F59F3B6697961E3A0C89B98CA12F04DACA444D699B775`。见 `spawn-radius-old-core-red-v1`／`spawn-radius-after-fix-v1`。
- 离屏：旧native925上两项官方原DDS派生的部分视口深度诊断稳定失败；接触比例约 `.81243/.78132`，正确参考 `.24971`。新F96保持新例原断言，两例接触／清空区域与完整视口逐像素一致。原同文件8项白纹理用例改用同一官方原DDS及相对亮度合同，另23项官方扭曲和1项顺序回归同进程运行，共 **34通过、0失败、0跳过**。这不是全解决方案测试或完整原版视觉通过数量。新原生Debug构建成功，正常主程序再构建0警告／0错误；最终native SHA256为 `F96BC9FF4BD7FE8960021A1F19F13587D9518C0564D9A2662A8F554E45BEEE60`。见 `coverage-partial-depth-{red-v1,green-f96-v1}`。
- 完整官方离屏：负半径修复后同相机／同阶段三Gyro分别改变145／434／156像素，maxRGB40，主要是局部折射边界，不能称整体明显改善。最终F96再跑这三阶段加Neferata消失／Malus月牙斩两类控制，共5帧均成功，和本轮Core0E70/native925对应帧逐像素相同，证明该批无退化，**不证明柔边或白弧在这批clear-depth背景下明显改善**。均无draw suppression／额外DIAG floor；Nef41定义中39原始inactive、Malus24中21原始inactive，不把原始未启用项算不支持。见 `fixed-negative-radius-official-review`／`coverage-depth-official-review`。
- 普通窗口：正常Debug121208/Core0E70/native925实际录看Gyro842帧／末帧20.009秒（计时28秒但不能按28秒有效画面计）、Nef1170帧／27.998秒、Malus1218帧／27.991秒。三个原版标签均无未保存标记后正常关闭以更新DLL，没有强制退出／丢弃。最终正常Debug63440/Core0E70/nativeF96又录看完整Gyro q1共1170帧／末帧27.996秒，已看双弧转动、出现／淡出及局部折射。此窗口沿正常默认相机，只证明实际动态显示，不能代替与Terry60度视图的同条件像素验收；最终加载模块路径／散列另核。错误Nef路径的IPC提示、首捕ready文件竞态、旧metadata不兼容失败及全部中间图均保留并排除成功结论。

### 历史纠正及剩余边界

R69 `internal-alpha-precision-boundary.json` 的“alpha2→scale4→2.007843”计算错误，原记录保留；实际Bridge batch scale是max(1,raw alpha)，AlphaExponent只分组，alpha2单批可精确恢复2。本次actual4仅改alpha反量化的最终棋盘RGB差0，未改alphaScale。原BC3硬件解码小差异只将同输入maxRGB6降至4，收益不足以扩改decoder；本轮保留。不能拿旧表继续解释全部残差。

Gyro白弧强度、原生MBOIT moments／半RT颜色重建／quarter深度及背景条件仍未完整匹配；当前没有同粒子colour PS单变量对照，不能按草地和棋盘画面直接降亮度。原生随机种子和大delta自旋与固定步长模拟未同步，不逐粒子宣称姿态正确。原生已复现的格线折射保留；更多画质、gradient alpha_mapping=true完整原件、模型生命周期和复杂场景合成仍未全面验收。最终普通Debug与三体DIAG Terry若仍在运行，均保留，不把当前DIAG窗口误当完整原版场景。下一步优先在既有三阶段上隔离原生颜色PS／MBOIT实际输入与重建，避免再重复无变化采样或调已原生确认的格纹。

## R69 官方零弧扩散与同实例统一折射合成（2026-10-23，继续推进中）

**当前状态**：已修正明确零 `SpawnArc` 与显式扩散的共用组合，arc360 血滴由竖直细针恢复为水平径向运动；另根据本次原生实捕，将同一实例中被颜色层隔开的扭曲统一累积后合成。**Gyro在.423与.6秒的两处残余竖条，现已用同一生成粒子在原生材质中复现，确认属于高对比格边的横向折射，不能再拿这两段认定矩形覆盖错误。小像素残差、其他阶段及完整动画仍未全面验收。** 原分支、禁止提交／推送／清理、保留全部成果及全局 `1.0 / 120` 不变。

### 本次现场与实际原生对照

- 证据根目录 `artifacts/vfx-native-strips-r69/`。入口实际核对原路径、`codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、stage0；186项源路径／状态／散列与 R68 `workspace-after-final.json` 完全相同。原186源件和278正常 Debug 文件均逐件保全于本轮备份，没有切分支／worktree、暂存、收起、清理或关闭用户程序。
- 本次新窗口为 Terry110316。`terry-quality-capture-2/terry_frame1500.rdc` 与 `quality-q1-replay-v1/quality-camera-values.json` 核实画质1、Bloom四项strength均0、实际FOV60°、near1/far12000、RT1707×1067及视口(0,0,360,655)。后续实捕核对应原版场景相机；没有用 `.terry` 中的45°标称值替代实际投影。
- Terry沿用已保留的官方传输别名场景；活动参数与原模型／DDS不变，唯一别名防止同名Mod覆盖。本次离屏重新从当前manifest的271个官方Pack索引原文件；不读取Mod。`arc360-first-frame11616-replay/blood-native-direction-evidence.json` 还核实实际GPU绑定的血滴BC7 mip0与保留官方DDS压缩字节相同，不把保留DDS说成本次重新提取的资源。
- 新arc360原生11616有833个血滴活粒，速度Y全0、XZ径向，速度约10～20；231个已录PostVS子集投影在地面烟团附近，没有向上延伸的细针。第一次仅凭缩图误归 `fire_beam_impact5` 已撤回；本轮20帧逐层隐藏／单独显示的双向对照确认针来自 `blood_droplets2`，旧误判与修正记录全部保留。
- Gyro3330～3335为本次新burst，抓帧停顿导致大delta；两端main／opposite的起点并不一致，不能冒充统一效果时钟或据此关闭自旋、硬加延迟。warm34629实际delta约.107、main阶段约2.81586067，已有跨帧转动四元数；这推翻“自旋禁用”的猜测。warm早期54272实际delta约.157、四层均活动、共同阶段约.423034668，提供了足够的同条件主层覆盖。
- `final-pass-native-contract.json` 核实54272先有普通颜色事件1331／1456，再在同一1627事件写入main与opposite扭曲，最后1982一次resolve；这与旧AECN按颜色层拆段的行为不同。Terry仅作原生参考，未等同游戏最终显示。

### 共享实现与本次重新执行的验证

- `TotalWarVfxConverter.cs`：仅普通面片中x/y明确为零的 `SpawnArc`、显式 `SpawnSpread`、无 `SpawnAngle`，并满足既有默认分布／非表面法线／相对上轴条件时，进入旧式水平仰角及 `SpreadArc` 方位分支；非零／不完整弧和未确认组合仍诊断。原有无Arc和无Spread极点路径不扩展。新 `VfxLegacySpawnArcCombinationTests.cs` 有5项，连同19项既有方向测试共24通过／0失败／0跳过。旧实现先跑的红测、其中一个旧极点控制过严的断言及修正记录均保留，没有把那次3失败都算产品缺陷。
- `Bridge.cpp`：只在同一次 `AecnVfxDraw` 出现扭曲后又有颜色时建立局部稳定分区；先画颜色，再统一扭曲，两类各自原有粒子／层顺序不变，不修改调用方数组，不跨实例合成。`WeightedDistortionTarget.h`、ABI128、材质公式及着色器参数均未改；未借此开放自定义模型材质与DISTORTION组合。
- 新 `VfxOfficialDistortionOrderTests.cs` 使用官方原normal／diffuse制作明确标记的派生诊断。旧67A1：透明颜色夹在相反方向扭曲间时唯一失败，maxRGB180；连续反向、颜色前置、透明层单独均0差。新92539959：同一新测试1通过／0失败／0跳过，夹层差降0，同向正控制仍有效；另一次既有GPU回归23通过／0失败／0跳过，原阈值未改。证据 `distortion-order-baseline-67a1/`、`distortion-order-after-92539959/validation.json`，不能与CPU合称一次全量测试。
- 普通Debug测试依赖图构建成功，1880条既有警告／0错误；新测试程序集单独构建3条既有警告／0错误。原生正常Debug构建成功，新DLL SHA256 `92539959D09B9EEC9C806F02227FBFAE6DB5C29CA85188AF0B1A7E6C3C6F8473` 已部署正常Debug，托管GameWorld.Core为 `83E6E9E9C5E7FF6E69E83387A17F8C95E732AD7D4F3F80E156DFE14D4886DCA4`。未运行完整解决方案、Release或CI；旧运行库均保留。
- 第一项修复后新跑30帧完整官方原件，最终共享合成库又新跑35帧，另补Archaon .04／.08／.12三帧。最终五类为Gyro、arc360、Nef消失重现、Malus及Archaon，均q1，未隐藏源层。见 `five-official-q1-final-sharedpass-20261023/visual-review-final925.json`。arc早期无针且径向保持，烟团／火星合成有变化；Gyro当时仍见格边条带／尖折，其后用下述原生同输入对照重新定性，不能把先前“未解决”判断当最终归因；Malus有小幅差异，不能称完全不受影响。Archaon先前.185过晚，本次补到.04清楚红橙多弧、.08淡出、.12近消失，仍不算原生匹配验收。
- 普通Debug4724仅加载All Game Packs，原生67A1和托管83E6组合中，完整原版arc360 q1录到1271帧／实际末帧27.9943秒，已看烟团出现、径向血滴、消散，无旧竖针；两个错误ROI录制保留但排除。无未保存标签后正常关闭4724再换新库，没有强制结束或丢弃。最终925普通Debug114656又实际录看五类完整原件q1：Gyro1193帧／27.9953秒、arc360270帧／5.9966秒、Nef1177帧／27.9827秒、Malus1197帧／27.9824秒、Archaon1137帧／27.9977秒；分别检查转动双弧、烟团与径向血滴、红黑云的聚散、紫色斩弧、红橙多弧的出现和淡出。arc实际末帧只有约6秒，不能按28秒计；Archaon首次误选q2的录制保留并排除，使用`debug-archaon-final925-q1-corrected-28s`。普通窗口保持正常默认相机，只证明实际动态表现，不能代替Terry同条件像素对照或完整视觉验收。
- `gyro-exact-input54272-v2` 将四个非cull粒子的实际角点、UV、浮点alpha、T/B和捕获DSV送入同一925库，深度回读235800像素逐值一致。`gyro-exact-render54272-comparison-v1` 原生／AECN各53个可见棋盘变化像素，范围均为[149,308,215,420]，最大RGB差6、15像素差超过2；这只是该帧可见棋盘近似，不证明内部加权场处处相同。另仅把深度改成远平面1的控制，两图逐像素相同，不支持把地面遮挡认作大条带主因；使用`gyro-exact-render54272-cleardepth-comparison-v2`，v1来源标签错误保留并排除。普通`native-match`中的Gyro其实没有写入平地深度，`VfxGroundHeight=0`并非已写地面的证明。
- `gyro-colour-isolation-review-v1` 同时排除Gyro两颜色层后，右条纹区域与完整图逐像素相同，故该区域不是颜色alpha／遮罩导致；同一四粒子在两套实测／配平相机的最大投影差约.0156像素，也不足解释位置差异。CPU与原生输入的旋转、出生分布仍需继续核对；原生实捕delta约.157秒与全局1/120模拟不同，opposite还含随机初始角，不能为抹掉某格线硬改自旋。
- `gyro-native-ps-cpu-input-diagnostic-v1` 是明确标记的官方派生诊断：仅替换原生VS顶点输出，保留官方DDS、原PS33274、常量、混合、RT及深度。先用捕获6粒的输出自校准，13项输出签名完全相同，14570952字节加权RT逐字节一致；首次系统语义大小写门禁失败保留，v2按原拼写重编后通过，没有放宽门禁。再把AECN在.423秒的全部6个、.6秒的全部5个实际生成扭曲粒子回送原生PS，第六空槽仅在5粒控制中明确cull，不改产品。
- `gyro-cpu0423-nativeps-exact-comparison-v1` 与 `gyro-cpu0600-nativeps-exact-comparison-v1` 的联系图均已实际查看：原生PS和AECN同输入均重现x240附近竖格边尖折；两阶段该区域位移Y为0、X最大约5.616／4.519像素。因此是横折射改变竖格边形状，并非向上拉伸的矩形覆盖证据。对照仍有小像素误差：.423为44像素差超过2／maxRGB8，.6为22像素／maxRGB5；未放宽测试阈值，不能称逐像素一致。.423同CPU6仅换realDSV／clear1另跑一帧，仅1像素RGBA差1，右格边形状不变；它只约束这一组输入。棋盘重组采用已核实的半像素提前线性采样，不等同游戏草地最终画面或未修改的完整原版验收。

### 已辨明的剩余现象与验证边界

Gyro .423／.6秒两条具体格边现象已在原生PS同输入复现，不再以消除它们为由增加模糊、删除饱和判断、翻转法线轴或改变全局步长。其他阶段的尖角、精确出生／旋转时钟和完整动画仍未逐一原生配对；原生低帧率积分与随机初始角不能当作已校准。另观察到官方main的固定负SpawnArea半径-0.04在原生出生时形成约0.04环，而当前CPU钳成0；这是独立输入差异候选，隐式mesh发射缺省尚未单变量排除，不能拿约1像素位置差解释当前条带，未为此扩改。其他画质、更多材质组合、gradient alpha_mapping=true完整原件、完整模型生命周期和复杂场景透明合成仍未全面验收。录制时正常Debug114656及Terry110316均经路径核对，原场景无未保存标记；最终只读核查两程序已不存在，114656已被10:54:18启动的Chrome复用，不能再以旧PID操作，也不推断退出原因。随后Computer Use报告用户物理Escape停止，已停止桌面操作，本轮不再调用桌面工具。全部失败、工具、采样和旧Mod保留。

## R68 多粒子加权、浮点法线基轴及软深度（2026-10-22，继续推进中）

**当前状态**：已经实现连续扭曲层的原生加权方向／透射率累积，并根据本次官方 GPU 捕获修正超过 1 的透明度截断、镜像相机的法线横轴、扭曲软深度及普通面片的基轴量化偏移。**完整官方画面仍需核实局部偏移、Gyrocopter 尖碎边及 arc360 细竖条，不能称矩形硬边或全部 VFX 已正常。** 沿 `codex/vfx-effekseer-preview` 原地工作；不提交、不推送、不创建 PR、不切分支／worktree、不清理。源码、正常 Debug、全部中间版本、失败捕获、工具、官方诊断包及旧 Mod 成果均保留。全局 `1.0 / 120` 不变。

证据根目录为 `artifacts/vfx-weighted-distortion-r68/`。以下是本次重新执行；R67 的 CPU／GPU结果和窗口记录不计入本次复测。用户的 Debug／Terry 桌面测试授权继续有效，实际输入冲突才立即停止，不能反复抢焦点。

### 现场与官方来源

- 入口核实实际非联接仓库、原分支、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区；184 条源码状态／散列与 R67 `workspace-after.json` 逐项相同。`workspace-before.json`、`source-before/` 的184源件和 `runtime-before/` 的278个正常 Debug 文件保留。入口JSON中继承的 `sourceBackupFiles=181` 是旧统计，实际184项逐文件散列匹配为准；原快照不覆盖。中途 `workspace-current-review.json`／`preservation-review.json` 保留其时点，最终源码散列以 `workspace-after-final.json` 为准；保全报告为 `preservation-final-2.json`（最后文档链接修正前的只读时点，原版transport与其他非manifest包已区分），核对186项状态：14项既有文件变化、170项未变、0缺失、新增共享加权头和极点测试两文件；index散列不变、stage0。
- 离屏工具仅索引当前 manifest 的271个官方 Pack。本次一个导出包包含 Malus／Gyro／Archaon 三个完整活动源闭包，另一包包含 Neferata disappear／arc360；在 Terry 使用唯一别名避免 Mod 同名覆盖。所有活动源参数、三档位闭包和原始模型／DDS依赖保留，纹理／模型原字节散列匹配。导出 XML 只做传输别名、TED可见性和移除不活动库定义，不声称导出 XML 与原 XML 字节相同；AECN 新离屏及普通窗口直接使用未修改的官方原文件。
- `quality-control-v4` 为明确标记的官方派生诊断：实际 GPU 的主标记位置 `(512,3,512)` 唯一对应源画质1。v3标记的错误位置／速度记录保留并排除。`full-official-scenes.json` 的画质证据是共享档位校准，不是五个样本都通过视觉验收。
- 本次实际原生相机为60°、near1／far12000，RT1707×1067、viewport360×655；sidecar中45°被 Terry 忽略。相机位置／朝向有实际常量核对，既有投影横轴镜像约定保留。新 `native-match` 按这些实际尺寸与同一无泛光天气重渲；棋盘是诊断背景，不等同真实草地。普通 Debug 仍用其正常默认相机，单独检查运行表现，不能将其画面与 Terry 作逐像素成功证明。

### 共享实现与失败基线

- `WeightedDistortionTarget.h` 与 `Bridge.cpp`：R68当时的连续 DISTORTION 层共享浮点方向及透射率，先累积再解析背景；普通颜色层是顺序边界，不跨独立 Effect 实例全局合成。保留场景 Alpha、真实深度、原视口和采样数。无贡献遮罩／零偏移不覆写前序颜色。无 normal 的普通面片用既有自定义数据传递精确浮点第三轴，避免依赖字节法线的零值偏差。
- B553版本在官方 Gyro 实际 GPU 中复现原生 `T=-0.1640625`，而产品的两处 saturate 错误截断它；现在保留 `T=Π(1-A)` 和未饱和 `(1-T)^2`。旧3项 raw alpha 合同全部失败；4BE版本后完整16项通过。`weighted-v2-before-alpha/` 保存该阶段源码／DLL，原始失败图和数据不改。
- 4335版本修复只限 DISTORTION：镜像摄像机朝向面片的 normal.X 横切线贡献反转，与本次 Nef actual PostVS 基轴一致；normal.Y 和无 normal 第三轴不盲翻。软深度改为绝对 `SV_Position` 对完整深度纹理 point Load，带有符号 minScale、原 scale_strength 和 `.001` 分母。软因子0仍保留启用标记与真实深度，其他14种材质不套用这项专属采样替换。
- 127619阶段另修普通面片的法线基轴精度：用同一固定面片渲染器的 SIMD Normalize／Cross，直接以四个浮点数传递 T.xy／B.xy，避免8位打包将精确零解成 `+1/255`。只改 normalSource=2 的普通面片，有 normal 的模型及无 normal 第三轴不变，层 ABI128、粒子 ABI和5个uniform不再扩充。`weighted-v4-before-float-normal-basis/` 保留4335源码／DLL。官方 Gyro 原始 normal、A2、实际1707×1067 RT／360×655视口的纯水平折射诊断，在4335有52像素改变水平条纹、最大RGB差11；127619同一阈值通过。首次256²小RT未复现也保留，不将这项零轴回归等同全部硬边根因。
- 随后67A1BE版本继续修共享速度朝向：缺省角与扩散角的竖直极点采用固定方位，仍消耗原随机通道，避免将极小横向余量放大为随机面片方向；显式 SpawnSpread／SpreadArc 路线保持。几何沿原生2×2平面旋转，扭曲着色另传完整 rotation×velocityBasis：既有 NormalRotation 四浮点对有normal传 T.xy／B.xy，无normal传 N.xyz／1，原生桥全零时沿用127619回退。普通颜色若属于同缺省发射模式，其几何方向同样受固定方位修正；不笼统声称全部普通颜色几何不变。没有扩 ABI、shader uniform或改全局步长。
- 该完整基轴公式已用本次 ground Gyro 两个实际 PostVS 实例核实，最大分量误差约 `1.19e-7`／`5.96e-8`；原生几何只用二维投影而 normal 用完整旋转，不能靠翻转 Transform 第三轴混修。Alignment3 近乎平行时基轴含非单位幅度，采用直接浮点而非四元数／单位化。`weighted-v5-before-velocity-basis/` 的32文件保存127619源码、四个托管文件和正常／原生DLL；旧3项极点回归失败及128粒全零normal payload失败均保留。
- 层 ABI 120→128字节，C#尾字段、转换器、模拟每粒尺寸和深度缺失诊断同步。粒子 ABI不变。`weighted-v3-before-soft-depth/` 保留4BE源码／运行库；旧ABI独立运行时先复现镜像normal与偏移视口真实深度两个用例各最大RGB差180，再构建新库通过，未放宽旧16项阈值。
- 独立源码审阅未发现本轮加权改动的新实质缺陷。外层旧 `FloatSceneTarget` 不保留 OM-UAV 是既有状态契约缺口，未证明普通 Debug 当前触发，不作为本次硬边根因。Pfim软件BC3解码与原生硬件BC3之间的精度差异尚未实际采样比较，不能据此猜改法线或泛化根因。

### 本次自动与视觉验证

- 最终67A1BE阶段相关 CPU本次重跑126通过／0失败／0跳过，含先前119及新增7项缺省极点／显式扇区控制／完整着色基轴。官方派生 GPU分两批重新执行：既有21项通过／0失败／0跳过，新增独立基轴上传2项通过／0失败／0跳过，不合写成同一次23项全套。新两项在隔离的127619旧库均最大RGB差180失败，新库两图逐像素完全一致，正控制仍明显不同。首次有normal参考与显式payload幅度不等（SSE rsqrt近似）导致max4失败，保留并改为相同显式payload的严格等价参考，未放宽阈值或据此改产品。资源缺失门禁先前另跑20跳过，不算通过。日志／TRX见 `tests-velocity-basis-*.log`、`tests-basis-payload-*.log` 和对应结果目录。
- 正常原生 Debug构建及主程序 Debug构建成功；最新主程序 `debug-build-v5-velocity-basis.log` 为0警告／0错误，DLL SHA256 `67A1BE7DEAA8034FFCFAECF2ECEB3A352C94F0C6598B36B172561324A1618903`。本次最后相关测试依赖重建有1880条既有警告／0错误，测试单项目重建有3条既有警告。先前阶段的1903条警告日志仍保留，不混为本次最后构建。没有执行全解决方案、Release、CI或用户视觉整体验收。
- 完整官方离屏先有九样本×三画质×八阶段的216帧旧／新加权对照；随后围绕实际原生条件，用五个完整官方样本q1的45阶段帧、Gyro抬高3米4帧、Nef精确阶段1帧作v3／v4共50对对照。v4实际看完18张联系图；39帧相同，11帧变化只在Nef／arc360前期。全部原始帧和中间版本保留，不把216或50当视觉通过数量。
- 最后127619新增五类q1原版28帧及Gyro抬高3米2帧；67A1BE又重新生成五类完整原版30帧及抬高3米3帧，共33帧，六个probe均exit0。与v5同AECN条件的30帧中，24帧逐像素未变（Nef／arc360／Malus／Archaon），6个Gyro阶段变化；已看Gyro9帧和本次原生ground同背景对照。早期左侧碎条消失／减少，其他阶段仍有较大折射条块，不能称全部硬边解决。`native-match-v6-review/` 与 `gyro-ground-stage2308-review/` 保留观察、严格配对条件与覆盖弱的限制；不是33个视觉通过样本。
- Nef原生首帧由45个未截断出生参数共同反推出根阶段 `.63899994`；两活动层为颜色与 normal+diffuse+softDepth 扭曲，gradient／crush为false，不能替代渐变组合。actual RT外轮廓软云状；作用同一80像素棋盘后，原生也有格边位移，不能仅凭“出现矩形格”认定面片硬边。v4相对原生背景seam的严格34列平均偏差由v3约3.91降至1.03像素，只支持方向偏差减轻；随机粒子／烟云和局部偏移仍不同，未判完整外观通过。
- Gyro的普通 Debug和配平离屏仍见折射台阶／尖碎边；v4相对v3全部13帧不变。原生贴图GPU mip0与官方原DDS一致；地面主层与抬高后的opposite层阶段／方向不同，不能把两张直接当单变量深度成功。完整 arc360仍有细竖条与局部位移；原版启用了当前库中不存在的 `3D_ring`，只诊断源悬空引用，不补造层。
- Gyro阶段有本次纠正：lift3捕获的 `.18500137` 是首 opposite 粒子的年龄，四个未夹断 birthUnary 共同反推的共同阶段为 `.3699989319`。旧v3／v4／v5同AECN阶段的比较保留，但不能冒充严格native根阶段对齐。ground三粒未夹断出生参数及独立raw时钟给出实际main共同阶段推导 `2.308441162`，不是直接读到完整effectRootClock，也不是同随机粒子逐个配对。全buffer首轮分析混淆 emitterInstanceIndex 与 constantIndex，ground实际活跃main为实例4／5→常量0／1；先前“API读到初始状态”假设已撤回。最终扫描全部65536槽后，lift3该捕获确无活动main／其正寿命粒子，但原因仍未确定。以 `gyro-buffer-readback-correction/index-domain-correction.json` 为最后结论，旧分析原件和两次纠正均保留，**不据此改发射调度或删除main**。四发射器transport与官方原件逐节点语义相同。
- ground该主层阶段的实际 RT 按本帧 mode1／overscan1／enable1及同一80像素棋盘重合成后，原生与AECN都只在y320附近有小折角，未见该帧明显大矩形；原生透射率外形是软弧。棋盘对此阶段覆盖弱（原生约30像素发生大于2的变化），不作为形状／密度完整验收。其他阶段的大条带仍需有活动主层的对应原生阶段及足够背景覆盖，不一概归“格线正常”或随机序列。
- 新普通 Debug96324来自正常输出，只加载 All Game Packs／manifest官方容器，无已载入 Mod 覆盖。`debug-neferata-v4-q1/` 28秒1168帧、`debug-gyro-v4-q1/` 28秒1186帧，已实际复看动画。Nef可见红黑云出现／消散，Gyro可见双弧与轴线折射，仍有上述残差；没有保存原 Pack。首次IPC等待工具选择器超时保留，随后完成原弹窗打开，不冒充IPC成功响应。
- 127619的普通 Debug6956随后对五类完整原版都录制并看过动画，分别631／1178／759／1202／1168帧，最后实际帧为15.00／27.97／17.99／27.99／28.00秒，不能拿28秒计时填补无帧区间。见 `debug-v5-five-original-review.json`。Gyro双弧旋转，Nef红黑云展开退去，Malus紫弧向左移动淡出，arc360有烟尘／细竖条，Archaon短红弧出现后迅速消失。普通窗口不与60°原生视图作逐像素成功比较。五标签无未保存标记，6956已按规范正常关闭再构建新库；一次拼错官方路径的IPC失败提示已确认并保留，不作为加载成功。
- 最后67A1BE普通 Debug104212／窗口4131050来自正常输出，进程实际已加载该路径的原生DLL，托管 GameWorld.Core SHA256为 `5A5F7CB74E24706BA266E5232D7C3470AE23C980790A967FEBAF29E1189B00F1`，与本次测试／probe相同。重新录制Gyro1166帧／27.9834秒、arc3601287帧／27.9974秒、Nef1170帧／27.9985秒，并实际看出现／运动／消散；残留折射条块／细竖条没有判通过。只有原版容器和三个原版标签，未保存原Pack。
- 即时编辑另用明确的官方派生内存诊断：Nef根引用尺度上限由1滑到界面显示1.865431，看到烟云范围实时增大，1202帧／27.9802秒；随后工具栏撤销一次恢复上下限1、清除未保存标记。最终回到未经修改完整原版再录1166帧／27.9981秒，实际复看原大小、展开及消散。该证据只证明此根尺度实时刷新与撤销，不证明所有编辑字段。见 `debug-v6-window-and-live-edit-review.json`；当前Debug仍留原版恢复状态，Terry100500的本任务自有未保存重播实例同样保留，未强制关闭或丢弃。
- Terry本次 actual Bloom四种strength均0，tone brightness／burn=.5，auto exposure关闭，天气原字节与AECN matched probe一致。Nef新实例WGC见 `native-neferata-q1-new-instance/`，已看出现、展开与消散；Terry仅是原生参考，不等同游戏最终显示。属性文本重输没有可靠重播，尝试与空记录保留；选择已结束实例后CtrlC／CtrlV创建新实例才实际播放，WGC录制时钟不等于effect年龄。
- Malus即时与延迟1.2秒的GPU捕获分别为空／已过短弧阶段，保留但不算折射验收；`.4秒延迟` 的新首帧实际根阶段为 `.202026367`，白弧5、火弧14、扭曲1个实例均可见，不能用延迟值替代年龄。原生扭曲缓冲的Y方向精确为0，外轮廓软弧，非矩形片；最终画面及WGC看到紫色三道斩痕向左运动。Terry100500仍有本任务自有重播实例未保存标记，不强制退出、不丢弃内容；原单实例源件已备份，后续先核现场再保存或继续。

### 继续入口

优先给原版 Gyro 仍有大条带的早期阶段取得“主层确实活动”的同条件原生覆盖，再处理 arc360 的细竖条／局部硬折射及更多完整原版。当前没有新的源码合同或可靠像素证据支持继续改resolve／加模糊，不以猜测补偿。保持已校准的q1、实际相机、深度和阶段；只改有源码及actual GPU支撑的共用实现。普通库中未选择的定义不等同“不支持”；例如Nef的41个库定义中仅2个活动，其余39个停用不能写成39个材质失败。矩阵见 `official-five-combination-coverage-summary.json`：当前仅三类有有效本轮Terry捕获，arc360／Archaon仍无有效本轮原生画面；gradient alpha_mapping=true没有完整原版组合验收。还需更多原版画质／材质组合、模型渐变和生命周期、场景深度／复杂透明排序的实际视觉对照。不得用泛光调色、测试数量、映射层数或有像素代替正确。

## R67 官方样本与共享扭曲遮罩（2026-10-22，历史）

**当前状态**：沿原分支补齐 DISTORTION 的原始 diffuse 透明遮罩、可选渐变透明度、透明裁切和无 normal 几何方向。当前是候选的单粒子背景解析，**尚无原生多粒子加权合成，矩形／硬过渡折射和完整外观仍未验收**。本轮与后续验证只用官方原始资源，或明确标记的官方派生诊断；此要求覆盖下文历史的原版／Mod 交叉验证要求。历史 Mod 记录和素材全部保留。不提交、不推送、不清理，全局 `1.0 / 120` 不变。

证据根目录为 `artifacts/vfx-official-distortion-r67/`。R66 GPU 捕获仅作为历史语义证据；R66 自动测试、Mod 画面未计作 R67 复测。

### 现场与官方来源

- 入口只读核对实际非联接仓库、分支 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区；181 项状态及散列与 R66 `workspace-after.json` 一致。`workspace-before.json`、`source-before/` 保留源码，`runtime-before/` 保留正常 Debug 的 278 个文件。未创建分支／worktree，未暂存、收起、回滚、覆盖旧包或清理。
- 探针仅从当前游戏 manifest 的 271 个 Pack 建独立索引，不读或合并 Mod，不用 Mod 补缺失依赖。包长度与 manifest 一致，这不是 Steam 签名校验。本次重新提取 `fx/particle_vfx_library.hlsl` 于 `shader-current/`，SHA256 `400709701111e07657e3936b3008352303de0419c0aed399eb924174cfe9566e`。与 R66 实际 GPU 反汇编的对应及差距见 `native-contract.json`；没有新 GPU 捕获冒充复测。
- `samples-final/` 八个完整原版输入、`samples-extra-final/` 另一个输入，九输入 × 三档 × 八时点，共 216 张 640×640 图。47 条来源记录涉及 45 个资源，保存原字节散列吻合，均来自 manifest 索引，无 Mod 覆盖或补洞路径。没有缺失资源诊断，仍有未支持／候选诊断。详见 `official-coverage.json`、独立复核 `coverage-review.json`。这些图使用最终原生 DLL，但在最后“全空层隔离”guard 之前生成，没有重写成 guard 后的新运行。
- 九输入是 Neferata arc90／arc360、Malus 普通／竖直月牙、Bhashiva、Archaon medium melee、官方 `_warscape_test_distortion`、Gyrocopter rotor、Neferata disappear/reappear。覆盖渐变＋扭曲＋diffuse 遮罩＋无 normal、normal-only＋空 diffuse、normal＋diffuse、普通预乘、刚体溶解和 `wh_vfx_mesh`。本轮局部清单七个库、16 个渐变扭曲层、三档 48 个 `alpha_mapping` 均为 false；true 仅以官方派生像素诊断检查，没有对应完整官方 true 组合的视觉验收。

### 共享实现与限制

- C#／原生桥尾部追加独立 `DistortionMaskIndex`。normal、diffuse、gradient 保持独立槽位和用途，递归重基址保留 `-1`。空 diffuse 合法，不读空路径或伪造白图；非空缺失遮罩只停用依赖层。normal、diffuse、gradient 全空的未支持层在转换阶段单独拒绝，并撤回资源，避免原生拒绝整套预览。
- 新 `DistortionMaterial.h` 和固定材质编译器接入原始 diffuse alpha 的图集混合及 sRGB 采样、可选渐变 alpha、粒子透明度、逐粒子 alpha crush、之后软深度。模拟不提前截断大于 1 的扭曲透明度。渐变 RGB、粒子染色和普通发光不作为扭曲输出。无 normal 使用几何第三基轴；有 normal 保留 DXT5n 与前两基轴，UV 翻转独立批次。
- 当前按历史原生加权缓冲的**单粒子**公式解析背景，包括方向长度、深度权重、分母下限及透明度权重，不是仅删“不支持”。尚未建立原生多粒子共享加权缓冲；同批重叠可能用同份背景覆盖彼此扭曲。缓冲滤波／半像素、透明背景 alpha 守恒、方向手性及任意深度强度也未完整复现。中文候选提示、架构说明持续保留这些限制。
- 保留 R66 SpreadArc、画质覆盖及普通预乘裁切，本轮没有再次盲改方向或调泛光。普通模型流动／脉冲、其他专用材质和全部发射参数没有在本轮重新验收。

### 构建、自动检查与真实画面

- 原生 `Build.ps1` 使用当前仓库 `artifacts/effekseer-source`、`D:\Software\VisualStudio\2026` 和 Debug，最后 `native-build-v2.log` 成功。`dotnet build Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Debug --no-restore` 最后 `tests-build-v6.log` 成功，1880 警告／0 错误，依赖包含正常 Debug 主程序输出。没有全解决方案、Release、CI。
- 最新 `cpu-shared-regression-v3.log`／TRX 为 **109 通过、0 失败、0 跳过**，覆盖转换、模拟、重基址、透明度、层隔离，含 21 项新遮罩用例；此批仅纯 CPU 结构检查，未启用 GPU 或游戏目录，不作为画面验证。`gpu-pixels-v3.log`／TRX 为 **6 通过、0 失败、0 跳过**，均由官方 Neferata 库 `fire_beam_impact3` 单层及原始 `wh_fire_loop_a_grayscale_diffuse.dds` 派生，验证零透明、遮罩、渐变 alpha／RGB 分离、图集、裁切和软深度，不是六种完整特效。GPU 在最后全空层 guard 前执行，shader 与参数不变，按同范围复用，不能称最新程序集重新运行。仅 GPU 六项以 `ASSETEDITOR_VFX_HEADLESS_TEST=1` opt-in 执行；`ASSETEDITOR_VFX_GAME_DATA` 指向游戏 data 目录，测试仅索引其中 manifest 列出的官方 Pack。
- Debug 占用、测试分析器、旧断言、初次 GPU 设置层错误均有失败日志并保留；只正常关闭本任务确切只读 Debug 实例，未强制结束或丢弃内容。最终正常 Debug 原生 DLL SHA256 为 `866FED1C004C7CDC99BEFD1FCC337DB4D37E05B81270D68F1AC3AB0FBCCE9495`。
- 实际复看九样本 q0 的 36 张代表帧，q1／q2 未逐图审阅。Malus 两种右侧及 arc90 顶端裁边；arc360／Gyrocopter 有矩形或硬过渡折射边界；Archaon 红弧可见窗口很短。缺同条件原生配对，216 图不算视觉全通过。
- Terry 使用自有 `native-official-neferata-r67` 场景、唯一别名包 `aecn_r67_b9f5c4a9f8_official.pack`，15 张 DDS 与官方字节一致。传输 XML 仅删根引用闭包之外未启用定义，原库／删除清单保留；不把该传输副本算未经修改完整 XML 验收。初次两包未用模型引用失败、Bhashiva 事件闭包导出失败均保留。Archaon 包只准备，未安装或新作原生画面验收。
- Terry UI 新导出 `native-neferata-arc90/effective-environment.environment_group`，散列 `8A974FD194F37B055EA2AA1B6C2F5CF2FE2311A99DABA178AE537F2425EF463D`：四类 bloom strength 均 0，自动曝光关闭，brightness／burn 0.5。`capture-rebind-arc360/` 305 帧、最后有效帧 27.87 秒，看到暗环出现、灰雾退去；首次 autoplay 未重播保留为失败。Terry 实际画质未可靠核实，天气、视距／FOV、时钟也未与 AECN 配平，因此不宣称外观等效或游戏最终显示。最后 Terry 已不在进程清单，原因未知，本任务没有强制结束它。
- 普通 Debug 仅加载 All Game Packs，直接打开官方原文件。`debug-neferata-arc90-final/` 仅前 1.99 秒 80 帧有效；28 秒计时不是全程有帧。最后 guard 后 `debug-neferata-arc360-final/` 1008 帧、最后有效帧 22.99 秒，已看暗红烟尘／折射出现与消失，仍大量层停用，不用 7／41 映射或加载成功代替正确。
- 内存诊断仅把根引用尺度上限 1 改为 1.616698，`debug-neferata-scale-diagnostic/` 看到烟尘范围变化；随后编辑器撤销恢复上下限 1、无未保存标记，未保存原 Pack。`debug-neferata-original-restored/` 回到完整原始输入录制，1211 帧、最后有效帧 27.98 秒，已复看出现和淡出。这仅证明此参数实时刷新和撤销，不证明所有材质编辑入口正确。
- 文件名输入出现预期以外文字时立即停止，未提交该文件名；用户明确回复“可以恢复，我暂不使用键鼠”后恢复并纠正。最后普通 Debug PID 40364、窗口 920692，只有原版 arc360 标签，预览开启、无未保存标记。恢复仍重新核对进程及编辑状态，不凭旧 PID 关闭。
- 最终盘点 `workspace-after.json`：同分支／HEAD、空暂存区，入口 181 文件全部存在；12 个既有文件为本轮相关修改、169 个入口文件散列未变，新增原生材质头与两份测试共三文件，总状态 184 项。`git diff --check`、新增文件空白／冲突标记检查和中文资源 JSON 解析通过。正常 Debug、181 源码备份、278 运行文件备份、工具、全部中间失败和采样保留。没有执行任务清理或任何 Git 交付。

后续优先原生多粒子加权扭曲合成及已见硬边，再用同档位、同视角、同阶段的完整官方组合验证；保持普通渐变、刚体、滚动模型跨类型回归。不用未知画质截图调强度／颜色，不扩泛光，不回 Mod 单例。**全部 VFX 目标仍在推进，本轮未完成视觉整体验收。**

## R66 共用发射扇区、画质通道与透明裁切（历史）

> **历史状态（R66，2026-10-22）**：按用户要求改共用路径，已修正旧式斩击忽略 `SpreadArc` 而整圈散射、预览画质忽略透明通道覆盖，以及普通预乘面片忽略 `ps_alpha_crush` 的问题。裁切再按原生顺序先于软深度淡化。**这不是全部斩击、渐变或透明效果已正确；渐变扭曲、原生透明合成与完整 Mod 对照仍有缺口。** 沿当前分支，源码、Debug、备份、工具和失败记录全部保留；不提交、不推送、不清理，全局 `1.0 / 120` 不变。用户已持续授权对应 Debug／Terry 窗口操作，实际输入冲突才停桌面输入；普通焦点恢复不重复索取授权。

证据根目录：`artifacts/vfx-shared-slash-r66/`。以下是 R66 新执行；R65 及更早记录只作历史，不计为本轮重测。

- 入口只读核对实际非联接仓库、分支 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存区为空；180 项未提交／未跟踪文件逐项与 R65 最后快照一致。`workspace-before.json`、`source-before/`、`runtime-before/` 保留源码与正常 Debug 的 278 个文件。原 `asseteditor_cn_vfx_preview.pack` 未覆盖。新诊断包使用唯一文件名和别名：`aecn_vfx_r66_controls.pack`、`aecn_vfx_r66_gradient_controls.pack`、`aecn_vfx_r66_masque.pack`，游戏 data 中这些新包和对应证据副本均保留，不自动清理。
- 从当前原版 manifest 与 `@cth_shenhua*.pack` 读取来源，重新核对 1100／84 个既有模型材质清单，以及 38／5 个名称含 slash 的有效完整 VFX 在三个档位的转换。另按字段扫描原版 7497、Mod 135 个可读 VFX 文档，识别 `SpreadArc`、渐变与画质通道覆盖，不把这些数量称为支持率或视觉通过。`family-inventory/` 保留明细；`family-summary.json` 明确分开不支持的文档输入与真正 XML 解析错误，Mod 的 `sunwukong_lightning_melee_arc90_red_yellow.xml` 有重复属性，`wh3_dlc24_lib_entity_zengzhang_gi01c_flamethrower.xml` 有非法属性字符；没有改写这两个来源。

### 实现与证据边界

- `scene_view_type` 从默认档位加所选档位的有序结果取最后覆盖值。Neferata 的 blood_droplets 在档位 1／2 不再被档位 0 的未支持通道提前拒绝。档位 0 未支持通道仍明确诊断，半分辨率合成并未实现。
- 旧式普通面片在显式 `SpawnSpread`、无 `SpawnArc`、默认分布、无表面法线且相对方向为缺省／上轴时，按 `SpreadArc` 的水平扇区发射，+X 转向 -Z。未填扇区保留整圈随机；没有把该规则强加到刚体或缺少 `SpawnSpread` 的层。`native-arc0/`、`native-arc90-v2/` 是本轮 Terry 灰色面片方向控制，0°／90° 的轴向已观看；180°、45°～135° 当前只有自动几何检查，不能写成原生已看过。完整原版 Malus 普通 Debug 从环形散射变为向前的紫色条带，最终形状、亮度和原生时间仍未完整对齐。
- 普通、非扭曲、带 `ps_texture` 的预乘面片接入 `ps_alpha_crush`，包括已支持的渐变分支；分别裁切覆盖和发光形状，使用原始寿命曲线与硬边开关，同一层不同粒子保留不同阈值。新增 CPU／原生粒子字段，原生结构尺寸核对仍生效。实际原生捕获和游戏附带着色器支持该公式；不以整片淡出替代边缘收缩。进一步测试复现“先软深度淡化再裁切导致发光提前消失”，已将裁切移到软边之前。扭曲、专用模型、自定义像素着色器与非预乘分支没有据此宣布支持。
- `native-gradient-v3/` 是成功准备的四组诊断控制；`native-gradient/` 和 `native-gradient-v2/` 的空实体／XML 准备失败保留。本轮 RenderDoc 捕获 `native-gradient/session/terry_frame3456.rdc`，反汇编在 `gpu-gradient-detail/`。实际 DISTORTION 像素着色器使用底图透明度、可选渐变透明度映射、法线或几何基轴，再写加权扭曲；不使用渐变 RGB 发光。AECN 仍走仅法线的 Effekseer 扭曲候选，**缺少独立底图透明遮罩、渐变透明度与原生加权合成，未修完**，没有简单删掉限制或把扭曲改画彩色面片。

### 窗口与原生对照

- Terry 本轮重新导出实际环境到 `native-direction/effective-environment.environment`，四类 bloom strength 均为 0，brightness／burn 为 0.5，自动曝光关闭；后续自有场景继续指向该无泛光环境。普通 Debug 使用未选天气的默认环境，未冒称两边照明一致。
- `debug-malus-spread/`、`debug-neferata-q1/` 为裁切实现之前的本轮完整源文件窗口记录。`debug-masque-crush/`、`debug-malus-crush/` 为裁切实现后、最后软深度顺序修正之前的记录：实际复看 Mod 金色斩击从碎片到月牙再消失，原版紫色斩击定向移动；这不证明整类正确。
- 完整 Mod 原生导出在 `native-masque-bundle/`，包含四个 VFX 和两个原字节自定义模型／材质，只改导出别名与 TED 可见性。`native-full-masque/` 首次原名称加载失败。`native-full-masque-v2/capture/` 未成功捕获重播；`capture-rebind/` 最初粗采样误以为空白，逐帧复查发现真正播放集中在录制约 8.15～9.03 秒，见 `review-event.png`：**原生出现白黄实心面片，不能作为正确材质基准**。这是录制时间，不是精确粒子年龄。`capture-library/review-event.png` 另确认同一直接子库在录制约 11.6～12.4 秒也出现相同白黄面片；因此不是只由最外层引用引起。保留原始帧供继续定位，不能把未解决的原生导出／资源问题当成 AECN 应模仿的外观。

### 构建与自动检查

- 具体日志和 TRX 见 `test-summary.json`，各阶段有重叠，不相加当一次全量。画质修复前 2 失败／2 通过；最终该批含 GPU 的 `quality-gpu.trx` 47 通过、0 失败、0 跳过。扇区修复前 4 失败；`spread-after.trx` 67 通过、0 失败、1 跳过，跳过原始水平层像素用例的原因是未设置 `ASSETEDITOR_VFX_GAME_DATA`。
- 裁切修复前 `crush-before.trx` 3 失败；修复后 `crush-regression.trx` 54 通过、0 失败、0 跳过，覆盖透明度、渐变和材质合成。`crush-shared-abi.trx` 27 通过、0 失败、0 跳过，检查共享粒子结构对烟雾、刚体溶解和普通自发光的影响。后续批次独立阈值检查 `crush-batch.trx` 7 通过。软深度顺序修复前 `crush-depth-before.trx` 1 失败；最后 `crush-depth-after.trx` **16 通过、0 失败、0 跳过**，包含 8 项裁切与 8 项深度投影检查。
- 使用 `dotnet build Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Debug --no-restore` 后，以 `ASSETEDITOR_VFX_HEADLESS_TEST=1`、对应筛选运行 `dotnet test ... -c Debug --no-build --no-restore`。最后测试构建 `crush-depth-after-build-v2.log` 为 0 警告／0 错误。早期项目名错误、Debug 占用导致的失败构建，以及采样／准备失败记录均保留；占用实例核对只读标签后正常关闭，没有强制结束或丢弃编辑。
- 原生 `Build.ps1` Debug 构建成功，最后主程序 `debug-crush-depth-build.log` 为 0 警告／0 错误。最终正常 Debug 的原生 DLL SHA256 为 `9D294E5EEDB467897953B50F39AF50913C1DDFFFFF6314D2AFF37E0153A8EE11`。没有执行全解决方案、Release、CI，也没有提交。
- `runtime-after-crush/` 使用最后软边修正之前的 DLL，只证明当时的加载／路由／四个时点绘制；最终 DLL 的重复检查单独放 `runtime-final-depth/`。上述运行检查均不作为视觉验收。
- 最终 DLL 的 `runtime-final-depth/` 已运行退出 0，原版 38／Mod 5 个完整输入没有报告运行或纹理加载失败，其他候选／未支持诊断仍存在。`debug-neferata-final/`（档位 1）和 `debug-masque-final/`（档位 0）为最后软深度修正后的普通 Debug 28 秒录制并已复看：前者有尘雾和背景坐标轴扭曲，后者有金色碎片、成形月牙与消失；没有据此确认色彩、扭曲或整个特效合格。
- 最后正常 Debug 实例 PID 86068、窗口 591958，保留两个只读原版／Mod 标签，无未保存星号；Terry PID 73240、窗口 5245452，自己的 `native-full-masque-v2-r66` 场景当前是直接子库 `aecn_r66_masque_c0df65abf5542045`，无泛光环境不变。恢复时重新核对实际进程与编辑状态，不凭历史 PID 关闭程序。所有本轮采样已结束，程序保留供继续看画面。
- 交付复核仍为同一分支与 HEAD，暂存区为空；入口 180 项全部存在。当前 181 项，新增 `VfxAlphaCrushTests.cs`，本轮共改变 13 个源／测试／说明文件，其他入口文件散列未变；`workspace-after.json` 留下完整状态与散列。`git diff --check` 通过，中文资源 JSON 可解析。没有执行任务清理，所有原件、运行产物、备份、工具、诊断包和失败采样均保留。

剩余优先项：按已捕获原生 DISTORTION 合同补齐透明遮罩与渐变透明度；查明原生 Mod 组合白黄面片的资源／导出差异，再作相同输入比较；继续跨类型排查尚未支持的发射方式、力场和专用材质。万物宁和叶片密度、烟雾、完整模型材质和所有原版／Mod 外观仍未整体验收，不恢复只围绕单例调亮度的做法。

> **历史状态（R65，2026-10-22，已按用户授权恢复）**：用户明确要求扩大到共用材质与其他类型，不再围绕万物宁和单例反复调整。本轮修正刚体溶解材质超过六张独立纹理时的白色实体回退、补上水平仅绕 Y 旋转的速度朝向，并修正世界空间独立朝向被参考旋转压扁的问题。**仅上述具体故障有新证据；斩击及其他类型远未全部正确。** 原版月牙斩击恢复可见，但仍向四周散开，与 Terry 前进弧形不符。叶片、烟雾、完整原生照明及未覆盖材质继续未验收。沿当前分支；全部成果、Debug、工具及失败记录保留，不提交、不清理，全局 `1.0 / 120` 不变。

## R65 共用材质与斩击分类核对（2026-10-22）

证据根目录为 `artifacts/vfx-material-families-r65/`。以下为 R65 新核对；后面的 R64、R63 分开保留，不视作本轮重测。

- 入口重新核对实际非联接仓库 `D:\Project\Szy-Cathay\TheAssetEditor-CN`，分支 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，暂存区为空。全部 180 项未提交／未跟踪文件与 R64 接续快照逐项散列一致；`workspace-before.json`、`source-before/` 保存入口，`runtime-before/` 保留正常 Debug 的 278 个文件，生成的原生材质头另有备份。没有新分支、worktree、游戏 Pack 写入、提交或清理。
- 从当前 manifest 原版包重新读取资源，并读取当前 `@cth_shenhua*.pack`。原版 1100、Mod 84 个既有材质清单的字节与 R60 保存来源一致，重新检查当前解析路线。名称含 slash 的有效 VFX 为原版 38、Mod 5 个，检查了三个画质的转换；另有 7 个同名匹配实际是材质 XML，保留为非 VFX 输入。`live-before-complete-routes/`、`live-after-runtime/`、`live-final-runtime/` 保存各阶段；早期遗漏自定义面片路线的 `live-before/` 未删。**清单、解析、64×64 离屏运行均不等于全量视觉验收，模型路线之外的资源也不能一律统计为不支持。** `live-final-runtime/` 在参考旋转修复之前；修复后以最终代码又完成 `live-reference-yaw-runtime/`（退出 0），原版 38、Mod 5 个运行记录均未报告运行时或纹理解码加载失败，但已知参数／材质诊断仍存在。另有针对性的 GPU、原版离屏及普通窗口记录；这份运行清单仍不是完整视觉验收。

### 本轮实现及其范围

- `VfxRigidDissolveMaterial.cs`、`VfxMaterialMaskAtlas.cs`、`EffekseerVfxPreview.cs` 与原生 `RigidDissolveMaterial.h`／`Bridge.cpp`：保留原始 10 个逻辑纹理与 184 字节作者参数。超过六个独立纹理视图时，把溶解、噪声、自发光 A 三张同色彩空间遮罩装入带循环边界保护的图集，以六个物理绑定采样；不降分辨率。超过 4096 或混合色彩空间仍诊断并停用依赖层。六绑定内的旧别名路线不变。当前来源中 7 个原版、1 个 Mod 材质原先触发容量回退；这不是八个完整特效已验收。Mod 的 `wh3_dlc25_elspeth_cone_d2` 属于库内 `continuous_rigid6`；当前找到的 `yangjian_elspeth_slash.xml` 没有启用这一层，不能用杨戬斩击录像证明该 cone 已实机验收。
- `TotalWarVfxConverter.cs`／`TotalWarVfxSimulation.cs`：为普通面片精确映射 `Particle only rotating around local Y (horizontal)`，保持水平平面，速度水平分量决定朝向，完整速度长度参与拉伸。现有其他模式和全局步长保留。
- 世界空间、未启用 `ParticleRelativeOrientation` 的速度朝向粒子不再把参考旋转额外投影进自身平面。参考旋转仍用于出生位置和速度。原先 Y 轴 ±90° 参考旋转会压成零面积；局部空间及启用相对朝向的路径保持原行为，任意组合仍需原生对照。游戏随附 `particle_vfx_library.hlsl` 本轮重新提取，保留在 `current-shader/`，散列 `400709701111E07657E3936B3008352303DE0419C0AED399EB924174CFE9566E`；可读源码不能单独证明所有游戏运行分支。
- 原生依赖版本、六纹理／十六 float4 的现有桥接布局未升级。当前正常 Debug 与原生 bin 的 `AssetEditor.CN.Effekseer.dll` 散列为 `0F7AAE998DF5E205DAC96FA7F5DFAF3500910714F33A50CD3D4114CF39BE81CF`。

### 新的普通窗口与 Terry 证据

- Terry 使用自己的 `slash-r65-1de4747bfd3e4f17b859a4021aa9a29d` 场景。实际从 UI 导出 `native-slash/effective-environment.environment`，四类 Bloom 强度均为 0，自动曝光关闭，Brightness／Burn 均为 0.5；文件散列 `994CC4EC6D31B3AA82A95CEDCA62EF5CD13F7C7CA2F25178C34839FF3F845F2A`。没有随后改动泛光。普通 Debug 天气未选择，双方相机／时钟不完全一致，不能据此校准绝对亮度或宣布逐像素相等。
- 原版 Bhashiva：`debug-bhashiva-before-q1/` 的白色硬模型与 Terry `native-slash/bhashiva-capture/` 的橙色破碎弧明显不同。修复后 `debug-bhashiva-after/` 在普通窗口可见橙色破碎弧、消失过程，原白色实体消失。错误 ROI 的初次 `debug-bhashiva-before/` 保留为失败记录。
- 其他分支：`debug-clawspeaker-shadows-after-q0/` 可见蓝灰／暗色破碎弧；`debug-archaon-after-q0/` 可见短暂红色滚动弧线；`debug-masque-mod-q0/` 可见黄色弧形变化；`debug-yangjian-slash-armed-q0/` 可见黄色斩击出现和消失，伴随暗红点状粒子。均实际复看录像联系图，但没有每项完整同条件 Terry 配对，**不能写成整类验收或全部层正确**。杨戬第一次采样 `debug-yangjian-slash-q0/` 含预览未打开阶段，另录已启动采样后重播的完整版本，旧记录保留。
- 原版 Malus 月牙：仅补上朝向映射后的 `debug-malus-after-q0/`、`debug-malus-after-armed/` 仍基本空白，保留失败。`malus-soft-zero/` 将诊断副本的软深度设为零仍空白，排除软深度独自造成消失。参考旋转修正后，`malus-reference-yaw/` 离屏及 `debug-malus-reference-yaw/` 普通窗口都恢复紫色火焰；**向四周散开仍与 Terry `native-slash/malus-capture/` 的前进弧形不符**。来源含 `SpreadArc=90`，当前尚未读取该字段，且 `Arc_white` 的运动／地面关系仍有偏差。此项明确未完成，不用恢复可见替代完整外观。
- 当前本任务正常 Debug 实例 PID 4456，正常仓库输出路径；两个只读标签为 Malus 与杨戬斩击，没有本任务未保存编辑。Terry PID 6372，自有场景保存且窗口标题无脏标记。恢复时仍重新确认进程及用户状态，不根据此历史 PID 直接关闭程序。所有 R65 采样均为具名目录，无需清理。

### 构建、自动检查和剩余问题

- 刚体容量回归：`tests/rigid-capacity-before.trx` 修复前 2 失败。初版 `rigid-atlas-after.trx` 14 通过／3 失败暴露原生仍用旧别名校验；修正桥接后 `rigid-atlas-after-v2.trx` **17 通过、0 失败、0 跳过**。随后独立滚动遮罩补充用例 `rigid-atlas-motion.trx` **1 通过、0 失败、0 跳过**。这些验证 CPU 参数、真实 GPU 图集采样、透明裁切及独立滚动，不等于完整效果外观。
- 水平朝向：`tests/yaw-before.trx` 修复前 3 失败；`yaw-after.trx` **30 通过、0 失败、0 跳过**。进一步旋转回归 `reference-yaw-before.trx` 修复前 5 失败；最后 `reference-yaw-after.trx` **62 通过、0 失败、0 跳过**，涵盖水平几何、普通朝向、模拟及按距离发射。30 项与 62 项有重叠，不能相加当新的全量结果。
- 命令使用 `dotnet build Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Debug --no-restore`，最后该构建 1877 警告／0 错误；随后以 `ASSETEDITOR_VFX_HEADLESS_TEST=1` 执行 `dotnet test ... -c Debug --no-build --no-restore`，筛选 `VfxHorizontalGeometryTests|VfxTranslationOrientationTests|TotalWarVfxSimulationTests|VfxDistanceEmissionTests`，TRX 如上。错误项目名的构建日志、原生初次校验失败及探针多根 XML 解析失败均保留。最新主程序 `dotnet build AssetEditor/AssetEditor.csproj -c Debug --no-restore` 成功，`debug-reference-yaw-build.log` 为 0 警告／0 错误。没有执行全解决方案测试、Release 或 CI。
- **未解决分类**：月牙等组合的发射扇区／运动；Neferata 的渐变材质和特殊透明通道；部分 Skulltaker／Pendulum 力场；非等比缩放的速度拉伸；其他未知专用材质与动画；原版 `giant_spined_beast_slash` 启用的三个名称在当前 basilisk 库中不存在，需要缺失引用诊断和来源核对。另一个 Skreech 样本确为来源全部禁用，两者不能混为“原文件没效果”。`slash-open-issues.json` 是定位线索，诊断可能是候选警告而非整层拒绝。
- 下一步按共用失败类型处理上述问题，并跨原版／Mod 实际窗口复查；不要退回仅围绕万物宁和或单个斩击调亮度。万物宁和叶片／烟雾、完整原生动态自发光和游戏后处理仍未整体完成。本轮没有重新验收这些旧样本。

## R64 恢复后的历史证据（不计作 R65 重测）

`artifacts/vfx-resume-r64/` 保存暂停后恢复记录。R63 最后的 `ViewportShadingPolicy.AllowsEmissiveBloom` 已在 R64 构建，显式天气发光 Bloom 为零时关闭普通模型旧泛光而保留表面发光。`tests/bloom-weather-emissive.trx` 22 通过、`tests/kitbash-bloom.trx` 39 通过，均无失败／跳过。R64 普通窗口的标准 Mod 武器为 `@szy_weapon/hu1e_cg_weapon/hu1e_cg_weapon_01.wsmodel`，不是万物宁和琵琶；原始速度 0.05、脉冲 0，临时脉冲 1 和速度 0 的录制后分别撤销，恢复无脏标记。`debug-weapon-original/`、`debug-weapon-pulse-one/`、`debug-weapon-flow-zero/` 显示流动／脉冲差异。Terry 万物宁和 `native-wanwu/placed-full-capture/` 有无泛光的出生、破碎消失和根部雾采样，但双方时钟与相机未严格对齐，仍未整体验收。后来检测到用户输入及 Escape，停止桌面操作；用户随后重新授权恢复。

> **以下为 R63 暂停时的历史状态**：沿现有分支修复共同材质、烟雾法线／散射、ForceA 曲线和动态自发光。用户新增授权：把普通 3D 材质的流动、扭曲和脉冲接入实时预览；继续边看画面边修，Terry 关闭泛光。**万物宁和的完整照明、叶片密度和烟雾外观仍未验收；当时普通模型旧泛光还会遮住表面细节，不能称整类完成。** 保留全部源码、Debug、备份、采样及失败记录；不提交、不清理、不切换分支，全局 `1.0 / 120` 不变。

## 暂停接续点（2026-10-21）

用户明确要求暂停，准备关电脑；停止开发、启动／采样及所有桌面输入，等待用户回来后明确恢复。没有提交、推送、切换分支或清理。

- 磁盘源码已保存。最后追加的 `ViewportShadingPolicy.AllowsEmissiveBloom` 及普通预览／照片捕获调用，遵守所选天气中明确为 0 的 `bloom_emissive_strength`；对应 4 个策略用例已写入，但 **尚未构建或执行，当前 Debug 程序不含这项最后修改**。已有 26／39 项通过记录是该修改之前的范围，不能套用于最后修改。
- 普通 Debug 最后由本任务启动的实例 PID 78624，输出路径为 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。本任务已将武器流动速度从临时 0 撤销恢复为 0.05，看到未保存标记消失；随后接口检测用户输入并立即停止，因此用户后续未保存状态未知，恢复时重新核对，不能强制退出或丢弃内容。
- `debug-standard-weapon-speed-zero/` 和 `debug-standard-weapon-restored/` 分别完成 28 秒采样、831／813 帧，实际复看联系图。默认对象模式仍被旧泛光过曝遮挡，不能据此认定材质外观合格。普通窗口脉冲参数的修改验证尚未执行。
- `native-standard-pipa/` 与 `native-standard-weapon-valid/` 两次静态武器控制仍未获得可辨认模型，均属失败／未验证。后者另在 Terry 创建同名实例，保存自己的场景后正常退出；两份自有临时游戏 Pack 均按散列转存各自 `session/disabled-after-session.pack`，原件和场景保留。构造类型／裁切修正没有解决空白，不能写成根因已确认。
- `native-standard-rigid-lifetime/` 是下一步准备的原版刚体控制，真实模型 `projectiles/chd_dreadquake_mortar_01_projectile.wsmodel`，保留 12 个原资源散列，增加诊断发射器明确寿命；只完成准备，**尚未启动 Terry、未录制、未验收**。不要把名称含 pipa 的旧武器目录当万物宁和琵琶证据。
- 恢复后先核对现场与用户窗口状态，再构建最后修改并跑对应检查；用正常对象模式及显式无泛光天气继续原版／Mod 动态材质参数验证，随后回到完整复苏风暴和万物宁和的生命周期、叶片与烟雾。全部未覆盖来源、完整 PBR、非零泛光合成及原生随机相位仍未验证或待修。

## R63 共同材质、烟雾与动态自发光

证据相对 `artifacts/vfx-wanwu-material-r63/`。R62 及以前是历史记录，未冒称本轮重测。

- 入口核对实际仓库、`codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区和全部 166 项状态，与 R62 快照逐项散列一致。`workspace-before.json`、`source-backup/`、`runtime-backup/` 及 `runtime-manifest.json` 保存入口；中间候选与失败未清理。
- 普通面片的受限 ForceA 曲线按粒子寿命进度采样，以原生控制支持的 0.01 换算，再应用阻尼及位移；不改全局步长。补上画质 0／1／2 的独立预览选择，避免把不同画质的叶片／粒子数量当材质错误。Terry High 在本轮控制中实际采用质量 1；这不等于全部叶片密度已正确。
- `TotalWarVfxConverter.cs` 读取真实 `ps_texture/normal`，兼容旧字段并诊断双重定义。`CoverageMaterial.h` 按原生采样重建 DXT5n 法线和粒子旋转基；太阳散射接入实际 Rayleigh／Henyey-Greenstein 相函数、透射项和原生辐射系数，替代旧近似。受限速度朝向一并核对；地形跟随等未覆盖分支不扩成已支持。
- `GrowingVinesMaterial.h` 按原 `material_map`、金属量与原生 10 位发光值重建 `albedo * (1-metalness) * E * 255`，保留共享时间驱动的流动及枝叶裁切。`debug-growing-emission-front-cycle/` 的普通 Debug 录制可见破碎出现、退去和表面变化；`native-wanwu-lighting-fixed/capture-emission-reference/` 为本轮新原生记录。两边相机／时钟不完全一致，原生顶部还有裁切，不能据此验收完整外观。完整游戏 PBR 环境反射和间接光未还原。
- 普通标准角色自发光现在由 `RenderEngineComponent` 提供共享时间，`EmissiveCapability` 传入流动方向／速度、扭曲纹理及幅度、脉冲频率／幅度。着色器按随游戏提供的原生角色着色器读取 R/B 扭曲、红通道渐变输入、脉冲和边缘项，并重建受发光影响的底色、金属／粗糙度与发光量；发光移到色调映射之前，移除旧的固定乘 5。普通绘制、发光绘制和照片捕获使用同一时刻。原生每实例随机脉冲相位、完整游戏后处理和其他专用着色器不在本次已验证范围。
- `standard-emissive-inventory/result-v2/` 重新读取当前 manifest 原版包和所列神话人物包，记录 14065 个材质、2124 个含角色发光名称的资源及 2233 个引用模型，解析错误 0；其中六种标准角色发光着色器合计 1700 个资源。**这只是来源清单，不是视觉测试数量、支持率或整类验收。** 初次参数提取错误的 `result/` 仍保留。
- 普通 Debug 已加载原版 `chd_fireborn_01.wsmodel` 与 Mod `hu1e_cg_weapon_01.wsmodel`。后者画面是武器，不能当作万物宁和琵琶；早先目录 `debug-standard-pipa-surface/` 与 `native-standard-pipa/` 的名称不准确，原记录保留并以 `conditions.json` 的真实模型路径为准。前者表面录制使用骨骼检查模式隔离泛光，不能当默认对象模式验收。当前默认对象模式旧 `SuperWide` 泛光仍严重过亮；不得把此问题写成已修好。
- UI 中文动态提示已经在普通窗口看到。`debug-standard-weapon-speed-zero/` 验证流动速度临时改为 0 后表面停止，随后通过编辑器撤销恢复原值 0.05，未保存源资源；恢复后的录制为 `debug-standard-weapon-restored/`。其他实时编辑与原生静态武器控制仍在继续。
- 原生 `native-wanwu-lighting-fixed/environment-check.json` 对实际导出环境与选中环境的光照、曝光及泛光字段比较为无差异：四类泛光强度及旧泛光为 0，自动曝光关闭。这个结果仅适用于该次会话；新对照仍要核对实际载入。实际原生技能存在根部向外的雾，不能将所有雾全部隐藏；异常上喷、数量与消散差异仍须逐项定位。
- 最新普通模型渲染相关检查：`common-emissive-reconstruction.trx` 26 通过／0 失败／0 跳过；`kitbash-emissive-reconstruction.trx` 39 通过／0 失败／0 跳过。对应 Debug 构建成功，前者 3 条警告、后者增量 0 警告；`build-common-emissive-reconstruction.log` 的 HLSL 未初始化失败及修正记录保留。之前的自发光 25 项、GrowingVines／烟雾 24 项、ForceA／画质 191 项及其他较早筛选是不同范围的历史本轮运行，不能相加作为一次全量通过。没有运行全解决方案、Release、CI，也没有用户外观验收。
- 静态武器原生控制的首次绝对路径启动失败、空白结果和随后相对路径重开均保留在 `native-standard-pipa/session/`。`native-standard-weapon-valid/` 修正诊断控制的 construction_type、刚体裁切／透明度和相机高度；尚需窗口检查，准备成功不算原生对照成功。

接下来继续确认普通材质的实时编辑、原生静态控制及默认对象模式泛光合成；回到原版完整复苏风暴与 Mod 完整万物宁和，按实际阶段核对材质、枝叶和烟雾。所有未覆盖来源继续列为未验证，不以少数样本代表全部。后续更新本节的实际运行状态，保留下方历史记录。

> **历史状态（R62，2026-10-21 +08:00）**：继续实际操作 Terry 和普通 Debug，修复了扭曲通道把背景重复叠亮、以及折射擦掉前序特效这两个共同问题。晶体样本的白灰硬片和中间版本的矩形空洞在新普通窗口录制中消失，未隐藏烟雾或模型。**这不表示整个材质族或全部原版／Mod 外观已经正确。** 用户补充：Terry 默认泛光不能直接作为游戏显示标准；本轮已在实际环境中单独关闭泛光并新录万物宁和，仍有照明、密度和消散细节差异。保持当前分支、全部成果与全局 `1.0 / 120`，不提交、不推送、不清理，继续以材质和生命周期为优先，不扩展泛光能力。

## R62 共同修复及新证据

以下路径相对 `artifacts/vfx-material-visual-r62/`。R61 及更早结果只按原范围复用，没有写成本轮重测。

- 入口只读核对实际仓库、分支 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区及全部 166 项未提交／未跟踪文件，与 R61 末次快照逐项散列相同。入口源码在 `source-backup/`；正常 Debug 的 278 个文件及原生 Debug 的 5 个文件在 `runtime-backup/`，共 283 项，附 `runtime-manifest.json`。没有切换、收起、回滚或清理。
- `TotalWarVfxConverter.cs`：`DISTORTION` 输出的是折射后的背景，不能套用颜色层的 `additive_blend_mul` 加法混合。现在使用透明混合，原 DXT5n 法线、图集、软深度和粒子仍保留。Mod 的完整 `vfx/wh3_dlc24_lib_entity_jade_long.xml` 第 19 层定位到这一共同错误；没有按技能名特判。`jade-isolated-before/` 与 `jade-isolated-after/` 是 27 层各五个时刻的定位记录，仅扭曲层变化，不能作为整类外观验收。
- `Bridge.cpp`：背景改在 Effekseer 扭曲绘制回调中复制，包含此前已经画出的模型和粒子。只修混合后出现的矩形空洞说明原先“整套 VFX 绘制前抓背景”仍错误；新的 GPU 重叠用例先稳定复现前序蓝色粒子被擦掉，再修复。回调按固定 Effekseer 版本的所有权释放。后续普通粒子顺序、原始材质和全局时间步长不变。
- 完整晶体 Mod 普通窗口：`debug-jade-before/` 是入口白灰硬片；`debug-jade-after/` 是仅修混合的中间失败，仍有矩形空洞；`debug-jade-final/` 是两个修复后的正常 Debug 连续 20 秒录制，354 帧，已实际查看。硬片和空洞消失，晶体、绿烟与光点仍显示。扭曲强度、软边尺度、多层折射及完整光照仍未完成原生标定。
- 完整原版 `vfx/wh2_dlc12_lib_lizard_acid_explosion.xml` 的新普通窗口在 `debug-original-acid-final/`，已查看爆发与消散，未见上述硬片和空洞。新 Terry 原来源在 `native-original-acid/`；晶体原来源在 `native-jade-library/`。**这两组使用当时的默认天气和开启的泛光，只能按原条件记录结构观察，不能作为亮度／光晕正确的证明。** 原版来源散列、原模型、贴图与依赖记录在各 `conditions.json`，没有改原 Pack。
- `distortion-inventory-v2/` 读取实际官方 manifest 中的包和当前 `@cth_shenhua*.pack`，保留全部画质／禁用层的来源，转换检查仅针对质量 0。命中原版 476 个来源、659 个转换扭曲实例，其中 51 个原会走加法；当前 Mod 27 个来源、34 个实例，其中 5 个原会走加法，修复后没有加法扭曲。原版另有 24 个来源没有生成扭曲层，原因保留。**这是数据路径盘点，不是视觉覆盖率，不包括磁盘上的全部其他 Mod。** 首版 `distortion-inventory/` 错用 UTF-8 预筛漏掉原版，作为失败保留，不引用其数量。

## R62 万物宁和与关闭 Terry 泛光

- 完整源 `vfx/chiguo_wanwuninghe.xml` 的 SHA256 为 `0c5ebe46bae7130b1e0e9bed7359e28deb80da313cc6b40da146b0097786be4d`。新普通 Debug `debug-wanwu-final/` 录制 28 秒、987 帧，实际查看了模型逐段出现、持续、退场与低平扩散；录制中有一次手动重播，记录时钟不等于特效年龄。R61 的模型桥接、源定义总开关、旧发射角、发射窗口及默认世界空间修复没有冒充本轮新增。
- 本轮先前 `native-wanwu-baseline/session/default-weather-capture/` 可观察出现／持续，没有完整退场；旧控制天气的黑屏／缺画面没有用作完整对照。`native-wanwu-lifecycle/session/lifecycle-capture/` 切换已结束实例的 Autoplay 未重播，整段空场，保留并排除。另建实例的 `placed-lifecycle-capture/` 为 28 秒、464 帧，已实际查看完整生灭，但 **Bloom 开启，不作为亮度标准**。
- 用户补充后，在实际运行的 Terry 的 `Warscape Environment > Selected > Bloom` 中，将 `bloom strength` 从 1 改成 0。`terry-bloom-on.environment` 与 `terry-bloom-off.environment` 是 UI 导出的前后快照；逐字比较仅这一项变化，粒子／自发光／水面三项强度原本均为 0，曝光及其余环境参数不变。`bloom-setting-verification.json` 记录比较及原始 `default_battle.environment`、`terry_default_battle.environment` 未变化的散列。未通过关停单个渲染阶段冒充关闭泛光，未改写原天气。
- 新 `native-wanwu-lifecycle/session/bloom-off-lifecycle-capture/` 连续 28 秒、540 帧，ROI／源时钟／快照错误均为 0，已查看逐段出现、完整模型、退场和贴地向外扩散。这里仍有贴地绿雾，不能因旧错误上喷而删除作者的所有烟雾。设置导出证明泛光强度为零，本轮没有另做该会话 GPU 常量捕获。双方随机相位、背景、完整光照及精确时钟尚未相同；叶片密度、模型裁切细节、烟雾亮度与消散仍待继续修复，不称破碎效果已通过。
- 为配平输入，将上述关闭泛光环境的原字节打包成 `weather-export/r62_bloom_off.pack` 并在 Debug 只读载入，回读散列完全相同。**实际天气选择失败**：`GameEnvironmentLighting.Read` 拒绝 `use_custom_ambient_cube_colours=false` 的自动环境光，界面显示保留上次选择；当时上次是“未选择”。已恢复“天气：未选择”。没有绕过该门槛、伪称双方都关闭泛光或已经配平照明，也没有据此调整材质倍率。此失败包、工具与日志保留。
- Terry 实际保存相机为 FOV 60°、视口比例约 0.55046、俯仰 24°、水平朝向对应既有 26°，视距 98.78685；准备值 45° 不是运行值。Debug 滚轮后的相机距离仍未回读，`wanwu-camera/result.log` 只是按包围盒和滚轮算法推算。两边不能作逐像素或绝对尺寸验收。手工放置的前后两个实例位于同一位置，先前实例已结束后才进行下一录制；保存测试场景提示 overlapping entities。场景连同提示原样保留，不能把该已保存的多实例场景直接重开当成单实例基线。

## R62 验证、保留与继续入口

- 新增有意义的 CPU 混合回归、两种采样数下的 GPU 背景守恒／实际折射／前序粒子保留检查。两个已有原版扭曲测试原先依赖 R61 已修正的缺省局部跟随假设，现明确测试缺省世界空间和显式局部空间，重载后再采样。最终重建并运行 `distortion-final.log`／`test-results/distortion-final.trx`：**43 通过、0 失败、0 跳过**；这只验证上述契约和像素行为，不是外观得分。
- 原生首次构建因 Visual Studio 预发行版发现失败，日志 `distortion-background-native-build.log` 保留；其 shell 退出 0 不算原生构建成功。明确指定 `D:\Software\VisualStudio\2026` 后，`Build.ps1 -Configuration Debug` 成功，见 `distortion-background-native-build-v2.log`。最终测试项目重建 `distortion-final-build.log` 退出 0、0 警告／0 错误；正常主程序 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 成功，0 警告／0 错误，见 `debug-final-build.log`。未运行全解决方案、Release 或 CI。采样工具引用目录内原生 DLL 的 MSB3246 警告另存，不混入产品构建结果。
- 本轮实际启动的 Terry 会话均保存自己的场景后正常退出，各自临时预览 Pack 核对归属和散列后保留到 `session/disabled-after-session.pack`，不删除；`native-wanwu-lifecycle/session/closed.json` 为最后一场。没有丢弃未保存内容。正常 Debug PID 35324 来自本仓库标准输出，仍保留完整万物宁和只读预览、原版和晶体只读标签，天气为未选择。原版／Mod 文档参数没有保存改动。
- 继续从共用材质、裁切和透明合成处理实际差异，先取得关闭泛光且照明可核实的同源对照；自动环境光不支持是已实际复现的限制。斩击残留硬颗粒、万物宁和破碎边缘／密度／消散及其他材质族仍未完成，不因这两个扭曲缺陷修复而推进其他能力或宣布整类通过。

> **历史状态（R61，2026-10-21 +08:00）**：用户重新明确要求 Agent 操作电脑、看实际画面共同修复，替代 R60 当时“全部视觉测试由用户进行”的边界。本轮持续做 Terry、普通 Debug 和共用链路修复。白色实心斩击弧面、万物宁和缺失模型／错误上喷，以及拖尾集中在一端，已有可观察的变化；**完整外观仍未通过，晶体白色硬片、破碎细节、粒子明暗等仍未解决。不能用几个样本、资源盘点或测试数量代表全部原版和 Mod。** 暂停泛光扩展，沿用原分支，保留全部成果，不提交、不推送、不清理。

## R61 现场与实现

以下证据相对 `artifacts/vfx-material-visual-r61/`，均为本轮新执行；R60 及以前只作历史定位。同轮早期结果复用时也分别标明范围。

- 入口真实仓库 `D:\Project\Szy-Cathay\TheAssetEditor-CN`，分支 `codex/vfx-effekseer-preview`，HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`，空暂存区，159 项未提交／未跟踪状态与 R60 快照一致。`workspace-before.json`、`source-backup/` 和 `runtime-backup/` 保存入口。没有更换分支、worktree、暂存、收起、回滚或 Git 交付；全局 `1.0 / 120` 未改。
- `Bridge.cpp` 不透明模型检查遗漏 `growingVines`，已有专用材质被桥接层拒绝。本轮接通并做源码、参数和 GPU 裁切验证。完整万物宁和现在能看到乐器模型按透明度逐段出现和消失；仍不能称破碎形状、叶片材质和完整光照已正确。
- `Enable=false` 源定义不能由引用 `enable=true` 重新启用。新的 Terry 同源真假控制支持源定义是总开关；转换器现在同时遵守源定义和引用选择。万物宁和的 `whirlwind` 原本由作者关闭，现在不会被误启用。没有按技能名隐藏图层。
- 普通面片的旧式 `SpawnSpread` 在 `SpreadMethod=0`、无 `SpawnArc`、不用表面法线、相对 UP 的受限分支中，是相对水平面的仰角、方位覆盖整圈。Terry 地形开关与角度控制支持此语义：`0..3` 度低平扩散，`90` 度向上；该分支不把 `SpawnAngle` 误作方位。保留随机数消耗顺序及明确 `SpawnArc` 的既有路径。完整万物宁和退场改为向外扩散。
- 固定正值 `BurstIntervalLength` 是发射窗口长度，`BurstInterval` 是随后等待时间；周期为二者之和，零间隔连续发射。短寿命 Terry 脉冲控制明确后才修改模拟，以窗口和固定步长的交集累计发射，遵守有限寿命与引用时间倍率。随机范围及非零间隔配零长度仍诊断未映射。没有凭原长寿命烟雾录像猜公式。
- `wh3_vfx_rigid_emissive_dissolve_one_shot` 增加独立候选材质，读取原 184 字节参数与十个逻辑纹理槽，按原蓝／绿遮罩、透明度阈值、抖动裁切及边缘自发光绘制。相同路径和用途合并为最多六个物理槽，颜色与数值用途分开。超六槽时保留原普通模型回退及诊断，不丢弃整个模型层。精确原生时钟、随机 UV 和完整延迟光照仍未对齐；当前粒子年龄时钟只作明确标注的候选。
- 正 `SpawnSpacing` 改为移动距离发射，保留不足一个间隔的余量；静止、移动、不同间隔和零／不同时间发射率的 Terry 控制支持其不按每秒发射率累计，也不在起点额外生成粒子。无模型事件辅助粒子现在使用实际移动位置，显式世界／局部空间均参与；可见刚体模型保留原已标定的出生锚点。精确相位、复杂轨迹、引用发射倍率及发射窗口内部的精确位置仍未完全验证。
- 普通面片缺省 `ParticleSimulationSpace` 改为 `WORLD_SPACE`。Terry 四列分别为缺省、显式世界、仅位置局部、位置旋转局部：前两列留在出生位置，后两列随辅助发射器移动。先复现缺省错误，再修复；已有“局部跟随”测试改为明确指定局部字段。模型和其他类型的缺省值未因此外推。该修复消除了完整斩击拖尾挤在一端的错误。
- 后续查明 `pass == null` 错误排除了显式 `FULLRES_DEFERRED_ALPHA`／`HALFRES_DEFERRED_ALPHA` 的既有天气照明和简单自发光覆盖增益。Terry v3 实际 GPU 的两类通道使用相同像素着色器及光照常量，见 `alpha-lighting-native-evidence.json`；受限普通面片分支现在接受这两类透明通道。未扩展纹理发光形状门槛、UI 专用通道、模型完整照明或原生半尺寸透明合成，没有按技能名调亮暗。

## R61 当前画面与覆盖边界

- 原版完整 `vfx/wh3_dlc25_lord_elspeth_slash.xml`：`native-slash-material-fixed/session/create-capture/` 是本轮 Terry 原模型、原材质、原纹理录制；`debug-slash-dissolve-fixed/`、`debug-slash-distance-fixed/`、最后 `debug-slash-space-fixed/` 是不同修复阶段的普通 Debug。最后一段 20 秒、468 帧，已实际查看序列。实心白弧面消失，紫色裁切弧纹出现，粒子沿弧线分布；**暗红颗粒仍偏硬、原生亮点和消散细节仍不同。** 最后一段采用自动取景，不冒充相同尺寸的逐像素对照。随后 F4 画面可见，但 `debug-slash-space-f4` 因未建输出目录没有留下录制，排除为采样证据。
- 神话人物完整 `@cth_shenhua_asseteditor.pack` 的 `vfx/chiguo_wanwuninghe.xml`：复用同轮 `native-wanwu-full-lifecycle-v2/` 原生录像；最终代码的 `debug-wanwu-space-full/` 新录 20 秒、302 帧，帧到达覆盖至 19.95 秒，已查看出现、持续和退场序列。模型能够逐段显示和裁切退场，不再启用作者关闭的上喷层；退场烟雾向外扩散。**叶片偏荧光、面片颜色／密度、破碎细节与节奏仍不一致。** 较早 `debug-wanwu-burst-fixed/` 属于默认空间修正前，不能当成最后构建复测。
- `debug-wanwu-space-fixed/` 是首次自动取景且中途重播的 336 帧录制；其录像时间不是特效起始时间。`debug-wanwu-space-near/` 虽结束记录为 20 秒，最后帧仅到 1.98 秒，不能证明完整动画。两者都保留。`debug-wanwu-final-view/` 是调整最终观看视距后另存的普通窗口记录，须以其实际采样摘要为准。
- Terry 相机参数保存在各场景及条件文件；Debug 保持既有坐标、方向和深度约定，通过 F4／滚轮调整观看。**Debug 交互视距没有从运行相机回读，不能把滚轮参数推算的距离写成已核实值。** 两边窗口比例、背景天气、随机种子及精确相位未全部匹配，不作逐像素、绝对尺寸或完整光照验收。
- `dissolve-family-check/results.json` 遍历 R60 全量盘点中该 one-shot 溶解材质的原版与当前神话人物 Mod 来源。原版 23 项中 16 项解析，Mod 6 项中 5 项解析；8 项超六独立纹理槽，保留回退。**这是解析范围，不是视觉覆盖率，也不是其他材质族已完成。** 图一晶体白色硬片的可靠根因及完整修复尚未确认。
- 早期 `native-lighting-pass-control/` 与 `debug-lighting-pass-control/` 没有配平天气，不能据此归因。后续 v1／v2 天气虽显示路径，实际 GPU 仍是默认照明；去除控制副本 XML 声明及 BOM 后，`native-lighting-pass-weather-v3/` 才核实零太阳光和六面一致环境光。原生四列录像与修复后的 `debug-alpha-lighting-control-after/` 已实际查看，均呈现一列自发光较亮、另外三列受光照衰减；这只是通道关系验证，不是精确亮度或整类外观验收。原生 `.5` 环境色解码为 `.21763763`，托管现有解码为 `.21404114`，该差异未改。
- 最后代码的新普通窗口录像：`debug-slash-alpha-lighting/` 为 20 秒、928 帧；`debug-wanwu-alpha-lighting/` 为 20 秒、740 帧。后者从头重播，覆盖模型逐段显示、持续、退场及向外扩散烟雾，原始来源未编辑。斩击仍有深色硬颗粒，万物宁和的破碎细节、叶片明暗／密度及精确阶段尚未验收。控制天气为 `weather/r61_lighting_pass/control.environment`，不能与早期无天气画面混称同条件。
- `native-slash-alpha-lighting-v3/session/capture/` 未捕获有效斩击阶段，排除为完整外观证据。`native-wanwu-alpha-lighting-v3/session/create-capture/` 新建实例后部分帧可见模型与退场烟雾，但长段空白及局部画面缺失使连续生命周期不能可靠判断；`lifecycle-detail.png` 保留密集复看，初次 `contact.png` 的 failed 标签只指此次对照未通过，不表示每帧都没有特效。不得把这两组称为配平后的完整原生复测通过；较早有效 Terry 录像仍只按原条件复用。控制包重导出后原资源逐字节一致，`inspect-native-pack/output.log` 另保留排查，两类失败不归咎于产品画面。
- 原生 GPU 核实实际 FOV 为 60°，早期条件文件写的 45° 只是准备值，不能当作运行值；原生退出保存的相机也印证 60°。两边实际视口比例、Debug 交互距离和时钟仍未完全对齐。`debug-lighting-pass-weather-before/` ROI 无效，`...-before-v2/` 只录到应用角落，两者均排除为粒子外观证据。
- 多标签观察发现：已有 VFX 预览保持开启时，另一个标签的预览开关未响应；关闭无未保存内容的旧只读标签后恢复。后续先停用旧预览再打开控制可以操作。该现象已记录，根因未查明，不声称多预览交互已通过。

## R61 构建、测试和保全

- 原生 Debug 材质编译／桥接构建成功，见 `native-growing-build.log`、`native-dissolve-build.log`。最新正常主程序命令 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore --verbosity quiet` 成功，0 警告／0 错误，见 `app-alpha-lighting-build.log`；此前 `app-space-build.log` 为上一阶段。原生候选材质总数不作为外观依据。
- 测试均先重建 `Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj`，再以 Debug `dotnet test ... --no-build --no-restore --filter ...` 执行。最后缺省空间、事件、模拟和相关方向检查 `space-final.log`／`tests-space-final/space-final.trx` 为 **70 通过／0 失败／0 跳过**。启用了显式 GPU 开关和真实游戏资源目录。没有运行完整解决方案、Release 或 CI。
- 同轮较早距离／发射窗口／事件／引用／材质组合 `distance-material-final.log` 为 **58／0／0**；其后缺省空间有改动，不能称这 58 项在最后构建又全部重跑。溶解 CPU／GPU 与回退专项 `dissolve-fallback.log` 为 **5／0／0**，后续材质代码未再变化，复用该范围。早先运动像素专项 `motion-linear.log` 为 **16／0／0**，只证明位置测量。这些分开运行且存在重叠，不相加为全量成绩，不等于外观验收。
- 显式透明通道补充八项真实 GPU 输出回归：修复前 **0 通过／8 失败／0 跳过**，见 `alpha-lighting-red.log`。修复后重建再运行天气、简单自发光、缺省纹理发光和合成相关筛选，**141 通过／0 失败／0 跳过**，见 `alpha-lighting-green.log`、`tests-alpha-lighting-green/alpha-lighting-green.trx`；开启 `ASSETEDITOR_VFX_HEADLESS_TEST=1`，不含桌面输入测试。重建初次有 1880 条既有警告，0 错误；未与前面重叠结果相加，未运行全解决方案／Release／CI。
- 红测、错误测试工程路径、夹具错误、旧像素阈值错误、DLL 占用构建失败、采样失败均保留。旧运动测试以显示字节阈值 200 判断标尺，但实际峰值为 193；已改读实际渲染器的线性浮点输出，原位置容差未放宽。早先不存在的可选 PDB 备份失败保留，实际四文件副本不冒称全目录备份。
- 入口 159 个文件及其备份保留；中间源码／运行库分别保存在 `source-before-*`、`runtime-*-before-*` 和 `native-runtime-before-dissolve/`。透明通道修改前另存 `source-before-alpha-lighting/` 和六个运行文件的 `runtime-before-alpha-lighting/`。此前无后缀的 `workspace-after.json`、`workspace-preservation.json`、`implementation-delta.patch`、`final-session.json` 保留为中间记录；末次状态以对应 `-alpha-lighting` 后缀文件为准，不覆盖旧记录。
- 自有 Terry 会话正常退出，临时预览包按散列核对后转存各 `session/disabled-after-session.pack`，原场景、工具、采样、失败和来源 Pack 均保留。普通 Debug 保留完整万物宁和只读预览；没有保存任何原版或 Mod 参数，没有清理或丢弃未保存内容。应用日志中的原版空动画解析错误仍存在，不能称运行日志完全无错。

**下一步仍是材质和生命周期正确性。** 优先排除完整原生对照间歇空白／缺帧的采样或场景问题，配平实际相机与时间后继续破碎遮罩／时钟、面片明暗和晶体白片。按共享材质族盘点全部已知来源，逐项保留未支持与未验证项；不以样本通过宣告整类完成，不提前恢复泛光或其他能力扩展。

> **历史状态（R60，当时授权已由 R61 替代）**：用户指出白色硬片、白色弧面及“万物宁和”的模型出现／消失和上喷表现仍错误，并明确这些只是举例。当时全部视觉测试由用户进行；以下记录保持原有证据边界。

## R60 模型材质与生命周期

证据相对 `artifacts/vfx-model-lifecycle-r60/`。下方 R59 及更早的 Terry、窗口和像素记录均为历史，不能视为本轮重测或用户认可。

- 入口核对仓库、`codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区及全部 155 项状态，与 R59 后置快照逐项散列一致。入口源码在 `source-backup/`，正常 Debug 的 278 个文件在 `runtime-backup/`；原生构建目录、输出和本轮中间候选分别保存在 `native-build-backup/`、`native-output-backup/`、`native-candidate-v1/`。没有分支/worktree、暂存、收起、回滚、Git 交付或清理操作。
- 全量资源盘点读取实际 manifest 中的全部原版 Pack 及当前 `@cth_shenhua*.pack`，未用截图文件筛选。`inventory-before/` 保存来源、所有画质／禁用层的模型引用、材质、参数、原始字节和失败记录；`material-coverage-before.json` 按着色器归类，区分模型 LOD0、面片与其他 LOD。范围不包括磁盘上所有其他 Mod。原版有 885 个 XML 候选不具有受支持的 VFX 文档结构及 5 个缺失模型路径；Mod 有 10 个此类 XML 候选和 2 个原始 XML 语法错误，全部记录且未改源件。清单不是外观或支持率报告。
- 共同问题：`ReadModel` 对带颜色贴图的未适配专用材质会退回普通模型，丢失遮罩、溶解等语义。本轮为仍存的近似路径增加实际着色器诊断，保留内容，不把隐藏模型当修复。`wh3_vfx_rigid_emissive_dissolve*` 等仍需实现；游戏着色器和公开 D3D 反汇编在 `material-shaders/`，没有执行游戏或 GPU。
- `vfx_growing_vines` 接入独立模型材质：原五张资源、48 字节参数、原法线参与的边缘发光、千分位粒子透明度驱动的枝干／叶片裁切、单面剔除及不透明深度。原生像素着色器支持其裁切公式；这修复了同类模型退回整块底图的代码路径，没有技能名特例。**内在颜色仍是候选，材质图的金属／粗糙度、完整延迟光照及最终外观未还原，不能称此类已完全解决。**
- `vs_vertex_alpha` 起止偏移使用同一粒子随机权重重映射透明度曲线，再乘初始透明度；事件继承使用同一规则，运动、模型动画和全局 `1.0 / 120` 不变。非零偏移仅见于部分原版文档，不能把此改动当成万物宁和独有问题的原因。
- `family-after/` 对盘点命中的该材质全部来源及引用组合做无图形设备转换和生命周期检查；未启用相关模型的引用保持原选择。万物宁和模型的透明度输入从 0 上升至 1，末尾下降至 0，并进入专用材质绑定。**这只证明数据／参数传递，不证明破碎出现、消失或烟雾外观正确。** 全部已声明支持的模型材质同时重读，既有参数不支持记录未抹去。
- 原生 Debug 和材质着色器编译成功，见 `native-build.log`、补全加载／释放路径后的 `native-build-final.log`。正常主程序 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore --verbosity quiet` 成功，0 警告／0 错误，见 `app-build-final.log`；没有启动程序。
- 最终受影响的 CPU 模型、透明度、生命周期、事件和引用回归为 **79 通过／0 失败／2 跳过**，见 `tests-final-v2.log`、`test-results/final-v2.trx`。两项原有 GPU 用例因开关关闭而跳过。未运行全解决方案、Release、CI、Computer Use、Terry、GPU 像素测试或窗口采样。红测、一次测试项目路径错误和两处夹具错误的日志全部保留；较早重编译的 1902 条、最终测试项目重编译的 1879 条既有警告与主程序增量零警告分别记录，不相加作为验收结果。
- 用户反馈前的会话日志在 `user-reported-session.log`，其中关闭空白 VFX 编辑器时出现一次 `RenderEngineComponent.Dispose` 空引用。本轮未顺手修复，不得称日志完全无错。

后续继续处理共用模型材质、遮罩及生命周期。白色硬片／弧面的实际失败路径、其他溶解材质族及异常上喷仍待修复；未获用户视觉验收前不恢复泛光优先级。**所有外观、动画、Terry 对照和普通窗口测试由用户执行；此前“键鼠空闲”不再授权 Agent 做视觉测试。**

> **历史进展（R59，优先级已由 R60 替代）**：沿共用材质与渲染链路补上经过 Terry GPU 对照的五级参考泛光，并修复原贴图只上传最清晰一层、缩小后仍偏硬亮的问题。原版球、Mod 金球、Mod 环境与龙雷电的录制仅证明当时检查过的范围，完整原生外观仍未验收。原证据与结果完整保留如下。

## R59 本轮结果与继续入口

本节证据相对 `artifacts/vfx-shared-bloom-r59/`。下方 R58 及更早记录均为历史；本轮复用 R58 保存的原始来源资源，但新 Terry GPU 采样、当前产品渲染和普通窗口检查均另存，不把旧结果写成重测。

### 现场保护与共同修复

- 入口只读核对实际仓库、分支 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区及全部 155 项状态，与 R58 后置快照逐项散列一致。`workspace-before.json` 和 `source-backup/` 保留入口；旧完整 Debug 的 278 个文件及原生 Debug 在 `runtime-backup/`。本轮泛光完成、修改 mip 之前的源码和完整 Debug 另存 `pre-mip-backup/`，均有清单。没有切换分支、创建 worktree、暂存、收起、回滚或清理。
- `FloatSceneTarget.h` 在既有浮点合成后、固定曝光前提取增量高亮，使用五级半尺寸下采样、各级横纵十一点高斯过滤和逐级相加。原场景背景的高亮贡献被扣除，未受 VFX 影响的 RGBA 保持原值，避免模型和网格被再次作为泛光源；这不能恢复背景已截断的 HDR。浮点离屏输出继续保持线性，不叠加显示后处理。缓存编译后的着色器并恢复 DX11 资源、采样器和渲染状态。
- `GameEnvironmentLighting.cs` 保留天气阈值及普通、粒子、自发光、水面四类强度。`EffekseerVfxPreview.cs` 只在阈值存在且四类强度全部存在、完全相同时使用作者值；其余沿用本轮受控参考阈值 4、强度 0.1。未构造游戏的分类遮罩，因此不能把这个参考值称为当前天气的真实强度。曝光沿用 R58 的受限手动值或亮度 1、Burn 0.05，不额外调低雷电球亮度。
- 同一预览类现在保留原 DDS 的全部解码 mip，去掉小尺寸压缩层的块填充后逐层上传；法线通道变换及不透明用途处理覆盖所有层，恒定透明度判断也覆盖全部层。`Bridge.cpp` 新增验证完整 mip 链的上传入口，旧单层入口保留；无效长度/层数不会占用纹理槽。遮罩合并图集仍只读取基础层，不伪称其全部 mip 已重建。
- 固定版本 Effekseer 的普通线性采样器实际只选一层 mip。`CompileMaterials.cpp` 使用同版公开着色器生成器，为十二种共用候选材质生成按未取整 LOD 连续混合相邻原始层的像素采样，保留寻址和 sRGB 规则；未修改固定依赖源码。普通内置回退通道仍选择单层，边界已写明。
- 修改范围为以上共享渲染/纹理路径、对应原生与托管参数、曝光和共享纹理测试、现有中文提示、`docs/architecture.md` 及本交接。原模型、原贴图、原始 Pack、方向/深度/坐标约定及全局 `1.0 / 120` 未改，没有增加其他特效能力。

### 本轮原生证据与适用边界

- 完整原版与 Mod 的新 Terry 录制为 `native-uniform-gain-original-full/`、`native-uniform-gain-mod-full/`，各约 12 秒（200、195 帧），并分别捕获 `terry_frame4945.rdc` 和 `terry_frame3313.rdc`。使用原模型/原贴图、固定视距 12、俯仰 0、FOV 60、根与相机 Y=12，既有坐标反射不变。实际 GPU 视口为 682×654，资源底图 1707×1067；WGC 的 1024×980 是系统 DPI 后的窗口图像，不能混作内部采样尺寸。地面和选择框仍存在，不是纯黑全屏。
- GPU 常量直接确认阈值 4、四类强度均为 0.1、亮度 1、Burn 0.05。`native-pyramid-verification/verification.json` 用实际原生 HDR/高亮输入重放五级过滤，与原生泛光纹理的最大/平均差分别约 0.00293 / 0.00000155（原生峰值约 1.52832）；提取阶段最大差约 0.00098。它验证过滤链与半精度误差范围，**不证明整个粒子材质或完整动画一致**。
- `native-core-gpu/` 固定同一原始 `charge_glow_orb` 及 `glow_cells_a_diffuse.dds`，仅控制位置、尺寸、寿命、颜色与透明度。`core-mip-source/verification.json` 证明原生全部 11 层 BC7 数据与原 DDS 逐字节一致。实际像素和邻像素公开调试记录在 `gpu-core-pixel/`、`gpu-core-pixel-neighbour/`；相邻 UV 推得 LOD 约 3.439，原生采样约 0.83057，原图相邻两层混合约 0.83025，而只读基础层约 0.96480。见 `core-mip-sampling-evidence.json`。这是采样根因证据，不是截图拟合亮度倍率。
- 原生 GPU 中默认粒子先在半尺寸目标绘制，再经过带深度的透明合成还原。`half-resolution-mip-diagnostic.json` 保留同源诊断：当前完整分辨率产品线性峰值约 6.35262，原生约 5.33594，单纯半尺寸产品加双线性放大约 5.12916，但空间亮度轮廓仍不一致。**半分辨率诊断没有进入产品，也不等于原生透明合成已实现**；不能仅为峰值更接近而套模糊或统一压暗。
- 最终产品固定相机证据为 `bloom-mip-product-core/`、`bloom-mip-product-original-full/`、`bloom-mip-product-mod-full/`，完整来源各九个时刻，加载的 Core/原生库散列与最终正常 Debug 一致，见 `final-runtime-verification.json`。`full-sample-mip-review/` 保留与新原生录制的外观对照。双方随机状态和时间相位独立，爆发范围、弧线形状和环境密度仍有差异，不能逐帧宣称对齐。

### 构建、回归与普通窗口新验证

- 最终原生 Debug 构建 `build-mip-native-v4.log` 退出 0；测试项目最后重编译 `build-mip-green-v3.log` 退出 0、0 警告、0 错误；正常主程序 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 退出 0、0 警告、0 错误，见 `build-app-mip.log`。较早完整编译有 1902 条既有警告，不以增量零警告否认该记录。
- mip 缺陷先红后绿：`mip-red/` 四个有效样例都错误读取基础层；最终 `mip-green-v3/` 为 **5 通过 / 0 失败 / 0 跳过**，覆盖压缩/未压缩原层、整数/分数 LOD、线性颜色混合与无效上传隔离。最终材质、纹理、曝光、泛光、深度及天气相关回归 `mip-material-final/` 为 **275 / 0 / 0**，退出 0，约 1 分 42 秒。含前述五项，不相加。
- 泛光完成、mip 修复之前的 `bloom-final/` 为 **78 / 0 / 0**，是本轮较早结果，不与最终 275 项相加或冒充另一套完整验收。GPU 回归均使用 `ASSETEDITOR_VFX_HEADLESS_TEST=1`；没有运行全解决方案、Release、CI 或桌面输入测试套件。测试通过只证明对应契约与像素行为。
- 最终普通 Debug PID 14772 从正常输出路径运行，见 `app-mip-session/launch.json`。四个完整来源各新录制约 12 秒：原版球 259 帧、Mod 金球 227 帧、Mod 环境完整取景 230 帧、Mod 龙雷电 257 帧。证据为 `app-mip-original-window/`、`app-mip-mod-window/`、`app-mip-enviro-framed-window/`、`app-mip-dragon-window/`；复看图与记录在 `ordinary-mip-framed-review/`。环境更近但底缘裁切的 220 帧另保留在 `app-mip-enviro-window/`，不作为完整取景。普通窗口相机与固定原生相机不同，不冒充逐帧对照。
- 普通窗口可见白紫球和金球周围柔光随动画出现、收缩，电弧与龙雷电弯折持续变化，环境滚动弧纹和黄色中心电弧持续运动，网格未整体调亮。没有修改或保存任何来源参数。最终展开看到了含参考泛光/固定曝光的中文高亮诊断；其余更新提示未逐条完成窗口验收。四个只读来源标签保留，最后选中原版球播放，诊断区收起。
- mip 修改前的正常 Debug 四样本录制和 `ordinary-window-review/` 另保留，不混作最终版。未重做 R56 内存编辑/撤销，也没有用户对最终外观的人工认可。最终 Core SHA256 为 `0b29ca7b270e1940efa58fef8ef26bdfa7aac268af2ee32523a7776e7061ce96`，原生库为 `8f2333a8998f669c0aa7bbdbe7985d74517f696a24bf2ecaeb083ab202f76715`。
- 最终运行日志副本在 `app-mip-session/runtime-observed.log`。游戏资源索引阶段有九条空 `.anim` 文件解析失败，均发生在打开 VFX 之前；不能描述为整份日志无错。本轮未修改或扩大处理这个无关动画索引路径；打开并播放上述 VFX 后没有记录新的 Error/Fatal。

### 失败保留与下一优先级

- 原生 mip 首次构建因 `ps_4_0` 不能映射 LOD 指令失败，随后改为既有显示路径同样使用的 `ps_5_0`。一次 `mip-green/` 在测试项目重编译结束前误启动，读取了旧程序集，已用 `excluded.json` 明确排除。之后等待构建成功才跑的 v2 仍有两个分数 LOD 失败，确认是受采样器取整影响，改用未取整 LOD 后 v3 五项全通过。所有失败日志保留，不能称一次全成功。
- 首次普通原版录制的 ROI 落到参数面板，`app-original-window/excluded.json` 已排除；修正后另录。个别工具路径、解析器和原生反汇编辅助尝试报错，原记录保留。Computer Use 的真实用户输入冲突出现后停止桌面输入，用户明确回复“键鼠已空闲，继续窗口验证”后才恢复；普通焦点/独立模态窗口按重新观察处理，没有丢弃未保存内容或反复抢占键鼠。
- 本轮八次 Terry 会话均已结束；临时预览 Pack 经散列核对保留为各 `session/disabled-after-session.pack`，见 `native-session-preservation.json`，场景与控制资源全部保留。游戏数据目录当前不留下启用的本轮预览包，原始包未改写。最终现场与本轮源码差异见 `workspace-after.json`、`workspace-preservation.json`、`implementation-delta.patch`。
- **继续优先共用渲染的实际外观。** 下一项是原生半尺寸粒子与深度相关的透明合成，先解释同源亮芯轮廓残差，再决定产品实现；不要凭峰值或“更柔”引入全局模糊/倍率。天气分类泛光尚需原生分类遮罩证据；自动曝光、天气调色、多个独立 Effect 的完整合成、完整动画相位/随机状态及环境密度仍未验收。普通回退通道 mip 过滤和合并遮罩图集也是明确剩余边界，不能转向其他能力扩展。

---

> **历史进展（R58，2026-10-21 +08:00）**：原地修复了雷电球的固定曝光输出、遗漏的简单光照覆盖增益和纹理发光形状。原版白球与 Mod 金球恢复亮芯周围的渐变及纹理光晕，已完成新的 Terry 对照和普通 Debug 连续动画检查。**完整原生外观仍未验收：屏幕泛光、自动曝光、天气调色、环境雷电密度和动态相位仍未还原。** 保留当前分支及全部成果，不提交、不推送、不清理。

## R58 本轮结果与继续入口

本节证据相对 `artifacts/vfx-orb-display-r58/`。下方 R57 及更早记录均为历史；R57 的最终显示公式用于本轮实现，其历史常量采样没有冒充本轮重测。

### 现场保护与修改

- 入口只读核对实际路径、`codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区和全部 155 项状态，逐项 SHA256 与 R57 后置快照一致。`workspace-before.json`、`source-backup/` 保存入口；旧正常 Debug 的 278 个文件及原生 Debug 分别在 `runtime-backup/full-debug/`、`runtime-backup/native-debug/`，附散列清单。同步最后几条提示文字前的完整 Debug 另保留在 `runtime-backup/pre-diagnostic-copy-debug/`。工具、采样、失败和中间运行库均保留。
- `FloatSceneTarget.h` 在浮点合成后对 8 位目标应用原生色调公式、gamma 2.2 和默认色彩查找表。已有场景背景先按同一变换逆算再参与合成，未受 VFX 影响的像素保持原 RGBA；无法恢复背景已截断的 HDR。浮点离屏目标继续输出线性值。`Bridge.h/.cpp`、`EffekseerNative.cs` 同步参数/ABI并恢复 DX11 状态。
- `EffekseerVfxPreview.cs` 仅在天气明确关闭自动曝光且手动亮度/Burn 在支持范围时使用手动值；其余使用亮度 1、Burn 0.05 的固定参考。没有将 `target_exposure` 当手动 EV，参考值也不是对全部天气实际曝光的推定。
- `TotalWarVfxConverter.cs` 使默认通道普通面片的简单光照支持到 `emissive=10`，预乘材质可同时保留独立纹理发光。增益只作用于覆盖颜色，不乘到透明度或全部发光。原版短闪光层确实使用 10。
- 同一转换器与 `CoverageMaterial.h` 补回普通默认通道预乘面片的发光形状：先用原纹理发光亮度、原始粒子透明度及饱和后的发光倍率形成权重，再乘粒子色；发光软深度作用于颜色和权重两处。显式其他通道、渐变、模型和贴花不套用此规则。复查发现渐变索引尚未绑定时不能据索引排除，已改为检查解析出的渐变对象，先复现再修复该范围回归。
- 更新现有中文提示及 `docs/architecture.md`。原始模型、贴图、Pack、图集、方向/坐标约定及全局 `1.0 / 120` 未改，没有扩展新特效能力。

### 原生证据及适用范围

- 完整原版与 Mod：`native-original-full/`、`native-mod-full/`。来源、模型、材质及贴图保留散列；视距 12、俯仰 0、FOV 60、视口 1024×980，根/相机一起抬高 12，固定天气、关闭自动曝光与泛光。关闭天空盒后下半部仍有地面及选择框，整幅不是纯黑背景。完整动画的随机与时间相位独立，不能逐帧宣称一致。
- `native-core-fixed/` 保留原始 `glow_cells_a_diffuse.dds` 及原图集，只将粒子固定位置、尺寸、颜色、透明度和长寿命。发光权重修复后，避开地面/选择框的区域平均通道差由约 43.68 降至 3.51，最大残差 10；见 `core-shape-hypothesis.json`、`core-shape-product-result.json`。数值只用于定位，原 mip/过滤尚未对齐，也不能将全部残差直接归因于 mip。
- `native-emission-grey-v3/` 与 `native-emission-reference-v3/` 均使用正确的原生 min/max 曲线。灰纹理与“白纹理、粒子色为线性灰度平方”的等效输入在指定区域得到相同 RGB 59；见 `native-emission-shape-equivalence-v3.json`。实际编译 shader 与可读库作为佐证，没有使用截图拟合倍率。
- `native-glow10-control/`、`native-glow1-reference/` 使用原 `glow_cells_c_diffuse.dds` 和图集，支持覆盖增益 10。早期“颜色乘 10、发光倍率除 10 普遍保持独立发光”的解释过宽：低于 1 的饱和倍率会改变形状。原件保留，更正在 `native-emissive-equivalence-scope-correction.json`；最终测试使用包含该饱和权重的等效值。
- `native-mod-bloom/` 只将 `bloom_particle_strength` 从 0 改为原版编辑器天气的 0.5；实际环境散列以 `bloom-override.json` 为准，复制来的早期 `conditions.json` 中 Bloom=0 不适用。两段相位未对齐，不能反推泛光核；关闭泛光的原生样本也有大金色光晕。本轮修复的是原纹理光晕和输出，没有补造屏幕后处理泛光。
- 最终产品离屏证据为 `verified-original-full/`、`verified-mod-full/`，实际加载正常 Debug Core 并核对散列，每个有九个时刻。最后渐变范围修正前的 `shape-*/` 与这两组画面逐字节一致，见 `final-scope-render-verification.json`。最终对照图：`review-final-original.png`、`review-final-mod.png`。

### 构建、回归和普通窗口

- 原生 Debug 构建退出 0，见 `native-emission-build.log`。最终 `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 退出 0、0 警告、0 错误，见 `app-build-copy-final.log`。测试项目最后一次相关重编译为 1877 警告、0 错误，见 `gradient-scope-build-green.log`；不能用主程序增量结果否认既有警告。
- 受影响测试项目先构建，再以 Debug `dotnet test ... --no-build --no-restore --filter ...` 验证。曝光/合成/发光/天气/状态专项 **177 通过 / 0 失败 / 0 跳过**（`tests-emission-green2/`），共享纹理与相关模型专项 **45 / 0 / 0**（`tests-shared-material-final/`）；二者在最后渐变范围修正之前。范围修正后相关渐变/发光重跑 **88 / 0 / 0**（`tests-gradient-scope-green/`）。存在重叠，不相加为新全量。最后只同步提示文字，渲染 DLL 散列不变，复用这些结果。
- GPU 测试使用 `ASSETEDITOR_VFX_HEADLESS_TEST=1`，未跑全解决方案、Release、CI 或未授权桌面输入套件。缺失曝光、emissive=10、发光形状及渐变范围错误的先红后绿日志全部保留；旧测试预期/量化输入造成的中间失败也保留，不能称一次全成功。
- 普通 Debug PID 50060 中，本轮新录制四个完整来源：原版雷电球 91 帧、Mod 雷电球 193 帧、Mod 环境近景 454 帧、Mod 龙雷电 545 帧，各约 12 秒。见 `ordinary-window-review/records.utf8.json`、`contact.png` 及 `app-*-window/`。环境初始远景及原版球近景 `app-original-orb-close-window/` 另保留。只改变预览启用、临时天气选择和相机视距，没有修改/保存原资源。
- 白球/金球的渐变与光晕随动画出现和收缩，周围电弧持续变化；环境弧纹、中心雷电和龙雷电弯折也变化，网格背景未整体调亮。环境雷电密度及完整光照仍与原生不同，不能称全部材质问题已解决。窗口相机用于整例运行检查，不冒充固定原生相机。
- 从本轮停用控制 Pack 作为只读参考加载 `weather/aecn_r58/orb.environment`，实际显示固定曝光的中文诊断，见 `app-weather-diagnostic/`；随后清除天气。最后同步其他旧提示前正常关闭无未保存标记的 Debug 并重建。最终普通 Debug PID 51188 已重新只读打开原版完整球并播放，现场见 `app-final-session/current-state.json`；其渲染 Core/原生 DLL 与四样本验证相同。新的高亮/固定曝光中文诊断已实际看到；只读 WGC 再录制 536 帧，见 `app-final-session/diagnostic-capture/`。其他几条提示未在最终窗口逐条展开验收。
- 本轮未重做 R56 的内存编辑/撤销验收，也没有用户对完整游戏外观的最终验收。历史结果不能计入本轮新验证。

### 失败保留及下一优先级

- 首次构建被旧 Debug 占用，保留失败，核对无未保存内容后正常退出再构建，没有强杀。首次 IPC 在加载游戏资源时超时。主窗口 Computer Use 曾报 `foreground window did not report a process id`，停止重复焦点操作后重建工具会话恢复，未关闭应用或丢弃内容；一次对话框坐标越界未触发点击，重新观察控件后完成。失败记录均保留。最终诊断栏拖动被 Computer Use 拒绝，因为落点被输入法 `wetype_update.exe` 悬浮条遮挡；随即停止桌面输入，没有操作其他程序或重复抢焦点，后续只做无输入的采样与文件核对。另一次录制因尚未建立输出目录而失败，随后新建本轮目录完成录制；见 `app-final-session/final-check-events.json`。
- `native-emission-reference-v2/` 的简化颜色曲线未被 Terry 按预期读取，`excluded.json` 明确排除。最终依据是 v3 正确曲线的完整一对控制。早期空采样、辅助资源缺失和其他中间尝试也保留。
- 本轮 11 次 Terry 会话均正常退出，对应临时游戏预览包经散列核对移入各 `session/disabled-after-session.pack`；场景与停用包保留，原始游戏包未改。最终快照、入口差异及保全结果见 `workspace-after.json`、`implementation-delta.patch`、`workspace-preservation.json`。
- **下一步仍优先真实外观。** 将纹理自身光晕与屏幕泛光分开，用固定粒子、相同深度和曝光校准后处理。已找到的 downsample/gaussian shader 不能仅凭名字推定为粒子泛光，不套经验模糊。继续原 mip/缩小采样、环境雷电密度与相位的单变量对照；保留原件、方向/深度和步长，不推进其他能力扩展。

---

> **历史进展（R57，2026-10-21 +08:00）**：继续优先修复真实模型材质。新的 Terry 同源对照确认：`MaterialValue` 纹理忽略 DDS DX10 的显式 sRGB 格式，导致原始遮罩过亮。正式代码已修复并验证正常 Debug 中的完整原版及 Mod 样本。**完整特效仍有白色硬亮核心、光晕、雷电密度及时间相位差异，尚未达到游戏原生水准。** 保留当前分支、所有原成果和失败记录，不扩展其他能力，不清理，不进行 Git 交付。

## R57 本轮结果与继续入口

本节证据相对 `artifacts/vfx-material-texture-r57/`，均为本轮新执行；下方 R56 及更早的测试、原生采样和内存编辑结果只作为历史。

### 现场与实际修复

- 开始时只读核对实际路径、分支 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区及全部 155 项状态，文件散列与 R56 后置快照吻合。接手源码在 `source-backup/`，完整旧 Debug 在 `runtime-backup/full-debug/`，278 个文件有逐文件 `manifest.json`。旧工具、失败记录和采样未清理。
- 本轮产品代码仅改 `EffekseerVfxPreview.cs` 的材质纹理颜色空间选择：显式 sRGB 的 DDS 按 sRGB 上传；旧 DDS 和显式线性格式仍走线性；漫反射与法线的既有用途规则保留。没有更改原模型、原贴图、原 Pack、材质公式、相机约定、原生桥或全局 `1.0 / 120` 步长。
- 新增回归写入既有 `VfxSharedTexturePixelTests.cs`：完整模型/材质/纹理解码/GPU 路径分别覆盖遮罩与颜色叠加槽，并在同一路径反复重载旧 DXT5、BC1 sRGB 和 BC1 线性资源。长期约定更新在 `docs/architecture.md`；本交接保留详细证据与未完成项。

### 根因证据及适用范围

1. **不能把所有数值纹理统一改为 sRGB。** `texture-controls/` 的五槽灰色贴图与白色等效参数对照证明，这批旧 DDS 的原生采样仍是线性；`texture-reading-analysis/result.json` 保留结论。这与后续发现的显式 DX10 格式问题并不冲突。
2. **原始 nagash 模型混用了两类格式。** `winds_of_magic_base_colour.dds` 是旧 DXT5；`wh2_space_stars_base_colour.dds` 与 `vfx_noise_01_base_colour.dds` 原为 DX10 BC1_UNORM_SRGB（72）。`format-controls/` 使用同一原模型、冻结滚动的原材质、原贴图块及完整 mip 数据；两个控制只把后两张 DDS 的格式字段 72 改为 71，其他有效载荷不动。相机、方向、深度、根位置与显式天气固定，缓存使用独立资源别名。Terry 的线性标记控制重现原预览过亮趋势。
3. **采样错误与最终颜色输出分开验证。** 本轮从当前游戏 `shaders.pack` 的实际 `hdr_to_screen` 二进制及 `lut_default.dds` 核实最终显示公式，在明确的亮度 1、burn 0.05、gamma 2.2、关闭自动曝光/泛光等控制条件下，以 16 组常量/透明合成对照验证。证据为 `shader-final-pass/`、`default-display-resources/`、`tone-analysis/`。该公式仅用于离线诊断，**没有加入产品，也不能外推为全部天气、泛光或游戏最终画面的还原**。
4. 固定原纹理模型的上部区域，统一使用上述已核实显示公式后，修复前对原生画面的平均通道差为 50.23，按格式标记读取后为 4.43；修复前对 Terry 强制线性标记控制为 3.80。见 `format-analysis/result.json`。这些数值用于分离根因，不作为完整外观通过标准。剩余差异包括未上传原 mip 链，不能把全部残差直接归因于 mip。
5. `frozen-product-gpu/` 重新加载本轮正常 Debug 的 `GameWorld.Core.dll` 并核对散列：实际产品输出与已校准诊断候选的 RGBA32F 文件完全相同，见 `verification.json`。原模型和原纹理未替换。**最终显示、完整动态相位和游戏随机状态仍未对齐。**

### 本轮构建、回归和普通窗口

- 先构建受影响测试项目，再运行新回归。第一次测试夹具漏写材质参数 `type`，导致两项空引用失败，保留于 `texture-red/`；修正夹具后的 `texture-red2/` 才是缺陷复现：两项都得到错误线性值 0.517647，应为 0.230740。
- 修复后 `dotnet build Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj --configuration Debug --no-restore` 退出 0，有 1882 条编译警告、0 错误；随后同配置 `dotnet test ... --no-build --no-restore` 的纹理、相关模型材质、失效隔离和重载专项为 **75 通过 / 0 失败 / 0 跳过**，见 `texture-green/` 与对应日志。GPU 离屏环境开关为 `ASSETEDITOR_VFX_HEADLESS_TEST=1`，没有运行桌面输入测试或全解决方案测试。
- `dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore` 退出 0，0 警告、0 错误；使用正常输出目录启动，并通过普通 IPC 与 Computer Use 只读打开原资源。四个完整样本各录制约 12 秒，见 `ordinary-window-review/records.json`、`contact.png` 和 `app-*-window/`：Mod 环境、Mod 龙雷电、原版雷电球、Mod 雷电球。未修改或保存任何样本参数，**本轮没有重做 R56 的内存编辑/撤销验收**。
- 普通窗口确认环境滚动弧纹、闪电和光束持续变化，龙雷电有不同弯折阶段，原版/Mod 雷电球的周围电弧持续变化。原版仍有白色硬亮核心；Mod 黄光球及环境雷电密度/亮部仍不等于 Terry 原生。窗口取景用于整例运行检查，不能冒充固定相机的原生逐帧对照。
- 本轮正常 Debug Core SHA256：`32b5d8d14f3611e9923a1e2abf1cddac711e5a2ba3621536b98e7fd413f8a16a`；原生 DLL 仍为 `80f4685a4565752ea2bd094fc686adabd71001a8ad4ada8203c485e7eb3aa3d0`。应用启动身份及运行库记录见 `app-session/launch.json`；当前窗口和未保存状态须在下一次操作前重新核对。

### 保留的失败与下一步

- `frozen-nagash/` 首版材料副本原生未出现，后续改用无 XML 声明的副本才能显示；未证明唯一解析原因。`nagash-original-pan/` 未对齐滚动相位；`tone-controls/` 首次录制错过低亮度时段，`tone-controls-capture2/` 才覆盖全部控制。早期“隐藏天空盒”等于黑背景的假设不成立，`frozen-black/` 与 `format-controls/` 另将实际天空渐变置零。所有原始失败、纠正和控制副本保留。
- 产品验证辅助程序首次因沿用旧 Core 被散列检查拒绝，日志 `product-frozen-gpu.log`；保留旧 DLL 后同步当前正常 Debug Core，第二次验证成功。旧辅助目录没有 PDB，备份 PDB 的尝试报缺失；DLL 原件已保留，不影响后续 GPU 结果。不得把这些尝试记为一次全成功。
- 九次本轮 Terry 会话均已正常退出，对应临时游戏预览包经散列核对转为各 `session/disabled-after-session.pack` 恢复副本；原始 Pack 未改写。Debug 程序保留打开四个只读样本供查看；当前快照见 `app-session/current-state.json`。不清理任何成果。
- **仍先处理明显外观错误。** 下一步围绕原版雷电球白色硬亮核心、环境雷电密度及最终颜色/光晕继续同源控制。已找到并验证受控条件下最终显示公式，但共享场景已有自身明暗处理，不能直接再给整幅场景套一次公式，也不能恢复统一调亮或逐粒子经验倍率。
- 保留对原 mip 链、纹理缩小过滤及精确滚动相位的调查；未经对照不能因“细节更像”直接改 UV 或时钟。完整天气/自动曝光、泛光、透明排序、随机种子和完整原生动画仍未验收，不推进其他能力扩展。

---

> **历史进展（R56，2026-10-20 至 2026-10-21 凌晨 +08:00）**：用户已明确要求“按照交接文档来开始继续开发 VFX”，覆盖下方历史暂停状态。本轮修复共同模型材质的背面剔除、模型相机距离可见性和持续渲染下预览重载被阻塞的问题，并做了新的 Terry 对照、普通 Debug 窗口及内存编辑验证。**完整外观仍未达到游戏原生标准，不能把本轮修复、测试或录制称为全部材质问题已解决。** 不扩展其他能力，不清理，不进行 Git 交付。

## R56 本轮结果与继续入口

以下证据均相对 `artifacts/vfx-wh-mesh-fidelity-r56/`；是本轮新执行的结果。下方 R55 及更早的结果仍为历史，不计作重测。

### 现场保护

- 接手时实际路径、分支 `codex/vfx-effekseer-preview`、HEAD `7c1b2a1ab8517e6bdc41efa79de2e9364761814b`、空暂存区和全部 154 项状态均与旧交接吻合，154 个文件 SHA256 逐一一致。没有新建或切换分支/worktree、暂存、收起、回滚、清理或 Git 交付。
- 154 份接手源码与 5 份原运行库备份保留并复核散列。完整旧 Debug 在 `runtime-backup/full-debug/`（278 文件、164300969 字节），旧原生构建在 `runtime-backup/native-build-all/`，调度修复前 Debug 在 `runtime-backup/post-distance-pre-scheduling/`。完整 Debug 未另建原始逐文件散列清单，不能把文件数/字节数复核称为全目录历史散列验收。
- 本轮未改 `AGENTS.md` 或其他既有成果；新增 `VfxModelDistanceTests.cs` 后为 26 个已跟踪修改、129 个未跟踪文件，共 155 项。后置快照及本轮差异见 `workspace-after.json`、`workspace-preservation.json`、`implementation-delta.patch`。
- 全局步长保持 `1.0 / 120`。原始模型、贴图、来源 Pack 未保存或改写；控制副本、临时包停用副本、工具、失败构建和采样全部保留。

### 修复及证据边界

1. **共同 `wh_vfx_mesh` 的背面剔除。** 原 shader 声明 `CULL_MODE_CCW`，原预览的 49 参数滚动网格却走双面，背面可能再次叠加透明颜色。`Bridge.cpp` 改为单面并沿用投影反射约定，同时覆盖 colour 变体；未替换模型、纹理、UV、顶点颜色/法线或曝光。`control-spiral/`、`control-curve/` 是同原模型与原材质贴图、固定单粒子和视角的 Terry 控制；`product-*-before/`、`product-*-culling/` 保留修复前后。它支持剔除错误的修正，**不证明动态相位、HDR、光晕或完整雷电已还原**。
2. **模型相机距离可见性。** Mod 环境 `nagash_circle_top` 的原始 `ParticleCullDistances=(30,100,150,300)` 在近距离 Terry 中应隐藏；原预览忽略该参数，近处多出蓝色环状模型。`TotalWarVfxConverter.cs`、`TotalWarVfxModel.cs`、`TotalWarVfxSimulation.cs` 现在读取模型四段距离，按世界相机到粒子距离处理近/远隐藏及中间透明度，不销毁粒子、不改变时钟和随机状态。
   - 六组控制位于 `control-top-near-sustained-v2/`、`control-top-near-sustained-no-cull/`、`control-top-near-sustained-mid-cull/`、`control-top-sustained-no-near/`、`control-top-sustained-hard-near-ten/`、`control-top-sustained-far-mid/`。全部 7 个原资源散列一致，副本仅距离字段不同。
   - 原距离近处隐藏；取消近端后可见；近/远中点的整段背景扣除蓝色贡献约为完全可见控制的 0.534/0.521。最初单帧曾把暗滚动相位误判为完全消失，整段复核已纠正，记录保留。详见 `native-model-distance-evidence.json`。
   - 支持隐藏及渐淡方向，中间采用线性候选，**未证明完整 HDR 曲线或全距离精确一致**。仅模型粒子，未扩展面片或发射器 `CullDistances`。
   - 新运行库 `product-top-distance-after/` 近处隐藏，`product-top-far-after/` 远处恢复可见，粒子仍存在；后者是产品相机检查，不是新 Terry 采样。`product-enviro-distance-after/` 的近处错误蓝环消失，其他外观差异仍在。
3. **持续渲染阻塞预览重载。** 普通 Debug 中环境预览开启后，第二个雷电标签只有网格、没有诊断；关闭环境预览后立即加载。`VfxEditorViewModel.cs` 的 300 毫秒 Background 计时器被持续 Render 工作饿死；现只改为 Normal 优先级，保留合并修改间隔、3D 帧循环及全局步长。`VfxEditorTests.cs` 使用统一 WPF 宿主、真实计时器和持续排队的渲染工作复现并验证，修复后能发布内存快照。

### 本轮验证

- 背面剔除先红：4 项中 2 失败；修复后相关原生材质专项 **61 通过 / 0 失败 / 0 跳过**，见 `culling-red/`、`culling-green/`。
- 模型距离先红：9 项中 7 失败；修复后转换、模拟、模型、事件、引用及相关材质专项 **99 / 0 / 0**，见 `distance-red/`、`distance-green/`。新增 9 项在 99 内，且与前述 61 有重叠，不能相加宣称新全量。
- 共享 Effect **3 / 0 / 0**，见 `shared-distance/`；调度先红 **0 / 1 / 0**，修复后调度和共享内存文档专项 **3 / 0 / 0**，见 `ui-scheduling-red/`、`ui-scheduling-green/`。
- 原生 Debug 构建成功；最新正常主程序 Debug 构建 **0 错误 / 0 警告**，见 `native-culling-build.log`、`app-scheduling-build.log`。其他测试项目重编译仍有既有警告，日志保留，不能用末次增量构建否认它们。未跑整个解决方案全量、Release 或 CI。
- 普通 Debug 的四个完整来源，各新录制约 12 秒：Mod 环境 `app-enviro-scheduling-window/` 83 帧；Mod 龙雷电 `app-dragon-scheduling-window-v2/` 217 帧；原版雷球 `app-original-orb-scheduling-window/` 199 帧；Mod 雷球 `app-mod-orb-scheduling-window/` 202 帧。四标签预览保持开启，后开标签已加载并播放。`ordinary-window-contact.png` 展示形态变化；**帧数、marker 数和层数不是外观通过门槛**。
- Mod 雷球第一个引用 X 在内存由 0 改为 3，雷电部分随之偏移；随后编辑器撤销回 0，未保存标记消失，未点击保存。见 `app-mod-orb-live-edit-x3/`、`app-mod-orb-live-edit-undone/`、`live-edit-contact.png`。各段相位独立，不作逐帧相等比较。
- **明确排除**：`app-dragon-distance-window/` 是修复调度前的空白失败录制；`app-dragon-scheduling-window/` 的截图标识失效、预览未开启，写有 `excluded.json`；更早 `app-dragon-culling/` 的 ROI 不适用于完整窗口。均保留，不计作有效显示验证。

### R56 后续原参数分层对照（本轮新验证）

- 继续隔离了完整 Mod 环境的四个原始层，保留各层原参数、原模型和原贴图：`product-enviro-layer-nagash_circle/`、`product-enviro-layer-core_lightning/`、`product-enviro-layer-pyramid_beam_top_large/`、`product-enviro-layer-pyramid_beam_top_medium/`。上方白弧来自 `nagash_circle`，黄色密集分支来自 `core_lightning`，两个顶部光束层主要贡献中心亮点；没有据此调统一亮度或修改更多产品代码。
- `native-enviro-layer-nagash/` 与 `native-enviro-layer-lightning/` 分别完成新的 Terry 原参数单层录制（约 12 秒，121/117 帧）。两边隔离输入字节 SHA256 完全相同，相机距离 24、俯仰 55°、根高度 0、FOV 60°。原始完整来源 SHA256 为 `7cd4a717ac67fcd8c12fa224caff220c1f767ad969556d5ff68fdefd82789fc5`。完整粒子随机、材质相位、背景及最终曝光没有对齐，不能逐帧比较或反推精确数量/亮度。
- `native-enviro-layer-nagash-raised/` 与 `product-enviro-layer-nagash-raised/` 另外将根和相机一起抬高 12，角度、视距、隔离输入和原生预览包保持相同；原生新录制约 12 秒、122 帧。两组原生模型都能在整段中辨认很淡的灰弧，不能说模型未加载或据单帧判断运动方向。第一组采样后曾关闭 Skybox，但草地仍在，没有取得可称为暗背景对照的录制。
- 黄色雷电在 Terry 中较粗、较疏，产品中较细密；该来源三个画质档位的发射率曲线、图集及速度对齐参数相同。这里需进一步分别控制图集、分布和拉伸，**尚未确认新的单一根因，不把亮度调整当成修复**。白弧最终明暗、完整环境中心叠加、雷球光晕仍未通过原生外观关卡。
- 对照图与记录：`original-layer-comparison.png`、`original-layer-raised-comparison.png`、`original-layer-isolation-evidence.json`。图片保持原录制颜色，仅按共同区域裁剪排版，所列帧的时钟独立。工具的 `no-detected-markers` 不代表无可见效果，也不作为外观判据。
- 新建的隔离导出工具第一次因缺少运行库 `Shared.Core` 启动失败；失败说明及工具运行库补齐前的副本保留在 `layer-native-tools/`，原工具未覆盖。三次新增 Terry 已正常退出，各自预览包均移入 session 停用备份。此前 16:09 UTC 的阶段快照、交接文本及检查结果保留在 `stage-before-layer-isolation-review/`；后续刷新快照不表示重跑之前的测试或构建。

### 未通过项与继续优先级

- 完整 Mod 环境仍中心偏亮、雷电分布比 Terry 密，顶部白色弧形模型叠加和运动未对齐；原版雷球仍有较硬亮白核心，Terry 紫色光晕范围不同。龙雷电的随机分布、滚动相位和最终亮度未精确匹配。**没有用户外观验收，严重材质问题尚未全部解决。**
- `nagash_circle` 在 Terry 不是完全缺失：原始亮度低，关闭该测试场景天空后能辨认暗圆柱；固定单粒子的原生和产品均有暗部形态。不能根据亮背景下单帧看不到就断言未加载。
- 下一步继续限定真实样本外观，沿用上方已定位的原始层，对 `nagash_circle` 的遮罩相位/最终颜色，以及 `core_lightning` 的图集、分布与拉伸做单变量原生对照；继续处理雷球合成与光晕差异。保持同源件、视角、方向和深度约定，不调统一增益掩盖问题，不改全局步长，不先推进其他能力或全库有像素统计。
- 当前正常 Debug 的 Core SHA256 为 `b506c0f0e22c0019d5bb82a31e3b9cd11ffb9876676b8cbc839377b45792473e`，原生 DLL 为 `80f4685a4565752ea2bd094fc686adabd71001a8ad4ada8203c485e7eb3aa3d0`，VFX 编辑器 DLL 为 `6e5fa919cc1784394bead68dea6cc81142f8c24245b8febb4ab2fbce6f9feb0c`。
- 最后启动的 Debug PID 26680、UTC `2026-10-20T15:50:50.3313262Z`，归属见 `app-scheduling-session/launch.json`，留有四个只读来源标签。继续前仍须实时检查 PID、路径与未保存状态。该记录初次用了错误 DLL 名，`launch-native-name-error.json` 保留，随后按真实 `AssetEditor.CN.Effekseer.dll` 修正散列。
- 本轮 Terry 均正常退出，归属预览包移入各 session 的停用备份；游戏 `data/asseteditor_cn_vfx_preview.pack` 已确认不存在。保留 Debug 和后台 MSBuild 服务，无强制结束、清理或丢弃未保存内容。

---

> **历史暂停交接（2026-10-20，19:23 +08:00，已由上方继续授权覆盖）**：用户当时要求“先别搞了，做个交接文档，我要开启新的对话用 Astra 来做了”。当时仅整理交接，不清理、不归档、不进行 Git 交付；模型材质尚未修复或验收。以下保留历史现场和证据，不能替代上方 R56 状态。

## Astra 接手先读

### 当前问题与推进纠正

- 用户目标是尽量还原游戏原生效果，并覆盖原版和“神话人物”Mod，不是仅让少数示范样本出现像素。用户已多次在本任务启动的 Debug 程序中复测，报告大量特效不显示、不完整、动画不同；最新明确指出模型贴图材质“完全就不正确”。不要归因于用户打开安装版。
- 此前把受控数值测试通过、转换层数增加及存在像素作为继续扩展下一项的依据，没有完成真实模型材质外观关卡。这里的推进标准有误；**数值通过不代表用户看到的效果正确**。最新投诉的具体材质根因仍未确认，不能声称已经修复。
- 暂停新的模型动画、地形等能力扩展。下一步先对最近的雷电、龙类、环境来源做同模型、原贴图、相同视角的 Terry/产品对照，修共同模型材质路径，再验证完整原始样本的普通 Debug 预览。不要以白材质替代、修改源件、统一曝光增益或有像素计数代替这项工作。

### 原地现场与保护范围

- 项目绝对路径：`D:/Project/Szy-Cathay/TheAssetEditor-CN`。目录当前不是联接。
- 分支：`codex/vfx-effekseer-preview`；HEAD：`7c1b2a1ab8517e6bdc41efa79de2e9364761814b`。暂存区为空；盘点为 **26 个已跟踪修改、128 个未跟踪文件，共 154 项**。成果主要未提交，且工具、采样、构建输出在被忽略的 `artifacts/` 中。
- **不创建替代分支或 worktree，不切换、不暂存、不收起、不覆盖、不回滚、不清理；不提交、推送、PR 或合并。** `AGENTS.md` 是接手前已有的修改，本阶段未改它；不要把它恢复成 HEAD。
- 全局模拟步长仍为 `1.0 / 120`，`TotalWarVfxSimulation.cs` 的字节与 R55 基线一致。不要通过修改全局步长修外观或时序。
- 本次交接只修改本文件，另新增交接盘点记录，产品源码不变；原文已备份到 `artifacts/vfx-handoff-astra-20261020-1920/HANDOFF-VFX-LOCAL.before.md`。完整逐文件状态、大小、SHA256 见该目录 `workspace-before.json`，后置核对见 `workspace-after.json`；保留原始 `git-status-before.txt`。这些记录是接手核对依据，不是可以恢复覆盖用户工作的一键清单。
- R55 的 151 份既有源码备份和 5 份运行库备份本次重新核对散列全部有效。所有早期失败采样、旧版本、原始帧、临时包的停用副本、工具和构建产物全部保留，无清理授权或清理行为。
- 19:23 进程只读查询未发现 AECN、Terry、capture、dotnet、MSBuild、VSTest 或 testhost 匹配进程；未执行关闭程序的动作。游戏 `data/asseteditor_cn_vfx_preview.pack` 当前不存在。旧 PID/窗口句柄仅是历史记录，不能直接重用。
- 可运行 Debug 产物仍在 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`，未覆盖安装版。当前 Core SHA256 为 `372015d771b42d5b264954d201a50cebf79557fc71a6abfd5ff3c324d80031f9`，原生 DLL 为 `f246e831498ffa9e5fb748e82b005fb895bfe654700cf8db7506490cc9706dbf`；主程序散列和绝对路径另存快照。
- 旧最终 Debug 由本任务启动，PID 42156，启动 UTC `2026-10-20T11:02:18.8748940Z`，原 HWND 5900416；依据在 R55 `app-final-session/launch.json`。用户随后在此实例打开过下列三个来源，后来的 UIA 树显示标签已空；交接时未再操作 GUI，未确认未保存编辑，不能据历史状态重放关闭或打开操作。

### 最优先的真实来源与共同材质

游戏目录为 `E:/Steam/steamapps/common/Total War WARHAMMER III/data`；三个来源均在该目录 `@cth_shenhua_asseteditor.pack` 中。源件及原始模型/贴图不应改写。

| Pack 内虚拟路径 | 当前定位事实 | 已有离线证据（相对 R55 目录） |
| --- | --- | --- |
| `vfx/animation_hu1e_yj_entity_storm_mage_lightning_charge_orb.xml` | 通过 `wh3_main_lib_entity_cth_storm_mage` 引用选层；实际递归得到 3 个发射器，其中模型层 `lightning_hand_charge`，另两个是面片 | `recent-storm-orb/`：完整原文、资源字节与 SHA、12 个时间点图片、`results.json` |
| `vfx/animation_hu1e_yj_entity_strom_dragon_lightning_strikes.xml` | 注意原文件拼写是 `strom`；通过 `wh3_main_lib_entity_6` 得到模型层 `lightning_strikes` | `recent-dragon-lightning/`：同上；首粒子约在采样 t=0.1 时出现，早期零帧不等于永久不显示 |
| `vfx/animation_hu1e_yj_lib_campaign_enviro.xml` | 15 个直接层进入候选，其中模型层 `nagash_circle`、`nagash_circle_top`；计数不是外观通过 | `newly-opened-source-diagnostics.json`；尚无本轮独立整例窗口录制或新的同源原生材质对照 |

前两个模型分别为 `vfx/models/wh3_storm_dragon_lightning_curve01.wsmodel` 和 `vfx/models/wh3_storm_dragon_lightning_spiral.wsmodel`，却共用 **`vfx/materials/wh3_storm_dragon_lightning_spiral.xml.material`**，材质字节 SHA256 同为 `d54daf8c140903b1dbb481794b79168525aff6a4b9659a1f1b8e63a703ec1299`。现有转换诊断为 `ScrollingMeshCandidate`，走 **`wh_vfx_mesh` 滚动模型材质**。这提供了优先查共同路径的具体入口；**R55 新加的 soul-panning 并不能证明该材质正确。**

最近两个离屏诊断使用 1024×980、距离 24、俯角 55°、根高度 0、FOV60；完整时钟、随机种子、光照并未对齐 Terry，不能直接把图片差异判为唯一材质因果。优先冻结几何、方向、深度、相机和输入，再隔离真实原材质的颜色、遮罩、UV/滚动及透明合成。以下只是待验证方向：纹理槽位与 sRGB/数值读取是否匹配、UV 约定、顶点颜色、材质公式、HDR/透明混合。**未确认具体根因，不要把这些猜测写成结论；soul-panning 的灰度证据不能外推成所有模型贴图都按线性读取。**

直接入口为 `GameWorld/View3D/Vfx/VfxScrollingMeshMaterial.cs`、`EffekseerVfxPreview.cs`、`TotalWarVfxConverter.cs`，以及 `GameWorld/Vfx.Effekseer.Native/ScrollingMeshMaterial.h`、`Bridge.cpp`、`CompileMaterials.cpp`。先读当前代码和随游戏资源的原始 shader 契约，不只读本文。两个 `recent-*/results.json` 给出 Pack 内资源路径到 `.data` 文件的对应散列。

**重要排除项**：全库 census 只统计源文件直接声明的层，不展开引用；引用组合在这份表中直接层为 0，并不表示实际运行时没有递归加载。上述球体实际有 3 层、龙雷电有 1 层。不要把 `newly-opened-source-diagnostics.json` 的 Direct/Mapped=0 当作新的引用失效根因。

### R55 已实现内容及证据边界

下列路径均相对 `artifacts/vfx-soulpanning-material-r55/`。本次交接**仅复核已有记录，没有重跑构建、测试、Terry、离屏或普通窗口采样**。

- 实现：`vfx_soulpanning` 及其 w2/w4 分支的四纹理、双噪声、原始 UV 渐变、底图 alpha 平方根、带符号自发光/偏置、粒子颜色与透明度、单面视角响应及近零覆盖的加法 RGB。新增源件为 `VfxSoulPanningMaterial.cs`、`SoulPanningMeshMaterial.h`、`VfxSoulPanningMaterialTests.cs`，另改共享转换/桥/材质编译器、中文诊断及相关测试/架构说明。没有改变公开 ABI、固定依赖源码、全局步长。
- 初次错误地禁止负自发光，曾使 300 份队列中的 7 份丢映射；失败在 `sweep-full/` 和 `corpus-comparison.json` 保留。随后依据原生减暗控制移除此限制，负值有效失败回归 8 项已修复。不能只引用第一次扫描作为最终状态。
- 恒定参数 6 对、中间灰度 4 对、正负参数 3 对的同位置原生控制，分别见 `native-scalar-acceptance.json`、`native-gamma-acceptance.json`、`native-signed-acceptance.json`，误差依次为 `[0,0,1,0,0,0]`、`[0,0,0,0]`、`[1,0,1]` 色阶，均在预定每通道 3 色阶内。最初两组不可见平面已排除，后续用原生确认可见的反向绕序平面；失败尝试仍在 `material-native/`、`material-native-lod/`。
- 这些控制支持有限数值公式，不证明原始模型完整外观。动态滚动相位、世界 UV、非零场景深度淡出、雾、曝光、普通 `wh_vfx_mesh` 外观、完整时序/随机数仍未验收。
- 现有 11 个编译材质字节均保持不变，新 soul 材质在索引 11。依据为 `compiled-materials-check.json`；旁边的 `.invalid-empty-parser.json` 是已排除的空解析器结果。
- 材质专项 **24 通过 / 0 失败 / 0 跳过**（`signed-green/green.trx`）；最终 VFX Debug **979 / 0 / 0**（`signed-final-regression/regression.trx`）；共享 Effect 另 **3 / 0 / 0**（`shared-signed-final/shared.trx`）。专项包含于全量，不相加。不是整个解决方案全量测试。
- 编辑器先 **35 通过 / 0 失败 / 2 跳过**（`ui-tests/ui.trx`，缺真实样本环境），设置路径后只补跑这 2 项并全部通过（`ui-game-tests/ui-game.trx`），不能说一次新跑 37 项全部通过。中文四主题诊断渲染包含在已有范围内。TRX Counters 的 notExecuted 没有计入这两个跳过，须看 UnitTestResult 和 `ui-tests.log`。
- 最终主程序普通 Debug 构建为 **0 错误 / 0 警告**（`app-final-build.log`）；其他中间失败、警告、跳过和命令筛选错误均保留，不能借最终增量构建把旧警告称为不存在。
- `sweep-final/` 的 300 份同散列来源已在最终运行库重新检查：有像素仍 278，22 份最佳图变化，无旧映射层或整例像素丢失（`corpus-final-comparison.json`）。全部 58 份受影响来源中 57 份在 0～7 秒有像素。剩余原版 `wh3_lib_dlc23_camp_magic_haze.xml` 含 0～200 秒随机启动，晚期另有像素证据，但该晚期检查使用符号修复前、此正强度分支未变化的运行库，属于复用，不伪装为最终版重跑。**278/300 和 57/58 都不是完整显示率，更不是原生还原率。**
- `census-final/` 对比 7,634 份原版/Mod 有效定义的源散列不变，无直接映射层丢失（`catalog-comparison.json`）；直接 15,479 层中 12,777 候选、954 原件停用、1,748 阻断。不是全库完整外观验收，不展开全部递归引用。
- 普通 Debug 最终录制完整 Mod `vfx/wh3_dlc22_lib_yangjian.xml` **586 帧**，原版 `vfx/wh3_dlc22_lib_ulrika.xml` **588 帧**，各约 12 秒，依据在 `app-yangjian-final/`、`app-ulrika-final/`，联系图为同名 `*-contact.png`。早期闪光和后续流动束状模型可见；界面各 10/11、原件停用 1。**没有通过模型材质外观、消失周期或原生时序验收。** WGC 摘要的颜色标记计数是分析辅助，EmptyFrames 不能当作所有特效为空的帧数。
- 另一个暗部样本 `vfx/animations_hu1e_cg_lord_masque_attack_slash.xml` 的 IPC 返回 ok，仅证明请求被接收；未录到或确认普通窗口画面，不算验证成功。
- R54 的模型粒子 X→Y→Z 旋转修正仍保留；两模型控制严格 2 像素欧氏轮廓门限尚未全通过，最大为 2.236 像素。不要把它写成完整几何精确通过。

**未完成的汇总文件**：R55 `tools/summarize-r55.py` 已写但未执行，`validation-summary.json`、`workspace-preservation.json`、`implementation-delta.patch`、`app-final-session/current-state.json` 在本次盘点时均不存在。旧脚本还硬编码“继续下一共同缺口”的 Next，已被本次用户优先级否定，且依赖缺失的 GUI 状态文件；不要直接运行或补写虚假的 GUI 成功记录。本次交接另在 `artifacts/vfx-handoff-astra-20261020-1920/existing-evidence-review.json` 记录了实际回读结果。

### 工具、构建与授权交接

- 先读本仓库 `AGENTS.md`，再读 `docs/architecture.md` 的“VFX 可视化编辑”和“场内预览的验证边界”；涉及共享 Effect 或 UI 时按规则读相应现有文档。第 1～11 节和下方 R54 以前的记录都是历史，旧“当前”“下一项”及旧清单不能代替最新快照。
- 公开 Effekseer 固定源码在 `artifacts/effekseer-source/`，tag `170e`，提交 `92fdeb349c0c7eab9dfaf152047f22dec2fa102d`；不要恢复旧游戏私有 DLL 路线。原生构建入口 `GameWorld/Vfx.Effekseer.Native/Build.ps1`，普通运行使用正常 Debug 输出。
- R55 `render-tools/bin/Test.GameWorld.Core.dll` 是实际转换/模拟/GPU 诊断器，`library-tools/` 用于固定来源扫描和原生准备，`catalog-tools/` 的 census 只统计直接声明。先读各自 `Program.cs` 确认参数；输出模式目录已有时不要覆盖旧证据。
- 工具通常是 `dotnet <工具DLL> <仓库> <mode> <SOURCE> <PACK> ...`；`render-tools` 对普通源件的相机与 12 个采样点见上述 results。R55 `render-runtime-hashes.json` 与 `tools/sync-render-runtime.py` 用于核对普通 Debug 依赖。不要把 `--output` 构建导致的另一套 Core MVID 混入产品；工具改动采用既有 `-p:BuildProjectReferences=false` 方式并核对实际加载散列。现有结果不能在换 DLL 后继续标成新 DLL 结果。
- R55 `tools/Start-Native.ps1`、`tools/Capture-Loaded.ps1`、`tools/Finish-Native.ps1` 沿用确切进程/路径/临时包散列保护；各 `*-native/` 目录保留 prepared、帧、关闭和停用包记录。不要直接重放历史 PID、HWND 或开启遗留临时电影包。
- GPU 回归曾使用 `ASSETEDITOR_VFX_HEADLESS_TEST=1`、`ASSETEDITOR_VFX_GAME_DATA=E:/Steam/steamapps/common/Total War WARHAMMER III/data`；真实样本 UI 测试还使用 `ASSETEDITOR_VFX_SAMPLES=<仓库>/artifacts/vfx-samples`。缺环境导致的跳过必须明确，不为了累积数字重复跑未变化的完整队列。
- PATH 上 Python 是 Store 入口；本次可用 Python 为 `C:/Users/热心市民石先生/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`，有 numpy/Pillow。只读进程快照第一次因 Windows PowerShell 不支持 ConvertTo-Json `-AsArray` 失败，改用 `-InputObject @()` 成功；错误说明已保留，未执行 GUI 或产品操作。
- 此任务此前已获 Debug、Terry、采样和 Computer Use 授权，用户也明确不希望因普通焦点提示反复询问桌面是否空闲；但**当前“先别搞了”优先，交接本身不启动任何窗口或采样**。新对话收到继续指令后，按该指令范围执行，不擅自扩大到 Git 交付或清理。保存提示、无法确认未保存内容时仍不能强退或丢弃。
- 本次实际桌面工具路线是已读取的 `computer-use:computer-use` Skill、`mcp__node_repl.js` 加 `@oai/sky`；原生 CUA 接口当时不可用。最后出现过 screenshot 与 UIA 所属窗口不一致、`user input was detected ... call get_window_state`、激活报 `foreground window did not report a process id`。停止使用缓存坐标，重新枚举并绑定真实窗口后再读状态；不循环抢焦点，也不把未提交的聊天草稿当作用户指令。新对话需重新读取可用 Skill/API，不复用旧 REPL 变量。

### 新对话可直接使用的启动指令

> 请读取 `D:/Project/Szy-Cathay/TheAssetEditor-CN/HANDOFF-VFX-LOCAL.md` 和 `AGENTS.md`，在原目录、原分支接手。先只读核对分支、HEAD、全部未提交及未跟踪文件和现有 Debug 运行库，保留所有成果，不创建替代分支。当前第一优先级是修复真实模型贴图材质的巨大错误；先检查交接列出的雷电共用 `wh_vfx_mesh` 材质，做同源 Terry 对照后再修改，并验证完整样本的普通 Debug 外观。不要再用测试数量、映射或像素非空作为外观通过依据。不改全局步长，不提交、推送、合并或清理。先报告接手状态，再按本对话给出的继续授权推进。

## 历史阶段记录（不覆盖上方最新交接）

> R55 的最终数值回归、300 份扫描、58 份受影响来源及两份普通窗口录制已有记录，具体数字与限制见上方。**用户已否定模型材质外观，R55 不代表系统或完整模型材质修复完成。** 原 top 段落称上述检查“仍在复核”，已在本次交接中按实际证据更正；原文备份保留。

> R54 的 `artifacts/vfx-rigid-orientation-r54/`：先对齐镜头并隔离原始 `arrow_trail`，仍复现 Terry 竖向束、产品横向散开；再用原版锥形和冰块各六组固定角度、零运动控制，比较全部六种三轴组合，确定模型粒子按 X→Y→Z 旋转。运行时只修正模型粒子及继承其朝向的事件子特效，保留发射器、引用和面片的既有约定、轴心、随机序列和全局步长。原始材质控制过淡，后用测试副本白材质隔离轮廓；所有尝试均保留。两种模型的产品边框每边差不超过 2 像素，最大欧氏轮廓距离 2.236 像素，原定严格 2 像素轮廓门限仍未全通过，不包装为完整几何/外观验收。
>
> 新增 10 项先为 3 通过/7 失败，修复后专项 69、完整 VFX Debug 955、共享 Effect 另 3 项全部通过，无失败/跳过；专项不与完整相加。主程序 Debug 增量构建 0 错误/0 警告，受影响测试构建 0 错误/1879 警告，完整日志保留。Core 为 daf2dff…，原生 DLL 仍 1ed30d3…。300 份同源、相同 11 时间点全部重新 GPU 检查，有像素仍 278，85 份最佳图变化，无源散列改变、映射层丢失、整例像素丢失或异常；不是完整显示率。
>
> 普通 Debug 的完整 Mod 杨戬与原版 Ulrika 均为 10/11、原件停用 1，分别录 538/567 帧，早期闪光与后续束状模型可见；相同固定镜头的完整 Mod 离屏结果也已从星状散开恢复成束状。材质、光照、原生随机种子/时钟及完整动画仍未验收。4 次 Terry 会话均正常退出并停用临时包，Debug 已正常退出；149 份既有源件与 5 份运行库基线、全部原始帧和失败工具启动记录原地保留，无清理、暂存或 Git 交付。下一共同缺口为 `vfx_soulpanning` 等模型材质：当前未知着色器可能沿用单底图候选，需核对原生噪声、遮罩、滚动和边缘响应，不能继续把已映射当作完整外观。
>
> R53 的 `artifacts/vfx-curve-coverage-r53/`：补齐 `bezier_c` 与既有非线性解析曲线的混合：先按两端存储切线求三次贝塞尔值，再按段内归一化时间混合两端函数，保留原有线性/Bezier 与纯 Bezier 路径、随机上下界和源件。四组独立下降/非对称切线控制在后续批次各 125 次观测通过原定 ±0.02 相位及两像素门限；首批未明确分辨率、未循环的准备以及线框切断遮罩的两版测量均保留并排除于最终验收。`step_next`、`s_inv` 仍未通过，其他 Bezier 类型不外推。含 `bezier_q` 的准备触发 Terry 的 calibs 访问冲突，Windows 故障日志与本任务无窗口崩溃进程的终止记录保留，去掉该控制后可加载，不把它写成正常退出或单因果已证实。
>
> 新增 7 项先全失败，受影响专项 135、完整 VFX Debug 945、中文四主题渲染 1 项通过，均无失败/跳过，专项不与完整相加。Main Debug 增量构建 0 错误/0 警告，Core 为 117a7c5…，原生 DLL 仍 1ed30d3…，全局步长不变。7,634 份源散列全不变，无旧映射层丢失；直接映射 12,637→12,777，新增 105 面片、23 可绘制模型、11 贴花、1 辅助层，影响 86 份来源，仍 1,748 层阻断。两个原先阻断的零发射项现归为原件停用（952→954）。300 份相同 11 个时间点全部重渲染，有像素 275→278，20 份最佳图变化、21 份增映射，无整例像素或映射丢失；另把全部 86 份受影响完整来源测至 20 秒，86 份均有像素/无异常，其中 12 份与 300 重叠，不能相加成 386 个独立来源。不是完整显示率。
>
> 完整 Mod `wh3_dlc22_lib_yangjian.xml`、原版符印分别原生录 123/126 帧，普通 Debug 10/11（原停 1）录 561 帧、10/10 录 539 帧；原版雾 1/6（原停 4）录 514 帧并见淡雾。Mod 早期闪光恢复，但后续普通窗口呈水平散开，Terry 呈竖向束；符印在产品中也明显较暗，均未通过完整外观/运动验收。镜头、时钟和 RNG 尚未对齐，下一项先对齐镜头、隔离模型和面片，查明差异再改产品。147 份既有源件与 5 份运行库基线、全部生成包、失败与原始帧保留；Debug 正常退出，5 次有效 Terry 会话正常退出，1 次崩溃另记，所有本次临时包已停用。无清理、暂存、提交、推送或合并，任务继续，整个 VFX 仍非完整可用。
>
> R52 的 `artifacts/vfx-particle-event-children-r52/`：补齐刚体的出生/死亡子特效及无模型事件辅助层，递归导出独有事件依赖；明确 EventEnable 缺省为关闭，普通面片事件尚无原生触发证据，不外推。接入延迟、跟随位置/大小/颜色/透明度、父死亡停止继续发射但保留现有尾迹，允许子特效引用回父库中其他被选择的层；真正递归保留深度/资源和 4096 动态实例、65536 粒子限制。不把旧连续模型运动候选叠加到事件锚点。13 次 Terry 控制/真实来源会话均正常退出并停用临时包；非零刚体出生微小位移、按速度朝向、复杂引用时钟及光照仍未校准。
>
> 完整 VFX 在 BOM/换行规范化前 937 项通过，规范化后含新增容量回归的专项 56 项通过；共享 Effect 3 项另通过，中文四主题/导出 15 通过、1 个既有真实资源集成因未提供样本环境跳过，不相加冒充一次全量。Debug 构建 0 错误、1877 个已有警告。产品解码源文本在编码规范化中不变，Core 散列由 8527374… 变为 181f6a6…，两版运行库与完整证据均保留。300 份原版/Mod 同源全部重渲染，272→275 份有像素，35 份增加映射根层、37 份最佳图变化，无原像素来源/原映射层丢失或异常；不是完整显示率。全库直接声明 12,329→12,637 映射，新增 308 个事件辅助层，仍 1,890 层阻断。普通 Debug 完整 Mod 环斩 6/6 录 568 帧、原版 Viletide 16/17 录 473 帧、原版风流 1/1 录 590 帧，均确实可见并记录变化；环斩早期白亮与原生金色尚未对齐，不宣称外观或时序通过。首段暂停录制及 IPC 超时均保留并区分。
>
> 剩余 25 个空样本已另做只读 CPU/源件检查；四份 Mod 为扭曲层，不因平色背景零像素而虚构颜色；其他样本仍需核对晚发射、透明度和通用阻断。146 份既有源件与 5 份运行库基线、全部失败尝试/工具/原始采样原地保留。Debug 已正常退出以继续原生对照；无清理、提交、推送、合并、原生 DLL 或全局 1/120 步长变化。继续按共同缺口推进，不将本轮阶段记录当作整个 VFX 已可用。
>
> R51 的 `artifacts/vfx-scaled-terrain-roots-r51/`：原生 1、0.5、2、0.7 等比引用缩放控制录 128 帧，确认贴地出生的本地高度偏移不随比例变化，几何尺寸仍缩放；完整原版传送门另录 131 帧，确实可见。运行时仅对贴地出生的普通面片/模型按正等比缩放归一化根基轴后做水平验证，保留原地面加本地高度，不改贴花、非等比、旋转、镜像或未贴地缩放边界。实际产品控制轮廓无需拟合，边界差 0～2 像素。专项 53、共享 Effect 3、中文四主题渲染 1 项通过；R50 完整 904 用例只在未变范围复用，不冒充新一轮完整 914 项。
>
> 普通 Debug 完整 `wh3_dlc24_spirit_hex_trail_portal.xml` 录 517 帧，先前全空的同一组合现有蓝色门环与烟迹，界面仍 8/665、源件/引用停用 649，其他活动层及完整外观未验收。四档控制暂停归零后录 496 帧，均可见但模型明显偏暗；Mod 完整琵琶 21/151、源件/引用停用 129，归零前段 322 帧及继续播放的后段 412 帧，各 12 秒，中间有未采集间隔，不宣称连续 24 秒或原生时序通过。可见琵琶、绿色圈/能量、后段竖直绿色片状内容与重播；后段形状未证实为正确叶片。300 份源散列全新核对，旧队列中仅 1 份含受影响的缩放贴地引用并重渲染，299 份复用 R50，图像/粒子数无变化，合计仍 272 份有像素；这不覆盖全库新恢复来源，完整原版样本另有实际窗口验证。146 份原源件与 5 份运行库、失败及所有帧保留，程序/Terry 正常退出，无清理、提交、推送、合并、原生 DLL 或全局步长变化。下一共同缺口是粒子出生/死亡触发的子特效；先做独有子资源的 Terry 原生控制，避免导出漏依赖影响对照。
>
> R50 的 `artifacts/vfx-rigid-terrain-height-r50/`：静态模型补齐 RMV 材质头轴心及入口 `PivotPoint` 相对默认半单位的局部偏移，先加到顶点再按原粒子缩放/旋转；缓存区分入口轴心，多网格保留各自轴心，动画旧边界不变。Terry 同位置模型高度、四组单轴轴心和第二种原版冰块的缩放/组合旋转控制分别录 122、129、122 帧；产品轮廓无需平移拟合，边界差 0～2 像素，冰块 0～1 像素。原始四层女巫传送门另录 128 帧。38 个原层重新 GPU 检查为 36 个有像素，恢复四个传送门，无原可见层丢失；300 份完整来源全部重渲染仍 272 个有像素，98 份最佳图变化，无整例像素丢失、异常、粒子数量或源散列改变。全库直接转换计数不变。
>
> 专项 65 项通过；完整 VFX 首轮 878 通过/26 因未配置游戏目录跳过，补齐目录后仅补跑这 26 项并全部通过，覆盖 904 个唯一用例；共享 Effect 同样为 2 通过/1 跳过后补跑该 1 项通过，不冒充一次全量通过。普通 Debug 四层隔离样本录 513 帧并显示 4/4，完整 `wh3_dlc24_spirit_hex_trail_portal.xml` 录 511 帧却仍为空（界面 8/665，原件/引用停用 649），不能把单层可见当整例通过。CPU 明确发现其中两个地形模型引用含 0.7 缩放，受仅平移根边界拒绝；其他粒子仍投影，原生光照/亮度也未还原。索引工具因资源覆盖不同映射 7 层，与实际窗口 8 层分别记录。下一项必要验证是原生缩放地形根及完整样本，再决定运行时扩展；继续处理出生/死亡触发的子特效这一全库共同缺口（329 个无网格辅助层中，301 个明确启用出生事件）。145 份既有源件与 5 份运行库备份、失败尝试、全部帧原地保留；本次 Debug/Terry 均正常退出，无清理、Git 交付或全局步长变化。
>
> R49 的 `artifacts/vfx-flat-terrain-basis-r49/`：Terry 固定方向、深度的五个普通面片高度控制录 123 帧，地形朝向但不贴地保留原高度，贴地出生使用地面加发射器高度；负偏移受遮挡、零偏移只露一半。原始树根另隔离录 132 帧，依赖完整，无材质或贴图替代。受限接入零初速度、零力、零发射器旋转、无风/网格发射/持续贴地的水平地形面片和模型；根只允许平移，贴花旧边界不变。有效静止地形层补写无颜色的水平深度，保留出生范围、原几何、粒子旋转与大小；缺少地面/深度或旋转缩放根时继续诊断。模型高度规则仍是候选泛化，须继续核对空层，不能把普通面片证据当作全部模型保真。
>
> 新扫 7,634 份定义，直接声明新增 38 层（模型 31、面片 7；主 Mod 3、原版 35），涉及 24 份来源，无原转换层或源件散列丢失；对比基线复用 R46，R47 数量夹限不改变转换有效性门禁，R48 未改转换器。38 个新增层逐个 GPU 检查，32 个有像素；四个女巫传送门、熊冰面和地裂模型仍为空，CPU 确认所有抽样几何位于地面以下，尚未确认是模型贴地语义还是源件放置，不抬高源件充数。300 份完整来源全新重渲染，仍 272/300 有像素、11 份最佳图变化、12 份增加转换层，无原像素来源丢失或异常；像素率不是完整显示率。完整 Mod 为 21/151，原件/引用停用 129，点光源仍缺；树枝库 19/21、原件停用 2。
>
> 有效修复前测试 10 失败/4 通过，初版不完整向量曲线的 14 个失败不计入；修复后相关专项 41 项、完整 VFX Debug 894 项、共享 Effect 3 项、新中文诊断四主题离屏测试 1 项通过，均无失败/跳过，不把专项相加冒充全量。普通窗口高度控制 489 帧；完整 Mod 暂停归零后连续两段各 12 秒录 315/429 帧，隔离原始树根另录 503 帧，出现/聚集/消失可辨但明显偏暗。普通窗口默认镜头与 Terry 不同，未作绝对屏幕或时钟对齐；原始模型外观未通过。143 份既有源码与 5 份运行库、失败/工具错误和全部帧保留，Debug 已正常退出，无清理或 Git 交付，全局 1/120 不变。继续原生核对六个地下模型及模型材质明暗。
>
> R48 的 `artifacts/vfx-constant-alpha-passes-r48/`：普通预乘面片/模型的完整解码贴图透明度恒为 0 或 1 时，按固定 DX11 MAX Alpha 契约省略恒无贡献通道；中间/混合透明度、渐变、自定义材质和贴花保留原路径。另限制优化合批必须保留每粒子 RGB 与高透明度归一化尺度，修复最初合批的量化差异；源件、模拟和全局步长不变。12,002 粒子同源离屏控制由约 270 毫秒、24,000 次提交降至约 41 毫秒、2 次提交，前者复用 R47 测量，不能称为全库帧率。
>
> 最终完整 VFX Debug 878 项、共享 Effect 3 项及相关专项 48 项通过，均无失败或跳过，专项与完整结果不相加。300 份完整来源全部以最终运行库重渲染，全部最佳图逐字节一致，粒子/转换层数量及源件散列不变，仍 272/300 有像素，无丢失或异常，不是完整显示率。普通窗口高数量控制录 345 帧，两个后发层间隔约 1.000 秒，与复用的 Terry 原生控制一致；完整 Mod 暂停归零后连续两段各 12 秒录 337/424 帧，可见琵琶、粒子圈、后期叶片、消失和再循环，模型过暗与地形模型缺层尚未通过。142 份既有源码与 5 份运行库、所有失败证据及帧保留，Debug 已正常退出以继续原生对照，无清理或 Git 交付。下一共同缺口为地形法线朝向与贴地出生，须先确认原生高度规则。
>
> R47 的 `artifacts/vfx-preview-capacity-r47/`：固定方向、深度、零运动的 Terry 控制录 266 帧，两个后发红/蓝层可见，原程序在全局 8,192 上限下均未出生。预览改为保留各层原数量上限、单层/组合安全预算 65,536（省略字段默认仍 8,192）；原生绘制按固定的 8,192 面片缓冲分批，修复同批尾部静默丢失，不改源件、全局步长或依赖源码。完整 Mod 第 8 秒从 8 个活动层恢复为 9 个，新增可见的 `explosion_leaves_2` 有 30 粒子；完整来源仍 20/151，129 层源件/引用选择停用，树根地形朝向与点光源仍未实现。
>
> 实际高数量控制另发现每帧截成 0.1 秒会使动画变慢；独立 VFX 组件改用实际帧间隔的绝对 Seek，暂停仍更新附着，重载、归零和循环末端边界保持。Terry 两层出现间隔约 1.000 秒，首次程序记录约 3.033 秒；暂停并归零后的最终普通窗口单循环录 113 帧，间隔约 1.003 秒，单帧区间约 0.3 秒，未对齐绝对效果时钟，也不算原始环斩完整时序验收。首次修复后 101 帧混有前一循环，保留但不作精确时序证据。完整原始 Mod 最终连续前/后两段各 12 秒录 336/427 帧，显示琵琶、粒子圈、后期叶片与消失/再循环；更早含较长暂停前导的 486/350 帧也保留，未用作完整循环证明。
>
> 容量修复前 7 项有效测试均失败，初次辅助类缺失的编译失败另存不计入；容量后专项 34 项、完整 VFX Debug 849 项通过。时钟修复前 7 失败/1 通过，修复后时钟/容量/模拟专项 28 项及重新构建的共享 Effect 3 项通过，均无失败/跳过；849 项为时钟变化前的完整结果，未把复用与专项相加冒充一次新全量。300 份完整来源以容量最终运行库全部重渲染，仍 272/300 有像素，4 份图像/粒子数变化，无原像素来源、转换层或源件散列丢失；后续时钟组件不参与离屏扫描，复用该结果。中文 65,536 诊断在实际窗口检查，Debug 已正常退出以继续构建。140 份既有源码与 5 份运行库、失败与超时证据完整保留，无清理或 Git 交付。性能分解另测 12,002 粒子：CPU 画面准备约 14 毫秒，单通道原生绘制约 27 毫秒，现行遮盖/发光约 270 毫秒，指向逐粒子重复提交；下一步验证省略恒无贡献通道，尚未实施。
>
> R46 的 `artifacts/vfx-prelit-sprite-r46/` 继续：已受限接入 `wh3_vfx_custom_basic_pre_lit` 的五纹理、21 参数内在颜色/透明度/发光遮罩分支，全库直接声明新增 45 层（神话人物主 Mod 10、原版 35），16 份直接来源，无原转换层或源件散列丢失。45 个新增层均在真实开始时间之后隔离 GPU 检查且有像素；完整树枝库目前 18/21、原件停用 2 层，仍缺地形法线朝向的 `roots_ground`。完整 Mod 入口为 20/151、源件/引用选择停用 129，尚缺地形模型与点光源，不能称为完整显示。
>
> 原生材质控制 253 帧、原始全分辨率通道 239 帧；半透明叶片用同位置死亡后背景差分确认，最初肉眼未辨认的结论已修正并保留初版。完整原始入口另录 139 帧，但有四张旧 DDS 路径缺失，未替换贴图，不用于完整外观验收。同位置独立材质色阶另有 272 帧有效记录，灰色贴图与线性颜色参考仅约 0.003 个字节差，证实漫反射 sRGB 读取；首次错误使用模态窗口 HWND 的零帧采样已排除。普通无光照片段的五色阶 276 帧不支持直接给全预览套标准输出伽马，尚未修改全局输出、曝光或天气光照。
>
> 普通 Debug 原完整入口基线 170 帧、叶片过暗控制 303 帧；只读数值快照优化后，同样 1440×900 窗口重录早期 399 帧及约延后 10 秒的后期 445 帧，均完成 12 秒有效窗口，无并行 GPU 回归，播放有改善，但时钟未精确对齐，不能外推为全库完整显示或固定帧率验收。该 Debug 已正常退出。曲线修复前分配测试 1 失败/2 通过，修复后 103 项曲线专项通过；12 个同源时点的完整帧散列全部相同，此次 CPU 抽样高峰由 67～76 毫秒、约 34 MB 临时分配降至约 20～23 毫秒、约 5 MB。最终完整 VFX Debug 842 项与共享 Effect 3 项通过，0 失败/0 跳过；300 份完整来源全部以最终运行库重渲染，仍 272/300 有像素、4 份图片变化，无原像素来源、转换层或源件散列丢失。全局步长和 8,192 预算本轮未改；受控离线案例已复现上限使两个后发层不出生，转入 R47 做 Terry 对照。136 份原源码、5 份运行库、失败证据与所有帧保留，无 Git 交付或清理；完整 PBR、树根、点光源、环斩时序仍未通过。
>
> R45 的 `artifacts/vfx-onset-and-framing-r45/`：先用固定方向、深度和恒定速率的 Terry 两次发射对照，四种有限时长均观测为 1/1/2/6 粒子；修复因绝对开始时间和最后部分 tick 丢失末尾粒子的问题，持续发射和全局 `1/120` 不变。初始取景按当前动画姿态及地面以上颜色几何估算，扭曲和完全地下来源保留回退，不改模型姿态、运动或地形遮挡。原版完整碎片在相同普通窗口下重录 557 帧，较修复前 560 帧明显可辨；仍 11/19 转换、7 层原件停用、点光源未实现，不能算完整外观通过。R44 实体控制本轮另录 487 帧，有限发射普通窗口录 508 帧并看到完整 1/1/2/6 队列，WGC 与效果时钟未精确对齐，未作为两个精确时点的验收。
>
> 修复前出生专项 3 失败/4 通过，取景专项另复现 3 项失败；首次扩大到持续发射的实现回归失败已保留并收紧。最终 74 项受影响专项、完整 VFX Debug 829 项与共享 Effect 3 项通过，无失败或跳过；300 份完整来源全部重新渲染，仍 272/300 有像素、268 份画面变化，无原像素来源、转换层或源件散列丢失。直接声明普查复用 R44 未变转换器结果，未重新扫描；不能把像素计数称为完整显示率。134 份原源码及 5 份运行库基线、失败日志和全部帧保留，当前 Debug PID 50568 仍运行，原生会话已正常退出并停用本次包；无清理或 Git 交付。原始环斩出现时序仍未通过，下一共同缺口为 `wh3_vfx_custom_basic_pre_lit` 的树叶面片材质，已提取实际五纹理、21 参数及原生公开字节码，须先做原生可见性对照再接入。
>
> R44 的 `artifacts/vfx-real-window-gaps-r44/`：已受限接入 `vfx_mesh_solid` 的六纹理、49 源参数、滚动遮罩、粒子颜色和高透明度硬裁切及不透明深度；白色抖动图与黑色材质图按实际像素验证，其他材质分支仍诊断。Terry 固定遮罩控制 268 帧可辨认白/红/零透明度/高透明度三个实体，原始遮罩 260 帧和高透明度原遮罩 244 帧均未形成可靠模型，不用于外观验收。修复前有效测试 8 失败、4 通过，另一次格式缺失的首批测试明确无效；修复后 14 项专项、完整 VFX Debug 816 项及共享 Effect 3 项通过，无失败或跳过。全库直接声明新增原版 17 层、13 份来源，Mod 无新增直接层；总计 12,246 转换、952 原件停用、2,281 阻断，无原可转换层丢失或源件散列变化。17 个新增层均在各自真实开始时间之后隔离 GPU 检查且有像素；同一原生控制输入另录 81 帧离屏程序图。300 份组合中重查 6 份受影响或依赖不确定来源，其余 294 份复用 R43 未变化结果，仍 272/300 有像素，6 份图片与原基线一致，不能算完整显示率提升。
>
> 原版 `wh3_main_slaanesh_slicing_shards_launch.xml` 当前离线转换为 11/19 层，另 7 层原件停用，剩余为点光源；取景仍包含约 259 米的后期动画轨迹，尚未修改相机或粒子运动。本次 Debug PID 42792 已启动，但桌面工具随后收到物理 Esc 中断，窗口复验未执行；首次 IPC 在启动期间连接超时，不当作打开成功。按 Computer Use 中断规则停止桌面输入，程序保留。用户已明确自己先前测试的是本次 Debug，不能归因于安装版入口不同。131 份原源码及 5 份运行库备份、所有采样和失败证据保留，全局 `1/120` 不变，无 Git 交付或清理。后续先完成普通窗口的实体控制与完整碎片组合，再继续环斩短时出生/消失和取景验证；未宣称全库完整可用。

> R43 的 `artifacts/vfx-ring-onset-r43/`：依据游戏附带粒子着色器与 Terry 的 0.5/1/2/3 透明度渐变对照，修复普通预乘面片在模拟和绘制前提前截断透明度的问题。覆盖在纹理取样后饱和，预乘 RGB 不因锐化重复增亮；另复现并修复同层极强透明度使弱粒子在字节编码中消失的问题，保持原绘制顺序。原生有效控制 260 帧、同位置消失后背景 253 帧；首次格式错误探针仅 3 帧，已排除。最终 7 项专项、802 项完整 VFX Debug、共享 Effect 3 项均通过，无失败或跳过；最终运行库重新检查 300 份完整来源，仍 272/300 有像素，主 Mod 126/135，21 份像素变化，无异常、运行时不可用、原可见来源丢失、转换层丢失或源件散列变化。全库 12,229 转换、952 原件停用、2,298 阻断不变；637 份来源中 1,513 个已转换普通预乘面片具有高透明度候选标记（Mod 69、原版 1,444），不是完整显示率或精确外观验收。
>
> 本轮普通 Debug 原始环斩录 394 帧，四级透明度控制录 385 帧；原版 `wh3_main_slaanesh_slicing_shards_launch.xml` 另录 279 帧，仍只转换 9/19 层、原件停用 7 层，画面很小，明确未通过完整显示。环斩的原始贴图在 Terry 重播、隔离、独立内部 ID 和 DDS 格式/分辨率探针中仍未形成可靠原生弧光，不能据此更改默认出生规则或宣称出现时序通过；相关采样、失败现场和条件更正均保留。已复用 R30 原生证据核对直接子层选择规则，不放开未列出的子层。源码、127+3 份续接源码基线及 5 份运行库备份保留；桌面安装版未替换，全局 `1/120` 不变，无 Git 交付或清理。继续在 `artifacts/vfx-real-window-gaps-r44/` 定位原版碎片组合：两层共同被 `vfx_mesh_solid` 材质阻断，当前初始取景还合并了很远的碎片动画轨迹和大范围扭曲，尚未修改这一部分产品代码。

> R42 的 `artifacts/vfx-premultiply-material-r42/`：依据原生独立材质名的四格颜色/自发光对照、同位置背景和游戏附带着色器，接入 `fx_premultiply_animated_uvs` 的六纹理、材质颜色、遮罩及全局滚动；零自发光保留暗色覆盖，不套统一亮度补偿。原生颜色 259 帧、背景 237 帧；普通 Debug 从头播放控制组 310 帧，真实护盾库后期另录 290 帧，约 12.7 秒生成的碎片已恢复紫色并散开，整体光照和完整外观仍未验收。适用 10 份来源中的 27 个直接层，全部按各自真实开始时间之后做隔离 GPU 检查并有像素；全库转换数仍为 12,229、停用 952、阻断 2,298，无层丢失或源件散列变化。完整 VFX Debug 为 795 通过、0 失败、0 跳过，共享 Effect 3 项通过。300 份来源中重新检查 4 份受影响或依赖解析不全的组合，复用 R41 的 296 份未受影响结果，仍为 272/300 有像素，不能称为全库完整显示率。Mod 完整护盾入口没有选中碎片层，不能宣称该入口已改善。127 份原源码及 5 份运行库、失败复现和全部证据保留；本次 Debug 正常退出。继续同位置环斩短时发射与地形标志对照，不凭原生缺画面改初始出生规则。全局 `1/120` 不变，无 Git 交付或清理。

> R41 的 `artifacts/vfx-rigid-fragment-animation-r41/`：接入独立根、按矩阵编号读取的 `building` 刚体分块动画，避免重复名称匹配和缺少普通绑定骨架导致整层跳过；网格上限扩到 64，原姿态、材质和绘制总量上限保持。Terry 的 42 分块静止/播放对照保留 170 和 259 帧；实际 Debug 控制组录 289 帧，原始护盾库在真实后期生成并散开碎片另录 299 帧。颜色仍偏白，完整外观未验收。新增 5 个直接层（Mod 1、原版 4），均在各自真实开始时间之后做隔离 GPU 检查并有像素，包含 12.7 秒才出现的护盾碎片；原有 0～7 秒整组检查不能证明这些新层可见。完整 VFX Debug 为 787 通过、0 失败、0 跳过，共享 Effect 3 项通过。300 份来源中本轮重查 3 份受影响或依赖无法完整解析的组合、复用 R40 的 297 份未受影响结果，合计仍 272/300 有像素；另查全部 5 份新增层所在的原始完整来源。全库为 12,229 直接层转换、952 停用、2,298 阻断，无原转换层丢失或源件散列变化。护盾库为 18/21 转换、原件停用 1 层；Mod 完整入口没有选中 `shield_cracks`，仍是 11/21 转换、原件停用 8 层，不能把该入口称为已改善。原始环斩出现时序仍未解决。126 份原源码、5 份运行库及全部失败证据保留，无 Git 交付或清理，全局 `1/120` 不变。继续处理 `fx_premultiply_animated_uvs` 的六纹理、材质颜色与发光；原始材质、原生画面及游戏附带着色器字节码均已保留，不能按画面猜统一亮度补偿。

> R40 的 `artifacts/vfx-opaque-model-material-r40/`：依据 R39 原生四格对照，显式不透明角色材质改用自身颜色和不透明覆盖，先绘制并写入深度；独立纹理用途保留普通面片的原始透明度，不改共享 DDS。此前 5 项失败已复现并修复，完整 VFX Debug 为 781 通过、0 失败、0 跳过，共享 Effect 3 项通过。四模型控制从 2 个恢复为 4 个，普通窗口保留 401 连续帧；完整青云雷书另录 235 帧，仍只转换 8/43 层（原件停用 34 层、活动点光源未支持），整体外观未验收。重新检查全部 300 份来源仍为 272/300 有像素，主 Mod 126/135，无异常、运行时不可用、转换层丢失或来源散列变化；全库直接层数不变，实体规则适用 5 个直接层，不能算全库显示率提升。完整 PBR 光照、取景和其他专用材质仍未恢复。125 份原源码及 5 份运行库基线、失败现场和全部采样保留。继续做按矩阵编号运动的刚体分块动画原生对照；护盾有 42 个独立块，普通绑定骨架缺失，不能套用蒙皮动画。全局 `1/120` 步长不变，无 Git 交付或清理。

> R39 的 `artifacts/vfx-model-animation-clock-r39/`：修复转换失败层遗留的姿态预算、模型缓存和资源，失败引用也撤回预算；旧行为的失败测试已保留。VFX 骨骼动画改按片段记录的帧率和时长在粒子寿命进度内取样，末帧后保持，不改共享动画播放器或全局步长。重录的真实片段从约 1.925 秒恢复为 2.000 秒，倍速有 1.000 秒周期，与已有原生记录相符；相机、精确姿态和完整光照仍未验收。

> R39 另修复显式零 `SpawnArc`、没有旧 `SpawnSpread`、默认扩散方式下的方向。Terry 固定双边曲线、关闭贴地出生的四方位角均向上；原先程序把部分方向算成横向或向下。适用 342 个直接层（Mod 13），不是所有 1,476 个带该字段的层。原始青云雷书同散列轨迹在 0.5 秒从 Y=-9.44 改为 Y=12.99，普通窗口的连续帧和从头重播已保留，但主剑材质、取景及整体外观仍不完整。青云雷书主剑自身没有骨骼片段，属于多网格模型的平移与自转；水火触手另属骨骼动画。最终完整 VFX Debug 为 774 通过、0 失败、0 跳过；共享 Effect 先 2 通过、1 缺游戏路径跳过，补路径后只重跑该项并通过。全部 300 份完整来源重跑后仍 272/300 有像素，Mod 126/135，无异常、运行时不可用、原转换层丢失或源件散列变化；全库直接声明数仍为 12,224 转换、952 停用、2,303 阻断，不能算完整显示率。

> 下一共同缺口已有原生证据：`plain-model-tint-native` 使用同一三网格主剑、原材质和原贴图，白/红粒子色与透明度 1/0 共四组在原生均显示原材质实体，当前产品只显示前两组并错误染红。材料为显式 `rigid_alpha_off`、`shaders/rigid_character.xml.shader`；继续受限接入实体材质颜色和不透明规则，不能直接扩展到其他着色器、透明材质或宣称完整 PBR 光照通过。R38 的规范 V7/V8 动画探针提前退出，仅 3 帧，已排除；R39 首批方向探针缺少最大值通道，也已排除。原始环斩短爆发仍没有清晰原生弧光，不能视为时序通过。R39 的 Terry 与两次 Debug 已正常退出，提前退出的旧探针也已停用临时包。失败现场、源件、备份与连续帧保留；任务继续，不执行 Git 交付或清理。

> R38 的 `artifacts/vfx-model-animation-r38/`：补上标准骨架、双/四骨骼的候选姿态与普通纹理多网格，按随游戏提供的顶点着色器重建隐式末权重，Terry 导出也收集显式 Mod `.anim`。原有拒绝动画的失败复现、首次真实权重拒绝及所有探针保留。完整 VFX Debug 为 768 通过、0 失败、0 跳过；Terry 导出 9 项通过；共享 Effect 先 2 通过、1 缺 GPU 开关跳过，补开关后只重跑该项并通过。普通窗口四组模型 4/4 转换，561 连续帧与中文诊断已核对，材质过暗、相机与精确姿态仍未验收。

> R38 新跑全部 300 份来源，仍 272/300 有像素、主 Mod 126/135，组合内多 18 个活动层，无异常、运行时不可用、原转换层丢失或来源散列变化。全库直接声明增加 64 个转换层（主 Mod 5、原版 59），共 12,224 层转换、952 层源件停用、2,303 层仍阻断；全部 39 份新增直接来源另做 GPU 检查，39/39 有像素。Mod 增加青云雷书主剑及水火炼魔库的四个触手动画层，但尚未通过这些完整效果的普通窗口原生外观验收。备份 119 份原源码和 5 份运行库；全局步长不变，无 Git 交付或清理。

> 模型动画仍有约 4% 周期偏差，不能据此认定时序已经正确。两/四关键帧探针与宽范围重分析分别保留；前批探针含额外末帧保持、贴地出生和预模拟字段，不用于直接外推产品规则。当前继续核对具有规范末帧时间、关闭贴地出生及预模拟、单次有限发射的 V7/V8 原生对照，并修复失败层占用动画资源预算的问题。入口为 R38 的 `validation-summary.json`、`catalog-comparison.json`、`library-comparison.json`、`native-clock-observation.json` 与 `native-four-key-observation.json`；最新探针状态以各 `session/launch.json`、`closed.json` 为准。任务继续推进，不回退到少量静态样本验收。

> R37 的 `artifacts/vfx-basic-sprite-material-r37/`：受限接入 `wh3_vfx_custom_basic.xml.shader` 普通面片，保留四张资源、原始参数、透明度幂、颜色叠加和全局遮罩滚动，暂不支持视角反射变化。17 项专项、747 项完整 VFX Debug 与共享 Effect 另 3 项均通过，无失败或跳过。Terry 的四次克隆材质对照没有呈现独立参数变化，已排除其标定效力；未修改原始火焰在 Terry 可见并连续动画，程序中的同源火焰仍明显偏白，且点光源层未显示，不能称为完整还原。

> R37 全部 300 份同散列来源重新 GPU 检查，仍 272/300 有像素，主 Mod 126/135，新增 4 个组合内活动层，0 运行异常、0 运行时不可用、0 原可转换层丢失。全库 7,634 份直接声明新增 43 个转换层（主 Mod 2、原版 41），共 12,160 层转换、952 层源件停用、2,367 层仍阻断，无原层丢失或源件散列变化。正常 Debug 原始火焰为 1/2 层，Mod 铁龙库为 17/28 层、另 7 层源件停用，完整连续帧保留；转换及有像素不等于完整外观。所有本轮 Terry 与 Debug 已正常退出，临时原生包停用、源件与运行库备份及失败尝试保留。继续优先处理共同缺口和实际动画，不改共享步长，不交付 Git。
>
> R36 的 `artifacts/vfx-custom-sprite-material-r36/`：依据游戏附带 DXBC 的公开反汇编与固定方向/深度的 Terry 对照，接入普通面片 `fx_animated_uvs_custom` 的七纹理、遮罩、透明度、发光和全局时钟滚动。固定依赖只支持六个用户纹理槽，因此将数值噪声和全局遮罩原分辨率打包，带独立 UV 和循环边缘，不改官方依赖。最初七槽实现的失败像素测试保留；六槽实现的 8 项专项和 738 项完整 VFX Debug 回归全部通过，共享 Effect 另 3 项通过，均无失败或跳过。Terry 238 帧与产品 81 帧中的归一化滚动速度支持源参数 0.15；全局时钟起点不同，不把相位或最终曝光当作通过。
>
> 普通 Debug 原始真武护盾由 9/21 增至 11/21 转换，8 层原文件停用，剩余两层动态灯光仍未支持。原尺寸及放大观察的连续动画保留，火焰、火星已可见；原始杨戬环斩也重录连续扫转，尚未通过完整原生出现时序或外观验收。完整 300 份来源、每份 11 个时间点均已重新做 GPU 检查，仍为 272/300 有像素、主 Mod 126/135，组合中新增 10 个活动层，0 运行异常、0 原可转换层丢失，来源散列不变。全库直接声明由 12,043 增至 12,117 层，新增 74 层（主 Mod 4、原版 70）、无丢失；2,410 层仍阻断，不能称为全库完整显示。另已对全部 37 份直接使用新材质的有效来源做 GPU 检查；最终数值见该目录记录。
>
> R36 的 111 份原有源码与 5 份运行库基线保留；所有尝试、原生与程序帧均保留。本次 Terry 和 Debug 已正常退出，临时原生包已停用，未保存游戏源件、未清理、未改全局步长或 Git 状态。全库材质追加扫描首次误将非 VFX XML 交给转换器而失败，已保留日志并根据同轮有效普查清单重新选择；不将首次失败记成通过。下一步继续处理共同阻断和真实动画，不能退回只调少数静态样本。
>
> R35 的 `artifacts/vfx-animation-fidelity-r35/`：先做固定位置、零速度的 Terry 自转及角阻尼对照，补齐原来完全忽略的 `ParticleSpin_Min/Max` 与各轴方向。90/180/800 度控制的连续帧拟合支持度/秒与顺逆方向；未填写方向按出生随机选择。恒定角阻尼 0～5 使用衰减积分候选，保留数度残差与多轴组合、动态角阻尼限制。正常 Debug 原始杨戬环斩已记录连续扫转，`app-ring-animation.gif` 与 399 帧保留；原始短爆发在 Terry 仍有明显材质/外观差异，未通过完整原生动画验收。
>
> R35 另补齐没有贴图阶段的普通纯颜色面片；指定贴图缺失、自定义着色器、扭曲和投影贴花不使用白值替代。Terry 的 600/3600 秒寿命早期可见性与负发射尾段/显式截零的同步对照后，取消把长粒子寿命误当成预览越界的整层拒绝，负发射区间按零处理。原始源寿命、引用偏移保留，预览 seek、重播上限和容量预算不扩大。正常 Debug 真武护盾当前为 9/21 转换、8 层原文件停用，仍缺两层自定义火焰/火星与两个动态灯光，不能称为完整显示。
>
> R35 最终 Debug VFX 为 730 项通过、0 失败、0 跳过，共享 Effect 另 3 项通过。完整 300 份来源重查后，纯颜色另重查 2 份、时间边界另重查 13 份；累计 272/300 有像素，主 Mod 126/135，0 整体运行异常，较 R34 多 16 个来源内活动层进入转换。全库 7,634 份直接声明普查由 11,993 增至 12,043 层转换，新增 50 层（Mod 6、原版 44）、无原可转换层丢失；2,484 层仍阻断，不能将转换率当作完整显示率。新旧源件散列相同。108 份原有源码与 5 份运行库基线、所有失败尝试及六次原生会话均保留，六次 Terry 正常退出并停用各自临时包，未清理、未改全局 `1/120` 步长。
>
> 接下来优先处理普通面片的 `ps_custom_shader`，不是继续只校准静态少数样本。R35 的 `custom-material-audit/` 已定位原版与主 Mod 的 `wh3_vfx_custom_basic_pre_lit` 等普通自定义材质，以及真武火焰/火星实际使用的 `fx_animated_uvs_custom` 动画材质；这些目前未接入，禁止按纯颜色白块冒充恢复。审计包括材质原件与来源列表，先核对游戏着色器契约并做 Terry 对照，再接入实际预览。
>
> R34 的 `artifacts/vfx-vertical-facing-r34/`：已按带明确类型的 Terry 控制接入普通面片的竖直 `restriction=1` 与空间 `restriction=3`，并修复普通窗口镜像投影下摄像机朝向面片的纹理左右方向；模型与投影贴花不外推这些朝向。最初三组未写 `type=int` 的控制无效，已保留并排除，后续五个角度/朝向控制共 330 帧用于几何与 UV 方向核对，不作为精确光照验收。普通窗口中两份空白 Mod 实际使用的是 `restriction=3`，先前归因于 1 不准确。
>
> R34 还修复无光照/发光片段的普通预乘面片丢失纹理发光：Terry 的有效 BGRA32 零透明度控制中，缺省与显式倍率 1 均可见，显式 0 不可见；倍率 2500 另作阳性控制，不用于产品增益拟合。前几组 DDS 未正确采样，均排除于标定。产品仅为这一缺省分支保留倍率 1，显式值、模型、扭曲、贴花及全局曝光不改。原始杨戬环斩与真武断魔斩范围技能在正常 Debug 窗口分别恢复金色、红色弧光；它们原文件实际只启用 2 层，其他 9/31 层为原文件停用，不能算作本轮恢复。原始短爆发的 Terry 会话尚无可辨识弧光，另有明确延长发射时长的控制，不能据此宣称原始爆发时间或完整原生外观通过。
>
> R34 最新 Debug VFX 为 708 项通过、0 失败、0 跳过，共享 Effect 3 项另通过。朝向/投影修复后重新检查全部 300 份来源；缺省发光修复后重查受影响的 136 份，其余 164 份复用本轮未受影响结果。相对 R33，共 22 份来源的 51 个活动层进入转换，72 份来源的像素统计随发光修复变化；合计 269/300 有像素、主 Mod 125/135，0 新整体加载失败或异常。这些是候选转换与可见性数据，不是完整显示率。最新全库只读普查为 7,634 份有效定义、15,479 个直接声明层，11,993 层进入转换、950 层停用、2,536 层仍阻断；原始普查中的候选提示不能作为阻断原因，使用 `catalog-current/audited-blockers.json` 的校正分类。引用组合、真实像素和完整外观需另证。
>
> 本轮 105 份源码与 5 份运行库基线保留，原目录、分支、HEAD 和全局 `1/120` 步长保持。新程序入口仍是 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`，安装版未替换。当前用户已持续授权本任务窗口、Terry、采样及普通焦点恢复，不因通常的窗口焦点变化重复询问；授权不扩大到提交、推送、合并、清理或无关程序。继续按全库共同阻断优先处理，不把少数样本可见描述为整个系统可用。入口为 `validation-summary.json`、`library-comparison.json`、`aggregate-current-results.json`、`emission-control-validity.json`、`pixel-geometry-final.json`、`workspace-preservation.json` 与 `implementation-delta.patch`。
>
> R33 的 `artifacts/vfx-emissive-decal-pass-r33/`：先完成原始发光贴花、普通贴花与白色控制的 Terry 对照，再从游戏附带字节码及技术状态确认独立发光贴花的加法 RGB、2500 线性辐射倍率与透明度权重，已接入专用通道；普通贴花、普通透明面片和全局曝光不改。普通 Debug 的真实裂纹层由黑色恢复为金色发光，完整真武可见地面光环和爆发组合；最终曝光、接收面法线衰减及完整原生外观仍未通过，不能称为系统完整可用。材质头文件的增量构建依赖也已补全，避免生成器或嵌入着色器继续使用旧内容。
>
> R33 本地 Debug VFX 回归为 685 项通过、0 失败、0 跳过，共享 Effect 另 3 项通过；91 份受贴花影响的真实来源重新做 GPU 检查，其余 209 份复用 R32 未受影响结果，26 份来源画面像素变化，合计仍为 267/300 有像素、神话人物主 Mod 123/135，0 新运行异常或整体加载失败。这次主要修复发光外观，未扩大可转换层数。104 份原有源码与 5 份运行库基线保留，147 份 Mod XML 散列不变；原生会话正常退出并停用本次临时 Pack。入口为 `validation-summary.json`、`native-pass-contract.json`、`native-observation.json`、`library-comparison.json`、`workspace-preservation.json` 与 `implementation-delta.patch`。所有失败尝试和截图保留，不清理、不提交、不推送、不合并，全局 `1/120` 步长不变。接下来继续优先查会阻止整层转换的普通竖直朝向 `restriction=1`，须先做原生对照。
>
> R32 的 `artifacts/vfx-terrain-model-visibility-r32/`：同内容同用途的纹理在每次重新加载中复用原生资源、保留逻辑槽位；固定材质在原生构建阶段编译并嵌入 DLL。完整真武诊断样本的 610 个纹理槽仅有 26 个不同输入，受控加载由约 42 秒降至约 4 秒。Terry 的水平接收面四格对照确认正、负 90° 贴花在 VFX 上轴/地形法线朝向、贴地出生开关两种条件下均可见，已受限接入静止地形法线投影贴花；非平面、旋转缩放挂点及未经确认的出生高度仍跳过并诊断。入口为该目录的 `validation-summary.json`、`loading-library-comparison.json`、`library-comparison.json`、`workspace-preservation.json` 和 `implementation-delta.patch`；100 份源码与 5 份运行库基线均备份，既有成果全部保留，全局 `1/120` 步长未变。不要通过新分支、清理或覆盖恢复成果。
>
> R32 最终本地 Debug VFX 回归为 677 项通过、0 失败、0 跳过，共享 Effect 另 3 项通过；编辑器边界复用 R30 结果。纹理/预编译修改后重新检查全部 300 份来源，最佳帧与 R31 当前证据逐像素一致；随后只重查朝向修改涉及的 42 份，其余 258 份复用该轮未受影响结果，合计仍为 267/300 有像素，主 Mod 为 123/135，未新增整体加载失败或异常。25 个独立来源/层组合（主 Mod 10 个）接入受限地面朝向；完整增长、真武由 49/62 增至 51/62，另 4 层原文件停用，剩余 7 层均缺同一旧碎石漫反射。普通 Debug 完整真武组合及隔离贴花已验证投影几何；自发光贴花当前呈黑色裂纹，发光通道外观仍未通过，不能把 1/1 转换写成原生外观恢复。R26 的 `native-decal-pass-response.json` 已记录普通/自发光贴花响应不同，下一项须先做真实源件原生对照并核对着色器契约，不能从显示色阶猜统一增益。
>
> R32 的 6 次原生会话均已退出并校验停用临时包，其中 5 次正常关闭；保留旧碎石材质的控制超过 180 秒无窗口，仅终止本任务该采样进程，保留 `termination.json`，不将其作为原生失败结论。最初两次真实贴花控制使用未确认的单边曲线编码且无可辨认图案，已排除于标定；后续两组四格使用既有可见模板并保留完整上下界格式。白色碎石几何控制可见，旧材质/缺失贴图的正确原生行为尚未确认，不添加白色替代材质。
>
> R31 的 `artifacts/vfx-gemstone-material-r31/`：Terry 同位置白色几何、原始绿色晶体及半透明度对照确认粒子透明度驱动晶体溶解轮廓，已补齐材质透明度为 1 时的六纹理、40 参数、材质颜色、自发光、菲涅耳、溶解裁切及不透明深度基础分支。普通 Debug 窗口已核实 Mod 玉龙绿色拖尾、增长白蓝拖尾、真武金色拖尾和原版玉龙拖尾重新可见，完整真武库可见晶体、烟雾、光环及爆发组合。完整金属反射、微表面法线、延迟光照与挂点运动仍未校准，不能称为原生保真通过。96 份源码与 5 份运行库基线及所有证据保留。
>
> R31 本地 Debug VFX 回归为 666 项通过、0 失败、0 跳过，共享 Effect 另 3 项通过；编辑器边界复用 R30 结果。受影响的 25 份来源重新做 GPU 检查，另 275 份复用 R30 未受影响的结果，合计 267/300 有像素、0 运行异常、0 整体运行时加载失败，其中神话人物主 Mod 为 123/135；这不是新跑 300 份，也不是完整外观验收。116 个独立来源/层组合（主 Mod 58 个）补上晶体分支；完整增长和真武库由 25/62 增至 49/62，原文件另停用 4 层，剩余 9 层集中在旧碎石模型纹理与地形法线朝向。普通窗口完整真武首次加载出现约一分钟 UI 停顿；最初 60 帧仅记录加载，后续 80 帧才记录已加载组合，不能把首批空帧判为永久不显示。下一步先核对剩余两类问题的 Terry 原生表现，再修改。
>
> R30 的 `artifacts/vfx-reference-selection-r30/`：Terry 同位置红绿双层、单选、空列表对照确认引用列表选择直接子发射器，现已排除未列出的直接子层，避免额外绘制与取景干扰；根文档直接打开的启用规则不变，嵌套选择传播仍待校准。普通 Debug 窗口已确认单选只显示红环。95 份源码与 5 份运行库基线及其证据全部保留。
>
> R30 本地 Debug VFX 回归为 654 项通过、0 失败、0 跳过；编辑器边界检查先为 47 项通过、2 项因缺少本地样本路径跳过，补齐路径后只重跑这 2 项并通过，共享 Effect 另 3 项通过。300 份同散列真实来源已全部重新做 GPU 检查，263 份有像素、0 运行异常、0 整体运行时加载失败；其中神话人物主 Mod 为 120/135。引用选择改变 73 份来源，10 份原来依靠未选中层产生的画面消失，仍须补齐真正选中层，不能称为显示修复完成。广目普通窗口仍稀疏；完整依赖、每隔 24 秒重复 8 次的 Terry 采样仍未形成可辨认原生效果，未通过外观验收。主 Mod 中晶体材质出现 108 次不支持诊断，是下一项共同缺口；诊断次数不等于独立模型数。
>
> 上一轮 `artifacts/vfx-model-variants-r29/`：按 Terry 红绿单粒子对照接入静态模型列表的出生选择；补齐 `wh3_vfx_basic_mesh` 的零颜色扭曲材质分支；将贴图加载失败限制到实际依赖它的整个发射器。入口为该目录的 `validation-summary.json`、`aggregate-current-results.json`、`workspace-preservation.json` 和 `implementation-delta.patch`。R29 基线 90 份源码及 5 份运行库有校验备份，R28 的 85 份基线及全部证据也保留；未改变全局 `1/120` 步长。
>
> R29 最新本地 Debug VFX 回归为 651 项通过、0 失败、0 跳过。10 份原版来源的 28 个静态模型列表层重新进入转换；普通窗口中的荆棘由大片实色面恢复为细枝，原生及前后对照为 `witch-material-before-native-after.jpg`，不证明相机、随机数、时序或完整光照保真。300 份真实来源的去重证据中 273 份有像素、0 运行异常、0 整体运行时加载失败：其中 299 份复用材质修复后的结果，异常贴图影响的纳加什来源在最后隔离修复后单独重跑，恢复 72/124 层（原文件另停用 6 层）；不能把这些像素数字当成完整显示验收。Mod 对照中的广目组合在普通窗口仍仅有稀疏画面，Terry 独立场景也未得到可辨认的原生效果；前两次导出误省略 Mod 依赖，已废弃并修正采样工具，完整依赖版本仍未通过原生可见性验收。
>
> R28 的共用修复涉及行为片段内参数作用域、贴图渐变、旧版缺省尺寸、发射参数默认值、发射器与粒子旋转、有限引用时序、两类模型遮罩材质，以及独立预览的循环和初始取景。R28 历史回归为 VFX 634 项、编辑器相关 49 项、共享 Effect 3 项通过；其 265 份来源有 240 份像素、0 运行异常。这些旧记录不作为本轮重新执行的检查。
>
> 神话人物目录 `D:/WH3 Modding/MyMod/mod工程/@cth_shenhua_asseteditor` 中 135 份有效 VFX 与游戏 data 中同名 Mod Pack 的字节一致；另有 10 份材质 XML 和 2 份原有格式错误的 VFX，147 份源件已核对保持原散列。静态多模型列表已受限接入，原生随机种子和选择权重仍未同步；骨骼动画、晶体完整光照及其他专用材质、事件生成、部分朝向/曲线、引用与挂点运动仍有缺口。R31 玉龙静止拖尾已形成可辨认绿色晶体原生对照；更早其他腕部拖尾未显示的采样仍不能作为外观通过。最新运行入口是本仓库正常 Debug 输出，其他位置的安装版没有替换。

核对日期：2026-10-16。本文件是本轮未提交任务的一次性交接记录；稳定约束以 `AGENTS.md`、当前代码和相关文档为准。这里只完成交接，尚未完成游戏原生保真适配。

## 1. 从现有本地工作区接手

- 本地项目打开目录：`D:/Project/Szy-Cathay/TheAssetEditor-CN`。
- 当前且已经存在的分支：`codex/vfx-effekseer-preview`。
- 当前 HEAD 与本轮基线相同：`7c1b2a1ab8517e6bdc41efa79de2e9364761814b`（简称 `7c1b2a1a`）。本轮没有提交、推送、PR、合并或发布，暂存区为空。
- 实现保存在这个目录的未提交修改、未跟踪源文件及被 Git 忽略的构建/证据目录中。分支名本身不包含这些文件；仅新建或检出一个同名分支不能恢复成果。
- **必须原地核对工作区并保留全部已有成果。不要新建空分支替代，不要切换、重建、清空、收起或覆盖这批修改。** 若看到不同分支、HEAD 或额外修改，先说明差异，不自动恢复。
- 本任务在交接完成后暂停，不归档。接手前确认旧任务和其他任务没有同时在该目录编辑；不要并发修改同一工作区。

建议先只读执行：

```powershell
Get-Location
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
git status --untracked-files=all
git diff --stat
```

先读 `AGENTS.md`、`docs/architecture.md` 的“VFX 可视化编辑 / 场内预览的验证边界”；涉及 Effect 时读 `docs/superview.md` 的 7.5 节与第 17 节。修改界面时另读 `docs/ui-design-system.md`。

## 2. 已放弃的路线与当前底层

用户已放弃旧的游戏私有原生 DLL 接入调查；此前独占旧工作树已清理、旧分支删除、旧任务归档。不要恢复旧修改、复用旧 `vfx-native` 目录或运行游戏私有 DLL 探针。

当前路线是官方开源 Effekseer 的公开核心及 DX11 渲染器：

- 官方源：<https://github.com/effekseer/Effekseer>。
- 固定 tag：`170e`；源码提交：`92fdeb349c0c7eab9dfaf152047f22dec2fa102d`。
- 本地源码：`artifacts/effekseer-source/`，本次交接已只读核对其 HEAD。核心 MIT，输出另保留 DirectX 运行时及 stb 相关许可声明。
- C++ 桥：`GameWorld/Vfx.Effekseer.Native/`；它是本轮公开依赖的互操作层，与已放弃的游戏私有 DLL 路线不同。
- AECN 继续使用 `net10.0-windows`、WPF、MonoGame WindowsDX `3.8.4.1`，没有升级图形栈。
- 普通 Effekseer runtime 不能读取全战定义。本实现由 C# 读取游戏文档、转换语义并固定步长模拟，再将逐粒子数据交给公开 sprite/model renderer；不能描述为 Effekseer 已原生加载全战格式。

## 3. 已有实现与边界

- `IVfxPreview` 共用文档快照、资源、画质、时间、摄像机和根/骨骼挂点输入。VFX 编辑器与超级视图使用同一底层；其他 3D 场景可复用。
- 游戏原始文档始终是编辑、撤销和保存的权威来源。转换不删除未知字段、不把 Effekseer 数据写回游戏格式；曲线解析保留空通道与原有元数据。
- 编辑器通过共享快照存储发布未保存编辑，即使本页场内预览关闭，其他依赖场景也能消费。修改经短暂合并后刷新，不要求保存或重启；无效输入停止受影响项，修正后恢复。
- 场内候选预览默认关闭，界面明确标识验证用途。普通面片、单网格静态模型、静态引用组合、随机上下界曲线、图集、部分运动/材质语义已有转换。未支持的字段/资源/分支有诊断，必要时停用受影响层。
- DX11 桥复用 MonoGame 已有设备/上下文，传入摄像机、硬件深度及多重采样深度解析结果；扭曲使用背景副本，已确认的 DXT5n 法线做通道转换，完成后恢复绘制状态。
- 预乘透明材质在原纹理过滤与图集混合后拆分覆盖颜色和自发光两次绘制；两次都使用软边深度。未实现完整游戏光照、曝光、泛光、雾及透明合成。
- 超级视图保留 Effect 标记、选中/Gizmo 与双文档保存边界，粒子作为只绘制子节点跟随时间、根/骨骼变换；世界空间粒子读取历史骨骼姿态，不反复 seek 主播放器。
- 非 Kitbash 场景继续禁止整模型选择与编辑；本轮没有放开这条边界。

主要源码入口：

| 范围 | 路径 |
| --- | --- |
| 公共契约、实例、资源与互操作 | `GameWorld/View3D/Vfx/IVfxPreview.cs`、`EffekseerVfxPreview.cs`、`EffekseerNative.cs` |
| 游戏语义与固定步长模拟 | `GameWorld/View3D/Vfx/TotalWarVfxConverter.cs`、`TotalWarVfxModel.cs`、`TotalWarVfxSimulation.cs`、`TotalWarVfxMaterial.cs` |
| 共享未保存快照与场景节点 | `GameWorld/View3D/Vfx/VfxPreviewDocumentSource.cs`、`VfxPreviewSceneNode.cs`、`VfxPreviewComponent.cs` |
| 原生桥和构建约束 | `GameWorld/Vfx.Effekseer.Native/Bridge.cpp`、`Bridge.h`、`CMakeLists.txt`、`Build.ps1`、`Effekseer.stb.LICENSE.txt` |
| 注册与原生输出复制 | `GameWorld/View3D/DependencyInjectionContainer.cs`、`GameWorld/View3D/GameWorld.Core.csproj` |
| 编辑器接入与界面 | `Editors/VfxEditor/VfxScenePreview.cs`、`VfxEditorViewModel.cs`、`VfxEditorView.xaml`、`DependencyInjectionContainer.cs`、`Editors.VfxEditor.csproj` |
| 共享 Effect | `Editors/MetaDataEditor/AnimationMeta/SuperView/` 下的 `Visualisation/Instances/EffectMetaInstance.cs`、`Visualisation/MetaDataBuilder.cs`、`Visualisation/MetaDataResourceResolver.cs`、`SuperViewViewModel.cs` |
| 游戏曲线保留与中文资源 | `Shared/GameFiles/Vfx/VfxCurve.cs`、`AssetEditor/Language_Cn.json` |
| 核心验证与原生回归数据 | `Testing/GameWorld.Core.Test/Vfx/`、`Fixtures/NativeVerticalForce.json`、`Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj` |
| 编辑器/选择隔离/架构门禁 | `Testing/AssetEditorTests/VfxEditorTests.cs`、`View3DComponentIsolationTests.cs`、`SharedUiArchitectureTests.cs` |
| 超级视图专项 | `Editors/AnimationMeta/Test.AnimationMeta/EffectVfxPreviewTests.cs` |

表内同一行的后续简写文件名相对于该行已给出的目录；完整工作区逐文件清单见末节。

## 4. 真实资产覆盖不等于保真度

真实 XML 样本位于 `artifacts/vfx-samples/`；原版资源目录为 `E:/Steam/steamapps/common/Total War WARHAMMER III/data`。最新核心日志为 `artifacts/vfx-single-particle-calibration/native-motion-core.log`。

| 真实样本 | 类别 | 进入候选转换的层数 |
| --- | --- | --- |
| `wh_dlc08_lib_warshrine.xml` | 普通粒子 | 8 / 11 |
| `wh2_dlc17_lib_entity_spit.xml` | 网格粒子 | 2 / 3 |
| `wh2_dlc12_lib_lizard_acid_explosion.xml` | 扭曲 | 5 / 8 |
| `wh_dlc08_lib_pinning_icicles.xml` | 补充粒子样本 | 3 / 5 |
| `cine_embers.xml` | 补充余烬样本 | 2 / 2 |

这些数字与离屏像素证明部分原始资产进入转换并可绘制，不是还原度，也不是全部字段已支持。余烬原作含非零初速度、阻尼和随机力，仍超出下面的受限恒力条件；不能因其 2/2 层可见而宣称运动已还原。

较早的 Terry 余烬画面和 32 帧控制对照仅作定性参考，没有对齐原生随机种子及准确效果时钟。它们不能证明三类样本都完成原生保真对照。

## 5. 121 帧标定及严格受限的恒力结论

最新实际原生采样目录：`artifacts/vfx-single-particle-calibration/native-continuous-r3/`。

- Terry 固定场景连续记录 12 秒，保留 121 帧记录：102 帧检测到标记、19 帧未检测到标记；12 张原生截图齐全。源时间戳递增，错误 ROI/截图错误均为 0。
- 控制包由真实 `cine_embers` 发射器派生，9 列分别隔离静止、ForceA、速度和阻尼；它是明确的诊断样本，不冒充原作效果。使用时快照保存在 `prepared-used/`。
- 实际源数据：`frame-centroids.jsonl`，SHA256 为 `2ec5f4db4588e0aeb334dc32b8685fef10e1e6821d108620b42d65ac23e9772a`。`capture-summary.json`、`workflow-summary.json`、`closed.json` 分别记录采集与清理。
- 使用 WGC 源时间戳分析三个可见周期，出生时间保留前后帧区间；没有伪造准确的效果出生时钟。摄像机为 512/6/502、旋转 0/0/0、FOV60；根为 512/0/512、单位缩放。固定列间距支持约 85 像素/场景单位。
- ForceA.Y=100 的三个周期拟合换算约为 `0.0099077`、`0.0100354`、`0.0099849`，支持在已验证条件下采用 `0.01`。低幅 ForceA=1 部分周期受像素量化与合并影响，不能夸大其精度。
- 第一个周期用于选择候选；后两个周期的 60 个独立点按出生区间和 3 像素容差验证。冻结回归数据为 `Testing/GameWorld.Core.Test/Vfx/Fixtures/NativeVerticalForce.json`；旧实现先失败，修正后通过，并验证了实际 GPU 像素位移。

**当前实现仅对 `continuous_mesh` 面片的竖直恒定 ForceA，在零初速度、零阻尼、零发射器旋转、挂点/引用只有平移且引用时间倍率为 1 时施加 `ForceA.Y * 0.01` 加速度。** 非法/动态/随机力、其他轴、其他类型或超出变换/时间范围继续诊断，不猜系数。不要把该结论扩大到速度、阻尼、随机力或力与阻尼组合；也没有证明原生积分器与候选完全相同。

完整证据见 `trajectory-analysis.json`、`force-subset-validation.json`。其中旧分析文件的 `ForceUnitFactorAppliedToProduct=false` 是分析形成时的历史状态；当前实现已经加入上述受限恒力。不要将这个历史字段误解为当前完全未实现，也不要将它改写为整体标定完成。

## 6. 原生采样实际工具路线

核实记录：`artifacts/vfx-single-particle-calibration/native-continuous-r3/tool-route-verification.json`。

1. PowerShell 启动 `artifacts/vfx-inspection/` 的 .NET 控制工具；`SingleParticleCalibration.cs` 调用仓库已有 `Editors/VfxEditor/TerryPreviewLauncher.cs`，启动官方 `assembly_kit/binaries/tweak.modder.x64.exe`（本次 PID 16416）。
2. 已有启动器通过限定到该进程的 Windows UI Automation、控件焦点和定向 `WM_CHAR` 消息设置特效筛选；不是调用 Computer Use 插件。
3. 已有 `artifacts/vfx-native-comparison/capture-window.exe` 用公开 Windows Graphics Capture 取得准备截图，并请求一次激活本任务窗口；后续 `capture-calibration.exe` 用相同公开采集能力录制 12 秒，连续录制期间没有激活或输入。
4. .NET 工具对自己启动的 Terry 调用 `CloseMainWindow` 并等待正常退出，没有强杀 Terry。两个采集辅助进程正常退出；校验散列后移除游戏临时包，保留自己的禁用副本。详情见 `capture.log`、`workflow-events.jsonl` 和 `closed.json`。

**本次未调用 Computer Use 插件，采集成功不能证明该插件已恢复。** 不要把“连续采集无输入”写成整个启动准备阶段没有窗口操作。

历史实机授权限定在已结束的那一次时段。现在游戏临时包不存在，本任务 Terry/采集实例已退出。下一次 GUI、激活窗口或输入操作需重新约定授权；本交接不授权重放这些工具。

## 7. 最新验证、复用与未跑范围

最新汇总：`artifacts/vfx-validation-summary.json` 的 `LatestValidation`，以及 `artifacts/vfx-single-particle-calibration/native-continuous-r3/offline-followup-validation.json`。

| 范围 | 结果与来源 |
| --- | --- |
| Debug 主程序、核心、相关测试项目构建 | 成功，退出 0；有既有编译警告，没有错误 |
| 核心 VFX 专项 | 55 通过、0 失败、0 跳过；`artifacts/vfx-expanded-test-results/vfx-native-motion-core.trx` |
| 编辑器、共享 UI 与选择隔离专项 | 47 通过、0 失败、0 跳过；`artifacts/vfx-expanded-test-results/vfx-native-motion-editor.trx` |
| 更新后的中文提示面板 | 单独复测 1 用例通过，四主题离屏渲染；`artifacts/vfx-expanded-test-results/vfx-native-motion-ui.trx`。这是前述用例的更新复测，不另加成一个独立总数 |
| 超级视图 Effect | 复用前一恒力阶段的 3 通过、0 失败、0 跳过；`artifacts/vfx-expanded-test-results/vfx-native-force-superview.trx`。后续诊断修正未改其场景/时间代码 |
| 恒力阶段 | 核心 54、编辑器 47、超级视图 3 项均通过；属前一阶段记录，不与最新结果相加 |

没有以局部测试冒充完整解决方案验证。未执行本轮全解决方案测试、Release/CI/发布验证；会显示窗口的 `VisualEditor_RendersAllThemesAndScaledLayoutsWithoutChangingData` 被明确排除。没有运行游戏战斗/技能逻辑实测，也没有完成所有效果的同时间、同随机种子、同光照原生对照。Terry 控制标定、离屏像素与用户人工认可不能互相替代。

本次文档交接没有重新构建、运行测试或采样；只读复核已有 TRX/日志、文件路径、Git 状态与文档内容。回归过程中的 `*-red.trx` 是预期的修复前失败证据，不是当前未解决失败。

## 8. 构建、测试与证据位置

以下命令仅供本地接手后按实际改动范围使用，本次交接没有执行它们。使用正常 Debug 输出，串行低负载；用户还有 Blender 动画任务，避免高负载并发。

原生桥已存在；仅在相关源码改变或输出缺失时重建：

```powershell
& ./GameWorld/Vfx.Effekseer.Native/Build.ps1 -EffekseerSource ./artifacts/effekseer-source -VisualStudioDirectory 'D:/Software/VisualStudio/2026'
dotnet build AssetEditor/AssetEditor.csproj --configuration Debug --no-restore --verbosity quiet -m:1 -nr:false -p:UseSharedCompilation=false
```

普通 .NET 构建不自动下载或编译原生依赖。`CMakeLists.txt` 检查固定源码提交及运行时相关目录无已跟踪修改，不能随意换 DLL、升级依赖或跳过 ABI/许可校验。

现有正常输出：

- 主程序：`AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。
- 桥：`GameWorld/Vfx.Effekseer.Native/bin/Debug/AssetEditor.CN.Effekseer.dll`；同目录 `Effekseer.LICENSE.txt`、`Effekseer.stb.LICENSE.txt`。
- 现有桥输出 SHA256：`ee3a72b93166ddc14f6b5b5d890986b0a42555775fd5107a914821d81f5f6a92`；5 份输出核对记录在 `artifacts/vfx-native-force-native-copies.json`。

测试前先构建受影响测试项目；不要用旧程序集的 `--no-build` 结果验证新代码。此前使用的离屏环境：

```powershell
$repoVfx = 'D:/Project/Szy-Cathay/TheAssetEditor-CN'
$env:ASSETEDITOR_VFX_HEADLESS_TEST = '1'
$env:ASSETEDITOR_VFX_GAME_DATA = 'E:/Steam/steamapps/common/Total War WARHAMMER III/data'
$env:ASSETEDITOR_VFX_SAMPLES = Join-Path $repoVfx 'artifacts/vfx-samples'
$env:ASSETEDITOR_VFX_RENDER_OUTPUT = Join-Path $repoVfx 'artifacts/vfx-native-force-pixels'
```

相关筛选命令（具体 TRX 输出命令和退出码保存在最新汇总中）：

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj --configuration Debug --no-build --no-restore --filter FullyQualifiedName~GameWorld.Core.Test.Vfx
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Debug --no-build --no-restore --filter '(FullyQualifiedName~VfxEditorTests&FullyQualifiedName!~VisualEditor_RendersAllThemesAndScaledLayoutsWithoutChangingData)|FullyQualifiedName~View3DComponentIsolationTests|FullyQualifiedName~SharedUiArchitectureTests'
dotnet test Editors/AnimationMeta/Test.AnimationMeta/Test.AnimationMeta.csproj --configuration Debug --no-build --no-restore --filter FullyQualifiedName~EffectVfxPreviewTests
```

关键日志/证据：

- 当前主程序构建：`artifacts/vfx-single-particle-calibration/native-motion-diagnostic-app-build.log`、`native-motion-diagnostic-app-final-build.log`；核心/编辑器测试构建：同目录 `native-motion-diagnostic-core-build.log`、`native-motion-diagnostic-editor-build.log`、`native-motion-diagnostic-ui-build.log`。
- 最近原生材质构建：`artifacts/vfx-postfilter-native-build.log`；五份原生输出散列：`artifacts/vfx-native-force-native-copies.json`。
- 最新真实样本/GPU 像素：`artifacts/vfx-native-force-pixels/`；最新中文面板四主题图：`artifacts/vfx-native-motion-ui/`。
- 已有原生/候选余烬参考：`artifacts/vfx-native-controlled-r2/aggregate-evidence.json`、`artifacts/vfx-native-controlled-r2/temporal-identifiability-evidence.json`、`artifacts/vfx-native-comparison/cine_embers-ground-calibrated/aec-candidate-8s.png`。这些是定性或旧阶段记录，不是已完成的保真比较。
- 最新采集/单位证据：`artifacts/vfx-single-particle-calibration/native-continuous-r3/` 中的 `capture-summary.json`、`frame-centroids.jsonl`、`frame-0.png` 至 `frame-11.png`、`trajectory-analysis.json`、`force-subset-validation.json`、`existing-motion-lighting-audit.json`、`offline-followup-validation.json`、`tool-route-verification.json`、`closed.json`、`final-state.json`。
- 固定样本/原始工具快照：该目录 `prepared-used/`；旧失败尝试及 `final-state-before-*` 仍保留，不当作最新结果。
- 一次性离线工具源码：`artifacts/vfx-inspection/NativeTrajectoryAnalysis.cs`、`ExistingNativeEvidenceAudit.cs`、`SingleParticleCalibration.cs`、`CalibrationSessionWorkflow.cs`、`OwnedCaptureProcessRunner.cs`、`OfflineSamplerVerification.cs`、`Program.cs`、`vfx-inspection.csproj`。该入口同时含历史实机命令，不能不看参数就运行。
- 采集源码：`artifacts/vfx-single-particle-calibration/CaptureRecording.h`、`ContinuousCapture.h`、`capture-calibration.cpp`；准备截图源码：`artifacts/vfx-native-comparison/capture-window.cpp`。
- 从原版数据包读取的可读游戏着色器：`artifacts/vfx-samples/particle_vfx_library.hlsl`；既有模拟着色器文本：`artifacts/vfx-shader-inspection/fx_particle_vfx_simulation_cs_hlsl_4188af96aa55a4af7ef73433dbaa675e.hlsl_compiled.txt`。它们没有提供已证明的 CPU 参数打包/单位映射，不要改走私有 DLL 探针。

`artifacts/`、`bin/`、`obj/` 等通常被 Git 忽略；它们当前仍在本地，包含继续工作所需的官网依赖、可运行 Debug 输出和证据。本次没有清理、移动、归档或复制到另一个工作区。

## 9. 尚未确定的部分、下一项必要验证及授权

目前没有完成保真适配：初速度单位及游戏缺省发射方向、阻尼单位及与力场组合、其他力/动态曲线、旋转缩放挂点和非单位时间倍率下的力语义、游戏光照与 emissive 参数、曝光/泛光/雾/半分辨率/透明排序、事件链和专用材质等仍有限制。

现有 121 帧中相同 speed-1 输入在不同周期有不同水平运动方向、标记尺寸也变化，不能从一个屏幕斜率反推速度单位。drag-5 只在最初几帧有可分辨位移，出生区间约 0.107 秒；高阻尼的早期变化更难分辨。标记亮部红/蓝通道中位数已到 255，无法反推未截断的辐射亮度。源码 `ps_lighting` 将 emissive 送入光照函数，不能直接等同于纹理自发光的统一增益。

下一项真正必要的原生验证：先证明显式发射设置能固定方向与深度，再用同一速度的无阻尼/有阻尼控制，记录出生前后和早期轨迹并保证足够时间分辨率；力与阻尼组合另行对照。亮度需要不饱和的阶梯样本和可记录的曝光/环境光条件。不要继续用已截断的洋红标记猜亮度系数。

当前用户要求只完成文档与交接，然后暂停。原任务已有官方 Effekseer 路线、必要本地 Debug 构建及对应离线验证的授权，但不意味着此交接阶段可以自动继续功能实现。恢复工作后的范围以本地用户指令及根 `AGENTS.md` 为准。

已结束的实机时段不构成永久许可。新 Terry/游戏窗口、激活或输入、指定范围之外的外部写入/依赖，以及提交、推送、PR、合并、发布等尚未获得本次交接授权；不能自动执行。没有任务收尾、清理或归档授权。本文件不新增自定义审批规则。

## 10. 接手启动指令

> 请在当前目录接手 VFX 任务，先读 HANDOFF-VFX-LOCAL.md 和 AGENTS.md，只读核对分支 codex/vfx-effekseer-preview、HEAD、全部未提交及未跟踪文件；原地保留已有成果，不创建替代分支，不与其他任务同时编辑。先报告接手状态和下一项必要验证，暂不启动 GUI、采样或执行 Git 交付操作。

## 11. 本次交接完成时的工作区清单

以下是本地 `git status --porcelain=v1 --untracked-files=all` 的一次性清单，包含本轮全部源文件与本次交接文档；不包含被忽略的构建及证据。AGENTS 只补稳定阅读入口，文档索引增加同一入口，架构文档补验证限制，未增加功能代码。

```text
 M AGENTS.md
 M AssetEditor/Language_Cn.json
 M Editors/MetaDataEditor/AnimationMeta/SuperView/SuperViewViewModel.cs
 M Editors/MetaDataEditor/AnimationMeta/SuperView/Visualisation/MetaDataBuilder.cs
 M Editors/MetaDataEditor/AnimationMeta/SuperView/Visualisation/MetaDataResourceResolver.cs
 M Editors/VfxEditor/DependencyInjectionContainer.cs
 M Editors/VfxEditor/Editors.VfxEditor.csproj
 M Editors/VfxEditor/VfxEditorView.xaml
 M Editors/VfxEditor/VfxEditorViewModel.cs
 M GameWorld/View3D/DependencyInjectionContainer.cs
 M GameWorld/View3D/GameWorld.Core.csproj
 M Shared/GameFiles/Vfx/VfxCurve.cs
 M Testing/AssetEditorTests/SharedUiArchitectureTests.cs
 M Testing/AssetEditorTests/VfxEditorTests.cs
 M Testing/AssetEditorTests/View3DComponentIsolationTests.cs
 M Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj
 M docs/README.md
 M docs/architecture.md
 M docs/superview.md
?? Editors/AnimationMeta/Test.AnimationMeta/EffectVfxPreviewTests.cs
?? Editors/MetaDataEditor/AnimationMeta/SuperView/Visualisation/Instances/EffectMetaInstance.cs
?? Editors/VfxEditor/VfxScenePreview.cs
?? GameWorld/Vfx.Effekseer.Native/Bridge.cpp
?? GameWorld/Vfx.Effekseer.Native/Bridge.h
?? GameWorld/Vfx.Effekseer.Native/Build.ps1
?? GameWorld/Vfx.Effekseer.Native/CMakeLists.txt
?? GameWorld/Vfx.Effekseer.Native/Effekseer.stb.LICENSE.txt
?? GameWorld/View3D/Vfx/EffekseerNative.cs
?? GameWorld/View3D/Vfx/EffekseerVfxPreview.cs
?? GameWorld/View3D/Vfx/IVfxPreview.cs
?? GameWorld/View3D/Vfx/TotalWarVfxConverter.cs
?? GameWorld/View3D/Vfx/TotalWarVfxMaterial.cs
?? GameWorld/View3D/Vfx/TotalWarVfxModel.cs
?? GameWorld/View3D/Vfx/TotalWarVfxSimulation.cs
?? GameWorld/View3D/Vfx/VfxPreviewComponent.cs
?? GameWorld/View3D/Vfx/VfxPreviewDocumentSource.cs
?? GameWorld/View3D/Vfx/VfxPreviewSceneNode.cs
?? HANDOFF-VFX-LOCAL.md
?? Testing/GameWorld.Core.Test/Vfx/EffekseerVfxPreviewTests.cs
?? Testing/GameWorld.Core.Test/Vfx/Fixtures/NativeVerticalForce.json
?? Testing/GameWorld.Core.Test/Vfx/TotalWarVfxConverterTests.cs
?? Testing/GameWorld.Core.Test/Vfx/TotalWarVfxMaterialTests.cs
?? Testing/GameWorld.Core.Test/Vfx/TotalWarVfxSimulationTests.cs
?? Testing/GameWorld.Core.Test/Vfx/VfxPreviewDocumentStoreTests.cs
?? Testing/GameWorld.Core.Test/Vfx/VfxVerticalForceTests.cs
```

交接完成后旧任务保持暂停，等待用户在本地项目对话接手，不自动归档。
