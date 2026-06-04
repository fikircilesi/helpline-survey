#nullable enable

using Microsoft.AspNetCore.Http;

namespace survey.Infrastructure;

public sealed class LegacySession
{
    private readonly ISession _session;

    public LegacySession(ISession session)
    {
        _session = session;
    }

    public object? this[string key]
    {
        get => _session.GetString(key);
        set
        {
            if (value is null)
            {
                _session.Remove(key);
                return;
            }

            _session.SetString(key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    public void Clear()
    {
        _session.Clear();
    }
}
