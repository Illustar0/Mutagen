using Noggog;
using System.Linq.Expressions;
using System.Reflection;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DynamicData;
using Loqui;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records.Loqui;

namespace Mutagen.Bethesda.Plugins.Records
{
    /// <summary>
    /// A static class encapsulating the job of creating a new Mod in a generic context
    /// </summary>
    /// <typeparam name="TMod">
    /// Type of Mod to instantiate.  Can be the direct class, or one of its interfaces.
    /// </typeparam>
    public static class ModFactory<TMod>
        where TMod : IModGetter
    {
        public delegate TMod ActivatorDelegate(ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false);
        public delegate TMod ImporterDelegate(ModPath modKey, GameRelease release, BinaryReadParameters? param = null);
        public delegate TMod ImportMultiFileGetterDelegate(ModKey targetModKey, IEnumerable<ModPath> splitFiles, IEnumerable<ModKey> loadOrder, GameRelease release, BinaryReadParameters? param = null);
        public delegate TMod ImportGetterWithMultiFileDetectionDelegate(ModPath modPath, IEnumerable<ModKey> loadOrder, GameRelease release, BinaryReadParameters? param = null);
        public delegate TMod ImportSetterWithMultiFileDetectionDelegate(ModPath modPath, IEnumerable<ModKey> loadOrder, GameRelease release, BinaryReadParameters? param = null);

        /// <summary>
        /// Function to call to retrieve a new Mod of type T
        /// </summary>
        public static readonly ActivatorDelegate Activator;

        /// <summary>
        /// Function to call to import a new Mod of type T
        /// </summary>
        public static readonly ImporterDelegate Importer;

        /// <summary>
        /// Function to call to import multiple split mod files as a unified multi-file overlay
        /// </summary>
        public static readonly ImportMultiFileGetterDelegate ImportMultiFileGetter;

        /// <summary>
        /// Function to call to import a mod that may be split across multiple files.
        /// Automatically detects split files and calls the appropriate import method.
        /// </summary>
        public static readonly ImportGetterWithMultiFileDetectionDelegate ImportGetterWithMultiFileDetection;

        /// <summary>
        /// Function to call to import a mutable mod that may be split across multiple files.
        /// Automatically detects split files, imports as overlay, then deep copies to mutable.
        /// </summary>
        public static readonly ImportSetterWithMultiFileDetectionDelegate ImportSetterWithMultiFileDetection;

        static ModFactory()
        {
            Warmup.Init();
            bool createActivator = true;
            var type = typeof(TMod);
            var hasStaticFactory = GameRegistrations.TryGetModFactory(type, out var factory);
            if (hasStaticFactory)
            {
                createActivator = type != factory.DisposableGetterType;
            }
            else if (type.Name.EndsWith("DisposableGetter"))
            {
                var className = type.Name.TrimStringFromEnd("DisposableGetter") + "Getter";
                type = Type.GetType($"{type.Namespace}.{className}, {type.Namespace}");
                createActivator = false;
            }

            if (type == null)
            {
                throw new ArgumentException();
            }

            if (type == typeof(IModGetter) || type == typeof(IMod))
            {
                Activator = (modKey, release, headerVersion, forceUseLowerFormIDRanges) => (TMod)ModFactory.Activator(modKey, release, headerVersion, forceUseLowerFormIDRanges);
                if (type == typeof(IModGetter))
                {
                    Importer = (path, release, param) => (TMod)ModFactory.ImportGetter(path, release, param);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportMultiFileGetter(targetModKey, splitFiles, loadOrder, release, param);
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportGetterWithMultiFileDetection(modPath, loadOrder, release, param);
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportSetterWithMultiFileDetection is only supported for setter types (IMod), not getter types (IModGetter)");
                }
                else
                {
                    Importer = (path, release, param) => (TMod)ModFactory.ImportSetter(path, release, param);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportMultiFileGetter is only supported for getter types (IModGetter), not setter types (IMod)");
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportGetterWithMultiFileDetection is only supported for getter types (IModGetter), not setter types (IMod)");
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportSetterWithMultiFileDetection(modPath, loadOrder, release, param);
                }
            }
            else
            {
                ILoquiRegistration regis;
                if (hasStaticFactory)
                {
                    regis = factory;
                }
                else
                {
                    if (!LoquiRegistration.TryGetRegister(type, out var registration))
                    {
                        throw new ArgumentException();
                    }
                    regis = registration;
                }

                if (createActivator)
                {
                    Activator = hasStaticFactory
                        ? (modKey, release, version, ranges) => (TMod)factory.Create(modKey, release, version, ranges)
                        : ModFactoryReflection.GetActivator<TMod>(regis);
                }
                else
                {
                    Activator = (Key, Release, Version, Ranges) =>
                    {
                        throw new ArgumentException($"Cannot create a new mod of type {type}");
                    };
                }
                if (typeof(TMod).InheritsFrom(typeof(IMod)))
                {
                    Importer = hasStaticFactory
                        ? (path, release, param) => (TMod)factory.ImportSetter(path, release, param)
                        : ModFactoryReflection.GetImporter<TMod>(regis);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportMultiFileGetter is only supported for getter/overlay types, not mutable mod types");
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportGetterWithMultiFileDetection is only supported for getter/overlay types, not mutable mod types");
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportSetterWithMultiFileDetection(modPath, loadOrder, release, param);
                }
                else
                {
                    Importer = hasStaticFactory
                        ? (path, release, param) => (TMod)factory.ImportGetter(path, release, param)
                        : ModFactoryReflection.GetOverlay<TMod>(regis);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportMultiFileGetter(targetModKey, splitFiles, loadOrder, release, param);
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportGetterWithMultiFileDetection(modPath, loadOrder, release, param);
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportSetterWithMultiFileDetection is only supported for mutable mod types, not getter/overlay types");
                }
            }
        }
    }

