using FluentValidation;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;

namespace Karnama.Application.Roles;

public sealed class UpsertRoleValidator : AbstractValidator<UpsertRoleRequest>
{
    public UpsertRoleValidator()
    {
        RuleFor(x => x.Name)
            .NotNull().WithMessage("نام نقش الزامی است.")
            .Must(n => n is not null && n.Values.Any(v => !string.IsNullOrWhiteSpace(v))).WithMessage("نام نقش الزامی است.")
            .Must(n => n is null || n.All(kv => kv.Key.Length <= 10 && (kv.Value?.Length ?? 0) <= LocalizedText.MaxLength))
            .WithMessage("نام نقش خیلی طولانی است.");
        RuleFor(x => x.Description).MaximumLength(Role.MaxDescriptionLength).WithMessage("توضیح نقش حداکثر ۵۰۰ کاراکتر است.");
        RuleFor(x => x.Permissions).NotNull().WithMessage("فهرست مجوزها الزامی است.");
        RuleForEach(x => x.Permissions).ChildRules(g =>
        {
            g.RuleFor(p => p.Code).Must(c => Permissions.Find(c) is not null).WithMessage(p => $"مجوز «{p.Code}» وجود ندارد.");
            g.RuleFor(p => p.Scope).IsInEnum().WithMessage("دامنه‌ی مجوز معتبر نیست.");
        });
    }
}
