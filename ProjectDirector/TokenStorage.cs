// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Security.Cryptography;
using System.Text;

using ktsu.CredentialCache;
using ktsu.CredentialCache.Storage;

using Semantics.Strings;

using CredentialCache = ktsu.CredentialCache.CredentialCache;

/// <summary>
/// Holds GitHub personal access tokens in the operating system's secret store — Windows Credential
/// Manager, macOS Keychain, or libsecret (Secret Service) on Linux — rather than in
/// <see cref="ProjectDirectorOptions"/>'s settings file.
/// </summary>
/// <remarks>
/// <see cref="ktsu.AppDataStorage"/> serializes the whole options object to an unencrypted file
/// alongside window state and UI preferences. A PAT is not something to keep there.
/// </remarks>
internal static class TokenStorage
{
	/// <summary>
	/// Scopes ProjectDirector's entries within the OS secret store so they cannot collide with
	/// another ktsu tool's credentials on a shared host.
	/// </summary>
	internal const string CredentialServiceName = "ktsu.ProjectDirector";

	/// <summary>
	/// Prefixed to every persona seed. Versioned because changing the derivation would orphan every
	/// token already in the store, so a future change has to be a deliberate, visible one.
	/// </summary>
	private const string PersonaNamespace = "ktsu.ProjectDirector/v1/";

	private const string UnavailableMessage =
		"No usable OS secret store was found, so GitHub tokens cannot be read or saved. On Linux this " +
		"usually means no Secret Service provider (libsecret with GNOME Keyring or KWallet) is installed " +
		"and unlocked. ProjectDirector will not fall back to writing tokens to a plain file.";

	private static readonly Lazy<CredentialCache> LazyDefaultCache =
		new(() => new CredentialCache(CredentialStoreFactory.CreateDefault(CredentialServiceName)));

	private static int _unavailableReported;

	/// <summary>
	/// The cache substituted by <see cref="UseCache"/>, or <see langword="null"/> for the default.
	/// </summary>
	private static CredentialCache? InjectedCache { get; set; }

	/// <summary>
	/// Gets the cache backing this storage.
	/// </summary>
	/// <remarks>
	/// Built directly rather than taken from <see cref="CredentialCache.Instance"/> so the store
	/// carries ProjectDirector's own service name; the singleton can only ever use the library
	/// default.
	/// </remarks>
	internal static CredentialCache Cache => InjectedCache ?? LazyDefaultCache.Value;

	/// <summary>
	/// Reports whether the last read or write found the secret store unusable.
	/// </summary>
	internal static bool StoreUnavailable => Volatile.Read(ref _unavailableReported) != 0;

	/// <summary>
	/// Substitutes the backing cache. Test seam; pass <see langword="null"/> to restore the default.
	/// </summary>
	internal static void UseCache(CredentialCache? cache)
	{
		InjectedCache = cache;
		_ = Interlocked.Exchange(ref _unavailableReported, 0);
	}

	/// <summary>
	/// Derives the persona holding the account-level token that goes with
	/// <see cref="ProjectDirectorOptions.GitHubLogin"/>.
	/// </summary>
	internal static PersonaGUID LoginPersona(GitHubLogin login) => DerivePersona($"github/login/{login}");

	/// <summary>
	/// Derives the persona holding an owner's token.
	/// </summary>
	internal static PersonaGUID OwnerPersona(GitHubOwnerName owner) => DerivePersona($"github/owner/{owner}");

	/// <summary>
	/// Reads the token stored under <paramref name="persona"/>, or an empty token when there is none.
	/// </summary>
	internal static GitHubToken Read(PersonaGUID persona)
	{
		try
		{
			return Cache.TryGet(persona, out Credential? credential) && credential is CredentialWithToken token
				? GitHubToken.Create<GitHubToken>(token.Token.ToString())
				: new();
		}
		catch (Exception ex) when (IsUnavailable(ex))
		{
			ReportUnavailable(ex);
			return new();
		}
	}

	/// <summary>
	/// Stores <paramref name="token"/> under <paramref name="persona"/>, removing the entry when the
	/// token is empty.
	/// </summary>
	/// <returns><see langword="false"/> when the secret store refused the write.</returns>
	internal static bool Write(PersonaGUID persona, GitHubToken token)
	{
		try
		{
			if (token.IsEmpty())
			{
				_ = Cache.Remove(persona);
				return true;
			}

			Cache.AddOrReplace(persona, new CredentialWithToken
			{
				Token = SemanticString<CredentialToken>.Create(token.ToString()),
			});
			return true;
		}
		catch (Exception ex) when (IsUnavailable(ex))
		{
			ReportUnavailable(ex);
			return false;
		}
	}