    /// <summary>
    /// A static class encapsulating the job of creating a new Mod in a generic context
    /// </summary>
    public static class ModFactory
    {
        record Delegates(
            ModFactory<IModDisposeGetter>.ImporterDelegate ImportGetter,
            ModFactory<IMod>.ImporterDelegate ImportSetter,
            ModFactory<IMod>.ActivatorDelegate Activator);

        private static readonly ConcurrentDictionary<GameCategory, Delegates> Factories = new();

        /// <summary>Resolves only the requested game's static factory or compatibility fallback.</summary>
        private static Delegates GetDelegates(GameCategory category)
        {
            return Factories.GetOrAdd(category, static category =>
            {
                if (GameRegistrations.TryGet(category, out var definition)
                    && definition.Mod is IModFactory factory)
                {
                    Warmup.Init();
                    return new Delegates(factory.ImportGetter, factory.ImportSetter, factory.Create);
                }

                return GetReflectionDelegates(category);
            });
        }

        /// <summary>Builds legacy delegates for a game without a registered static factory.</summary>
        [RequiresUnreferencedCode("Unregistered games discover mod types and members by reflection. Register the game's static factory before using ModFactory.")]
        [RequiresDynamicCode("Unregistered games build delegates using runtime-selected types. Register the game's static factory before using ModFactory.")]
        private static Delegates GetReflectionDelegates(GameCategory category)
        {
            var type = Type.GetType(
                $"Mutagen.Bethesda.{category}.{category}Mod_Registration, Mutagen.Bethesda.{category}");
            if (type == null || System.Activator.CreateInstance(type) is not IModRegistration registration)
            {
                throw new KeyNotFoundException($"No mod factory is available for {category}.");
            }

            return new Delegates(
                ModFactoryReflection.GetOverlay<IModDisposeGetter>(registration),
                ModFactoryReflection.GetImporter<IMod>(registration),
                ModFactoryReflection.GetActivator<IMod>(registration));
        }

        /// <summary>Imports a single-file overlay whose resources are owned by the returned getter.</summary>
        public static IModDisposeGetter ImportGetter(ModPath path, GameRelease release, BinaryReadParameters? param = null)
        {
            return GetDelegates(release.ToCategory()).ImportGetter(path, release, param);
        }

        /// <summary>Imports a single-file mutable mod, closing its input before returning.</summary>
        public static IMod ImportSetter(ModPath path, GameRelease release, BinaryReadParameters? param = null)
        {
            return GetDelegates(release.ToCategory()).ImportSetter(path, release, param);
        }

