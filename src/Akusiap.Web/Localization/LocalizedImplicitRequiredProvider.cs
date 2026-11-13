using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Akusiap.Web.Localization;

/// <summary>
/// MVC marks every non-nullable value type (bool, int, enums) and non-nullable string as required and, for client validation, writes the
/// framework's English "The X field is required." into <c>data-val-required</c>, even on Indonesian pages. This
/// provider runs before the built-in one and writes the shared <c>Validation.Required</c> message instead; the
/// built-in adapter then leaves the attribute alone. Server-side validation is unchanged.
/// </summary>
public sealed class LocalizedImplicitRequiredProvider(IStringLocalizerFactory localizers) : IClientModelValidatorProvider
{
    public const string MessageKey = "Validation.Required";

    public void CreateValidators(ClientValidatorProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var metadata = context.ModelMetadata;
        // A [Required] with its own message is localized already; an implicit one (non-nullable string) has none.
        if (!metadata.IsRequired || context.ValidatorMetadata.OfType<RequiredAttribute>().Any(a => a.ErrorMessage is not null || a.ErrorMessageResourceType is not null))
        {
            return;
        }

        var adapter = new RequiredAttributeAdapter(new RequiredAttribute { ErrorMessage = MessageKey }, localizers.Create(typeof(SharedResource)));
        // First in the list: adapters only add data-val-required when it is not there yet.
        context.Results.Insert(0, new ClientValidatorItem { Validator = adapter, IsReusable = true });
    }

    /// <summary>Puts the provider first so its message is the one written.</summary>
    public sealed class Setup(IStringLocalizerFactory localizers) : IConfigureOptions<MvcViewOptions>
    {
        public void Configure(MvcViewOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.ClientModelValidatorProviders.Insert(0, new LocalizedImplicitRequiredProvider(localizers));
        }
    }
}

public static class LocalizedImplicitRequiredExtensions
{
    public static IServiceCollection AddLocalizedImplicitRequired(this IServiceCollection services) =>
        services.AddSingleton<IConfigureOptions<MvcViewOptions>, LocalizedImplicitRequiredProvider.Setup>();
}
