// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System.Text.Json;
using System.Text.Json.Serialization;

using ktsu.CredentialCache;
using ktsu.CredentialCache.Storage;
using ktsu.RoundTripStringJsonConverter;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

using CredentialCache = ktsu.CredentialCache.CredentialCache;
using ICredentialStore = ktsu.CredentialCache.Storage.ICredentialStore;

/// <summary>
/// Covers where GitHub personal access tokens live. The settings file sits next to window state and
/// UI preferences and is written on every debounced save, so the contract under test is that a PAT
/// reaches the OS secret store and does not survive in that file.
/// </summary>
/// <remarks>
/// Follows the repository's own rule for this codebase: the part with a decision in it is a plain
/// class, so it can be driven without a live ImGui context or a display.
/// </remarks>
[TestClass]
public sealed class TokenStorageTests
{
	private CredentialCache Cache { get; set; } = null!;

	[TestInitialize]
	public void SetUp()
	{
		Cache = new CredentialCache(new InMemoryCredentialStore());
		TokenStorage.UseCache(Cache);
	}

	[TestCleanup]
	public void TearDown()
	{
		TokenStorage.UseCache(null);
		Cache.Dispose();
	}

	/// <summary>
	/// A store standing in for a machine whose native secret library will not load — a Linux host
	/// with no Secret Service provider, which is what a container or an SSH session usually is.
	/// </summary>
	private sealed class UnavailableCredentialStore : ICredentialStore
	{
		public string Name => "Unavailable";

		public bool TryLoad(PersonaGUID persona, out Credential? credential) =>
			throw new DllNotFoundException("libsecret-1.so.0");

		public void Save(PersonaGUID persona, Credential credential) =>
			throw new DllNotFoundException("libsecret-1.so.0");

		public bool Remove(PersonaGUID persona) => throw new DllNotFoundException("libsecret-1.so.0");
	}

	/// <summary>
	/// Mirrors how <see cref="ktsu.AppDataStorage"/> writes the settings file, so a token would show
	/// as a plain string rather than as the char array a bare serializer produces for a semantic
	/// string.
	/// </summary>
	private static readonly JsonSerializerOptions AppDataLike = BuildAppDataLikeOptions();

	private static JsonSerializerOptions BuildAppDataLikeOptions()
	{
		JsonSerializerOptions options = new()
		{
			ReferenceHandler = ReferenceHandler.Preserve,
		};
		options.Converters.Add(new RoundTripStringJsonConverterFactory());
		return options;
	}

	private static GitHubOwnerName Owner(string name) => GitHubOwnerName.Create<GitHubOwnerName>(name);

	private static GitHubToken Token(string value) => GitHubToken.Create<GitHubToken>(value);

	/// <summary>
	/// Two owners never share a persona.
	/// </summary>
	[TestMethod]
	public void OwnerPersonasAreDistinct() =>
		Assert.AreNotEqual(TokenStorage.OwnerPersona(Owner("ktsu-dev")), TokenStorage.OwnerPersona(Owner("ktsu-io")));

	/// <summary>
	/// The account-level token and an owner token of the same name are separate entries, so one
	/// cannot overwrite the other.
	/// </summary>
	[TestMethod]
	public void LoginPersonaIsNotAnOwnerPersona() =>
		Assert.AreNotEqual(
			TokenStorage.LoginPersona(GitHubLogin.Create<GitHubLogin>("ktsu-dev")),
			TokenStorage.OwnerPersona(Owner("ktsu-dev")));

	/// <summary>
	/// Derivation is deterministic and GUID-shaped. Changing it would orphan every token already in
	/// the store.
	/// </summary>
	[TestMethod]
	public void PersonaDerivationIsStable()
	{
		Assert.AreEqual(
			TokenStorage.OwnerPersona(Owner("ktsu-dev")).ToString(),
			TokenStorage.OwnerPersona(Owner("ktsu-dev")).ToString());
		Assert.IsTrue(Guid.TryParse(TokenStorage.OwnerPersona(Owner("ktsu-dev")).ToString(), out _));
	}

