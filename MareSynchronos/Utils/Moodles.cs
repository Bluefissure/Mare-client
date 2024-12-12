// A global tuple statement for moodle status info.
global using MoodlesStatusInfo = (
    System.Guid GUID,
    int IconID,
    string Title,
    string Description,
    MoodlesStatusType Type,
    string Applier,
    bool Dispelable,
    int Stacks,
    bool Persistent,
    int Days,
    int Hours,
    int Minutes,
    int Seconds,
    bool NoExpire,
    bool AsPermanent,
    System.Guid StatusOnDispell,
    string CustomVFXPath,
    bool StackOnReapply
    );


global using MoodlePresetInfo = (
    System.Guid GUID,
    System.Collections.Generic.List<System.Guid> Statuses,
    MoodlesPresetApplicationType ApplicationType,
    string Title
    );

global using IPCProfileDataTuple = (
    System.Guid UniqueId,
    string Name,
    string VirtualPath,
    System.Collections.Generic.List<(string Name, ushort WorldId, byte CharacterType, ushort CharacterSubType)> Characters,
    int Priority,
    bool IsEnabled);