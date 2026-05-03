using AIChat.Application.Workspace;

namespace AIChat.Tests.Workspace;

public sealed class WorkspaceChangeGrouperTests
{
    [Fact]
    public void Group_StagedChanges_GoToStagedList()
    {
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "main",
            Changes =
            [
                new WorkspaceChange { Status = "M ", Path = "src/Foo.cs" },
                new WorkspaceChange { Status = "A ", Path = "src/Bar.cs" }
            ]
        };

        var result = WorkspaceChangeGrouper.Group(changeSet);

        Assert.Equal(2, result.Staged.Count);
        Assert.Empty(result.Unstaged);
        Assert.Empty(result.Untracked);
        Assert.All(result.Staged, c => Assert.True(c.IsStaged));
    }

    [Fact]
    public void Group_UnstagedChanges_GoToUnstagedList()
    {
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "main",
            Changes =
            [
                new WorkspaceChange { Status = " M", Path = "src/Foo.cs" },
                new WorkspaceChange { Status = " D", Path = "src/Bar.cs" }
            ]
        };

        var result = WorkspaceChangeGrouper.Group(changeSet);

        Assert.Empty(result.Staged);
        Assert.Equal(2, result.Unstaged.Count);
        Assert.Empty(result.Untracked);
        Assert.All(result.Unstaged, c => Assert.True(c.IsUnstaged));
    }

    [Fact]
    public void Group_UntrackedChanges_GoToUntrackedList()
    {
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "main",
            Changes =
            [
                new WorkspaceChange { Status = "??", Path = "src/New.cs" }
            ]
        };

        var result = WorkspaceChangeGrouper.Group(changeSet);

        Assert.Empty(result.Staged);
        Assert.Empty(result.Unstaged);
        Assert.Single(result.Untracked);
        Assert.True(result.Untracked[0].IsUntracked);
    }

    [Fact]
    public void Group_CleanRepo_ShowsCleanStatusText()
    {
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "main",
            Changes = []
        };

        var result = WorkspaceChangeGrouper.Group(changeSet);

        Assert.Equal("工作区干净", result.StatusText);
        Assert.Empty(result.All);
    }

    [Fact]
    public void Group_Truncated_ShowsTruncatedInStatusText()
    {
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "main",
            IsTruncated = true,
            Changes = [new WorkspaceChange { Status = "M ", Path = "a.cs" }]
        };

        var result = WorkspaceChangeGrouper.Group(changeSet);

        Assert.Contains("列表已截断", result.StatusText);
    }

    [Fact]
    public void Group_PreservesAllCountAndOrder()
    {
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "develop",
            Changes =
            [
                new WorkspaceChange { Status = "M ", Path = "a.cs" },
                new WorkspaceChange { Status = "??", Path = "b.cs" },
                new WorkspaceChange { Status = " M", Path = "c.cs" }
            ]
        };

        var result = WorkspaceChangeGrouper.Group(changeSet);

        Assert.Equal(3, result.All.Count);
        Assert.Equal("a.cs", result.All[0].Path);
        Assert.Equal("b.cs", result.All[1].Path);
        Assert.Equal("c.cs", result.All[2].Path);
        Assert.Equal("develop", result.Branch);
    }

    [Fact]
    public void Group_MixedChanges_CorrectlySeparates()
    {
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "main",
            Changes =
            [
                new WorkspaceChange { Status = "M ", Path = "staged.cs" },
                new WorkspaceChange { Status = "MM", Path = "both.cs" },
                new WorkspaceChange { Status = "??", Path = "new.cs" },
                new WorkspaceChange { Status = " M", Path = "modified.cs" }
            ]
        };

        var result = WorkspaceChangeGrouper.Group(changeSet);

        // "M " and "MM" both have staged index, go to Staged
        Assert.Equal(2, result.Staged.Count);
        Assert.Equal("staged.cs", result.Staged[0].Path);
        Assert.Equal("both.cs", result.Staged[1].Path);

        Assert.Single(result.Unstaged);
        Assert.Equal("modified.cs", result.Unstaged[0].Path);

        Assert.Single(result.Untracked);
        Assert.Equal("new.cs", result.Untracked[0].Path);

        Assert.Equal(4, result.All.Count);
    }
}
