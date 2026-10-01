# Mutagen Native AOT 与 trimming 路线图

> 状态：持续核查中的实施路线图。PR 编号表示建议的依赖关系，不表示 PR 已创建、代码已实现或兼容性已验证。本文区分「源码已确认」「分析器已复现」与「仍须发布验证」。

## 目标

从 `Mutagen.Bethesda.Skyrim` 的实际消费路径开始，使运行时类库逐步支持 Native AOT，并让未使用的功能能够被有效裁剪。分别验证类型化 Skyrim 调用、通用工厂调用和二进制读写，再扩展到其余游戏与可选包。构建期生成器和 WPF 前端单列评估；「整个项目完全兼容」须先明确这些边界，并通过各自的消费者发布测试。

## 已核实的边界与当前基线

- [`Mutagen.Bethesda.Skyrim.csproj`](../Mutagen.Bethesda.Skyrim/Mutagen.Bethesda.Skyrim.csproj) 目标框架为 `net9.0;net10.0`，依赖 Core、Loqui 与 Noggog.CSharpExt。[`Directory.Packages.props`](../Directory.Packages.props) 固定 Loqui / Loqui.Generation `3.7.0` 和 Noggog.CSharpExt `4.3.0`。相邻的 `../Loqui`、`../CSharpExt` 是独立源码树；当前项目引用的是包，修改相邻源码不会自动影响消费者构建。
- [`Mutagen.Bethesda.Generation`](../Mutagen.Bethesda.Generation) 通过 Loqui.Generation 离线生成记录代码；[`RecordGeneratorProvider.cs`](../Mutagen.Bethesda.Generation/Generator/RecordGeneratorProvider.cs) 装配生成模块。Skyrim 项目显式编译签入的 `*_Generated.cs`。[`Mutagen.Bethesda.SourceGenerators`](../Mutagen.Bethesda.SourceGenerators) 目前不是这些记录的生成器。故生成期使用反射不能直接等同于运行时 AOT 障碍，应逐条追踪生成结果的运行时调用。
- 已对 Skyrim 项目执行 `dotnet restore`，并以 `EnableTrimAnalyzer=true`、`EnableAotAnalyzer=true`、`GeneratePackageOnBuild=false` 重建：**0 error、121 warning，其中 37 个独立 IL 诊断位置**；这 37 个位置都在 `Mutagen.Bethesda.Core`，其中一个源自外部 `GameFinder.StoreHandlers.Xbox.XboxHandler` 的调用。按独立位置计数：IL2026×4、IL2032×1、IL2057×10、IL2060×1、IL2070×2、IL2075×12、IL2090×2、IL3050×5。重复打印的 warning 未重复计数。
- 这只是**库构建分析器基线**，不是 `PublishTrimmed` 或 `PublishAot` 的消费者发布结果，也不是运行行为、大小或裁剪粒度的证明。第一项 PR 要建立可复现的消费者探针。
- 已另用仓库外 `net10.0/win-x64` 消费者仅直接构造 `SkyrimMod`，执行 `PublishTrimmed=true`：发布成功，日志出现 42 条 IL warning 行（含依赖程序集 IL2104）；**运行失败**，`SkyrimListGroup<T>` 静态初始化调用 `PluginUtilityTranslation.GetRecordType<T>()` 时，Loqui 报 `Type was not a Loqui type: Mutagen.Bethesda.Skyrim.CellBlock`。这表明该最小消费者未得到所需注册；尚不能把原因单独归结为裁剪、自动发现或未调用 `Warmup.Init`，需要与未裁剪运行和显式初始化对照。对应顺序探针已加入仓库。
- 已加入 [`Mutagen.Bethesda.AotProbe`](../Mutagen.Bethesda.AotProbe/Program.cs) 独立进程探针。普通 Release：`explicit-first` 在首次 Loqui 查询前调用现有 `Initialization.SpinUp`，注册数 **1**；`query-first` 先查询 Loqui，显式调用前已有 **881** 个注册，调用后仍为 881；`read-esp` 经 `Warmup.Init` 成功读出用户目录中的 `CommunityShaders_AIO/TerrainHelper.esp`（194 字节）和 `SkyUI/SkyUI_SE.esp`（2388 字节）。相同探针 trimmed 发布成功，首次完整日志有 **49 条 IL warning 行**，包括 Loqui 与 CSharpExt 各一条 IL2104；运行 `explicit-first` 仍为 **1**，`query-first` 变为 **3 → 3**，而 `read-esp SkyUI_SE.esp` 因 `CellBlock` 未注册而失败。Native AOT 发布也成功，首次完整日志有 **59 条 IL warning 行**；显式先行通过，ESP 读取仍失败，查询先行触发自动扫描时反射构造协议失败。后续增量发布不会重报所有诊断，不能拿日志行数直接比较 PR 成效或与前述 37 个库源码位置相减。

官方判据：[为 trimming 准备库](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming/)、[Native AOT 部署](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)、[IL3050](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050)。`IsAotCompatible` 会启用相关分析并蕴含 `IsTrimmable`；它们应在目标消费者发布与行为验证后逐项目声明。

## Stacked PR 依赖图

