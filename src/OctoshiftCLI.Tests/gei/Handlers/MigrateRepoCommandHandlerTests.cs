using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using OctoshiftCLI.GithubEnterpriseImporter.Commands;
using OctoshiftCLI.GithubEnterpriseImporter.Handlers;
using Xunit;

namespace OctoshiftCLI.Tests.GithubEnterpriseImporter.Commands
{
    public class MigrateRepoCommandHandlerTests
    {
        private readonly Mock<GithubApi> _mockTargetGithubApi = TestHelpers.CreateMock<GithubApi>();
        private readonly Mock<GithubApi> _mockSourceGithubApi = TestHelpers.CreateMock<GithubApi>();
        private readonly Mock<OctoLogger> _mockOctoLogger = TestHelpers.CreateMock<OctoLogger>();
        private readonly Mock<EnvironmentVariableProvider> _mockEnvironmentVariableProvider = TestHelpers.CreateMock<EnvironmentVariableProvider>();
        private readonly Mock<AzureApi> _mockAzureApi = TestHelpers.CreateMock<AzureApi>();
        private readonly Mock<AwsApi> _mockAwsApi = TestHelpers.CreateMock<AwsApi>();
        private readonly Mock<HttpDownloadService> _mockHttpDownloadService = TestHelpers.CreateMock<HttpDownloadService>();
        private readonly Mock<FileSystemProvider> _mockFileSystemProvider = TestHelpers.CreateMock<FileSystemProvider>();

        private readonly MigrateRepoCommandHandler _handler;

        private const string TARGET_API_URL = "https://api.github.com";
        private const string GHES_API_URL = "https://myghes/api/v3";
        private const string AZURE_CONNECTION_STRING = "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=mykey;EndpointSuffix=core.windows.net";
        private const string SOURCE_ORG = "foo-source-org";
        private const string SOURCE_REPO = "foo-repo-source";
        private const string TARGET_ORG = "foo-target-org";
        private const string TARGET_REPO = "foo-target-repo";
        private const string ADO_PAT = "ado-pat";
        private const string GITHUB_TARGET_PAT = "github-target-pat";
        private const string GITHUB_SOURCE_PAT = "github-source-pat";
        private const string AWS_BUCKET_NAME = "aws-bucket-name";
        private const string AWS_ACCESS_KEY_ID = "aws-access-key-id";
        private const string AWS_ACCESS_KEY = "AWS_ACCESS_KEY";
        private const string AWS_SECRET_ACCESS_KEY = "aws-secret-access-key";
        private const string AWS_SECRET_KEY = "AWS_SECRET_KEY";
        private const string AWS_SESSION_TOKEN = "aws-session-token";
        private const string AWS_REGION = "aws-region";

        public MigrateRepoCommandHandlerTests()
        {
            _handler = new MigrateRepoCommandHandler(
                _mockOctoLogger.Object,
                _mockSourceGithubApi.Object,
                _mockTargetGithubApi.Object,
                _mockEnvironmentVariableProvider.Object,
                _mockAzureApi.Object,
                null,
                _mockHttpDownloadService.Object,
                _mockFileSystemProvider.Object);
        }

        [Fact]
        public async Task Dont_Generate_Archives_If_Target_Repo_Exists()
        {
            // Arrange
            _mockTargetGithubApi.Setup(x => x.DoesRepoExist(TARGET_ORG, TARGET_REPO)).ReturnsAsync(true);

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                Wait = false,
            };
            await FluentActions
                .Invoking(async () => await _handler.Handle(args)).Should().ThrowExactlyAsync<OctoshiftCliException>();

            // Assert
            _mockSourceGithubApi.Verify(x => x.StartGitArchiveGeneration(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Happy_Path_Without_Wait()
        {
            // Arrange
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://github.com/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi.Setup(x => x.StartMigration(migrationSourceId, githubRepoUrl, githubOrgId, TARGET_REPO, sourceGithubPat, targetGithubPat, null, null, false, false).Result).Returns(migrationId);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));

            var expectedLogOutput = new List<string>()
            {
                "Migrating Repo...",
                $"GITHUB SOURCE ORG: {SOURCE_ORG}",
                $"SOURCE REPO: {SOURCE_REPO}",
                $"GITHUB TARGET ORG: {TARGET_ORG}",
                $"TARGET REPO: {TARGET_REPO}",
                $"TARGET API URL: {TARGET_API_URL}",
                $"A repository migration (ID: {migrationId}) was successfully queued."
            };

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                Wait = false
            };
            await _handler.Handle(args);

            // Assert
            _mockTargetGithubApi.Verify(m => m.GetOrganizationId(TARGET_ORG));
            _mockTargetGithubApi.Verify(m => m.CreateGhecMigrationSource(githubOrgId));
            _mockTargetGithubApi.Verify(m => m.StartMigration(migrationSourceId, githubRepoUrl, githubOrgId, TARGET_REPO, sourceGithubPat, targetGithubPat, null, null, false, false));

            _mockOctoLogger.Verify(m => m.LogInformation(It.IsAny<string>()), Times.Exactly(7));
            actualLogOutput.Should().Equal(expectedLogOutput);

            _mockTargetGithubApi.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Skip_Migration_If_Target_Repo_Exists()
        {
            // Arrange
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://github.com/{SOURCE_ORG}/{SOURCE_REPO}";

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(migrationSourceId, githubRepoUrl, githubOrgId, TARGET_REPO, sourceGithubPat, targetGithubPat, null, null, false, false).Result)
                .Throws(new OctoshiftCliException($"A repository called {TARGET_ORG}/{TARGET_REPO} already exists"));

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));
            _mockOctoLogger.Setup(m => m.LogWarning(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));

