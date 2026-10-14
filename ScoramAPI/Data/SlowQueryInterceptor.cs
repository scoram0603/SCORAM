using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ScoramAPI.Data
{
    // Non-functional diagnostics only: logs one warning for any database command that takes longer than
    // Observability:SlowQueryMs (default 500 ms). It observes the command AFTER it has run and never
    // alters the command, its parameters or its result.
    //
    // What is logged: the command kind, the duration, the parameter COUNT and the first ~300 characters of
    // the SQL text. EF Core sends user-supplied values as parameters, so the SQL text itself holds
    // table/column names but not the values. Parameter values are intentionally never logged.
    public sealed class SlowQueryInterceptor : DbCommandInterceptor
    {
        public const string ConfigKey = "Observability:SlowQueryMs";
        public const int DefaultSlowQueryMs = 500;
        private const int MaxSqlChars = 300;

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        private readonly ILogger<SlowQueryInterceptor> _logger;
        private readonly int _thresholdMs;

        public SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger, IConfiguration configuration)
        {
            _logger = logger;
            var configured = configuration.GetValue<int?>(ConfigKey);
            _thresholdMs = configured is > 0 ? configured.Value : DefaultSlowQueryMs;
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Observe("reader", command, eventData.Duration);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Observe("reader", command, eventData.Duration);
            return new ValueTask<DbDataReader>(result);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Observe("non-query", command, eventData.Duration);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Observe("non-query", command, eventData.Duration);
            return new ValueTask<int>(result);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            Observe("scalar", command, eventData.Duration);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            Observe("scalar", command, eventData.Duration);
            return new ValueTask<object?>(result);
        }

        private void Observe(string kind, DbCommand command, TimeSpan duration)
        {
            try
            {
                if (!IsSlow(duration, _thresholdMs)) return;
                _logger.LogWarning(
                    "Slow database command ({Kind}) took {ElapsedMs} ms with {ParameterCount} parameter(s): {Sql}",
                    kind, (long)duration.TotalMilliseconds, command.Parameters.Count, Summarise(command.CommandText));
            }
            catch (Exception ex)
            {
                // Diagnostics must never break a query. Log at debug so a logging fault stays discoverable
                // without ever surfacing to the caller.
                _logger.LogDebug(ex, "SlowQueryInterceptor failed to record a command; ignoring.");
            }
        }

        internal static bool IsSlow(TimeSpan duration, int thresholdMs) => duration.TotalMilliseconds >= thresholdMs;

        internal static string Summarise(string? commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText)) return string.Empty;
            var collapsed = Whitespace.Replace(commandText.Trim(), " ");
            return collapsed.Length <= MaxSqlChars ? collapsed : collapsed.Substring(0, MaxSqlChars) + "...";
        }
    }
}