| PR | 仓库 | 可审查的变更单元 | 前置 |
| --- | --- | --- |
| M0 | Mutagen | Skyrim 消费者发布探针与基线。 | 无 |
| M2 | Mutagen | Skyrim 静态协议、映射、mask 和 Mod 注册入口。 | M0；不依赖 L1 |
| M3 | Mutagen | `ModFactory` 普通创建/导入的强类型工厂表。 | M2 |
| M4 | Mutagen | `MajorRecordInstantiator` 的强类型构造入口。 | M2 |
| M5 | Mutagen | 可变二进制记录的同步/异步静态读取工厂。 | M2 |
| M6 | Mutagen | Overlay、subgroup 与相关记录元数据的静态工厂。 | M5 |
| M7a–c | Mutagen | 类型字符串/FormLink、记录类型映射、属性枚举分别处理。 | M2；消费探针 |
| M8a–b | Mutagen | 动态泛型分派与多文件 overlay 分别处理。 | M3/M6 |
| M9a–b | Mutagen/依赖库 | AssetType 扫描与 Xbox 依赖警告分别处理。 | M0；对应调用证据 |
| C1 | CSharpExt/Loqui | 仅对发布探针仍可达的上游动态调用做独立修复。 | M5/M6 后重新发布验证 |
| M10 | Mutagen | 按游戏逐个推广生成契约，再验证聚合包。 | Skyrim 路径通过 |
| M11 | 各可选项目 | Json、Autofac、Sqlite、WPF 分别确定支持边界。 | M10；各自发布探针 |
| M12 | Mutagen | 裁剪保留量预算与逐项目兼容性声明。 | 目标消费者验证完成 |
| L1（非主线） | Loqui | 仅当要承诺「查询先于显式初始化」仍能自动发现所有协议时，另行设计 AOT 契约。 | M0/M2 的公开 API 范围决定 |

`L1` **不在 Skyrim 主线的前置链中**：现有 Loqui 显式注册在 trimmed 与 Native AOT 发布里可用，当前读取失败由 Mutagen 的协议发现造成。只有决定继续支持先查询 Loqui 的旧自动发现语义，才另开 L1 设计/实现 PR。`C1` 也只在消费者探针证明必要时创建。跨仓库后继 PR 依赖**实际发布的包版本**，并各自更新包版本与锁定测试，不能把相邻源码目录的改动视为已经进入 Mutagen。M4、M5 和 M7 的独立部分可以在 M2 后并行开发，但每个 PR 都应单独构建、验收。

## 逐项 PR 说明与验收

### M0 — 固定 Skyrim 消费者基线

**目标。** 新建最小的独立 `net9.0`/`net10.0`、`win-x64` 消费者探针，固定对 Skyrim 包的引用和公开调用。分别运行普通发布、`PublishTrimmed=true` 发布、`PublishAot=true` 发布。保留命令、SDK/RID、警告原文、运行结果与产物大小；发布结果应单独记录，不能用库分析器构建替代。

**事实依据。** 当前仅复现了上述 37 个独立 IL 诊断位置。官方文档要求用真实应用的发布分析完整依赖图；根程序集设置可帮助诊断，却不能证明高裁剪率。探针至少覆盖：直接 `new SkyrimMod(...)`；`ModFactory`/接口类型创建和导入；`MajorRecordInstantiator`；可变记录的同步与异步二进制读取；overlay/subgroup；FormLink 和映射；不同初始化顺序（首次触碰 Loqui 前/后）。测试 fixture 应选合法且可提交的最小插件，验证读出的记录类型、FormKey 和关键字段，不只验证进程退出码。

**验收。** 报告每个场景的构建/发布/运行状态、诊断归属（本仓库或外部包）及裁剪前后大小；把失败路径收窄成可重复的单项调用。基线 PR 不宣称 AOT 兼容。

**已发现失败。** 最小 `SkyrimMod` 构造的 trimmed 消费者在 `SkyrimListGroup<T>` → `PluginUtilityTranslation.GetRecordType<T>()` → `LoquiRegistration.GetRegister(typeof(CellBlock))` 处失败。普通、trimmed 与 Native AOT 的初步对照已确定 M2 需要静态可达的完整 Skyrim 协议；M0 仍需覆盖更多公开 API 场景。

### L1（非主线）— Loqui 旧自动发现语义的 AOT 契约

**决策。** **Skyrim 主线不需要 L1。** 已验证现有显式初始化在普通与 trimmed 发布中都能注册给定的 `SkyrimMod`，且不会先触发自动扫描。M2 要解决的是 Mutagen 未能静态提供完整 Skyrim 协议。如果产品目标还要求「调用方在显式初始化前查询 Loqui，仍可自动找到所有协议」，应把该旧语义单独定义为 L1；它依赖程序集扫描，不能默认为高度可裁剪。

**事实依据。** [`LoquiRegistration.cs`](../../Loqui/Loqui/Registration/LoquiRegistration.cs) 的静态构造函数在 `AutomaticRegistration` 为 true 时调用 `TypeExt.GetInheritingFromInterface<IProtocolRegistration>(loadAssemblies: true)`；[`LoquiRegistrationSettings.cs`](../../Loqui/Loqui/Registration/LoquiRegistrationSettings.cs) 默认设为 true。[`LoquiInitialization.cs`](../../Loqui/Loqui/Registration/LoquiInitialization.cs) **已经提供** `SpinUp(params IProtocolRegistration[])`，先将其设为 false 再注册；若这是首次接触 `LoquiRegistration`，无需新 Loqui API。但若其他入口已使静态构造函数运行，设置无法撤销先前扫描。Mutagen 的 [`Warmup.cs`](../Mutagen.Bethesda.Core/Plugins/Warmup.cs) 目前先按游戏名反射构造协议，再调用 `SpinUp`。

**实现边界。** 若以后决定实施 L1，必须先明确查询先行场景如何在没有全程序集扫描的条件下知道所有协议；不能通过无条件 root 所有程序集伪造自动发现。修改自动注册的旧语义要记录兼容影响。`LoquiRegister.TryGetRegister(Type)` 还有 `StaticRegistration` 的反射后备路径；已知注册可走显式表，未知类型的动态后备需准确标注。

**验收。** 仅在实施 L1 时要求两种顺序、重复注册、trim/AOT 消费者发布及运行均通过；Mutagen 若消费新的 Loqui 功能再升级已发布包。当前保留顺序探针作为回归证据，M2 不等待 L1。