            var expectedLogOutput = $"The Org '{TARGET_ORG}' already contains a repository with the name '{TARGET_REPO}'. No operation will be performed";

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                Wait = false
            };
            await _handler.Handle(args);

            // Assert
            _mockOctoLogger.Verify(m => m.LogWarning(It.IsAny<string>()), Times.Exactly(1));
            actualLogOutput.Should().Contain(expectedLogOutput);
        }

        [Fact]
        public async Task Happy_Path_GithubSource()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://github.com/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    null,
                    null,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
        }

        [Fact]
        public async Task Happy_Path_AdoSource()
        {
            var adoTeamProject = "foo-team-project";

            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceAdoPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var adoRepoUrl = $"https://dev.azure.com/{SOURCE_ORG}/{adoTeamProject}/_git/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateAdoMigrationSource(githubOrgId, null).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    adoRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceAdoPat,
                    targetGithubPat,
                    null,
                    null,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));

            _mockEnvironmentVariableProvider.Setup(m => m.AdoPersonalAccessToken(It.IsAny<bool>())).Returns(sourceAdoPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                AdoSourceOrg = SOURCE_ORG,
                AdoTeamProject = adoTeamProject,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
        }

        [Fact]
        public async Task Happy_Path_AdoServerSource()
        {
            var adoTeamProject = "foo-team-project";
            var adoServerUrl = "https://ado.contoso.com";

            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceAdoPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var adoRepoUrl = $"{adoServerUrl}/{SOURCE_ORG}/{adoTeamProject}/_git/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateAdoMigrationSource(githubOrgId, adoServerUrl).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    adoRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceAdoPat,
                    targetGithubPat,
                    null,
                    null,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));

            _mockEnvironmentVariableProvider.Setup(m => m.AdoPersonalAccessToken(It.IsAny<bool>())).Returns(sourceAdoPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                AdoServerUrl = adoServerUrl,
                AdoSourceOrg = SOURCE_ORG,
                AdoTeamProject = adoTeamProject,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
        }

        [Fact]
        public async Task Happy_Path_GithubSource_Ghes()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;
            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";
            var authenticatedGitArchiveUrl = new Uri($"https://example.com/{gitArchiveId}/authenticated");
            var authenticatedMetadataArchiveUrl = new Uri($"https://example.com/{metadataArchiveId}/authenticated");
            var gitArchiveFilePath = "path/to/git_archive";
            var metadataArchiveFilePath = "path/to/metadata_archive";

            _mockSourceGithubApi.Setup(x => x.GetEnterpriseServerVersion()).ReturnsAsync("3.7.1");
            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    authenticatedGitArchiveUrl.ToString(),
                    authenticatedMetadataArchiveUrl.ToString(),
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockAzureApi.SetupSequence(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<FileStream>()).Result).Returns(authenticatedGitArchiveUrl).Returns(authenticatedMetadataArchiveUrl);

            _mockFileSystemProvider
                .SetupSequence(m => m.GetTempFileName())
                .Returns(gitArchiveFilePath)
                .Returns(metadataArchiveFilePath);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
            _mockOctoLogger.Verify(x => x.LogInformation($"GHES API URL: {GHES_API_URL}"), Times.Once);
            _mockOctoLogger.Verify(x => x.LogInformation("AZURE STORAGE CONNECTION STRING: ***"), Times.Once);
            _mockFileSystemProvider.Verify(x => x.DeleteIfExists(gitArchiveFilePath), Times.Once);
            _mockFileSystemProvider.Verify(x => x.DeleteIfExists(metadataArchiveFilePath), Times.Once);
        }

        [Fact]
        public async Task Happy_Path_GithubSource_Ghae()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;
            var gitArchiveFilePath = "path/to/git_archive";
            var metadataArchiveFilePath = "path/to/metadata_archive";

            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";
            var authenticatedGitArchiveUrl = new Uri($"https://example.com/{gitArchiveId}/authenticated");
            var authenticatedMetadataArchiveUrl = new Uri($"https://example.com/{metadataArchiveId}/authenticated");

            _mockSourceGithubApi.Setup(x => x.GetEnterpriseServerVersion()).ReturnsAsync("GitHub AE");
            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    authenticatedGitArchiveUrl.ToString(),
                    authenticatedMetadataArchiveUrl.ToString(),
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, "", ""));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockAzureApi.SetupSequence(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<FileStream>()).Result).Returns(authenticatedGitArchiveUrl).Returns(authenticatedMetadataArchiveUrl);

            _mockFileSystemProvider
                .SetupSequence(m => m.GetTempFileName())
                .Returns(gitArchiveFilePath)
                .Returns(metadataArchiveFilePath);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
            _mockOctoLogger.Verify(x => x.LogInformation($"GHES API URL: {GHES_API_URL}"), Times.Once);
            _mockOctoLogger.Verify(x => x.LogInformation("AZURE STORAGE CONNECTION STRING: ***"), Times.Once);
            _mockFileSystemProvider.Verify(x => x.DeleteIfExists(gitArchiveFilePath), Times.Once);
            _mockFileSystemProvider.Verify(x => x.DeleteIfExists(metadataArchiveFilePath), Times.Once);
        }

        [Fact]
        public async Task AdoServer_Source_Without_SourceOrg_Provided_Throws_Error()
        {
            await FluentActions
                .Invoking(async () => await _handler.Handle(new MigrateRepoCommandArgs
                {
                    AdoServerUrl = "https://ado.contoso.com",
                    AdoTeamProject = "FooProj",
                    SourceRepo = SOURCE_REPO,
                    GithubTargetOrg = TARGET_ORG,
                    TargetRepo = TARGET_REPO
                }
                ))
                .Should().ThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task Github_With_Archive_Urls()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://github.com/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();

            var gitArchiveUrl = "https://example.com/git_archive.tar.gz";
            var metadataArchiveUrl = "https://example.com/metadata_archive.tar.gz";

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    gitArchiveUrl,
                    metadataArchiveUrl,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GitArchiveUrl = gitArchiveUrl,
                MetadataArchiveUrl = metadataArchiveUrl,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
        }

        [Fact]
        public async Task Github_Only_One_Archive_Url_Throws_Error()
        {
            var gitArchiveUrl = "https://example.com/git_archive.tar.gz";

            await FluentActions
                .Invoking(async () => await _handler.Handle(new MigrateRepoCommandArgs
                {
                    GithubSourceOrg = SOURCE_ORG,
                    SourceRepo = SOURCE_REPO,
                    GithubTargetOrg = TARGET_ORG,
                    TargetRepo = TARGET_REPO,
                    TargetApiUrl = TARGET_API_URL,
                    GitArchiveUrl = gitArchiveUrl,
                    Wait = true
                }))
                .Should().ThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task No_Source_Provided_Throws_Error()
        {
            await FluentActions
                .Invoking(async () => await _handler.Handle(new MigrateRepoCommandArgs
                {
                    SourceRepo = SOURCE_REPO,
                    GithubTargetOrg = TARGET_ORG,
                    TargetRepo = TARGET_REPO,
                    TargetApiUrl = ""
                }))
                .Should().ThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task Ado_Source_Without_Team_Project_Throws_Error()
        {
            await FluentActions
                .Invoking(async () => await _handler.Handle(new MigrateRepoCommandArgs
                {
                    AdoSourceOrg = SOURCE_ORG,
                    SourceRepo = SOURCE_REPO,
                    GithubTargetOrg = TARGET_ORG,
                    TargetRepo = TARGET_REPO,
                    TargetApiUrl = ""
                }))
                .Should().ThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task Defaults_TARGET_REPO_To_SourceRepo()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://github.com/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    SOURCE_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    null,
                    null,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, SOURCE_REPO, null, null));

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
        }

        [Fact]
        public async Task Ghes_Without_AzureConnectionString_Or_Aws_Bucket_Name_Throws_Error()
        {
            await FluentActions
                .Invoking(async () => await _handler.Handle(new MigrateRepoCommandArgs
                {
                    GithubSourceOrg = SOURCE_ORG,
                    SourceRepo = SOURCE_REPO,
                    GithubTargetOrg = TARGET_ORG,
                    TargetRepo = TARGET_REPO,
                    GhesApiUrl = GHES_API_URL
                }
                ))
                .Should().ThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task Ghes_AzureConnectionString_Uses_Env_When_Option_Empty()
        {
            var azureConnectionStringEnv = Guid.NewGuid().ToString();

            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;

            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";
            var authenticatedGitArchiveUrl = new Uri($"https://example.com/{gitArchiveId}/authenticated");
            var authenticatedMetadataArchiveUrl = new Uri($"https://example.com/{metadataArchiveId}/authenticated");

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    authenticatedGitArchiveUrl.ToString(),
                    authenticatedMetadataArchiveUrl.ToString(),
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockAzureApi.SetupSequence(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<FileStream>()).Result).Returns(authenticatedGitArchiveUrl).Returns(authenticatedMetadataArchiveUrl);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.AzureStorageConnectionString(It.IsAny<bool>())).Returns(azureConnectionStringEnv);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
        }

        [Fact]
        public async Task Ghes_With_NoSslVerify_Uses_NoSsl_Client()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;

            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";
            var authenticatedGitArchiveUrl = new Uri($"https://example.com/{gitArchiveId}/authenticated");
            var authenticatedMetadataArchiveUrl = new Uri($"https://example.com/{metadataArchiveId}/authenticated");

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    authenticatedGitArchiveUrl.ToString(),
                    authenticatedMetadataArchiveUrl.ToString(),
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockAzureApi.SetupSequence(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<FileStream>()).Result).Returns(authenticatedGitArchiveUrl).Returns(authenticatedMetadataArchiveUrl);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                NoSslVerify = true,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
            _mockOctoLogger.Verify(x => x.LogInformation("SSL verification disabled"));
        }

        [Fact]
        public async Task Ghes_With_3_8_0_Version_Returns_Archive_Urls_Directly()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;

            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    gitArchiveUrl,
                    metadataArchiveUrl,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.GetEnterpriseServerVersion().Result).Returns("3.8.0");
            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                NoSslVerify = true,
                Wait = true
            };
            await _handler.Handle(args);

            _mockTargetGithubApi.Verify(x => x.GetMigration(migrationId));
            _mockOctoLogger.Verify(x => x.LogInformation("SSL verification disabled"));
        }

        [Fact]
        public async Task Ghes_Failed_Archive_Generation_Throws_Error()
        {
            var gitArchiveId = 1;
            var metadataArchiveId = 2;

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Failed);

            await FluentActions
                .Invoking(async () => await _handler.Handle(new MigrateRepoCommandArgs
                {
                    GithubSourceOrg = SOURCE_ORG,
                    SourceRepo = SOURCE_REPO,
                    GithubTargetOrg = TARGET_ORG,
                    TargetRepo = TARGET_REPO,
                    TargetApiUrl = TARGET_API_URL,
                    GhesApiUrl = GHES_API_URL,
                    AzureStorageConnectionString = AZURE_CONNECTION_STRING
                }
                ))
                .Should().ThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task It_Uses_Ado_Pat_When_Provided()
        {
            // Arrange
            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                AdoSourceOrg = SOURCE_ORG,
                AdoTeamProject = "adoTeamProject",
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                AdoPat = ADO_PAT
            };
            await _handler.Handle(args);

            // Assert
            actualLogOutput.Should().Contain("ADO PAT: ***");
            actualLogOutput.Should().NotContain("GITHUB SOURCE PAT: ***");
            actualLogOutput.Should().NotContain("GITHUB TARGET PAT: ***");
            actualLogOutput.Should().NotContain("Since github-target-pat is provided, github-source-pat will also use its value.");

            _mockEnvironmentVariableProvider.Verify(m => m.AdoPersonalAccessToken(It.IsAny<bool>()), Times.Never);
            _mockTargetGithubApi.Verify(m => m.CreateAdoMigrationSource(It.IsAny<string>(), null));
            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                ADO_PAT,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()));
        }

        [Fact]
        public async Task It_Uses_Github_Source_And_Target_Pats_When_Provided()
        {
            // Arrange
            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GithubTargetPat = GITHUB_TARGET_PAT,
                GithubSourcePat = GITHUB_SOURCE_PAT
            };
            await _handler.Handle(args);

            // Assert
            actualLogOutput.Should().NotContain("ADO PAT: ***");
            actualLogOutput.Should().Contain("GITHUB SOURCE PAT: ***");
            actualLogOutput.Should().Contain("GITHUB TARGET PAT: ***");
            actualLogOutput.Should().NotContain("Since github-target-pat is provided, github-source-pat will also use its value.");

            _mockEnvironmentVariableProvider.Verify(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>()), Times.Never);
            _mockEnvironmentVariableProvider.Verify(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>()), Times.Never);
            _mockTargetGithubApi.Verify(m => m.CreateGhecMigrationSource(It.IsAny<string>()));
            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                GITHUB_SOURCE_PAT,
                GITHUB_TARGET_PAT,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()));
        }

        [Fact]
        public async Task It_Uses_Github_Source_Pat_When_Provided()
        {
            // Arrange
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, It.IsAny<int>()).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, It.IsAny<int>()).Result).Returns(ArchiveMigrationStatus.Exported);

            _mockAzureApi.Setup(m => m.UploadToBlob(It.IsAny<string>(), It.IsAny<Stream>()).Result).Returns(new Uri("https://example.com/resource"));

            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                GithubSourcePat = GITHUB_SOURCE_PAT
            };
            await _handler.Handle(args);

            // Assert
            actualLogOutput.Should().NotContain("ADO PAT: ***");
            actualLogOutput.Should().Contain("GITHUB SOURCE PAT: ***");
            actualLogOutput.Should().NotContain("GITHUB TARGET PAT: ***");
            actualLogOutput.Should().NotContain("Since github-target-pat is provided, github-source-pat will also use its value.");

            _mockEnvironmentVariableProvider.Verify(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>()), Times.Never);
            _mockEnvironmentVariableProvider.Verify(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>()));
            _mockTargetGithubApi.Verify(m => m.CreateGhecMigrationSource(It.IsAny<string>()));
            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                GITHUB_SOURCE_PAT,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()));
        }

        [Fact]
        public async Task It_Falls_Back_To_Github_Target_Pat_If_Github_Source_Pat_Is_Not_Provided()
        {
            // Arrange
            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GithubTargetPat = GITHUB_TARGET_PAT
            };
            await _handler.Handle(args);

            // Assert
            actualLogOutput.Should().NotContain("ADO PAT: ***");
            actualLogOutput.Should().NotContain("GITHUB SOURCE PAT: ***");
            actualLogOutput.Should().Contain("GITHUB TARGET PAT: ***");
            actualLogOutput.Should().Contain("Since github-target-pat is provided, github-source-pat will also use its value.");

            _mockEnvironmentVariableProvider.Verify(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>()), Times.Never);
            _mockEnvironmentVariableProvider.Verify(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>()), Times.Never);
            _mockTargetGithubApi.Verify(m => m.CreateGhecMigrationSource(It.IsAny<string>()));
            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                GITHUB_TARGET_PAT,
                GITHUB_TARGET_PAT,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()));
        }

        [Fact]
        public async Task It_Skips_Releases_When_Option_Is_True()
        {
            // Arrange
            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                SkipReleases = true
            };
            await _handler.Handle(args);

            // Assert
            actualLogOutput.Should().Contain("SKIP RELEASES: true");

            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                true,
                It.IsAny<bool>()));
        }

        [Fact]
        public async Task It_Locks_Source_Repo_When_Option_Is_True()
        {
            // Arrange
            var actualLogOutput = new List<string>();
            _mockOctoLogger.Setup(m => m.LogInformation(It.IsAny<string>())).Callback<string>(s => actualLogOutput.Add(s));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                SkipReleases = false,
                LockSourceRepo = true
            };
            await _handler.Handle(args);

            // Assert
            actualLogOutput.Should().Contain("LOCK SOURCE REPO: true");

            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                true));
        }

        [Fact]
        public async Task Does_Not_Pass_Lock_Repos_To_StartMigration_For_GHES()
        {
            // Arrange
            _mockSourceGithubApi.Setup(m => m.GetArchiveMigrationStatus(It.IsAny<string>(), It.IsAny<int>()).Result).Returns(ArchiveMigrationStatus.Exported);

            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockAzureApi.Setup(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<Stream>()).Result).Returns(new Uri("https://example.com/resource"));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                SkipReleases = true,
                LockSourceRepo = true
            };
            await _handler.Handle(args);

            // Assert
            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                false));
        }

        [Fact]
        public async Task It_Skips_Releases_When_Option_Is_True_For_Ghes_Migration_Path()
        {
            // Arrange
            _mockSourceGithubApi.Setup(m => m.GetArchiveMigrationStatus(It.IsAny<string>(), It.IsAny<int>()).Result).Returns(ArchiveMigrationStatus.Exported);

            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockAzureApi.Setup(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<Stream>()).Result).Returns(new Uri("https://example.com/resource"));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                SkipReleases = true
            };
            await _handler.Handle(args);

            // Assert
            _mockSourceGithubApi.Verify(m => m.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, true, false));
        }

        [Fact]
        public async Task It_Locks_Repos_When_Option_Is_True_For_Ghes_Migration_Path()
        {
            // Arrange
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(m => m.GetArchiveMigrationStatus(It.IsAny<string>(), It.IsAny<int>()).Result).Returns(ArchiveMigrationStatus.Exported);

            _mockHttpDownloadService.Setup(x => x.DownloadToBytes(It.IsAny<string>()).Result).Returns(Array.Empty<byte>());
            _mockAzureApi.Setup(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<Stream>()).Result).Returns(new Uri("https://example.com/resource"));

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                SkipReleases = true,
                LockSourceRepo = true
            };
            await _handler.Handle(args);

            // Assert
            _mockSourceGithubApi.Verify(m => m.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, true, true));
        }

        [Fact]
        public async Task It_Extracts_Base_Ghes_Url_From_Ghes_Api_Url_Using_Alternate_Template()
        {
            // Arrange
            const string ghesApiUrl = "https://api.myghes.com";
            var expectedGithubRepoUrl = $"https://myghes.com/{SOURCE_ORG}/{SOURCE_REPO}";

            _mockSourceGithubApi
                .Setup(x => x.StartMigration(
                    It.IsAny<string>(),
                    expectedGithubRepoUrl,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    false,
                    false).Result)
                .Returns("migrationId");

            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(It.IsAny<string>(), It.IsAny<int>()).Result).Returns(ArchiveMigrationStatus.Exported);

            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockAzureApi.Setup(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<Stream>()).Result).Returns(new Uri("https://blob-url"));

            // act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = ghesApiUrl,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING
            };
            await _handler.Handle(args);

            // Assert
            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                expectedGithubRepoUrl,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                false,
                false));
        }

        [Fact]
        public async Task It_Falls_Back_To_The_Ghes_Api_Url_If_Could_Not_Extract_Base_Ghes_Url()
        {
            // Arrange
            const string ghesApiUrl = "https://non-conforming-ghes-api-url";
            var expectedGithubRepoUrl = $"{ghesApiUrl}/{SOURCE_ORG}/{SOURCE_REPO}";

            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    It.IsAny<string>(),
                    expectedGithubRepoUrl,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    false,
                    false).Result)
                .Returns("migrationId");

            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(It.IsAny<string>(), It.IsAny<int>()).Result).Returns(ArchiveMigrationStatus.Exported);

            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockAzureApi.Setup(x => x.UploadToBlob(It.IsAny<string>(), It.IsAny<Stream>()).Result).Returns(new Uri("https://blob-url"));

            // act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = ghesApiUrl,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING
            };
            await _handler.Handle(args);

            // Assert
            _mockTargetGithubApi.Verify(m => m.StartMigration(
                It.IsAny<string>(),
                expectedGithubRepoUrl,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                false,
                false));
        }

        [Fact]
        public async Task It_Uses_Aws_If_Arguments_Are_Included()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;

            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";
            var gitArchiveContent = new byte[] { 1, 2, 3, 4, 5 };
            var metadataArchiveContent = new byte[] { 6, 7, 8, 9, 10 };

            var awsAccessKeyId = "awsAccessKeyId";
            var awsSecretAccessKey = "awsSecretAccessKey";
            var awsBucketName = "awsBucketName";
            var archiveUrl = $"https://s3.amazonaws.com/{awsBucketName}/archive.tar";

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    archiveUrl,
                    archiveUrl,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, null, null));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockHttpDownloadService.Setup(x => x.DownloadToBytes(gitArchiveUrl).Result).Returns(gitArchiveContent);
            _mockHttpDownloadService.Setup(x => x.DownloadToBytes(metadataArchiveUrl).Result).Returns(metadataArchiveContent);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);

            _mockAwsApi.Setup(m => m.UploadToBucket(awsBucketName, It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(archiveUrl);

            var handler = new MigrateRepoCommandHandler(
                _mockOctoLogger.Object,
                _mockSourceGithubApi.Object,
                _mockTargetGithubApi.Object,
                _mockEnvironmentVariableProvider.Object,
                _mockAzureApi.Object,
                _mockAwsApi.Object,
                _mockHttpDownloadService.Object,
                _mockFileSystemProvider.Object);

            // Act
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AwsBucketName = awsBucketName,
                AwsAccessKey = awsAccessKeyId,
                AwsSecretKey = awsSecretAccessKey,
                Wait = true
            };

            await handler.Handle(args);

            // Assert
            _mockAwsApi.Verify(m => m.UploadToBucket(awsBucketName, It.IsAny<Stream>(), It.IsAny<string>()));
        }

        [Fact]
        public async Task Ghes_With_Both_Azure_Storage_Connection_String_And_Aws_Bucket_Name_Throws()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                AwsBucketName = AWS_BUCKET_NAME
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Is_Provided_But_No_Aws_Access_Key_Id_Throws()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AwsBucketName = AWS_BUCKET_NAME,
                AwsSecretKey = AWS_SECRET_ACCESS_KEY
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*--aws-access-key*");
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Is_Provided_And_Can_Fallback_To_Aws_Access_Key_Environment_Variable_Does_Not_Throw()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;

            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";
            var gitArchiveContent = new byte[] { 1, 2, 3, 4, 5 };
            var metadataArchiveContent = new byte[] { 6, 7, 8, 9, 10 };

            var archiveUrl = $"https://s3.amazonaws.com/{AWS_BUCKET_NAME}/archive.tar";

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    archiveUrl,
                    archiveUrl,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, "", ""));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockHttpDownloadService.Setup(x => x.DownloadToBytes(gitArchiveUrl).Result).Returns(gitArchiveContent);
            _mockHttpDownloadService.Setup(x => x.DownloadToBytes(metadataArchiveUrl).Result).Returns(metadataArchiveContent);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);
