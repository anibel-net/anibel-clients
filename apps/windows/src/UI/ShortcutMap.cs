using Windows.System;

namespace Anibel.App.Services;

internal enum ShortcutAction { Help, Search, Navigate, Back, FocusCards }
internal sealed record Shortcut(VirtualKey Key, VirtualKeyModifiers Modifiers, bool AfterG,
    ShortcutAction Action, string Keys, string Description, string? Page = null);

internal sealed class ShortcutMap
{
    private TimeSpan? _prefixAt;
    internal static readonly Shortcut[] Bindings =
    [
        new(VirtualKey.F1, 0, false, ShortcutAction.Help, "F1", "Спалучэнні клавіш"),
        new((VirtualKey)191, VirtualKeyModifiers.Shift, false, ShortcutAction.Help, "?", "Спалучэнні клавіш"),
        new((VirtualKey)191, 0, false, ShortcutAction.Search, "/", "Пошук"),
        new(VirtualKey.K, VirtualKeyModifiers.Control, false, ShortcutAction.Search, "Ctrl + K", "Пошук"),
        new(VirtualKey.Left, VirtualKeyModifiers.Menu, false, ShortcutAction.Back, "Alt + ←", "Назад"),
        new(VirtualKey.J, 0, false, ShortcutAction.FocusCards, "j", "Наступны тайтл"),
        new(VirtualKey.K, 0, false, ShortcutAction.FocusCards, "k", "Папярэдні тайтл"),
        new(VirtualKey.H, 0, true, ShortcutAction.Navigate, "g → h", "Галоўная", "home"),
        new(VirtualKey.E, 0, true, ShortcutAction.Navigate, "g → e", "Пошук", "search"),
        new(VirtualKey.A, 0, true, ShortcutAction.Navigate, "g → a", "Анімэ", "anime"),
        new(VirtualKey.M, 0, true, ShortcutAction.Navigate, "g → m", "Манга", "manga"),
        new(VirtualKey.C, 0, true, ShortcutAction.Navigate, "g → c", "Кіно", "cinema"),
        new(VirtualKey.G, 0, true, ShortcutAction.Navigate, "g → g", "Гульні", "games"),
        new(VirtualKey.B, 0, true, ShortcutAction.Navigate, "g → b", "Кнігі", "books"),
        new(VirtualKey.P, 0, true, ShortcutAction.Navigate, "g → p", "Профіль", "profile"),
        new(VirtualKey.D, 0, true, ShortcutAction.Navigate, "g → d", "Спампаванае", "downloads"),
        new(VirtualKey.S, 0, true, ShortcutAction.Navigate, "g → s", "Налады", "settings"),
    ];

    internal void Reset() => _prefixAt = null;

    internal Shortcut? Read(VirtualKey key, VirtualKeyModifiers modifiers, bool editing, TimeSpan now)
    {
        var afterG = _prefixAt is { } start && now >= start && now - start <= TimeSpan.FromSeconds(1.5);
        Reset();
        if (editing) return null;
        var match = Bindings.FirstOrDefault(b => b.Key == key && b.Modifiers == modifiers && b.AfterG == afterG);
        if (match is not null) return match;
        if (key == VirtualKey.G && modifiers == 0) _prefixAt = now;
        return null;
    }
}