**已运行的对照。** 探针普通 Release 在两个独立进程中得到 `explicit-first: 1` 与 `query-first: 881 → 881`；trimmed 发布后对应结果是 `1` 与 `3 → 3`。Native AOT 下 `explicit-first` 仍为 1 且通过；`query-first` 在 Loqui 静态构造的自动扫描中因 `Activator.CreateInstance` 抛 `MissingMethodException`。初次失败对象是探针自己的 `SkyrimModProtocol`；为排除探针干扰，已只对其公开构造函数添加 `DynamicDependency`，再次发布后失败对象变为 **真实的 `Loqui.ProtocolDefinition_Bethesda`**。这确认旧自动发现路径有实际 AOT 失败，但不改变 M2 对现有显式入口的可用性判断。M2 必须使完整 Skyrim 协议在消费者中静态可达，不能只注册 Mod 类型。

**ESP 对照。** `explicit-read-esp` 与 `query-read-esp` 各在独立的普通 Release 进程中运行，使用用户目录 `SkyUI/SkyUI_SE.esp`：前者 `before-warmup=1`，后者 `before-warmup=881`，两者经 `Warmup.Init` 均报告 `games=Skyrim`、0 master、7 条 major records。更新后的 trimmed 发布中，前者 `before-warmup=1`、后者 `before-warmup=3`，两者均报告 `games=`（空），随后在 `CellBlock` 注册缺失处失败。这把问题明确定位到裁剪后 `Warmup` 未找到 Skyrim 协议；即使 Loqui 显式初始化先行，只登记一个 Mod 类型也不足以读取插件。

### M2 — Skyrim 静态注册与映射组合

**目标。** 让 Core 接受由 Skyrim 提供的静态协议、接口映射、override mask 和 Mod 注册项，替换该游戏正常初始化路径上的按名字组装类型与 `Activator` 调用。Core 不能反向引用 Skyrim；契约应由 Core 定义，Skyrim 实现并在消费者可达的入口注册。直接复用现有 Loqui 显式初始化，不把 L1 作为前置。

**事实依据。** [`ProtocolDefinition_Skyrim.cs`](../Mutagen.Bethesda.Skyrim/Records/ProtocolDefinition_Skyrim.cs) 已直接枚举 `*_Registration.Instance`，其中包含 `SkyrimMod_Registration.Instance`。Skyrim 生成代码中已有 `SkyrimAspectInterfaceMapping`、`SkyrimInheritingInterfaceMapping`、`SkyrimLinkInterfaceMapping`、`SkyrimIsolatedAbstractInterfaceMapping`，另有 `SkyrimOverrideMaskRegistration`；Core 的四类 mapping、[`OverrideMaskRegistrations.cs`](../Mutagen.Bethesda.Core/Plugins/Cache/Internals/OverrideMaskRegistrations.cs)、[`GameCategoryExt.cs`](../Mutagen.Bethesda.Core/Extensions/GameCategoryExt.cs) 目前以 `GameCategory` 拼类型名并反射加载，分别对应多处 IL2057。`Warmup` 的 `Activator.CreateInstance` 对应 IL2032。

**实现边界。** 复用现有生成对象和映射，避免手写第二份记录清单。消费者在首次调用 Core 的游戏注册、warmup 或接口映射入口前，显式调用 `Mutagen.Bethesda.Skyrim.GameRegistration.Register()`。该公开调用使 Skyrim 程序集和完整注册协议在裁剪时静态可达；仅添加项目引用无法保证这一点。首次注册查询后，Core 拒绝新的静态游戏注册，避免派生缓存与注册表出现不同的游戏集合。未裁剪消费者仍可通过原有反射后备发现未显式注册的游戏。不要声称静态协议列表一定使所有解析代码被保留，也不要声称只引用协议就能达到高裁剪率；记录粒度须在 M12 用链接器保留报告测量。

**验收。** M0 相关初始化和映射场景在三种发布模式下行为一致；上述动态类型名路径不再由 Skyrim 正常路径触发；未知类别有明确错误或兼容后备，且后备的动态行为被标注。

**独立消费者。** [`Mutagen.Bethesda.CoreOnlyProbe.csproj`](../Mutagen.Bethesda.CoreOnlyProbe/Mutagen.Bethesda.CoreOnlyProbe.csproj) 的 `registered` 模式只静态引用公开注册入口，然后经 Core 查询 Skyrim 的 Mod 与 aspect mapping；`late` 模式在 Core 首次查询后尝试注册，预期得到明确异常；`warmup` 模式验证完整协议、`CellBlock` 注册和四类接口映射。每个模式须在新进程运行，并分别验证普通、trimmed 与 Native AOT 发布。裁剪消费者曾因 `AGroup<T>` 与 `AListGroup<T>` 的继承接口方法被移除而在协议初始化时失败；现针对这两个开放泛型基类保留必要的方法，普通与 trimmed 的 `warmup` 模式均已通过。

```powershell
rtk dotnet run -c Release --project Mutagen.Bethesda.CoreOnlyProbe/Mutagen.Bethesda.CoreOnlyProbe.csproj -- registered
rtk dotnet run -c Release --project Mutagen.Bethesda.CoreOnlyProbe/Mutagen.Bethesda.CoreOnlyProbe.csproj -- late
rtk dotnet run -c Release --project Mutagen.Bethesda.CoreOnlyProbe/Mutagen.Bethesda.CoreOnlyProbe.csproj -- warmup
rtk dotnet publish Mutagen.Bethesda.CoreOnlyProbe/Mutagen.Bethesda.CoreOnlyProbe.csproj -c Release -r win-x64 -p:PublishTrimmed=true -o "$env:TEMP\MutagenCoreOnlyProbe\trimmed"
rtk dotnet publish Mutagen.Bethesda.CoreOnlyProbe/Mutagen.Bethesda.CoreOnlyProbe.csproj -c Release -r win-x64 -p:PublishAot=true -o "$env:TEMP\MutagenCoreOnlyProbe\aot"
```

