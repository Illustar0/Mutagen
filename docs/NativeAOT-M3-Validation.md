# M3 — Skyrim ModFactory 验证记录

验证环境：2026-09-30，Windows / `win-x64`，SDK `10.0.401`，消费者仅目标 `net10.0`。分支 `feat/aot-skyrim-mod-factory` 从 `aot` 的 `8b2a3bc3d2426dbdbe21e22780afe664b521289d` 创建。

## 实现

- Core 的内部 `IModFactory` 契约提供创建、单文件可变导入、overlay 导入及 disposable getter 类型。
- `ModModule` 生成直接调用适配器；Skyrim 的完整协议注册已有 `SkyrimMod_Registration.Instance`，因此沿用 M2 的公开 `GameRegistration.Register()` 入口。
- 泛型工厂按登记的 class/setter/getter/disposable getter 类型分派，非泛型工厂按游戏分类分派。未登记静态工厂的游戏保留延迟创建的反射后备路径。
- 生成适配器保留 `TargetInvocationException` 包装、header/FormID 默认值和输入资源所有权。没有新增兼容性声明、诊断 suppress 或 linker root。
- Native AOT 运行暴露构造前置问题：`SkyrimListGroup<CellBlock>` 和 `SkyrimGroup<T>` 通过反射查注册的 `TriggeringRecordType` 字段。`TriggeringRecordModule` 为六类 group 生成静态 `IGroupRegistration.RecordType`；`ModModule` 同时为 Mod 的顶层 group 生成记录触发类型清单，Core 优先使用这些静态入口。这不替代 M5/M6 的记录解析工作。

所有七个 `*_Generated.cs` 差异均由实际运行 Skyrim 生成器产生。顶层触发类型清单追加前，已验证七份产物重复生成的 SHA-256 一致；最后追加清单后仅重新生成并验证 AOT。

## 消费者与结果

消费者：[项目](../Mutagen.Bethesda.ModFactory.AotProbe/Mutagen.Bethesda.ModFactory.AotProbe.csproj)、[场景与复现命令](../Mutagen.Bethesda.ModFactory.AotProbe/README.md)。所有模式分别启动独立进程。

普通运行、trimmed 和原有 13 项测试是先前对照结果；最终追加顶层触发类型清单后，按用户要求只重新发布、运行 Native AOT，没有重复这三组验证。

| 场景 | 普通 `legacy` | 普通 `static` | trimmed `static` | Native AOT `static` |
| --- | --- | --- | --- | --- |
| 创建：所有 Skyrim release、三类 header 参数、三类 FormID 参数、六个工厂入口 | 通过 | 通过 | 通过 | 通过 |
| 仅 TES4/HEDR 插件的可变导入与输入释放 | 通过 | 通过 | 通过 | 通过 |
| 同一插件的 overlay 导入与 disposal | 通过 | 通过 | 通过 | 通过 |
| 错误 release、disposable 创建、非法 getter 操作、缺失文件和异常包装 | 通过 | 通过 | 通过 | 通过 |
| 含一条 `GlobalFloat`：FormKey / EditorID / 值的可变读取 | 通过 | 通过 | 失败：M5 | 失败：M5 |
| 同一记录的 overlay 读取 | 通过 | 通过 | 失败：M6 | 失败：M6 |
| 外部 `SkyUI_SE.esp`：7 条记录 | 通过 | 通过 | 直接可变读取在 M5 失败 | 直接可变读取在 M5 失败 |
| 外部 `TerrainHelper.esp`：1 条记录 | 通过 | 通过 | 直接可变读取在 M5 失败 | 直接可变读取在 M5 失败 |

`legacy` 表示保留的反射后备分支，不表示旧版本完整二进制，也不承诺该分支的 trim/AOT 支持。创建检查比较实际类型、ModKey、release、header version 和初始 FormID。最小导入检查默认/显式读取参数、头字段以及文件独占访问。含记录 fixture 检查 `FormKey=000800:FactoryProbe.esp`、`EditorID=FactoryGlobal`、`IGlobalFloatGetter.Data=42`。外部 ESP 比较头字段、master 顺序及 major record 的 FormKey/EditorID 和枚举顺序，不修改或提交用户插件。