	/// <summary>
	/// An owner token written through the normal path is readable again.
	/// </summary>
	[TestMethod]
	public void OwnerTokenRoundTripsThroughTheStore()
	{
		Assert.IsTrue(TokenStorage.WriteOwnerToken(Owner("ktsu-dev"), Token("ghp_owner")));

		Assert.AreEqual("ghp_owner", TokenStorage.ReadOwnerToken(Owner("ktsu-dev")).ToString());
		Assert.IsTrue(TokenStorage.ReadOwnerToken(Owner("ktsu-io")).IsEmpty());
	}

	/// <summary>
	/// The account-level token set through the options property goes to the store, keyed by the
	/// login it belongs to.
	/// </summary>
	[TestMethod]
	public void AccountTokenRoundTripsThroughTheStore()
	{
		using ProjectDirectorOptions options = new()
		{
			GitHubLogin = GitHubLogin.Create<GitHubLogin>("someone"),
			GitHubToken = Token("ghp_account"),
		};

		Assert.AreEqual("ghp_account", options.GitHubToken.ToString());
		Assert.IsTrue(options.LegacyGitHubToken.IsEmpty());
	}

	/// <summary>
	/// Clearing a token removes the entry rather than leaving an empty one behind.
	/// </summary>
	[TestMethod]
	public void ClearingATokenRemovesItFromTheStore()
	{
		_ = TokenStorage.WriteOwnerToken(Owner("ktsu-dev"), Token("ghp_owner"));

		Assert.IsTrue(TokenStorage.WriteOwnerToken(Owner("ktsu-dev"), new()));

		Assert.IsFalse(Cache.TryGet(TokenStorage.OwnerPersona(Owner("ktsu-dev")), out _));
	}

	/// <summary>
	/// The settings file an earlier version wrote carries its tokens into the store, and the owners
	/// it listed become the owner registry.
	/// </summary>
	[TestMethod]
	public void MigrationMovesAccountAndOwnerTokens()
	{
		using ProjectDirectorOptions options = new()
		{
			GitHubLogin = GitHubLogin.Create<GitHubLogin>("someone"),
			LegacyGitHubToken = Token("ghp_account"),
			LegacyGitHubOwners =
			{
				[Owner("ktsu-dev")] = Token("ghp_owner"),
				[Owner("ktsu-io")] = new(),
			},
		};

		int migrated = TokenStorage.MigrateLegacyTokens(options);

		Assert.AreEqual(2, migrated);
		Assert.AreEqual("ghp_account", options.GitHubToken.ToString());
		Assert.AreEqual("ghp_owner", TokenStorage.ReadOwnerToken(Owner("ktsu-dev")).ToString());
		Assert.IsTrue(options.GitHubOwners.Contains(Owner("ktsu-dev")));
		Assert.IsTrue(options.GitHubOwners.Contains(Owner("ktsu-io")));
	}

	/// <summary>
	/// Migration also empties the old fields. Ceasing to write a secret does not remove the one
	/// already on disk, so this is the half that does the security work.
	/// </summary>
	[TestMethod]
	public void MigrationEmptiesThePlaintextFields()
	{
		using ProjectDirectorOptions options = new()
		{
			LegacyGitHubToken = Token("ghp_account"),
			LegacyGitHubOwners = { [Owner("ktsu-dev")] = Token("ghp_owner") },
		};

		_ = TokenStorage.MigrateLegacyTokens(options);

		Assert.IsTrue(options.LegacyGitHubToken.IsEmpty());
		Assert.AreEqual(0, options.LegacyGitHubOwners.Count);
	}

	/// <summary>
	/// The settings file written after migration carries no token.
	/// </summary>
	[TestMethod]
	public void SerializedOptionsCarryNoToken()
	{
		using ProjectDirectorOptions options = new()
		{
			LegacyGitHubToken = Token("ghp_account_secret"),
			LegacyGitHubOwners = { [Owner("ktsu-dev")] = Token("ghp_owner_secret") },
		};

		_ = TokenStorage.MigrateLegacyTokens(options);
		string json = JsonSerializer.Serialize(options, AppDataLike);

		Assert.DoesNotContain("ghp_account_secret", json, StringComparison.Ordinal);
		Assert.DoesNotContain("ghp_owner_secret", json, StringComparison.Ordinal);
		Assert.Contains("ktsu-dev", json, StringComparison.Ordinal);
	}