**已运行的失败路径。** 普通 Release 用 `Warmup.Init` 可读 `SkyUI_SE.esp`；同一探针的 trimmed 发布在显式先行和查询先行两种模式下都得到 `games=` 空列表，并在 `PluginUtilityTranslation.GetRecordType<CellBlock>()` 失败。Native AOT 的 `read-esp`/`explicit-read-esp` 也得到空列表与相同缺失注册；`query-read-esp` 更早停在 Loqui 自动发现时的反射构造异常。这将「完整 Skyrim 协议静态可达」提升为 M2 的硬性验收项。`explicit-first` 仅注册 `SkyrimMod` 的成功不能替代该验收。

### M3 — `ModFactory` 的静态 Skyrim 工厂

**目标。** 为普通创建、文件导入和 overlay 导入提供由 Skyrim 代码直接引用的委托/工厂对象；Core 按已注册的 `GameCategory`/`IModRegistration` 分派。保持现有泛型与非泛型公开入口的返回类型、释放语义和参数默认值。多文件 overlay 留给 M8b。

**事实依据。** [`ModFactory.cs`](../Mutagen.Bethesda.Core/Plugins/Records/ModFactory.cs) 的正常路径先由程序集限定字符串 `Type.GetType` 找 Mod 类型，再在 `ModFactoryReflection` 中枚举构造函数或 `CreateFromBinary`/`CreateFromBinaryOverlay` 重载，组合 `Expression.GetFuncType`、委托与 `DynamicInvoke`。现有 [`SkyrimMod_Generated.cs`](../Mutagen.Bethesda.Skyrim/Records/SkyrimMod_Generated.cs) 已公开 `(ModKey, SkyrimRelease, float?, bool?)` 构造函数，以及数个 `CreateFromBinary` 重载和 overlay 入口。由 [`ModModule.cs`](../Mutagen.Bethesda.Generation/Modules/Plugin/ModModule.cs) 生成适配器较适合维持所有游戏的一致性，但签名、`GameRelease` 到各游戏 release 的转换和重载选择必须逐项核对。

**实现与验收。** 先只支持 Skyrim 的已注册强类型路径，保持其他游戏既有后备行为并明确其动态诊断。使用同一组参数在旧普通运行与新普通/trim/AOT 消费者中比较实例类型、版本字段、读取结果和异常；确认 Skyrim 路径不再需要上述 `Type.GetType`、`Expression.GetFuncType` 与 `DynamicInvoke`。泛型与接口调用各测一次，防止只修一条分支。

**实现状态。** `ModModule` 生成 `IModFactory` 适配器，由 `SkyrimMod_Registration` 提供直接构造、单文件可变导入与 overlay 导入。消费者先调用 `Mutagen.Bethesda.Skyrim.GameRegistration.Register()`；Core 通过已登记的 Mod 类型/接口或 `GameCategory` 分派，不再为 Skyrim 建立反射委托。disposable getter 接口也显式登记。其他游戏按请求延迟建立原有反射委托，动态后备方法带 `RequiresUnreferencedCode`/`RequiresDynamicCode` 标注，未抑制调用处的诊断。生成器保留旧工厂的 `TargetInvocationException` 包装、参数默认值与释放语义；签入产物来自实际运行 Skyrim 生成器。

**消费者与边界。** 新增独立 [`ModFactory.AotProbe`](../Mutagen.Bethesda.ModFactory.AotProbe/Program.cs)，支持普通运行的旧反射路径与静态路径对照，检查所有 Skyrim release、header/FormID 参数、泛型/接口调用、文件释放和异常。最终 .NET 10 Native AOT 的创建、最小可变导入、overlay 导入和异常行为四组全部通过。自包含 fixture 分别覆盖仅含 TES4 头的合法插件和含一条 `GlobalFloat` 的插件；`read-esp` 接受外部插件路径，不复制用户文件。完整结果与复现命令见 [M3 验证记录](NativeAOT-M3-Validation.md)。M3 不声明任意记录的 trim/AOT 读取已经可用：含记录输入仍进入 M5 的 `LoquiBinaryTranslation` 和 M6 的 group/overlay 元数据路径；这些失败单独记录，不以最小文件导入成功代替完整解析验收。

**构造前置修复。** Native AOT 对照发现，直接构造 `SkyrimMod` 时 `SkyrimListGroup<CellBlock>` 和 `SkyrimGroup<T>` 仍按字段名读取注册的 `TriggeringRecordType`，字段被裁剪后失败。`TriggeringRecordModule` 因此同时为六类 Skyrim group 输出 `IGroupRegistration.RecordType`，`ModModule` 为 Mod 的顶层 group 输出记录触发类型清单；`PluginUtilityTranslation.GetRecordType<T>` 优先读取这些静态契约。此最小前置修复只覆盖 Mod 构造需要的 trigger，不扩展 M5 的记录解析工厂或 M6 的 group overlay 元数据。

**过渡契约与后续归属。** M3 保留上述最小前置修复；`IModFactory.TryGetGroupRecordType`、Mod 顶层 trigger 清单与 `IGroupRegistration` 并存属于明确的过渡设计。元数据契约统一由 M6 负责，不作为 M3 的验收或合并前置条件。统一时应让同一种元数据具有明确归属和一致的访问方式，使 Mod 工厂专注创建与导入。手写逻辑调整可能较集中，但若扩展到顶层 major record 的注册对象，会涉及大量生成产物；不为提前消除这项设计欠账扩大 M3 范围。

### M4 — `MajorRecordInstantiator` 的静态构造入口

**目标。** 生成或注册 `(FormKey, GameRelease) -> IMajorRecord` 的类型化构造函数，覆盖泛型与按运行时 `Type` 选择的入口。