	/// <summary>
	/// Reads an owner's token.
	/// </summary>
	internal static GitHubToken ReadOwnerToken(GitHubOwnerName owner) => Read(OwnerPersona(owner));

	/// <summary>
	/// Stores an owner's token.
	/// </summary>
	internal static bool WriteOwnerToken(GitHubOwnerName owner, GitHubToken token) =>
		Write(OwnerPersona(owner), token);

	/// <summary>
	/// Moves the tokens earlier versions kept in the settings file into the secret store, registers
	/// the owners they were keyed by, and blanks them where they were.
	/// </summary>
	/// <returns>How many tokens were moved.</returns>
	/// <remarks>
	/// The legacy owner map did double duty: it was both the registry of configured owners and the
	/// place their tokens lived. The names move to <see cref="ProjectDirectorOptions.GitHubOwners"/>
	/// — which is not credential-shaped, so it stays in the settings file — while the tokens go to
	/// the secret store.
	/// </remarks>
	internal static int MigrateLegacyTokens(ProjectDirectorOptions options)
	{
		Ensure.NotNull(options);

		int migrated = 0;

		if (MigrateToken(
			LoginPersona(options.GitHubLogin),
			options.LegacyGitHubToken,
			() => options.LegacyGitHubToken = new()))
		{
			migrated++;
		}

		foreach ((GitHubOwnerName owner, GitHubToken legacy) in options.LegacyGitHubOwners.ToArray())
		{
			_ = options.GitHubOwners.Add(owner);

			if (MigrateToken(OwnerPersona(owner), legacy, () => options.LegacyGitHubOwners[owner] = new()))
			{
				migrated++;
			}
		}

		// Owners whose token moved, or who never had one, no longer need an entry here at all.
		foreach ((GitHubOwnerName owner, GitHubToken remaining) in options.LegacyGitHubOwners.ToArray())
		{
			if (remaining.IsEmpty())
			{
				_ = options.LegacyGitHubOwners.Remove(owner);
			}
		}

		return migrated;
	}

	/// <summary>
	/// Moves one token, leaving the old copy in place if the secret store will not take it.
	/// </summary>
	/// <remarks>
	/// A token already in the store wins over a legacy one, so a stale copy in the settings file
	/// cannot overwrite a current credential — but the stale copy is still cleared, because ceasing
	/// to write a secret does not remove the one already on disk.
	/// </remarks>
	private static bool MigrateToken(PersonaGUID persona, GitHubToken legacy, Action clearLegacy)
	{
		if (legacy.IsEmpty())
		{
			return false;
		}

		if (Read(persona).IsEmpty() && !Write(persona, legacy))
		{
			return false;
		}

		clearLegacy();
		return true;
	}

	private static PersonaGUID DerivePersona(string seed)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(PersonaNamespace + seed));
		return SemanticString<PersonaGUID>.Create(new Guid(hash.AsSpan(0, 16)).ToString());
	}

	/// <summary>
	/// Recognises a machine with no usable secret store: the factory refusing the platform, the
	/// native library failing to resolve, or the store itself reporting a failure.
	/// </summary>
	private static bool IsUnavailable(Exception exception) =>
		exception is PlatformNotSupportedException
			or DllNotFoundException
			or EntryPointNotFoundException
			or CredentialStoreException;

	/// <summary>
	/// Records the unavailable store once per process. Tokens are read on paths that run per owner
	/// on every scan, so reporting each failure would bury the log.
	/// </summary>
	private static void ReportUnavailable(Exception exception)
	{
		if (Interlocked.Exchange(ref _unavailableReported, 1) == 0)
		{
			UnavailableReport = $"{UnavailableMessage} ({exception.Message})";
		}
	}

	/// <summary>
	/// The message describing why the secret store is unusable, or empty while it is fine.
	/// </summary>
	/// <remarks>
	/// Held rather than logged directly because the log in this application is an instance member of
	/// <see cref="ProjectDirector"/>; the caller drains this into it.
	/// </remarks>
	internal static string UnavailableReport { get; private set; } = string.Empty;

	/// <summary>
	/// Returns the pending unavailable-store message and clears it, so it is reported once.
	/// </summary>
	internal static string DrainUnavailableReport()
	{
		string report = UnavailableReport;
		UnavailableReport = string.Empty;
		return report;
	}
}