	/// <summary>
	/// A second start has nothing left to move.
	/// </summary>
	[TestMethod]
	public void MigrationIsIdempotent()
	{
		using ProjectDirectorOptions options = new()
		{
			LegacyGitHubToken = Token("ghp_account"),
		};

		_ = TokenStorage.MigrateLegacyTokens(options);

		Assert.AreEqual(0, TokenStorage.MigrateLegacyTokens(options));
		Assert.AreEqual("ghp_account", options.GitHubToken.ToString());
	}

	/// <summary>
	/// A stale token in the settings file must not overwrite the one currently in the store — but it
	/// is still cleared.
	/// </summary>
	[TestMethod]
	public void MigrationKeepsTheStoredTokenAndStillClearsTheStaleOne()
	{
		_ = TokenStorage.WriteOwnerToken(Owner("ktsu-dev"), Token("ghp_current"));
		using ProjectDirectorOptions options = new()
		{
			LegacyGitHubOwners = { [Owner("ktsu-dev")] = Token("ghp_stale") },
		};

		Assert.AreEqual(1, TokenStorage.MigrateLegacyTokens(options));
		Assert.AreEqual("ghp_current", TokenStorage.ReadOwnerToken(Owner("ktsu-dev")).ToString());
		Assert.AreEqual(0, options.LegacyGitHubOwners.Count);
	}

	/// <summary>
	/// If the secret store will not take the token, the old copy stays where it is. Clearing it would
	/// destroy the only copy the user has.
	/// </summary>
	[TestMethod]
	public void MigrationKeepsTheLegacyTokenWhenTheStoreRefuses()
	{
		using CredentialCache unavailable = new(new UnavailableCredentialStore());
		TokenStorage.UseCache(unavailable);

		using ProjectDirectorOptions options = new()
		{
			LegacyGitHubOwners = { [Owner("ktsu-dev")] = Token("ghp_owner") },
		};

		Assert.AreEqual(0, TokenStorage.MigrateLegacyTokens(options));
		Assert.AreEqual("ghp_owner", options.LegacyGitHubOwners[Owner("ktsu-dev")].ToString());
		Assert.IsTrue(options.GitHubOwners.Contains(Owner("ktsu-dev")));
	}

	/// <summary>
	/// A machine with no secret store reads as "no token" rather than throwing, and says why once.
	/// ProjectDirector is a desktop application, and an exception out of a token read would take down
	/// the render loop. What it must never do is fall back to a plain file.
	/// </summary>
	[TestMethod]
	public void ReadingWithoutASecretStoreIsEmptyAndReportedOnce()
	{
		using CredentialCache unavailable = new(new UnavailableCredentialStore());
		TokenStorage.UseCache(unavailable);

		Assert.IsTrue(TokenStorage.ReadOwnerToken(Owner("ktsu-dev")).IsEmpty());
		Assert.IsTrue(TokenStorage.StoreUnavailable);

		string report = TokenStorage.DrainUnavailableReport();
		Assert.Contains("secret store", report, StringComparison.Ordinal);
		Assert.AreEqual(string.Empty, TokenStorage.DrainUnavailableReport());
	}

	/// <summary>
	/// A write to an unusable store reports failure rather than pretending it saved.
	/// </summary>
	[TestMethod]
	public void WritingWithoutASecretStoreReportsFailure()
	{
		using CredentialCache unavailable = new(new UnavailableCredentialStore());
		TokenStorage.UseCache(unavailable);

		Assert.IsFalse(TokenStorage.WriteOwnerToken(Owner("ktsu-dev"), Token("ghp_owner")));
	}

