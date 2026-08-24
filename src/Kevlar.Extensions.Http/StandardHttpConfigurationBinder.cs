using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Kevlar.Extensions.Http;

internal static class StandardHttpConfigurationBinder
{
    public static StandardHttpShieldOptions BindStandard(IConfiguration configuration)
    {
        var options = new StandardHttpShieldOptions();

        BindTimeout(configuration, nameof(options.TotalTimeout), options.TotalTimeout);
        BindRetry(configuration.GetSection(nameof(options.Retry)), options.Retry);
        BindCircuitBreaker(configuration.GetSection(nameof(options.CircuitBreaker)), options.CircuitBreaker);
        BindConcurrencyLimit(configuration.GetSection(nameof(options.ConcurrencyLimit)), options);
        BindTimeout(configuration, nameof(options.AttemptTimeout), options.AttemptTimeout);
        BindHandler(configuration.GetSection(nameof(options.Handler)), options.Handler);

        ValidateStandard(configuration, options);
        return options;
    }

    public static StandardHedgingShieldOptions BindHedging(IConfiguration configuration)
    {
        var options = new StandardHedgingShieldOptions();

        RejectLegacyQueueKey(configuration);
        SetTimeSpan(configuration, nameof(options.TotalTimeout), value => options.TotalTimeout = value);
        SetInt(configuration, nameof(options.MaxAttempts), value => options.MaxAttempts = value);
        SetTimeSpan(configuration, nameof(options.HedgeDelay), value => options.HedgeDelay = value);
        SetTimeSpan(configuration, nameof(options.AttemptTimeout), value => options.AttemptTimeout = value);
        SetInt(configuration, nameof(options.MaxConcurrency), value => options.MaxConcurrency = value);
        SetInt(configuration, nameof(options.QueueLimit), value => options.QueueLimit = value);
        SetNullableInt(configuration, nameof(options.ConsecutiveFailures), value => options.ConsecutiveFailures = value);
        SetNullableDouble(configuration, nameof(options.FailureRatio), value => options.FailureRatio = value);
        if (configuration[nameof(options.ConsecutiveFailures)] is not null
            && configuration[nameof(options.FailureRatio)] is null)
        {
            options.FailureRatio = null;
        }
        SetInt(configuration, nameof(options.MinimumThroughput), value => options.MinimumThroughput = value);
        SetTimeSpan(configuration, nameof(options.SamplingWindow), value => options.SamplingWindow = value);
        SetTimeSpan(configuration, nameof(options.BreakDuration), value => options.BreakDuration = value);
        SetEnum<HttpEndpointSelectionMode>(configuration, nameof(options.SelectionMode), value => options.SelectionMode = value);
        SetInt(configuration, nameof(options.Seed), value => options.Seed = value);
        SetEnum<HttpContentReplayPolicy>(configuration, nameof(options.ContentReplayPolicy), value => options.ContentReplayPolicy = value);
        SetLong(configuration, nameof(options.MaximumBufferSize), value => options.MaximumBufferSize = value);
        SetBool(configuration, nameof(options.AllowUnsafeMethodReplay), value => options.AllowUnsafeMethodReplay = value);
        BindEndpoints(configuration.GetSection(nameof(options.Endpoints)), options.Endpoints);

        ValidateHedging(configuration, options);
        return options;
    }

    private static void BindTimeout(
        IConfiguration configuration,
        string key,
        TimeoutOptions options)
    {
        var section = configuration.GetSection(key);
        if (section[nameof(TimeoutOptions.Timeout)] is not null)
        {
            SetTimeSpan(section, nameof(TimeoutOptions.Timeout), value => options.Timeout = value);
        }
        else if (section.Value is not null)
        {
            options.Timeout = ParseTimeSpan(section, section.Value);
        }
    }

