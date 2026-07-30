using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace HesabYar.Web.Helpers;

public static class FormValueHelper
{
    public static string RawOrPersianDate(ViewDataDictionary viewData, string key, DateOnly? fallback)
    {
        if (viewData.ModelState.TryGetValue(key, out var entry))
        {
            if (entry.RawValue is string raw)
            {
                return raw;
            }

            if (entry.RawValue is string[] values && values.Length > 0)
            {
                return values[0];
            }

            if (entry.RawValue is not null)
            {
                return entry.RawValue.ToString() ?? string.Empty;
            }
        }

        return PersianCalendarHelper.ToInput(fallback);
    }
}