        /// <summary>Creates a mod for the requested game with the existing header and FormID defaults.</summary>
        public static IMod Activator(ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false)
        {
            return GetDelegates(release.ToCategory()).Activator(modKey, release, headerVersion: headerVersion, forceUseLowerFormIDRanges: forceUseLowerFormIDRanges);
        }

        /// <summary>
        /// Imports a mod that may be split across multiple files. Automatically detects split files
        /// and calls the appropriate import method (single file or multi-file).
        /// </summary>
        /// <param name="modPath">The path to the mod file (base path without _1, _2 suffixes)</param>
        /// <param name="loadOrder">Load order to use for master ordering (required for multi-file imports)</param>
        /// <param name="release">Game release for the mod</param>
        /// <param name="param">Binary read parameters</param>
        /// <returns>Mod getter, either single file overlay or multi-file overlay depending on detection</returns>
        public static IModDisposeGetter ImportGetterWithMultiFileDetection(
            ModPath modPath,
            IEnumerable<ModKey> loadOrder,
            GameRelease release,
            BinaryReadParameters? param = null)
        {
            var fileSystem = param?.FileSystem ?? new System.IO.Abstractions.FileSystem();

            // Check if split files exist
            if (Analysis.MultiModFileAnalysis.IsMultiModFile(modPath, fileSystem))
            {
                // Get the split files
                var splitFiles = Analysis.MultiModFileAnalysis.GetSplitModFiles(modPath, fileSystem);

                // Import as multi-file
                return ImportMultiFileGetter(
                    modPath.ModKey,
                    splitFiles.Select(f => (ModPath)f.Path),
                    loadOrder,
                    release,
                    param);
            }
            else
            {
                // Import as single file
                return ImportGetter(modPath, release, param);
            }
        }

        /// <summary>
        /// Imports a mutable mod that may be split across multiple files. Automatically detects split files,
        /// imports as overlay, then deep copies to a mutable mod.
        /// </summary>
        /// <param name="modPath">The path to the mod file (base path without _1, _2 suffixes)</param>
        /// <param name="loadOrder">Load order to use for master ordering (required for multi-file imports)</param>
        /// <param name="release">Game release for the mod</param>
        /// <param name="param">Binary read parameters</param>
        /// <returns>Mutable mod, deep copied from overlay</returns>
        public static IMod ImportSetterWithMultiFileDetection(
            ModPath modPath,
            IEnumerable<ModKey> loadOrder,
            GameRelease release,
            BinaryReadParameters? param = null)
        {
            // First import as getter (handles split detection)
            using var getter = ImportGetterWithMultiFileDetection(modPath, loadOrder, release, param);

            // Deep copy to mutable mod
            return getter.DeepCopy();
        }

        /// <summary>
        /// Imports multiple split mod files and returns a multi-file overlay that presents them as a single unified mod.
        /// </summary>
        /// <param name="targetModKey">The ModKey for the unified overlay (typically the base name without _1, _2 suffixes)</param>
        /// <param name="splitFiles">Paths to the split mod files to merge</param>
        /// <param name="loadOrder">Load order to use for master ordering</param>
        /// <param name="release">Game release for the mods</param>
        /// <param name="param">Binary read parameters</param>
        /// <returns>Multi-file overlay presenting all split files as a single mod</returns>
        public static IModDisposeGetter ImportMultiFileGetter(
            ModKey targetModKey,
            IEnumerable<ModPath> splitFiles,
            IEnumerable<ModKey> loadOrder,
            GameRelease release,
            BinaryReadParameters? param = null)
        {
            param ??= BinaryReadParameters.Default;

            // Standardize all split file ModPaths to use targetModKey, and collect
            // the original ModKeys so we know which masters are split siblings.
            var splitModKeys = new HashSet<ModKey> { targetModKey };
            var splitFilesList = new List<ModPath>();
            foreach (var splitFile in splitFiles)
            {
                var actualModKey = ModKey.FromFileName(Path.GetFileName(splitFile.Path));
                splitModKeys.Add(actualModKey);
                splitFilesList.Add(new ModPath(targetModKey, splitFile.Path));
            }

            // Import all split files as overlays, remapping split sibling masters to targetModKey
            var overlays = new List<IModDisposeGetter>();
            foreach (var splitFile in splitFilesList)
            {
                // Read header to get original masters
                var header = ModHeaderFrame.FromPath(splitFile, release, fileSystem: param.FileSystem);

                // Remap masters: replace any split sibling ModKey with targetModKey
                var remappedMasters = header.Masters(splitFile.ModKey)
                    .Select(m => splitModKeys.Contains(m.Master)
                        ? (IMasterReferenceGetter)new MasterReference { Master = targetModKey }
                        : m)
                    .ToList();

                var splitParam = param with
                {
                    MasterOverrides = MasterReferenceCollection.CreateUnsafe(targetModKey, remappedMasters)
                };

                var overlay = ImportGetter(splitFile, release, splitParam);
                overlays.Add(overlay);
            }

            // Validate no duplicate FormIDs across split files
            ValidateNoDuplicates(overlays, targetModKey, release);

            // Merge masters from all overlays according to load order
            // Filter out targetModKey and all split file ModKeys, since split files may
            // cross-reference each other as masters (e.g. Mod_3.esp mastering Mod_2.esp)
            var mergedMasters = MergeMasters(overlays, loadOrder, splitModKeys);

            // Create multi-file overlay that presents all the split files as one unified mod
            return CreateMultiFileOverlay(targetModKey, release, overlays, mergedMasters);
        }