    private static void BindRetry(
        IConfiguration section,
        RetryOptions<HttpResponseMessage> options)
    {
        SetInt(section, nameof(options.MaxRetries), value => options.MaxRetries = value);
        SetNullableTimeSpan(section, nameof(options.MaxDelay), value => options.MaxDelay = value);

        var kindValue = section["Backoff"];
        var baseDelayValue = section["BaseDelay"];
        var factorValue = section["Factor"];
        var jitterValue = section["Jitter"];
        var backoffMaxDelayValue = section["BackoffMaxDelay"];
        if (kindValue is null
            && baseDelayValue is null
            && factorValue is null
            && jitterValue is null
            && backoffMaxDelayValue is null)
        {
            return;
        }

        var kind = kindValue is null
            ? RetryBackoffKind.Exponential
            : ParseEnum<RetryBackoffKind>(section, "Backoff", kindValue);
        var baseDelay = baseDelayValue is null
            ? TimeSpan.FromMilliseconds(250)
            : ParseTimeSpan(section.GetSection("BaseDelay"), baseDelayValue);
        var factor = factorValue is null
            ? 2d
            : ParseDouble(section.GetSection("Factor"), factorValue);
        var jitter = jitterValue is null
            ? true
            : ParseBool(section.GetSection("Jitter"), jitterValue);
        var backoffMaxDelay = backoffMaxDelayValue is null
            ? TimeSpan.FromSeconds(30)
            : ParseNullableTimeSpan(section.GetSection("BackoffMaxDelay"), backoffMaxDelayValue);

        Ensure(baseDelay >= TimeSpan.Zero, section.GetSection("BaseDelay"), "must not be negative");
        Ensure(
            factor >= 1 && !double.IsNaN(factor) && !double.IsInfinity(factor),
            section.GetSection("Factor"),
            "must be finite and at least 1");
        Ensure(
            backoffMaxDelay is null || backoffMaxDelay >= TimeSpan.Zero,
            section.GetSection("BackoffMaxDelay"),
            "must not be negative");

        options.Backoff = kind switch
        {
            RetryBackoffKind.None => Backoff.None,
            RetryBackoffKind.Constant => Backoff.Constant(baseDelay),
            RetryBackoffKind.Linear => Backoff.Linear(baseDelay, backoffMaxDelay),
            RetryBackoffKind.Exponential => Backoff.Exponential(baseDelay, factor, backoffMaxDelay, jitter),
            _ => throw new InvalidOperationException("Unsupported retry backoff."),
        };
    }

    private static void BindCircuitBreaker(
        IConfiguration section,
        CircuitBreakerOptions options)
    {
        SetNullableInt(section, nameof(options.ConsecutiveFailures), value => options.ConsecutiveFailures = value);
        SetNullableDouble(section, nameof(options.FailureRatio), value => options.FailureRatio = value);
        if (section[nameof(options.ConsecutiveFailures)] is not null
            && section[nameof(options.FailureRatio)] is null)
        {
            options.FailureRatio = null;
        }
        SetInt(section, nameof(options.MinimumThroughput), value => options.MinimumThroughput = value);
        SetTimeSpan(section, nameof(options.SamplingWindow), value => options.SamplingWindow = value);
        SetTimeSpan(section, nameof(options.BreakDuration), value => options.BreakDuration = value);
    }

    private static void BindConcurrencyLimit(
        IConfiguration section,
        StandardHttpShieldOptions standard)
    {
        if (!section.GetChildren().Any())
        {
            return;
        }

        RejectLegacyQueueKey(section);
        var options = standard.ConcurrencyLimit ?? new ConcurrencyLimitOptions();
        SetInt(section, nameof(options.MaxConcurrency), value => options.MaxConcurrency = value);
        SetInt(section, nameof(options.QueueLimit), value => options.QueueLimit = value);
        standard.ConcurrencyLimit = options;
    }

    private static void BindHandler(
        IConfiguration section,
        ShieldHttpHandlerOptions options)
    {
        SetEnum<HttpContentReplayPolicy>(section, nameof(options.ContentReplayPolicy), value => options.ContentReplayPolicy = value);
        SetLong(section, nameof(options.MaximumBufferSize), value => options.MaximumBufferSize = value);
        SetBool(section, nameof(options.AllowUnsafeMethodReplay), value => options.AllowUnsafeMethodReplay = value);

        var routingSection = section.GetSection(nameof(options.Routing));
        if (!routingSection.GetChildren().Any())
        {
            return;
        }

        var routing = options.Routing ?? new HttpEndpointRoutingOptions();
        SetEnum<HttpEndpointSelectionMode>(routingSection, nameof(routing.SelectionMode), value => routing.SelectionMode = value);
        SetInt(routingSection, nameof(routing.Seed), value => routing.Seed = value);
        BindEndpoints(routingSection.GetSection(nameof(routing.Endpoints)), routing.Endpoints);
        options.Routing = routing;
    }

    private static void BindEndpoints(IConfiguration section, IList<HttpEndpoint> endpoints)
    {
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return;
        }