**事实依据。** [`MajorRecordInstantiator.cs`](../Mutagen.Bethesda.Core/Plugins/Utility/MajorRecordInstantiator.cs) 的两条路径通过 `BindingFlags.NonPublic` 寻找构造函数并 `Expression.Lambda(...).Compile()`；当前相关反射有 IL2070/IL2075。直接生成委托能同时避免依赖私有构造函数在裁剪后存活，并让记录类型到工厂的映射可审查。

**实现与验收。** 由记录生成流程生成直接调用，不在 Core 手抄 Skyrim 全部记录；若生成代码对非公开构造函数的访问受类型可见性限制，先在生成结果中确认可编译的放置位置，再决定内部桥接方法。对真实记录子类、未知类型和错误 `GameRelease` 比较 FormKey、具体类型与异常；在发布后的消费者内验证。此 PR 不同时重写二进制读取。

### M5 — 可变记录的同步/异步二进制工厂

**目标。** 让 `LoquiBinaryTranslation` 的同步及异步创建函数通过已注册的静态工厂进入生成记录解析器，保留现有 `TypedParseParams` 和回退行为。

**事实依据。** [`LoquiBinaryTranslation.cs`](../Mutagen.Bethesda.Core/Plugins/Binary/Translations/LoquiBinaryTranslation.cs) 两处通过方法名查找 `CreateFromBinary`，随后调用 CSharpExt `DelegateBuilder.BuildDelegate`；两处泛型参数各有 IL2090。Skyrim 生成组代码和手写的 `DialogTopic`、`Perk`、`Worldspace` 都消费这条路径；不能只检查生成代码中的标准记录。

**实现与验收。** 为每个实际支持的记录/组生成明确方法组或小适配器，若手写对象无法生成则在其类型附近显式登记。测试同步、异步、标准记录和至少一个手写边界案例的字节解析、触发 record type 与异常；trim/AOT 发布后确认对应动态委托路径不可达。CSharpExt 的通用 `DelegateBuilder` 可以保留给其他调用者，除非发布证据表明其本身必须改。

**范围衔接。** M5 专注可变记录的解析工厂，沿用构造所需的现有 trigger 查询，不顺带统一或扩展整个元数据体系；契约归属与重复查询路径的收敛留给 M6。

### M6 — Overlay、subgroup 与记录元数据

**目标。** 在同一组解析契约中显式登记 overlay 工厂、subgroup 填充函数，以及读取所需的常量/标记，消除这条链上按名称取方法、属性和字段。

**事实依据。** [`LoquiBinaryOverlayTranslation.cs`](../Mutagen.Bethesda.Core/Plugins/Binary/Translations/LoquiBinaryOverlayTranslation.cs) 反射 overlay 创建方法并使用 `DelegateBuilder`；[`SubgroupsBinaryTranslation.cs`](../Mutagen.Bethesda.Core/Plugins/Binary/Translations/SubgroupsBinaryTranslation.cs) 反射 `PartialForm`、`Subgroups`、内部 setter 类型和 `ParseSubgroupsLogic`。[`AGroup.cs`](../Mutagen.Bethesda.Core/Plugins/Records/AGroup.cs) 通过字段名读取 `GRUP_RECORD_TYPE`；[`PluginUtilityTranslation.cs`](../Mutagen.Bethesda.Core/Plugins/Binary/Translations/PluginUtilityTranslation.cs) 以字段名读取 triggering record type。生成侧 [`SubgroupsModule.cs`](../Mutagen.Bethesda.Generation/Modules/Plugin/SubgroupsModule.cs) 和 [`PartialFormModule.cs`](../Mutagen.Bethesda.Generation/Modules/Plugin/PartialFormModule.cs) 已把部分信息写入注册对象，可从这里扩展契约。Skyrim 生成代码中有具体 overlay 工厂，且 [`TypedParseParams.cs`](../Mutagen.Bethesda.Core/Plugins/Binary/Translations/TypedParseParams.cs) 接受 `RecordTypeConverter?` 隐式转换；新委托不得悄悄丢掉转换。

**元数据契约统一。** 结合 `TriggeringRecordType`、`GRUP_RECORD_TYPE`、`PartialForm` 和 `Subgroups` 的实际消费语义，确定元数据在生成注册对象中的归属与 Core 查询入口。统一的是同一种知识的来源和访问方式，不要求把不同含义的标记合并为一个值，也不要求把所有解析能力塞进一个大接口；按实际能力保留小契约。替代路径验证通过后，删除 M3 在 Mod 工厂上的 trigger 查询和顶层清单，收敛 `PluginUtilityTranslation.GetRecordType<T>` 的重复静态查询路径。未迁移游戏仍需保留明确的兼容后备。

**拆分顺序与验收。** 若整体改动过大，可先以 M6a 完成元数据契约统一，再分 PR 处理 overlay 与 subgroup 工厂；各 PR 保持独立可审查。元数据迁移须复跑 M3 的创建、最小导入、异常及资源释放场景，确认删除过渡路径后普通、trimmed 与 Native AOT 行为一致。解析部分用有 group/subgroup 和带 record type 转换的 fixture 分别比较可变读取与 overlay 读取的记录顺序、类型、长度、释放行为及异常，并验证对应 IL2026/IL2075 是否消除。

### M7a — 序列化类型名与 FormLink 运行时类型

**目标。** 为确需跨进程保存的类型名定义稳定的记录类型 ID/解析表；为运行时 getter 类型建立已知类型工厂，保留无法识别 ID 的明确失败语义。

**事实依据。** [`FormLinkInformation.cs`](../Mutagen.Bethesda.Core/Plugins/FormLinkInformation.cs) 用 `Type.GetType(typeString)` 恢复类型，对应 IL2057；[`FormLinkMixIn.cs`](../Mutagen.Bethesda.Core/Extensions/FormLinkMixIn.cs) 在某些非泛型/运行时类型路径上使用 `MakeGenericType` 和 `Activator.CreateInstance`。后者并未在本轮库分析器基线中形成诊断，不代表发布后一定安全。首先要查清已有序列化文本是否暴露给用户以及旧名称是否必须读取。

