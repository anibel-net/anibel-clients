// Anibel UI smoke test — drives the real WinUI3 app via Windows UI Automation.
// No WinAppDriver needed: System.Windows.Automation ships with the WindowsDesktop runtime.
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class Program
{
    private const string AppExe = @"apps\windows\src\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Anibel.Net.exe";

    private static int _pass;
    private static int _fail;
    private static Process? _app;

    private static void Main(string[] args)
    {
        Console.WriteLine("== Anibel UI smoke ==");
        try
        {
            _app = LaunchOrAttach(args);
            var window = WaitWindow(_app!.Id);
            Check("window appears (Anibel.Net)", window is not null);
            if (window is null)
            {
                return;
            }

            foreach (var item in new[] { "Главная", "Пошук", "Анімэ", "Манга", "Кіно", "Гульні", "Кнігі", "Профіль", "Налады" })
            {
                Check($"sidebar item «{item}»", FindByName(window, item, timeoutMs: 3000) is not null);
            }

            DumpUiInfo(window);

            Check("games catalog loads", OpenCatalogAndCount(window, "Гульні") > 0, "Усяго:");
            Check("anime catalog loads", OpenCatalogAndCount(window, "Анімэ") > 0, "Усяго:");

            var firstItem = FindFirstGridItem(window);
            Check("catalog first poster found (grid item)", firstItem is not null);
            if (firstItem is not null)
            {
                SelectItem(firstItem);
                Check("details page opens (Каментары/Эпізоды)", FindAny(window, new[] { "Каментары", "Эпізоды", "Главы" }, 15000));
                Check("details page does not crash (window alive)", _app is { HasExited: false });
            }

            Check("titlebar search box present (Edit in top area)", FindTitleBarEdit(window, 5000) is not null);
            GlobalSearch(window, "death note");
            Check("toolbar search results (Знойдзена: N)", WaitTextStarts(window, "Знойдзена:", 15000) is not null);
            Check("app still alive after search", _app is { HasExited: false });

            Console.WriteLine($"RESULT: pass={_pass} fail={_fail}");
        }
        finally
        {
            _app?.Kill(entireProcessTree: true);
        }
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static void DumpUiInfo(AutomationElement root)
    {
        Console.WriteLine("  -- ListItems: " + string.Join(" | ", Names(root, ControlType.ListItem).Take(30)));
        Console.WriteLine("  -- Edits: " + string.Join(" | ", NamesWithRect(root, ControlType.Edit).Take(10)));
    }

    private static IEnumerable<string> Names(AutomationElement root, ControlType type)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, type);
        var els = root.FindAll(TreeScope.Descendants, cond);
        foreach (AutomationElement el in els)
        {
            yield return el.Current.Name ?? "";
        }
    }

    private static IEnumerable<string> NamesWithRect(AutomationElement root, ControlType type)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, type);
        var els = root.FindAll(TreeScope.Descendants, cond);
        foreach (AutomationElement el in els)
        {
            var r = el.Current.BoundingRectangle;
            yield return $"«{el.Current.Name ?? ""}» top={(int)r.Top} w={(int)r.Width}";
        }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Cargo.toml")))
        {
            dir = Directory.GetParent(dir)!.FullName;
        }
        return dir;
    }

    private static Process LaunchOrAttach(string[] args)
    {
        var existing = Process.GetProcessesByName("Anibel.App");
        if (existing.Length > 0)
        {
            return existing[0];
        }
        var exe = ResolveAppExe(args);
        return Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)! })!;
    }

    /// <summary>
    /// App exe path: explicit CLI arg → ANIBEL_APP_EXE env var → embedded default.
    /// Relative paths are resolved against the repo root.
    /// </summary>
    private static string ResolveAppExe(string[] args)
    {
        var path = args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable("ANIBEL_APP_EXE");
        if (!string.IsNullOrEmpty(path))
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(RepoRoot(), path));
        }
        return Path.GetFullPath(Path.Combine(RepoRoot(), AppExe));
    }

    private static AutomationElement? WaitWindow(int pid, int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var cond = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
            var el = AutomationElement.RootElement.FindFirst(TreeScope.Children, cond);
            if (el is not null)
            {
                return el;
            }
            Thread.Sleep(500);
        }
        return null;
    }

    private static AutomationElement? FindByName(AutomationElement root, string name, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var cond = new PropertyCondition(AutomationElement.NameProperty, name);
            var el = root.FindFirst(TreeScope.Descendants, cond);
            if (el is not null)
            {
                return el;
            }
            Thread.Sleep(250);
        }
        return null;
    }

    private static bool FindAny(AutomationElement root, string[] names, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            foreach (var name in names)
            {
                if (FindByName(root, name, 300) is not null)
                {
                    return true;
                }
            }
            Thread.Sleep(300);
        }
        return false;
    }

    private static AutomationElement? WaitTextStarts(AutomationElement root, string startsWith, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
            foreach (AutomationElement el in root.FindAll(TreeScope.Descendants, cond))
            {
                if ((el.Current.Name ?? "").StartsWith(startsWith, StringComparison.Ordinal))
                {
                    return el;
                }
            }
            Thread.Sleep(400);
        }
        return null;
    }

    /// <summary>Clicks a sidebar catalog item and waits until «Усяго» shows a positive count.</summary>
    private static long OpenCatalogAndCount(AutomationElement window, string name)
    {
        var item = FindByName(window, name, 4000);
        if (item is not null)
        {
            if (!SelectItem(item))
            {
                Console.WriteLine($"  ! could not select «{name}»");
                return -1;
            }
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 15000)
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
            foreach (AutomationElement el in window.FindAll(TreeScope.Descendants, cond))
            {
                var text = el.Current.Name ?? "";
                if (!text.StartsWith("Усяго:", StringComparison.Ordinal))
                {
                    continue;
                }
                var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
                if (long.TryParse(digits, out var n) && n > 0)
                {
                    Console.WriteLine($"  «{name}» → {text}");
                    return n;
                }
            }
            Thread.Sleep(500);
        }
        return -1;
    }

    private static AutomationElement? FindFirstGridItem(AutomationElement root)
    {
        var cond = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        foreach (AutomationElement el in root.FindAll(TreeScope.Descendants, cond))
        {
            var name = el.Current.Name ?? "";
            if (name.Length > 1
                && !name.StartsWith("Главная") && !name.StartsWith("Пошук") && !name.StartsWith("Анімэ")
                && !name.StartsWith("Манга") && !name.StartsWith("Кіно") && !name.StartsWith("Гульні")
                && !name.StartsWith("Кнігі") && !name.StartsWith("Профіль") && !name.StartsWith("Налады"))
            {
                return el;
            }
        }
        return null;
    }

    private static AutomationElement? FindTitleBarEdit(AutomationElement root, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            foreach (AutomationElement el in root.FindAll(TreeScope.Descendants, cond))
            {
                var r = el.Current.BoundingRectangle;
                if (r.Top >= 0 && r.Top < 100 && r.Width > 200)
                {
                    return el;
                }
            }
            Thread.Sleep(300);
        }
        return null;
    }

    private static bool SelectItem(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
            {
                ((SelectionItemPattern)p).Select();
                return true;
            }
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var inv))
            {
                ((InvokePattern)inv).Invoke();
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static void GlobalSearch(AutomationElement window, string query)
    {
        var box = FindTitleBarEdit(window, 5000);
        if (box is null)
        {
            Check("titlebar search box present", false);
            return;
        }

        box.SetFocus();
        ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(query);
        Thread.Sleep(400);
        SendKeys.SendWait("{ENTER}");
        Thread.Sleep(1200);
    }

    private static void Check(string what, bool ok, string? note = null)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS  {what}{(note is null ? "" : $" · {note}")}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL  {what}{(note is null ? "" : $" · {note}")}");
        }
    }
}
