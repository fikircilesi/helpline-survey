using Microsoft.AspNetCore.Mvc.Razor;

namespace survey.Infrastructure;

public abstract class SurveyRazorPage<TModel> : RazorPage<TModel>
{
    public LegacySession Session => new(Context.Session);
}