        private static IReadOnlyList<IMasterReferenceGetter> MergeMasters(
            List<IModDisposeGetter> overlays,
            IEnumerable<ModKey> loadOrder,
            HashSet<ModKey> excludedModKeys)
        {
            // Collect all unique masters from all overlays
            // Exclude the target mod and all split file ModKeys, since split files may
            // cross-reference each other as masters (e.g. Mod_3.esp mastering Mod_2.esp)
            var allMasters = new HashSet<ModKey>();
            foreach (var overlay in overlays)
            {
                foreach (var master in overlay.MasterReferences)
                {
                    if (!excludedModKeys.Contains(master.Master))
                    {
                        allMasters.Add(master.Master);
                    }
                }
            }

            // Create a dictionary for quick load order lookup
            var loadOrderList = loadOrder.ToList();
            var loadOrderDict = loadOrderList
                .Select((m, i) => new { ModKey = m, Index = i })
                .ToDictionary(x => x.ModKey, x => x.Index);

            // Order masters according to the provided load order
            var orderedMasterKeys = allMasters
                .OrderBy(m => loadOrderDict.TryGetValue(m, out var index) ? index : int.MaxValue)
                .ThenBy(m => m.FileName.String) // Fallback to alphabetical for masters not in load order
                .ToList();

            // Convert to IMasterReferenceGetter list
            var result = new List<IMasterReferenceGetter>();
            foreach (var masterKey in orderedMasterKeys)
            {
                result.Add(new MasterReference { Master = masterKey });
            }

            return result.AsReadOnly();
        }

        private static IModDisposeGetter CreateMultiFileOverlay(
            ModKey modKey,
            GameRelease gameRelease,
            List<IModDisposeGetter> overlays,
            IReadOnlyList<IMasterReferenceGetter> mergedMasters)
        {
            // Determine which multi-file overlay class to instantiate based on game release
            var (typeName, assemblyName) = gameRelease.ToCategory().GetMultiFileOverlayTypeInfo();

            // Load the overlay type with assembly-qualified name
            var assemblyQualifiedName = $"{typeName}, {assemblyName}";
            var overlayType = Type.GetType(assemblyQualifiedName);
            if (overlayType == null)
            {
                throw new InvalidOperationException($"Could not find multi-file overlay type: {assemblyQualifiedName}");
            }

            // Find the constructor - look for any constructor matching the pattern:
            // (ModKey, IEnumerable<IXXXModGetter>, IReadOnlyList<IMasterReferenceGetter>)
            var constructor = overlayType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(c =>
                {
                    var parameters = c.GetParameters();
                    if (parameters.Length != 3) return false;
                    if (parameters[0].ParameterType != typeof(ModKey)) return false;
                    // Check that parameter 1 is IEnumerable<T> where T is assignable from IModGetter
                    if (!parameters[1].ParameterType.IsGenericType) return false;
                    var genDef = parameters[1].ParameterType.GetGenericTypeDefinition();
                    if (genDef != typeof(IEnumerable<>)) return false;
                    var listElementType = parameters[1].ParameterType.GetGenericArguments()[0];
                    if (!typeof(IModGetter).IsAssignableFrom(listElementType)) return false;
                    if (parameters[2].ParameterType != typeof(IReadOnlyList<IMasterReferenceGetter>)) return false;
                    return true;
                });

            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find appropriate constructor for {assemblyQualifiedName}");
            }

