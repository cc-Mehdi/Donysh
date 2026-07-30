using HesabYar.Web.Helpers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HesabYar.Web.ModelBinding;

public sealed class FlexibleDecimalModelBinder : IModelBinder
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

        if (InputNormalization.TryParseMoney(raw, out var amount))
        {
            bindingContext.Result = ModelBindingResult.Success(amount);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "مبلغ واردشده معتبر نیست.");
        }

        return Task.CompletedTask;
    }
}

public sealed class FlexibleDecimalModelBinderProvider : IModelBinderProvider
{
    private static readonly IModelBinder Binder = new FlexibleDecimalModelBinder();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type == typeof(decimal) ? Binder : null;
    }
}