#pragma warning disable CS0618
            _mockEnvironmentVariableProvider.Setup(m => m.AwsAccessKey(false)).Returns(AWS_ACCESS_KEY);
#pragma warning restore CS0618

            _mockAwsApi.Setup(m => m.UploadToBucket(AWS_BUCKET_NAME, It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(archiveUrl);

            var handler = new MigrateRepoCommandHandler(
                _mockOctoLogger.Object,
                _mockSourceGithubApi.Object,
                _mockTargetGithubApi.Object,
                _mockEnvironmentVariableProvider.Object,
                _mockAzureApi.Object,
                _mockAwsApi.Object,
                _mockHttpDownloadService.Object,
                _mockFileSystemProvider.Object);

            // Act, Assert
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AwsBucketName = AWS_BUCKET_NAME,
                AwsSecretKey = AWS_SECRET_ACCESS_KEY,
                Wait = true
            };
            await handler.Invoking(async x => await x.Handle(args)).Should().NotThrowAsync();

            _mockOctoLogger.Verify(m => m.LogWarning(It.Is<string>(msg => msg.Contains("AWS_ACCESS_KEY"))));
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Is_Provided_But_No_Aws_Secret_Key_Throws()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AwsBucketName = AWS_BUCKET_NAME,
                AwsAccessKey = AWS_ACCESS_KEY_ID
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*--aws-secret-key*");
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Is_Provided_And_Can_Fallback_To_Aws_Secret_Key_Environment_Variable_Does_Not_Throw()
        {
            var githubOrgId = Guid.NewGuid().ToString();
            var migrationSourceId = Guid.NewGuid().ToString();
            var sourceGithubPat = Guid.NewGuid().ToString();
            var targetGithubPat = Guid.NewGuid().ToString();
            var githubRepoUrl = $"https://myghes/{SOURCE_ORG}/{SOURCE_REPO}";
            var migrationId = Guid.NewGuid().ToString();
            var gitArchiveId = 1;
            var metadataArchiveId = 2;

            var gitArchiveUrl = $"https://example.com/{gitArchiveId}";
            var metadataArchiveUrl = $"https://example.com/{metadataArchiveId}";
            var gitArchiveContent = new byte[] { 1, 2, 3, 4, 5 };
            var metadataArchiveContent = new byte[] { 6, 7, 8, 9, 10 };

            var archiveUrl = $"https://s3.amazonaws.com/{AWS_BUCKET_NAME}/archive.tar";

            _mockTargetGithubApi.Setup(x => x.GetOrganizationId(TARGET_ORG).Result).Returns(githubOrgId);
            _mockTargetGithubApi.Setup(x => x.CreateGhecMigrationSource(githubOrgId).Result).Returns(migrationSourceId);
            _mockTargetGithubApi
                .Setup(x => x.StartMigration(
                    migrationSourceId,
                    githubRepoUrl,
                    githubOrgId,
                    TARGET_REPO,
                    sourceGithubPat,
                    targetGithubPat,
                    archiveUrl,
                    archiveUrl,
                    false,
                    false).Result)
                .Returns(migrationId);
            _mockTargetGithubApi.Setup(x => x.GetMigration(migrationId).Result).Returns((State: RepositoryMigrationStatus.Succeeded, TARGET_REPO, "", ""));
            _mockTargetGithubApi.Setup(x => x.DoesOrgExist(TARGET_ORG).Result).Returns(true);

            _mockSourceGithubApi.Setup(x => x.StartGitArchiveGeneration(SOURCE_ORG, SOURCE_REPO).Result).Returns(gitArchiveId);
            _mockSourceGithubApi.Setup(x => x.StartMetadataArchiveGeneration(SOURCE_ORG, SOURCE_REPO, false, false).Result).Returns(metadataArchiveId);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, gitArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationStatus(SOURCE_ORG, metadataArchiveId).Result).Returns(ArchiveMigrationStatus.Exported);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, gitArchiveId).Result).Returns(gitArchiveUrl);
            _mockSourceGithubApi.Setup(x => x.GetArchiveMigrationUrl(SOURCE_ORG, metadataArchiveId).Result).Returns(metadataArchiveUrl);

            _mockHttpDownloadService.Setup(x => x.DownloadToBytes(gitArchiveUrl).Result).Returns(gitArchiveContent);
            _mockHttpDownloadService.Setup(x => x.DownloadToBytes(metadataArchiveUrl).Result).Returns(metadataArchiveContent);

            _mockEnvironmentVariableProvider.Setup(m => m.SourceGithubPersonalAccessToken(It.IsAny<bool>())).Returns(sourceGithubPat);
            _mockEnvironmentVariableProvider.Setup(m => m.TargetGithubPersonalAccessToken(It.IsAny<bool>())).Returns(targetGithubPat);
