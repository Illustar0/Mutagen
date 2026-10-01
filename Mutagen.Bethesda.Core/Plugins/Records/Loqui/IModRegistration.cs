using Loqui;

using Mutagen.Bethesda.Plugins.Binary.Parameters;

namespace Mutagen.Bethesda.Plugins.Records.Loqui;

public interface IModRegistration : ILoquiRegistration
{
    GameCategory GameCategory { get; }
}

/// <summary>Creates and imports mods without runtime member discovery.</summary>
internal interface IModFactory : IModRegistration
{
    /// <summary>The disposable getter interface accepted by the generic factory.</summary>
    Type DisposableGetterType { get; }

    /// <summary>Finds a top-level record trigger needed to initialize the mod's groups.</summary>
    bool TryGetGroupRecordType(Type type, out RecordType recordType);

    /// <summary>Creates a mutable mod with the requested header and FormID defaults.</summary>
    IMod Create(ModKey modKey, GameRelease release, float? headerVersion, bool? forceUseLowerFormIDRanges);

    /// <summary>Imports a mutable mod and closes its input stream before returning.</summary>
    IMod ImportSetter(ModPath path, GameRelease release, BinaryReadParameters? param);

    /// <summary>Imports an overlay whose input remains owned by the returned disposable getter.</summary>
    IModDisposeGetter ImportGetter(ModPath path, GameRelease release, BinaryReadParameters? param);
}
