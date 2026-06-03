using Microsoft.Extensions.Configuration;

namespace survey.Infrastructure;

public static class LegacyConnectionStrings
{
    private static readonly Lazy<IConfigurationRoot> Configuration = new(() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build());

    public static string SurveyEntities => GetConnectionString(nameof(SurveyEntities));

    public static string EnvanterTakipLisansEntities => GetConnectionString(nameof(EnvanterTakipLisansEntities));

    private static string GetConnectionString(string name)
    {
        return Configuration.Value.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Connection string '{name}' was not found.");
    }
}