**验收。** 旧格式样本往返、未知类型、派生 getter 和 nullable getter；裁剪/AOT 发布后复测。若改持久化格式，独立记录兼容与迁移策略，不和 M7b 合并。

### M7b — `RecordTypeLookup` 的静态映射

**目标。** 将运行时 `Type` 到 `RecordType` 的常用映射纳入生成/注册契约，并保留显式的未知类型处理。

**事实依据。** [`RecordTypeLookup.cs`](../Mutagen.Bethesda.Core/Plugins/Records/Mapping/RecordTypeLookup.cs) 枚举类型属性和接口上的关联特性，对应 IL2070。生成记录已有静态注册对象；利用这些对象建立映射需核对接口可能对应多个 record type 的语义，不能简单把所有属性读取替换为一对一字典。

**与 M6 衔接。** 优先核对能否复用 M6 的元数据契约；只有语义一致的部分才共享。不能把构造 group 所需的单个 trigger 直接视为具体记录、接口及多个关联 record type 的完整映射。

**验收。** 以具体记录、getter/setter 接口、多个关联记录类型和未知类型比较现有返回值或异常；发布诊断与实际调用均通过。

### M7c — `MajorRecordTypeEnumerator` 属性枚举

**目标。** 对已知 Mod 类型输出所需的记录属性元数据，替换依赖 `GetProperties(DeclaredOnly)` 的正常路径。

**事实依据。** [`MajorRecordTypeEnumerator.cs`](../Mutagen.Bethesda.Core/Plugins/Records/Mapping/MajorRecordTypeEnumerator.cs) 反射公开实例属性，对应 IL2075。此枚举的语义依赖继承层级、属性声明位置与顺序；生成契约应复制这些可观察结果，而不是只列注册表里的记录类型。

**验收。** 比较 Skyrim Mod 的属性集合、顺序及对应记录类型；裁剪后未使用属性不应仅因枚举表存在就被无条件保留，具体保留量留在 M12 量化。

### M8a — `ModToGenericCallHelper` 动态泛型分派

**目标。** 让实际调用方以静态游戏专用适配器调用目标泛型功能，避免正常路径 `MakeGenericMethod`。

**事实依据。** [`CategoryToGenericCallHelper.cs`](../Mutagen.Bethesda.Core/Plugins/Utility/CategoryToGenericCallHelper.cs) 内的 `ModToGenericCallHelper` 从注册对象的 setter/getter `Type` 构造泛型方法，对应 IL2060 与 IL3050；当前调用点在 [`ModCompactor.cs`](../Mutagen.Bethesda.Core/Plugins/Utility/DI/ModCompactor.cs)。先确认游戏类型组合，再决定生成少量适配器还是改调用方接口。

**验收。** 与原逻辑比较 compaction 输出及边界条件；Skyrim 消费者 trim/AOT 发布无此路径的动态泛型要求。

### M8b — 多文件 overlay 的独立处理

**目标。** 将 `ModFactory.CreateMultiFileOverlay` 的运行时类型构造换成游戏专用工厂，保留多文件合并顺序和 master 列表处理。

**事实依据。** [`ModFactory.cs`](../Mutagen.Bethesda.Core/Plugins/Records/ModFactory.cs) 这一分支单独使用 `Type.GetType`、构造函数反射、`List<>.MakeGenericType` 和 `Activator.CreateInstance`。它与 M3 的单文件创建/导入具有不同的输入、生命周期和风险，因此单独验收。

**验收。** 至少两个输入 overlay 的合并、重复记录、master 顺序与资源释放；单文件与多文件两组 AOT 消费者调用均通过。

### M9a — AssetType 的显式目录

**目标。** 用已知游戏的 `IAssetType` 清单替换默认运行路径中的程序集实现类扫描，并验证新增资产类型的注册规则。

**事实依据。** [`AssetTypeLocator.cs`](../Mutagen.Bethesda.Core/Assets/AssetTypeLocator.cs) 调用 CSharpExt `GetInheritingFromInterface` 并读取静态 `Instance`，对应 IL2075。清单应由拥有这些具体资产类型的程序集提供，Core 不应反向依赖所有游戏项目。

**验收。** 对 Skyrim 各已支持资产扩展名、路径和优先级比较查找结果；trim/AOT 发布后能发现预期类型，未知类型行为明确。

### M9b — Xbox 游戏定位依赖

**目标。** 隔离可选 Xbox 定位功能的 AOT 风险，并依据 `GameFinder` 上游实际支持情况选择修复、拆包或对该入口精确标注限制。

**事实依据。** [`GameLocator.cs`](../Mutagen.Bethesda.Core/Installs/GameLocator.cs) 的 `new XboxHandler(FileSystem.Shared)` 触发 IL2026；这是外部依赖声明传递来的 warning，不能靠修改 Mutagen 的反射代码解决。先以只调用其他商店定位功能和调用 Xbox 的两个消费者发布测试定位影响范围。

**验收。** 非 Xbox 场景不被无关调用拖入运行失败；Xbox 场景按最终支持决定测试或明确的公共 API 限制，不能用无条件 suppress 宣称全功能兼容。

### C1 — 仅修复仍可达的 Loqui/CSharpExt 动态调用

**目标。** 在 M5/M6 等静态工厂就位后重新发布探针，按实际可达调用决定上游最小 PR。Skyrim 主线的协议可达性由 M2 负责；可选的旧自动发现语义归 L1。此处只处理剩余运行时动态委托、泛型或复制工厂问题。