	/// <summary>
	/// An owner with its own token authenticates as that owner, which is what makes a private
	/// repository in another organization reachable.
	/// </summary>
	[TestMethod]
	public void OwnerTokenShadowsTheAccountToken()
	{
		Credentials? credentials = ProjectDirector.ResolveGitHubCredentials(
			Owner("ktsu-dev"),
			Token("ghp_owner"),
			GitHubLogin.Create<GitHubLogin>("someone"),
			Token("ghp_account"));

		Assert.IsNotNull(credentials);
		Assert.AreEqual("ktsu-dev", credentials.Login);
		Assert.AreEqual("ghp_owner", credentials.Password);
	}

	/// <summary>
	/// Without an owner token the account-level login and token are used.
	/// </summary>
	[TestMethod]
	public void AccountCredentialsAreUsedWhenTheOwnerHasNoToken()
	{
		Credentials? credentials = ProjectDirector.ResolveGitHubCredentials(
			Owner("ktsu-dev"),
			new(),
			GitHubLogin.Create<GitHubLogin>("someone"),
			Token("ghp_account"));

		Assert.IsNotNull(credentials);
		Assert.AreEqual("someone", credentials.Login);
		Assert.AreEqual("ghp_account", credentials.Password);
	}

	/// <summary>
	/// With nothing usable the answer is no credentials, rather than credentials carrying a blank
	/// secret — an anonymous request is a better failure than one that authenticates as nobody.
	/// </summary>
	[TestMethod]
	public void NoCredentialsWhenNothingIsConfigured()
	{
		Assert.IsNull(ProjectDirector.ResolveGitHubCredentials(Owner("ktsu-dev"), new(), new(), new()));
		Assert.IsNull(ProjectDirector.ResolveGitHubCredentials(
			Owner("ktsu-dev"), new(), GitHubLogin.Create<GitHubLogin>("someone"), new()));
		Assert.IsNull(ProjectDirector.ResolveGitHubCredentials(
			Owner("ktsu-dev"), new(), new(), Token("ghp_account")));
	}

	/// <summary>
	/// A token typed into the popup reaches the secret store, and nothing is logged.
	/// </summary>
	[TestMethod]
	public void ApplyOwnerTokenStoresTheTokenSilently()
	{
		string message = ProjectDirector.ApplyOwnerToken(Owner("ktsu-dev"), "ghp_typed");

		Assert.AreEqual(string.Empty, message);
		Assert.AreEqual("ghp_typed", TokenStorage.ReadOwnerToken(Owner("ktsu-dev")).ToString());
	}

	/// <summary>
	/// When the store refuses the token the user is told. The popup closes either way, so silence
	/// here would look exactly like success.
	/// </summary>
	[TestMethod]
	public void ApplyOwnerTokenReportsARefusal()
	{
		using CredentialCache unavailable = new(new UnavailableCredentialStore());
		TokenStorage.UseCache(unavailable);

		string message = ProjectDirector.ApplyOwnerToken(Owner("ktsu-dev"), "ghp_typed");

		Assert.Contains("secret store", message, StringComparison.Ordinal);
	}

	/// <summary>
	/// Migrating nothing says nothing; migrating something says how much.
	/// </summary>
	[TestMethod]
	public void MigrationIsDescribedOnlyWhenSomethingMoved()
	{
		Assert.AreEqual(string.Empty, ProjectDirector.DescribeTokenMigration(0));
		Assert.Contains("2", ProjectDirector.DescribeTokenMigration(2), StringComparison.Ordinal);
		Assert.Contains("secret store", ProjectDirector.DescribeTokenMigration(2), StringComparison.Ordinal);
	}

	/// <summary>
	/// Owners list in a stable order, so the owner panels and the token menu agree with each other
	/// and with the previous run. The registry they come from is a set, which does not promise one.
	/// </summary>
	[TestMethod]
	public void OwnersListInAStableOrder()
	{
		HashSet<GitHubOwnerName> owners = [Owner("ktsu-io"), Owner("acme"), Owner("ktsu-dev")];

		string ordered = string.Join(",", ProjectDirector.OwnersInDisplayOrder(owners));

		Assert.AreEqual("acme,ktsu-dev,ktsu-io", ordered);
	}
}
