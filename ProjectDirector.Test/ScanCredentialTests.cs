// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

/// <summary>
/// Tests the rule that decides which credentials one owner is scanned with.
/// </summary>
/// <remarks>
/// <see cref="Octokit.GitHubClient.Credentials"/> is one mutable property on a client shared by
/// every owner in the scan, so the rule that sets it has to answer for every owner rather than only
/// for the ones that have credentials. It used to be a condition wrapped around the assignment,
/// which meant an owner with no credentials left the previous owner's in place and was scanned as
/// them. <see cref="ProjectDirector.ChooseCredentials"/> exists separately so that rule can be
/// driven without a live ImGui context or a GitHub account, the way <see cref="PullDecisionTests"/>
/// drives the pull rule.
///
/// Getting it wrong is quiet: the owner's own private repositories go missing, and anything that
/// genuinely needed its auth answers with an ApiException the caller swallows, so the user is given
/// no reason for the gap.
/// </remarks>
[TestClass]
public sealed class ScanCredentialTests
{
	private static GitHubOwnerName Owner(string value) => GitHubOwnerName.Create<GitHubOwnerName>(value);
	private static GitHubToken Token(string value) => GitHubToken.Create<GitHubToken>(value);
	private static GitHubLogin Login(string value) => GitHubLogin.Create<GitHubLogin>(value);

	[TestMethod]
	public void AnOwnerWithItsOwnTokenIsScannedAsItself()
	{
		Credentials chosen = ProjectDirector.ChooseCredentials(Owner("alpha"), Token("alpha-pat"), Login("global-login"), Token("global-token"));

		Assert.AreEqual(AuthenticationType.Basic, chosen.AuthenticationType);
		Assert.AreEqual("alpha", chosen.Login, "An owner's own token should win over the global login.");
		Assert.AreEqual("alpha-pat", chosen.Password);
	}

	[TestMethod]
	public void AnOwnerWithoutATokenFallsBackToTheGlobalLogin()
	{
		Credentials chosen = ProjectDirector.ChooseCredentials(Owner("beta"), Token(string.Empty), Login("global-login"), Token("global-token"));

		Assert.AreEqual(AuthenticationType.Basic, chosen.AuthenticationType);
		Assert.AreEqual("global-login", chosen.Login);
		Assert.AreEqual("global-token", chosen.Password);
	}

	/// <summary>
	/// The regression this file exists for.
	/// </summary>
	/// <remarks>
	/// Owner A has a PAT and owner B has nothing. B must come back anonymous, because the caller
	/// assigns whatever this returns to the shared client, and anything short of an answer for B
	/// leaves A's identity in place.
	/// </remarks>
	[TestMethod]
	public void AnOwnerWithNoCredentialsAnywhereIsScannedAnonymously()
	{
		Credentials forOwnerWithPat = ProjectDirector.ChooseCredentials(Owner("alpha"), Token("alpha-pat"), Login(string.Empty), Token(string.Empty));
		Credentials forOwnerWithout = ProjectDirector.ChooseCredentials(Owner("beta"), Token(string.Empty), Login(string.Empty), Token(string.Empty));

		Assert.AreEqual("alpha", forOwnerWithPat.Login, "The first owner should still be scanned as itself.");

		Assert.AreEqual(AuthenticationType.Anonymous, forOwnerWithout.AuthenticationType,
			"An owner with no credentials of its own and no global login must be scanned anonymously, " +
			"not as whichever owner was scanned before it.");
		Assert.AreNotEqual("alpha", forOwnerWithout.Login, "The previous owner's login must not carry over.");
	}

	[TestMethod]
	public void AGlobalLoginMissingItsTokenIsNotUsed()
	{
		// Octokit rejects an empty password, so a half-configured global login has to be treated as
		// no login rather than passed through.
		Credentials chosen = ProjectDirector.ChooseCredentials(Owner("beta"), Token(string.Empty), Login("global-login"), Token(string.Empty));

		Assert.AreEqual(AuthenticationType.Anonymous, chosen.AuthenticationType);
	}

	[TestMethod]
	public void AGlobalTokenMissingItsLoginIsNotUsed()
	{
		Credentials chosen = ProjectDirector.ChooseCredentials(Owner("beta"), Token(string.Empty), Login(string.Empty), Token("global-token"));

		Assert.AreEqual(AuthenticationType.Anonymous, chosen.AuthenticationType);
	}
}