        endpoints.Clear();
        foreach (var endpointSection in children)
        {
            var uriValue = endpointSection["Uri"] ?? endpointSection.Value;
            if (uriValue is null
                || !Uri.TryCreate(uriValue, UriKind.Absolute, out var uri))
            {
                throw InvalidValue(
                    endpointSection["Uri"] is null ? endpointSection : endpointSection.GetSection("Uri"),
                    uriValue ?? string.Empty,
                    "an absolute URI");
            }

            var weightValue = endpointSection["Weight"];
            var weight = weightValue is null
                ? 1
                : ParseInt(endpointSection.GetSection("Weight"), weightValue);
            Ensure(weight > 0, endpointSection.GetSection("Weight"), "must be positive");
            endpoints.Add(new HttpEndpoint(uri, weight));
        }
    }

    private static void RejectLegacyQueueKey(IConfiguration configuration)
    {
        const string legacyKey = "MaxQueue";
        if (configuration[legacyKey] is not null)
        {
            var path = configuration is IConfigurationSection { Path.Length: > 0 } section
                ? ConfigurationPath.Combine(section.Path, legacyKey)
                : legacyKey;
            throw new InvalidOperationException(
                $"Configuration key '{path}' is not supported; use 'QueueLimit'.");
        }
    }

    private static void ValidateStandard(
        IConfiguration configuration,
        StandardHttpShieldOptions options)
    {
        Ensure(options.TotalTimeout.Timeout > TimeSpan.Zero, TimeoutSection(configuration, nameof(options.TotalTimeout)), "must be positive");
        Ensure(options.Retry.MaxRetries >= 0, configuration.GetSection("Retry:MaxRetries"), "must not be negative");
        Ensure(options.Retry.MaxDelay is null || options.Retry.MaxDelay >= TimeSpan.Zero, configuration.GetSection("Retry:MaxDelay"), "must not be negative");
        ValidateCircuitBreaker(configuration.GetSection(nameof(options.CircuitBreaker)), options.CircuitBreaker);
        if (options.ConcurrencyLimit is { } concurrency)
        {
            Ensure(concurrency.MaxConcurrency > 0, configuration.GetSection("ConcurrencyLimit:MaxConcurrency"), "must be positive");
            Ensure(concurrency.QueueLimit >= 0, configuration.GetSection("ConcurrencyLimit:QueueLimit"), "must not be negative");
        }

        Ensure(options.AttemptTimeout.Timeout > TimeSpan.Zero, TimeoutSection(configuration, nameof(options.AttemptTimeout)), "must be positive");
        Ensure(options.Handler.MaximumBufferSize > 0, configuration.GetSection("Handler:MaximumBufferSize"), "must be positive");
        if (options.Handler.Routing is { } routing)
        {
            Ensure(
                routing.Endpoints.Count > 0,
                configuration.GetSection("Handler:Routing:Endpoints"),
                "must contain at least one endpoint");
        }
    }

    private static void ValidateHedging(
        IConfiguration configuration,
        StandardHedgingShieldOptions options)
    {
        Ensure(options.TotalTimeout > TimeSpan.Zero, configuration.GetSection(nameof(options.TotalTimeout)), "must be positive");
        Ensure(options.MaxAttempts >= 1, configuration.GetSection(nameof(options.MaxAttempts)), "must be at least 1");
        Ensure(
            options.HedgeDelay >= TimeSpan.Zero || options.HedgeDelay == Timeout.InfiniteTimeSpan,
            configuration.GetSection(nameof(options.HedgeDelay)),
            "must be non-negative or Timeout.InfiniteTimeSpan");
        Ensure(options.AttemptTimeout > TimeSpan.Zero, configuration.GetSection(nameof(options.AttemptTimeout)), "must be positive");
        Ensure(options.MaxConcurrency > 0, configuration.GetSection(nameof(options.MaxConcurrency)), "must be positive");
        Ensure(options.QueueLimit >= 0, configuration.GetSection(nameof(options.QueueLimit)), "must not be negative");
        ValidateCircuitBreaker(configuration, options.ConsecutiveFailures, options.FailureRatio, options.MinimumThroughput, options.SamplingWindow, options.BreakDuration);
        Ensure(options.MaximumBufferSize > 0, configuration.GetSection(nameof(options.MaximumBufferSize)), "must be positive");
        Ensure(
            options.Endpoints.Count > 0,
            configuration.GetSection(nameof(options.Endpoints)),
            "must contain at least one endpoint");
    }

    private static void ValidateCircuitBreaker(
        IConfiguration section,
        CircuitBreakerOptions options) =>
        ValidateCircuitBreaker(
            section,
            options.ConsecutiveFailures,
            options.FailureRatio,
            options.MinimumThroughput,
            options.SamplingWindow,
            options.BreakDuration);

    private static void ValidateCircuitBreaker(
        IConfiguration section,
        int? consecutiveFailures,
        double? failureRatio,
        int minimumThroughput,
        TimeSpan samplingWindow,
        TimeSpan breakDuration)
    {
        Ensure(consecutiveFailures is null or > 0, section.GetSection("ConsecutiveFailures"), "must be positive");
        Ensure(
            failureRatio is null || (!double.IsNaN(failureRatio.Value) && failureRatio > 0 && failureRatio <= 1),
            section.GetSection("FailureRatio"),
            "must be between 0 (exclusive) and 1 (inclusive)");
        Ensure(
            consecutiveFailures is null || failureRatio is null,
            section.GetSection("FailureRatio"),
            "cannot be set with ConsecutiveFailures");
        Ensure(minimumThroughput >= 1, section.GetSection("MinimumThroughput"), "must be at least 1");
        Ensure(samplingWindow > TimeSpan.Zero, section.GetSection("SamplingWindow"), "must be positive");
        Ensure(breakDuration > TimeSpan.Zero, section.GetSection("BreakDuration"), "must be positive");
    }

    private static IConfigurationSection TimeoutSection(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        return section[nameof(TimeoutOptions.Timeout)] is null
            ? section
            : section.GetSection(nameof(TimeoutOptions.Timeout));
    }

    private static void Ensure(bool condition, IConfigurationSection section, string requirement)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Configuration value '{section.Value}' for '{section.Path}' {requirement}.");
        }
    }

    private static void SetInt(IConfiguration section, string key, Action<int> setter)
    {
        if (section[key] is { } value)
        {
            setter(ParseInt(section.GetSection(key), value));
        }
    }

    private static void SetLong(IConfiguration section, string key, Action<long> setter)
    {
        if (section[key] is { } value)
        {
            setter(ParseLong(section.GetSection(key), value));
        }
    }

    private static void SetNullableInt(IConfiguration section, string key, Action<int?> setter)
    {
        if (section[key] is { } value)
        {
            setter(string.IsNullOrEmpty(value) ? null : ParseInt(section.GetSection(key), value));
        }
    }

    private static void SetNullableDouble(IConfiguration section, string key, Action<double?> setter)
    {
        if (section[key] is { } value)
        {
            setter(string.IsNullOrEmpty(value) ? null : ParseDouble(section.GetSection(key), value));
        }
    }

    private static void SetBool(IConfiguration section, string key, Action<bool> setter)
    {
        if (section[key] is { } value)
        {
            setter(ParseBool(section.GetSection(key), value));
        }
    }

    private static void SetTimeSpan(IConfiguration section, string key, Action<TimeSpan> setter)
    {
        if (section[key] is { } value)
        {
            setter(ParseTimeSpan(section.GetSection(key), value));
        }
    }

    private static void SetNullableTimeSpan(
        IConfiguration section,
        string key,
        Action<TimeSpan?> setter)
    {
        if (section[key] is { } value)
        {
            setter(ParseNullableTimeSpan(section.GetSection(key), value));
        }
    }

    private static void SetEnum<TEnum>(
        IConfiguration section,
        string key,
        Action<TEnum> setter)
        where TEnum : struct, Enum
    {
        if (section[key] is { } value)
        {
            setter(ParseEnum<TEnum>(section, key, value));
        }
    }

    private static int ParseInt(IConfigurationSection section, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(section, value, "an integer");

    private static long ParseLong(IConfigurationSection section, string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(section, value, "an integer");

    private static double ParseDouble(IConfigurationSection section, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(section, value, "a number");

    private static bool ParseBool(IConfigurationSection section, string value) =>
        bool.TryParse(value, out var parsed)
            ? parsed
            : throw InvalidValue(section, value, "a Boolean");

    private static TimeSpan ParseTimeSpan(IConfigurationSection section, string value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(section, value, "a TimeSpan");

    private static TimeSpan? ParseNullableTimeSpan(IConfigurationSection section, string value) =>
        string.IsNullOrEmpty(value) ? null : ParseTimeSpan(section, value);

    private static TEnum ParseEnum<TEnum>(
        IConfiguration section,
        string key,
        string value)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(typeof(TEnum), parsed))
        {
            return parsed;
        }

        throw InvalidValue(section.GetSection(key), value, $"a {typeof(TEnum).Name}");
    }

    private static InvalidOperationException InvalidValue(
        IConfigurationSection section,
        string value,
        string expected) =>
        new($"Configuration value '{value}' for '{section.Path}' is not {expected}.");

    private enum RetryBackoffKind
    {
        None,
        Constant,
        Linear,
        Exponential,
    }
}
