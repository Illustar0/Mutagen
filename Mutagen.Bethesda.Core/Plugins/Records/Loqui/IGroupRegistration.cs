namespace Mutagen.Bethesda.Plugins.Records.Loqui;

/// <summary>Supplies the trigger needed to construct a generated group without reflecting over fields.</summary>
internal interface IGroupRegistration
{
    /// <summary>The group's single triggering record type.</summary>
    RecordType RecordType { get; }
}
