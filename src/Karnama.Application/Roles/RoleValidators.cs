using FluentValidation;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;

namespace Karnama.Application.Roles;

public sealed class UpsertRoleValidator : AbstractValidator<UpsertRoleRequest>
{
    public UpsertRoleValidator()
    {
        RuleFor(x => x.Name)
            .NotNull().WithMessage("Der Rollenname ist Pflicht.")
            .Must(n => n is not null && n.Values.Any(v => !string.IsNullOrWhiteSpace(v))).WithMessage("Der Rollenname ist Pflicht.")
            .Must(n => n is null || n.All(kv => kv.Key.Length <= 10 && (kv.Value?.Length ?? 0) <= LocalizedText.MaxLength))
            .WithMessage("Der Rollenname ist zu lang.");
        RuleFor(x => x.Description).MaximumLength(Role.MaxDescriptionLength).WithMessage("Die Rollenbeschreibung darf höchstens 500 Zeichen haben.");
        RuleFor(x => x.Permissions).NotNull().WithMessage("Die Liste der Berechtigungen ist Pflicht.");
        RuleForEach(x => x.Permissions).ChildRules(g =>
        {
            g.RuleFor(p => p.Code).Must(c => Permissions.Find(c) is not null).WithMessage(p => $"Die Berechtigung „{p.Code}“ existiert nicht.");
            g.RuleFor(p => p.Scope).IsInEnum().WithMessage("Der Berechtigungsumfang ist ungültig.");
        });
    }
}
