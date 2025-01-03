namespace ktsu.ProjectDirector;

using ktsu.StrongStrings;

/// <summary>
/// Represents the name of an Azure DevOps organization.
/// </summary>
public sealed record class AzureDevOpsOrganizationName : StrongStringAbstract<AzureDevOpsOrganizationName> { }
/// <summary>
/// Represents the name of an Azure DevOps project.
/// </summary>
public sealed record class AzureDevOpsProjectName : StrongStringAbstract<AzureDevOpsProjectName> { }
/// <summary>
/// Represents the name of an Azure DevOps repository.
/// </summary>
public sealed record class AzureDevOpsRepoName : StrongStringAbstract<AzureDevOpsRepoName> { }

/// <summary>
/// Represents an Azure DevOps repository with organization name, project name, and repository name.
/// </summary>
public sealed class AzureDevOpsRepository : GitRepository
{
	/// <summary>
	/// Gets or sets the name of the Azure DevOps organization.
	/// </summary>
	public AzureDevOpsOrganizationName OrganizationName { get; set; } = new();

	/// <summary>
	/// Gets or sets the name of the Azure DevOps project.
	/// </summary>
	public AzureDevOpsProjectName ProjectName { get; set; } = new();

	/// <summary>
	/// Gets or sets the name of the Azure DevOps repository.
	/// </summary>
	public AzureDevOpsRepoName RepoName { get; set; } = new();
}
