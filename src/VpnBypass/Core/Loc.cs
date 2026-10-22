using System.Windows;

namespace VpnBypass.Core;

/// <summary>Swaps the Lang/*.xaml string dictionary; XAML binds via DynamicResource.</summary>
public static class Loc
{
    public static bool IsTurkish { get; private set; } = true;

    public static event Action? Changed;

    public static void Apply(string lang)
    {
        IsTurkish = !string.Equals(lang, "EN", StringComparison.OrdinalIgnoreCase);
        var dicts = Application.Current.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Lang/") == true);
        if (old != null) dicts.Remove(old);
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Lang/{(IsTurkish ? "tr" : "en")}.xaml")
        });
        Changed?.Invoke();
    }

    public static string T(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;
}
