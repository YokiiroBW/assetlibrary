namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Proves that overlapping sources are refused whatever order a caller submits them in, so the
/// isolated-staging and duplicate-registration rules cannot be defeated by reordering input. The
/// same rules cover a directory that contains a managed library and a directory that duplicates one.
/// </summary>
[TestClass]
public sealed class DedupOverlapTests
{
    [TestMethod]
    public void ChildThenParentRegistrationIsRefusedInBothOrders()
    {
        using var scenario = new DedupScenario();
        scenario.WriteText("bulk/child/one.bin", "content");
        var parent = DedupAnalysisFactory.Source(
            scenario.UnregisteredRoot("bulk"),
            "parent",
            DedupSourceRole.InboundStaging);
        var nested = DedupAnalysisFactory.Source(
            scenario.UnregisteredRoot("bulk/child"),
            "child",
            DedupSourceRole.InboundStaging);

        var childFirst = DedupScopePolicy.Resolve([nested, parent], [], []);
        var parentFirst = DedupScopePolicy.Resolve([parent, nested], [], []);

        // Both orders refuse exactly one of the pair, so the walk never runs twice.
        Assert.HasCount(1, childFirst.Accepted);
        Assert.HasCount(1, childFirst.Rejected);
        Assert.HasCount(1, parentFirst.Accepted);
        Assert.HasCount(1, parentFirst.Rejected);
        Assert.AreEqual(DedupSourceRejection.DuplicateSourceRegistration, childFirst.Rejected.Single().Rejection);
        Assert.AreEqual(DedupSourceRejection.DuplicateSourceRegistration, parentFirst.Rejected.Single().Rejection);

        // The retained source is the one submitted first, so the caller sees a stable rule.
        Assert.AreEqual(nested.SourceId, childFirst.Accepted[0].SourceId);
        Assert.AreEqual(parent.SourceId, parentFirst.Accepted[0].SourceId);
    }

    [TestMethod]
    public void TheSameDirectorySubmittedTwiceIsRefusedInBothOrders()
    {
        using var scenario = new DedupScenario();
        var root = scenario.UnregisteredRoot("identical");
        scenario.WriteText("identical/one.bin", "content");
        var first = DedupAnalysisFactory.Source(root, "first", DedupSourceRole.InboundStaging);
        var second = DedupAnalysisFactory.Source(root, "second", DedupSourceRole.InboundStaging);

        var oneWay = DedupScopePolicy.Resolve([first, second], [], []);
        var otherWay = DedupScopePolicy.Resolve([second, first], [], []);

        Assert.HasCount(1, oneWay.Accepted);
        Assert.HasCount(1, otherWay.Accepted);
        Assert.AreEqual(DedupSourceRejection.DuplicateSourceRegistration, oneWay.Rejected.Single().Rejection);
        Assert.AreEqual(DedupSourceRejection.DuplicateSourceRegistration, otherWay.Rejected.Single().Rejection);
    }

    [TestMethod]
    public void AnInboundRootThatIsOrContainsALibraryIsRefusedEitherWay()
    {
        using var scenario = new DedupScenario();
        var library = scenario.RegisterRoot("library");
        scenario.WriteText("library/kept.bin", "content");
        var parent = DedupScenario.CanonicalRoot(scenario.Root);
        var equal = DedupAnalysisFactory.Source(library, "as-inbound", DedupSourceRole.InboundStaging);
        var asLibrary = DedupAnalysisFactory.Source(library, "as-library", DedupSourceRole.RegisteredLibrary);
        var aboveLibrary = DedupAnalysisFactory.Source(parent, "above-library", DedupSourceRole.InboundStaging);

        // The same directory cannot be both managed and freshly inbound, in either order, and the
        // verdict does not change with the order: a role contradiction refuses the pair consistently.
        var inboundFirst = DedupScopePolicy.Resolve([equal, asLibrary], scenario.RegisteredRoots, []);
        var libraryFirst = DedupScopePolicy.Resolve([asLibrary, equal], scenario.RegisteredRoots, []);
        Assert.IsEmpty(inboundFirst.Accepted);
        Assert.IsEmpty(libraryFirst.Accepted);
        Assert.HasCount(2, inboundFirst.Rejected);
        Assert.HasCount(2, libraryFirst.Rejected);
        Assert.AreEqual(
            inboundFirst.Rejected.Select(rejected => rejected.Rejection).Distinct().Single(),
            libraryFirst.Rejected.Select(rejected => rejected.Rejection).Distinct().Single());

        // A parent directory would re-import the whole managed library as inbound content.
        var ancestor = DedupScopePolicy.Resolve([aboveLibrary], scenario.RegisteredRoots, []);
        Assert.IsEmpty(ancestor.Accepted);
        Assert.AreEqual(DedupSourceRejection.ManagedLibraryOverlap, ancestor.Rejected.Single().Rejection);

        // The library root itself stays analysable in place when submitted with its own role.
        var inPlace = DedupScopePolicy.Resolve([asLibrary], scenario.RegisteredRoots, []);
        Assert.HasCount(1, inPlace.Accepted);
        Assert.IsEmpty(inPlace.Rejected);
    }

    [TestMethod]
    public void AStagingAreaInsideTheOutputRootIsRefusedInEitherOrder()
    {
        using var scenario = new DedupScenario();
        var output = scenario.AddOutputRoot("published");
        scenario.WriteText("published/staging/incoming.bin", "archive");
        var inside = DedupAnalysisFactory.Source(
            scenario.UnregisteredRoot("published/staging"),
            "inside-output",
            DedupSourceRole.InboundStaging);
        var around = DedupAnalysisFactory.Source(
            DedupScenario.CanonicalRoot(scenario.Root),
            "around-output",
            DedupSourceRole.InboundStaging);

        var insideOnly = DedupScopePolicy.Resolve([inside], [], [output]);
        Assert.AreEqual(DedupSourceRejection.OutputBackflow, insideOnly.Rejected.Single().Rejection);

        // An ancestor of the output root is refused the same way, in either submission order.
        var ancestor = DedupScopePolicy.Resolve([around], [], [output]);
        Assert.AreEqual(DedupSourceRejection.OutputBackflow, ancestor.Rejected.Single().Rejection);
        Assert.IsEmpty(DedupScopePolicy.Resolve([around, inside], [], [output]).Accepted);
        Assert.IsEmpty(DedupScopePolicy.Resolve([inside, around], [], [output]).Accepted);
    }
}
