namespace ktsu.ProjectDirector;

using ktsu.StrongStrings;

public sealed record class AzureDevOpsOrganizationName : StrongStringAbstract<AzureDevOpsOrganizationName> { }
public sealed record class AzureDevOpsProjectName : StrongStringAbstract<AzureDevOpsProjectName> { }
public sealed record class AzureDevOpsRepoName : StrongStringAbstract<AzureDevOpsRepoName> { }

public sealed class AzureDevOpsRepository : GitRepository
{
	public AzureDevOpsOrganizationName OrganizationName { get; set; } = new();
	public AzureDevOpsProjectName ProjectName { get; set; } = new();
	public AzureDevOpsRepoName RepoName { get; set; } = new();
}
