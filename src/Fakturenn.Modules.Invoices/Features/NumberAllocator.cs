using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.Modules.Invoices.Features;

/// <summary>
/// One statement that also handles the first occurrence in a scope (spec section 6, review
/// S1). Executed during the review against PostgreSQL: three concurrent transactions on a
/// missing row received 1, 2 and 3, and a rolled-back fourth returned its number. Unlike a
/// sequence, whose nextval survives ROLLBACK, a failed finalization returns its number.
/// Plain ADO on the context's own connection and transaction, because EF would wrap a
/// composed SqlQuery in a subquery an INSERT cannot sit in.
/// </summary>
public static class NumberAllocator
{
    // public const Fields
    public const string Sql =
        "INSERT INTO invoices.\"NumberCounters\" (\"ScopeKey\", \"Value\") VALUES (@scope, @start) "
        + "ON CONFLICT (\"ScopeKey\") DO UPDATE SET \"Value\" = invoices.\"NumberCounters\".\"Value\" + 1 "
        + "RETURNING \"Value\"";

    // public static Methods
    public static async Task<long> AllocateAsync(DbContext db, string scopeKey, long start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Allocation must run inside the finalization transaction.");
        command.CommandText = Sql;
        AddParameter(command, "scope", scopeKey);
        AddParameter(command, "start", start);

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // private static Methods
    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
