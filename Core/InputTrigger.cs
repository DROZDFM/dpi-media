namespace DpiMusicControl.Core;

public enum InputTrigger
{
    XButton1,
    XButton2,
    MiddleButton,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24
}

public sealed record TriggerOption(InputTrigger Value, string Title, string Detail)
{
    public static IReadOnlyList<TriggerOption> All { get; } =
    [
        new(InputTrigger.XButton1, "Mouse 4 — доп. кнопка 1", "Mouse 4"),
        new(InputTrigger.XButton2, "Mouse 5 — доп. кнопка 2", "Mouse 5"),
        new(InputTrigger.MiddleButton, "Средняя кнопка колеса", "Средняя кнопка"),
        new(InputTrigger.F13, "F13 — свободная клавиша", "F13"),
        new(InputTrigger.F14, "F14 — свободная клавиша", "F14"),
        new(InputTrigger.F15, "F15 — свободная клавиша", "F15"),
        new(InputTrigger.F16, "F16 — свободная клавиша", "F16"),
        new(InputTrigger.F17, "F17 — свободная клавиша", "F17"),
        new(InputTrigger.F18, "F18 — свободная клавиша", "F18"),
        new(InputTrigger.F19, "F19 — свободная клавиша", "F19"),
        new(InputTrigger.F20, "F20 — свободная клавиша", "F20"),
        new(InputTrigger.F21, "F21 — свободная клавиша", "F21"),
        new(InputTrigger.F22, "F22 — свободная клавиша", "F22"),
        new(InputTrigger.F23, "F23 — свободная клавиша", "F23"),
        new(InputTrigger.F24, "F24 — свободная клавиша", "F24")
    ];

    public static TriggerOption FromValue(InputTrigger value) =>
        All.FirstOrDefault(option => option.Value == value) ?? All[0];
}
