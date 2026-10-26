namespace DpiMusicControl.Core;

public sealed class AppSettings
{
    public InputTrigger Trigger { get; set; } = InputTrigger.XButton1;

    public bool MappingEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; }
}