            // Get the expected list element type from the constructor parameter
            var listParameterType = constructor.GetParameters()[1].ParameterType;
            var listElementType = listParameterType.GetGenericArguments()[0];

            // Create a properly-typed list by casting each overlay to the expected type
            // Use reflection to create a List<TCorrectType>
            var typedListType = typeof(List<>).MakeGenericType(listElementType);
            var typedList = (System.Collections.IList)System.Activator.CreateInstance(typedListType)!;
            foreach (var item in overlays)
            {
                typedList.Add(item);
            }

            // Instantiate the overlay
            var overlay = constructor.Invoke(new object[]
            {
                modKey,
                typedList,
                mergedMasters
            });

            if (overlay == null)
            {
                throw new InvalidOperationException($"Failed to create multi-file overlay of type {assemblyQualifiedName}");
            }

            return (IModDisposeGetter)overlay;
        }

        private static void ValidateNoDuplicates(List<IModDisposeGetter> overlays, ModKey modKey, GameRelease release)
        {
            var parentRecordTypes = Meta.GameConstants.Get(release).GroupConstants.ParentRecordTypes;
            var seenFormKeys = new Dictionary<FormKey, string>();

            for (int i = 0; i < overlays.Count; i++)
            {
                var overlay = overlays[i];
                var fileName = $"{modKey.FileName.String.Replace(modKey.Type.ToString(), "")}_{i + 1}.{modKey.Type}";

                foreach (var record in overlay.EnumerateMajorRecords())
                {
                    if (seenFormKeys.TryGetValue(record.FormKey, out var previousFile))
                    {
                        if (parentRecordTypes.Contains(Mapping.RecordTypeLookup.GetRecordType(record.GetType())))
                        {
                            // Known parent record types (Cell, Worldspace, DialogTopic) can be
                            // legitimately duplicated across split files because GetOrAddAsOverride
                            // on child records implicitly adds parent containers.
                            // Accept the later copy following override rules.
                            seenFormKeys[record.FormKey] = fileName;
                            continue;
                        }

                        throw new InvalidOperationException(
                            $"Duplicate FormKey {record.FormKey} found in both {previousFile} and {fileName}. " +
                            "This indicates corruption in the split files.");
                    }

                    seenFormKeys[record.FormKey] = fileName;
                }
            }
        }
    }

    internal static class ModFactoryReflection
    {
        /// <summary>Builds a constructor delegate for the legacy dynamic game path.</summary>
        [RequiresUnreferencedCode("Mod constructors are discovered by reflection. Use a registered static mod factory instead.")]
        [RequiresDynamicCode("Mod constructor delegates use runtime-selected types. Use a registered static mod factory instead.")]
        internal static ModFactory<TMod>.ActivatorDelegate GetActivator<TMod>(ILoquiRegistration regis)
            where TMod : IModGetter
        {
            var ctorInfo = regis.ClassType.GetConstructors()
                .Where(c => c.GetParameters().Length >= 3)
                .Where(c => c.GetParameters()[0].ParameterType == typeof(ModKey))
                .First();
            var paramInfo = ctorInfo.GetParameters();
            ParameterExpression modKeyParam = Expression.Parameter(typeof(ModKey), "modKey");
            ParameterExpression headerVersionParam = Expression.Parameter(typeof(float?), "headerVersion");
            ParameterExpression forceUseLowerFormIDRangesParam = Expression.Parameter(typeof(bool?), "forceUseLowerFormIDRanges");
            if (paramInfo.Length == 3)
            {
                NewExpression newExp = Expression.New(ctorInfo, modKeyParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                LambdaExpression lambda = Expression.Lambda(typeof(Func<ModKey, float?, bool?, TMod>), newExp, modKeyParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                var deleg = lambda.Compile();
                return (ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false) =>
                {
                    return (TMod)deleg.DynamicInvoke(modKey, headerVersion, forceUseLowerFormIDRanges)!;
                };
            }
            else
            {
                ParameterExpression releaseParam = Expression.Parameter(paramInfo[1].ParameterType, "release");
                NewExpression newExp = Expression.New(ctorInfo, modKeyParam, releaseParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                var funcType = Expression.GetFuncType(typeof(ModKey), paramInfo[1].ParameterType, typeof(float?), typeof(bool?), typeof(TMod));
                LambdaExpression lambda = Expression.Lambda(funcType, newExp, modKeyParam, releaseParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                var deleg = lambda.Compile();
                return (ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false) =>
                {
                    return (TMod)deleg.DynamicInvoke(modKey, (int)release, headerVersion, forceUseLowerFormIDRanges)!;
                };
            }
        }

        /// <summary>Builds a mutable import delegate for the legacy dynamic game path.</summary>
        [RequiresUnreferencedCode("Mod import methods are discovered by reflection. Use a registered static mod factory instead.")]
        [RequiresDynamicCode("Mod import delegates use runtime-selected types. Use a registered static mod factory instead.")]
        public static ModFactory<TMod>.ImporterDelegate GetImporter<TMod>(ILoquiRegistration regis)
            where TMod : IModGetter
        {
            var methodInfo = regis.ClassType.GetMethods()
                .Where(m => m.Name == "CreateFromBinary")
                .Where(c => c.GetParameters().Length >= 3)
                .Where(c => c.GetParameters()[0].ParameterType == typeof(ModPath))
                .First();
            var paramInfo = methodInfo.GetParameters();
            var paramExprs = paramInfo.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            MethodCallExpression callExp = Expression.Call(methodInfo, paramExprs);
            var funcType =
                Expression.GetFuncType(paramInfo.Select(p => p.ParameterType).And(typeof(TMod)).ToArray());
            LambdaExpression lambda = Expression.Lambda(funcType, callExp, paramExprs);
            var deleg = lambda.Compile();
            var releaseIndex = paramInfo.Select(x => x.Name).IndexOf("release");
            var fileSystemIndex = paramInfo.Select(x => x.Name).IndexOf("fileSystem");
            var paramIndex = paramInfo.Select(x => x.Name).IndexOf("param");
            return (ModPath modPath, GameRelease release, BinaryReadParameters? param) =>
            {
                var args = new object?[paramInfo.Length];
                args[0] = modPath;
                if (releaseIndex != -1)
                {
                    args[releaseIndex] = release;
                }

                if (paramIndex != -1)
                {
                    args[paramIndex] = param;
                }

                return (TMod)deleg.DynamicInvoke(args)!;
            };
        }

        /// <summary>Builds an overlay import delegate for the legacy dynamic game path.</summary>
        [RequiresUnreferencedCode("Mod overlay methods are discovered by reflection. Use a registered static mod factory instead.")]
        [RequiresDynamicCode("Mod overlay delegates use runtime-selected types. Use a registered static mod factory instead.")]
        public static ModFactory<TMod>.ImporterDelegate GetOverlay<TMod>(ILoquiRegistration regis)
            where TMod : IModGetter
        {
            var methodInfo = regis.ClassType.GetMethods()
                .Where(m => m.Name == "CreateFromBinaryOverlay")
                .Where(c => c.GetParameters().Length >= 1)
                .Where(c => c.GetParameters()[0].ParameterType == typeof(ModPath))
                .First();
            var paramInfo = methodInfo.GetParameters();
            var paramExprs = paramInfo.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            MethodCallExpression callExp = Expression.Call(methodInfo, paramExprs);
            var funcType =
                Expression.GetFuncType(paramInfo.Select(p => p.ParameterType).And(regis.GetterType).ToArray());
            LambdaExpression lambda = Expression.Lambda(funcType, callExp, paramExprs);
            var deleg = lambda.Compile();
            var releaseIndex = paramInfo.Select(x => x.Name).IndexOf("release");
            var paramIndex = paramInfo.Select(x => x.Name).IndexOf("param");
            return (ModPath modPath, GameRelease release, BinaryReadParameters? param) =>
            {
                var args = new object?[paramInfo.Length];
                args[0] = modPath;
                if (releaseIndex != -1)
                {
                    args[releaseIndex] = release;
                }

                args[paramIndex] = param;
                return (TMod)deleg.DynamicInvoke(args)!;
            };
        }
    }
}