**事实依据。** Mutagen Core 当前直接调用 [`DelegateBuilder.cs`](../../CSharpExt/Noggog.CSharpExt/Utility/DelegateBuilder.cs) 的可见位置集中在 `LoquiBinaryTranslation`、`LoquiBinaryOverlayTranslation`、`SubgroupsBinaryTranslation`。Loqui 自身还有 `LoquiRegister` 的后备反射/复制工厂，但本轮 Mutagen 源码检索未证明 `GetCopyFunc` 进入目标 Skyrim 消费路径。CSharpExt `Enums<T>`/`MemberAccessor` 中也有 `Expression.Compile`；[Native AOT 文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)说明表达式编译在 AOT 下可解释执行，因此不能把每一处 `Compile` 直接判为必然运行失败。`Expression.GetFuncType`、未知泛型实例和靠名字寻找成员的保留问题仍须逐项验证。

**验收。** 每个上游 PR 指明触发它的消费者入口、发布诊断及调用栈，并有最小复现；发布新包后在 Mutagen 升级版本再运行同一探针。若 M5/M6 已切断某个调用，不为它预先重写整个上游库。

### M10 — 推广到其他游戏与聚合包

**目标。** 将 Skyrim 验证过的生成契约逐个应用于 Fallout3、Fallout4、Oblivion、Starfield；每个游戏 PR 自带类型化与通用入口的消费者测试。最后单独验证 [`Mutagen.Bethesda.csproj`](../Mutagen.Bethesda/Mutagen.Bethesda.csproj) 聚合包。

**事实依据。** 聚合包当前直接引用五个游戏项目及 Core；其他游戏同样有各自的 `ProtocolDefinition_*`，但 Mod 构造函数、release 枚举、记录结构和特殊解析路径不可从 Skyrim 的签名直接推断。以生成器修改加 Skyrim 产物为契约模板，再分游戏审查生成 diff，可把异常差异暴露出来。

**验收。** 每个游戏分别跑 M0 的普通/trim/AOT 场景和一份合法 fixture；跨游戏单消费者验证注册碰撞、游戏选择与裁剪后可用性。聚合包应报告所有游戏同时引用时的实际保留量，不能拿 Skyrim 单包尺寸代替。

### M11 — 可选项目分别作支持决定