外部输入：

- `C:\Games\Modding\MOData\SSE Backup\SkyUI\SkyUI_SE.esp`，2388 字节。
- `C:\Games\Modding\MOData\SSE Backup\CommunityShaders_AIO\TerrainHelper.esp`，194 字节。

此前现有 ModFactory 测试及当时新增的两个 Oblivion 用例共 **13 项通过、0 失败**，使用以下命令。这是调整测试前的历史结果，不代表当前测试已重新运行：

```powershell
rtk proxy dotnet test Mutagen.Bethesda.UnitTests/Mutagen.Bethesda.UnitTests.csproj -c Release -f net10.0 -p:GeneratePackageOnBuild=false --filter FullyQualifiedName~ModFactory
```

测试审查后，新增文件改为 [`ModFactoryCreationTests.cs`](../Mutagen.Bethesda.UnitTests/Plugins/Records/ModFactoryCreationTests.cs)：分别验证泛型与非泛型公开创建入口的返回类型、ModKey 和显式 header 参数，每个用例仅执行一次创建。删除混合多个导入入口与记录枚举的用例，去掉对内部反射后备分支的承诺。导入、资源释放及 AOT 行为由上述独立消费者验证，没有新增测试项目或进程启动框架。本次调整未重新运行普通测试、trimmed 或 AOT。

## 生成复现

```powershell
rtk proxy dotnet build Mutagen.Bethesda.Skyrim.Generator/Mutagen.Bethesda.Skyrim.Generator.csproj -c Release -f net10.0 -p:GeneratePackageOnBuild=false
Set-Location Mutagen.Bethesda.Skyrim.Generator/bin/Release/net10.0
rtk proxy dotnet Mutagen.Bethesda.Skyrim.Generator.dll
```

生成器以输出目录作为工作目录，项目定位沿用现有四级相对路径。`TriggeringRecordModule` 只为有单个触发类型的 group 输出契约；其他对象仍走原有记录元数据后备路径。

## 诊断与范围

动态后备方法精确标记 `RequiresUnreferencedCode` / `RequiresDynamicCode`，调用点仍产生 IL2026 / IL3050，因为分析器无法证明运行前已经登记静态工厂。正常登记的 Skyrim 工厂在运行时直接调用生成代码；不依赖 `Type.GetType`、`Expression.GetFuncType` 或 `DynamicInvoke` 来构造工厂。多文件 overlay 保留在 M8b。

最终 Native AOT 的四组工厂场景全部输出 `PASS`、退出码 0。含记录的可变导入及两个外部 ESP 均在直接 Skyrim 调用的 `LoquiBinaryTranslation.GetCreateFunc()` 失败，属于 M5；含记录 overlay 在 `GroupRecordTypeGetter<T>` 初始化失败，属于 M6。这四个失败进程的退出码均为 `-1073740791`。外部输入因直接可变读取失败，未继续执行后面的工厂及 overlay 比较。M3 的静态工厂分派已验证，任意插件的完整 trim/AOT 读取仍待 M5/M6，不能仅凭发布成功宣称整个 Skyrim 包 AOT 兼容。

最终 AOT 可执行文件为 **25,630,720 字节**；发布目录排除 `.pdb` 后共 **27,291,013 字节**。此前 trimmed 目录相同统计口径为 **30,764,100 字节**，未针对最后的清单改动重新发布。最终 AOT 发布日志包含 **75 行 IL 警告**，这是构建与发布合并日志的行数，包含重复诊断，并非唯一警告位置数或警告消除量。

原始本机日志保存于仓库根目录的 `artifacts-m3-*.log`（Git 忽略）；发布目录为 `artifacts/m3/net10.0/trimmed` 与 `artifacts/m3/net10.0/aot`。普通运行、单元测试、生成一致性、完整发布日志及运行失败栈均单独保存。日志中的编译分析器诊断和链接器/AOT 发布诊断分别解释，不用增量日志行数比较 M3 前后的警告减少量。