#pragma warning disable CS0618
            _mockEnvironmentVariableProvider.Setup(m => m.AwsSecretKey(false)).Returns(AWS_SECRET_KEY);
#pragma warning restore CS0618

            _mockAwsApi.Setup(m => m.UploadToBucket(AWS_BUCKET_NAME, It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(archiveUrl);

            var handler = new MigrateRepoCommandHandler(
                _mockOctoLogger.Object,
                _mockSourceGithubApi.Object,
                _mockTargetGithubApi.Object,
                _mockEnvironmentVariableProvider.Object,
                _mockAzureApi.Object,
                _mockAwsApi.Object,
                _mockHttpDownloadService.Object,
                _mockFileSystemProvider.Object);

            // Act, Assert
            var args = new MigrateRepoCommandArgs
            {
                GithubSourceOrg = SOURCE_ORG,
                SourceRepo = SOURCE_REPO,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                TargetApiUrl = TARGET_API_URL,
                GhesApiUrl = GHES_API_URL,
                AwsBucketName = AWS_BUCKET_NAME,
                AwsAccessKey = AWS_ACCESS_KEY,
                Wait = true
            };
            await handler.Invoking(async x => await x.Handle(args)).Should().NotThrowAsync();

            _mockOctoLogger.Verify(m => m.LogWarning(It.Is<string>(msg => msg.Contains("AWS_SECRET_KEY"))));
        }

        [Fact]
        public async Task Ghes_When_Aws_Session_Token_Is_Provided_But_No_Aws_Region_Throws()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AwsBucketName = AWS_BUCKET_NAME,
                AwsAccessKey = AWS_ACCESS_KEY_ID,
                AwsSecretKey = AWS_SECRET_ACCESS_KEY,
                AwsSessionToken = AWS_SESSION_TOKEN
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*--aws-region*AWS_REGION*--aws-session-token*AWS_SESSION_TOKEN*");
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Not_Provided_But_Aws_Access_Key_Provided()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                AwsAccessKey = AWS_ACCESS_KEY_ID
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*AWS S3*--aws-bucket-name*");
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Not_Provided_But_Aws_Secret_Key_Provided()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                AwsSecretKey = AWS_SECRET_ACCESS_KEY
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*AWS S3*--aws-bucket-name*");
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Not_Provided_But_Aws_Session_Token_Provided()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                AwsSessionToken = AWS_SECRET_ACCESS_KEY
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*AWS S3*--aws-bucket-name*");
        }

        [Fact]
        public async Task Ghes_When_Aws_Bucket_Name_Not_Provided_But_Aws_Region_Provided()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                GhesApiUrl = GHES_API_URL,
                AzureStorageConnectionString = AZURE_CONNECTION_STRING,
                AwsRegion = AWS_REGION
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*AWS S3*--aws-bucket-name*");
        }

        [Fact]
        public async Task Aws_Bucket_Name_Without_Ghes_Api_Url_Throws()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                AwsBucketName = AWS_BUCKET_NAME
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*--aws-bucket-name*");
        }

        [Fact]
        public async Task No_Ssl_Verify_Without_Ghes_Api_Url_Throws()
        {
            await _handler.Invoking(async x => await x.Handle(new MigrateRepoCommandArgs
            {
                SourceRepo = SOURCE_REPO,
                GithubSourceOrg = SOURCE_ORG,
                GithubTargetOrg = TARGET_ORG,
                TargetRepo = TARGET_REPO,
                NoSslVerify = true
            }))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage("*--no-ssl-verify*");
        }
    }
}
