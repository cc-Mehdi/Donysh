using HesabYar.Web.Helpers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HesabYar.Web.ModelBinding;

public sealed class PersianDateOnlyModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue;
        var isNullable = Nullable.GetUnderlyingType(bindingContext.ModelType) is not null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (isNullable)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            return Task.CompletedTask;
        }

        if (PersianCalendarHelper.TryParseInput(raw, out var date))
        {
            bindingContext.Result = ModelBindingResult.Success(date);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                "تاریخ را به‌صورت شمسی و با قالب ۱۴۰۵/۰۵/۰۸ وارد کنید.");
        }

        return Task.CompletedTask;
    }
}

public sealed class PersianDateOnlyModelBinderProvider : IModelBinderProvider
{
    private static readonly IModelBinder Binder = new PersianDateOnlyModelBinder();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type == typeof(DateOnly) ? Binder : null;
    }
}
