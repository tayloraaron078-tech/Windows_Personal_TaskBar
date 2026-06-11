using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Personal_TaskBar.Services;

/// <summary>
/// Registers and unregisters global hotkeys via RegisterHotKey (Win32).
/// Each hotkey gets a unique integer ID.  The owner form passes its handle
/// and listens for WM_HOTKEY in WndProc, then calls HandleHotkey.
/// </summary>
public class HotkeyService : IDisposable
{
    private readonly IntPtr                      _hWnd;
    private readonly Dictionary<int, Action>     _handlers  = new();
    private          int                         _nextId    = 0xBF00; // arbitrary range unlikely to collide

    public HotkeyService(IntPtr windowHandle)
    {
        _hWnd = windowHandle;
    }

    /// <summary>
    /// Registers a global hotkey. The action is invoked on WM_HOTKEY.
    /// Returns the hotkey ID so the caller can unregister it later.
    /// Throws InvalidOperationException if registration fails (e.g. key already in use).
    /// </summary>
    public int Register(uint modifiers, uint vk, Action handler)
    {
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_hWnd, id, modifiers | NativeMethods.MOD_NOREPEAT, vk))
            throw new InvalidOperationException(
                $"Could not register hotkey (id={id}). The key combination may already be in use.");

        _handlers[id] = handler;
        return id;
    }

    /// <summary>Unregisters a previously registered hotkey by its ID.</summary>
    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
            NativeMethods.UnregisterHotKey(_hWnd, id);
    }

    /// <summary>
    /// Call from the owner form's WndProc when msg == WM_HOTKEY.
    /// Invokes the registered action for the hotkey.
    /// </summary>
    public void HandleHotkey(int id)
    {
        if (_handlers.TryGetValue(id, out var action))
            action();
    }

    // ── Hotkey string parser ────────────────────────────────────────────────

    /// <summary>
    /// Parses a user-facing hotkey string like "Ctrl+Space" or "Ctrl+`"
    /// into (modifiers, vk) suitable for RegisterHotKey.
    /// Returns false if the string cannot be parsed.
    /// </summary>
    public static bool TryParse(string hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk        = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var p = part.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl":  modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt":   modifiers |= NativeMethods.MOD_ALT;     break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT;   break;
                case "win":   modifiers |= NativeMethods.MOD_WIN;     break;
                default:
                    // Attempt to map the key token to a virtual key code
                    if (p.Length == 1)
                    {
                        vk = (uint)char.ToUpper(p[0]);
                    }
                    else if (Enum.TryParse<Keys>(p, true, out var k))
                    {
                        vk = (uint)k;
                    }
                    else
                    {
                        // Special cases
                        vk = p switch
                        {
                            "`"  => 0xC0, // VK_OEM_3 (backtick/tilde key)
                            "space" => (uint)Keys.Space,
                            _    => 0,
                        };
                        if (vk == 0) return false;
                    }
                    break;
            }
        }

        return vk != 0;
    }

    // ── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var id in new List<int>(_handlers.Keys))
            Unregister(id);
    }
}
