using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Encrypts a string column with ASP.NET Core Data Protection.
/// <para>
/// Applied to <c>IdentityUserToken.Value</c>, which stores BOTH second factors in
/// plaintext by default: the base32 TOTP shared secret under the token name
/// <c>AuthenticatorKey</c>, and the recovery codes, semicolon-joined and unhashed,
/// under <c>RecoveryCodes</c>. A read of that one table would otherwise yield a
/// working second factor for every user.
/// </para>
/// <para>
/// This defends against partial exposure — a dump of one table, a read-only
/// replica, a query log — and not against full database compromise, because the key
/// ring lives in the same database. It is never worse than the plaintext default.
/// </para>
/// </summary>
public sealed class EncryptedStringConverter(IDataProtector protector)
    : ValueConverter<string, string>(
        plaintext => protector.Protect(plaintext),
        ciphertext => protector.Unprotect(ciphertext));
