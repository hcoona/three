using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Platform;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class CiCallerControlTests(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ControlPreparationRetainsAbsentBasisAndFullReuse(bool full)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("README.md", "initial");
        string basis = await repo.CommitAsync();
        await repo.SetAsync(ControlTestInputs.Project, "<Project />");
        string candidate = await repo.CommitAsync();
        if (full) basis = candidate;
        string directory = Path.Combine(Path.GetTempPath(), "three-control-" + Guid.NewGuid());
        try
        {
            ControlTestInputRequest request = await ControlCiCaller.PrepareAsync(repo.Directory,
                directory, new(basis, candidate, full), context.CancellationToken);
            Assert.AreEqual(candidate, request.Comparison.Candidate);
            Assert.AreEqual(basis, request.Comparison.Basis);
            Assert.AreEqual(full, request.Comparison.Full);
            Assert.AreEqual(candidate, (await new GitReader(request.CandidateDirectory)
                .ReadAsync("HEAD", context.CancellationToken)).Commit);
            if (full) Assert.AreEqual(request.CandidateDirectory, request.BasisDirectory);
            else Assert.IsNull(request.BasisDirectory);
            var endpoints = CiCallerTransfer.Read(Path.Combine(directory,
                "group-endpoints.json"), TransferJson.Default.CiCallerEndpoints);
            Assert.AreEqual(basis, endpoints.Basis.Reference);
            Assert.AreEqual(candidate, endpoints.Candidate.Reference);
            Assert.AreEqual(full, endpoints.Basis.Directory == endpoints.Candidate.Directory);
            Assert.AreEqual(request, CiCallerTransfer.Read(Path.Combine(directory,
                "request.json"), TransferJson.Default.ControlTestInputRequest));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task InvalidCandidateAndOverlappingStateFailBeforePreparation()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("README.md", "missing owner");
        string basis = await repo.CommitAsync();
        await repo.SetAsync(ControlTestInputs.Project, "<Project />");
        string candidate = await repo.CommitAsync();
        string directory = Path.Combine(Path.GetTempPath(), "three-control-" + Guid.NewGuid());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlCiCaller.PrepareAsync(
            repo.Directory, directory, new(basis, basis, false), context.CancellationToken));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlCiCaller.PrepareAsync(
            repo.Directory, directory, new(basis, candidate, true), context.CancellationToken));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlCiCaller.PrepareAsync(
            repo.Directory, repo.Directory, new(candidate, candidate, true),
            context.CancellationToken));
        Assert.IsFalse(Directory.Exists(directory));
    }
}