| 项目 | 已核实的风险/依赖 | 独立 PR 的目标与验收 |
| --- | --- | --- |
| Json | [`FormKeyJsonConverter.cs`](../Mutagen.Bethesda.Json/FormKeyJsonConverter.cs) 用 `MakeGenericType` 构造 FormLink 类型，且使用 Newtonsoft.Json。 | 为已知记录类型提供静态转换/序列化契约；用真实 JSON 的读写、未知类型及 trim/AOT 发布验证。不能只凭主游戏包通过就给 Json 标兼容。 |
| Autofac | [`MutagenModule.cs`](../Mutagen.Bethesda.Autofac/MutagenModule.cs) 调用 `RegisterAssemblyTypes` 扫描 Core 程序集。 | 将需要的服务显式注册并在消费者中解析；核对 Autofac 本身的发布诊断和支持声明。 |
| Sqlite | [`SQLiteFormKeyAllocator.cs`](../Mutagen.Bethesda.Sqlite/Persistance/SQLiteFormKeyAllocator.cs) 使用 EF Core `DbContext` 与 `EnsureCreated`。 | 独立发布及数据库读写探针；[EF Core 官方文档](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)仍称其 NativeAOT 支持高度实验性且不建议生产使用。即使 Mutagen 本地代码零警告，也不能据此宣称此包生产级完全兼容。 |
| WPF | [`Mutagen.Bethesda.WPF.csproj`](../Mutagen.Bethesda.WPF/Mutagen.Bethesda.WPF.csproj) 启用 `UseWPF`。 | 单独记录桌面 UI 的支持范围与发布结果；[WPF 官方路线图](https://github.com/dotnet/wpf/blob/main/roadmap.md)将 trimming/NativeAOT 列为长期方向。运行时类库达标不自动覆盖 WPF。 |

这些是不同依赖链，不应放进一个“清理剩余警告”PR。若外部依赖暂时无法支持，文档应精确表述已通过的包、框架、RID 与功能范围；“整个项目完全兼容”在这些包未通过前只能是目标。

### M12 — 裁剪粒度与逐项目兼容声明

**目标。** 在运行正确性和诊断收敛之后测量留存率，随后按包声明 `IsTrimmable`/`IsAotCompatible` 并加入 CI 发布门禁。

**测量。** 至少比较：只引用 Core；只创建 `SkyrimMod`；只用一类记录；读取任意 Skyrim 插件；聚合五个游戏。对每组记录 IL/Native 二进制大小、链接器保留的程序集/类型/方法以及执行路径。允许“读取任意记录”按设计保留大量解析器，但“只用一类记录”应检查是否因全量协议清单、静态初始化或笼统 root 而保留全部游戏。为规模设基线与可解释预算，再决定是否需要拆分注册表或延迟生成入口；不能预设静态登记天然具有细粒度裁剪。

**声明顺序。** 先在项目中启用分析器并修复属于其声明范围的诊断；再以消费者 `PublishTrimmed`/`PublishAot` 与运行测试验收；最后设置兼容属性。官方[库准备指南](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming/)指出库分析与完整应用发布关注点不同；`IsAotCompatible` 蕴含 `IsTrimmable`。必要的 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` 应贴在真正的动态公共入口，并由 API 使用者看到，不用全局 suppress、linker XML 或无条件全量 root 来制造“零警告”。

## 复现命令与诊断归属

当前库分析器基线使用 SDK `10.0.401`、`win-x64` 环境。先还原再重建；只更改分析器属性不重新还原曾得到 **84 个普通 warning、无 IL warning**，不能拿它当有效的 trim/AOT 基线。

```powershell
dotnet restore Mutagen.Bethesda.Skyrim/Mutagen.Bethesda.Skyrim.csproj -p:EnableTrimAnalyzer=true -p:EnableAotAnalyzer=true -p:GeneratePackageOnBuild=false
dotnet build Mutagen.Bethesda.Skyrim/Mutagen.Bethesda.Skyrim.csproj -t:Rebuild -p:EnableTrimAnalyzer=true -p:EnableAotAnalyzer=true -p:GeneratePackageOnBuild=false -v:q
```

| 诊断 | 独立位置 | 已确认的主要入口 |
| --- | ---: | --- |
| IL2026 | 4 | `GameLocator` 的 XboxHandler；overlay/subgroup 的动态委托调用 |
| IL2032 | 1 | `Warmup` 的按名创建协议 |
| IL2057 | 10 | 四类接口映射、override mask、GameCategory→Mod、FormLink 类型名、ModFactory 类型名 |
| IL2060 | 1 | `CategoryToGenericCallHelper.MakeGenericMethod` |
| IL2070 | 2 | `MajorRecordInstantiator` 与 `RecordTypeLookup` |
| IL2075 | 12 | AssetType、MajorRecord、ModFactory、二进制元数据、subgroup、属性枚举等成员反射 |
| IL2090 | 2 | `LoquiBinaryTranslation` 同步/异步泛型创建 |
| IL3050 | 5 | 动态泛型调用与 `ModFactory` 的运行时代码生成需求 |

这张表按**源码位置去重**，不是警告实例总数，也不是发布时全部警告清单；具体抑制条件和泛型实例在消费者发布时可能变化。每个 PR 应把修复前后的完整发布日志附到自身说明，而不只用此汇总表。

### L1/Skyrim 消费者探针的运行方法

[`Mutagen.Bethesda.AotProbe.csproj`](../Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj) 是独立 `net10.0` 可执行消费者，直接引用 Skyrim 项目；每个模式必须用**新进程**运行，因为 Loqui 的静态构造只执行一次。ESP 路径作为参数传入，仓库不复制用户插件。用户给出的备份目录含有零字节的 `.esp` 占位文件，因此选择了已实际读通的 `SkyUI/SkyUI_SE.esp`（2388 字节、7 条 major records）。

```powershell
rtk dotnet build Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj -c Release
rtk dotnet run --no-build -c Release --project Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj -- explicit-first
rtk dotnet run --no-build -c Release --project Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj -- query-first
rtk dotnet run --no-build -c Release --project Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj -- explicit-read-esp 'C:\Games\Modding\MOData\SSE Backup\SkyUI\SkyUI_SE.esp'
rtk dotnet run --no-build -c Release --project Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj -- query-read-esp 'C:\Games\Modding\MOData\SSE Backup\SkyUI\SkyUI_SE.esp'
rtk dotnet publish Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj -c Release -r win-x64 -p:PublishTrimmed=true -o "$env:TEMP\MutagenAotProbe\trimmed"
rtk dotnet publish Mutagen.Bethesda.AotProbe/Mutagen.Bethesda.AotProbe.csproj -c Release -r win-x64 -p:PublishAot=true -o "$env:TEMP\MutagenAotProbe\aot"
```

发布后直接运行输出目录中的 `Mutagen.Bethesda.AotProbe.exe`，沿用相同模式和 ESP 参数。不要在一个进程里连续跑模式，也不要把不同发布产物写到同一输出目录。当前对照：

| 模式 | 普通 Release | trimmed 发布 | Native AOT |
| --- | --- | --- | --- |
| `explicit-first` | 1 个注册；通过 | 1 个注册；通过 | 1 个注册；通过 |
| `query-first` | 显式前 881 个；通过 | 显式前 3 个；通过，但扫描未保留完整游戏 | 自动扫描反射构造 `ProtocolDefinition_Bethesda` 时失败 |
| `explicit-read-esp` | `games=Skyrim`；7 条记录 | `games=` 空；`CellBlock` 未注册 | `games=` 空；`CellBlock` 未注册 |
| `query-read-esp` | `games=Skyrim`；7 条记录 | `games=` 空；`CellBlock` 未注册 | 自动扫描反射构造 `ProtocolDefinition_Bethesda` 时失败 |

`query-first` 的退出码为 0 仅表示显式追加的 `SkyrimMod` 注册可查，不表示自动扫描保留了全部 Skyrim 记录；因此必须结合 ESP 读取模式解释结果。

## 核查原则

- 每项都应在未裁剪、裁剪发布和 Native AOT 发布下验证相同的公开行为。
- 将构建分析器警告与发布时的完整依赖图警告分开记录。
- 只有在运行时行为和诊断均通过后，才标记 `IsTrimmable` 或 `IsAotCompatible`。
- 不以全局 suppress、root descriptor 或运行时全量注册来替代对高裁剪率的验证。

## 尚未证明的事项

- 已执行最小消费者与 L1 探针的 `PublishTrimmed`/`PublishAot` 和运行对照，但尚未完成全部公开 API 场景矩阵或链接器保留报告；因此还不能给出“第几个 PR 后全部功能 Native AOT 可运行”、具体减小比例或零警告承诺。
- Loqui 与 CSharpExt 邻接源码树是否与当前 NuGet 包 `3.7.0`/`4.3.0` 完全一致尚未核对提交哈希；上游 PR 实施前必须以实际包源码/符号和发布消费者交叉验证。
- 生成的协议清单是否无条件 root 全部 Skyrim 记录、静态适配器是否可在消费者仅用一类记录时被裁剪，都需要 M12 的保留分析；当前仅能确认清单有直接引用。
- 各游戏的非标准记录、特殊 release/overlay 行为和可选包上游支持须在其独立 PR 中复测。EF Core 与 WPF 当前官方支持状态使“整个项目完全兼容”的范围尤其需要逐项目明确。
