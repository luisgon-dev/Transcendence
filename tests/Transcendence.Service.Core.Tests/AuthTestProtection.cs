using Microsoft.AspNetCore.DataProtection;

namespace Transcendence.Service.Core.Tests;

// Unit tests don't test DataProtection cryptography. PostgreSQL race tests use its real provider.
internal sealed class AuthTestProtection : IDataProtectionProvider, IDataProtector
{
    public IDataProtector CreateProtector(string purpose) => this;
    public byte[] Protect(byte[] plaintext) => plaintext.ToArray();
    public byte[] Unprotect(byte[] protectedData) => protectedData.ToArray();
}
