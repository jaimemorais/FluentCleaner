using Microsoft.Windows.ApplicationModel.Resources;

namespace FluentCleaner.Services;

//English string lookup for code-side strings (status messages, dialogs).
//XAML uses x:Uid directly.
public static class ResourceService
{
    private static readonly ResourceLoader Loader = new();

    // Returns the key itself as fallback when a string is missing
    public static string Get(string key)
    {
        try
        {
            var result = Loader.GetString(key);
            //resw stores \n literally will turn it into a real newline
            return string.IsNullOrEmpty(result) ? key : result.Replace("\\n", "\n");
        }
        catch { return key; }
    }

    //Format-string lookup; used for interpolated status messages.
    // e.g. ResourceService.Fmt("St_ScanComplete", files, reg, size)
    public static string Fmt(string key, params object[] args)
    {
        var template = Get(key);
        //If Get fell back to the key itself (no braces>> not a format string), skip formatting
        return template == key && !template.Contains('{') ? key : string.Format(template, args);
    }
}