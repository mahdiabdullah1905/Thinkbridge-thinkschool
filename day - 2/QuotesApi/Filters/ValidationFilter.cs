using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Filters;

public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validatable = context.Arguments.SingleOrDefault(x => x?.GetType() == typeof(T)) as T;

        if (validatable != null)
        {
            var results = new List<ValidationResult>();
            var validationContext = new ValidationContext(validatable);
            bool isValid = Validator.TryValidateObject(validatable, validationContext, results, true);

            if (!isValid)
            {
                var errors = results
                    .Where(r => r.MemberNames.Any())
                    .GroupBy(r => r.MemberNames.First(), r => r.ErrorMessage ?? "")
                    .ToDictionary(g => g.Key, g => g.ToArray());

                return TypedResults.ValidationProblem(errors);
            }
        }

        return await next(context);
    }
}
