using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Search;

public class QueryTemplateDraftTests
{
    [Fact]
    public void LoadedCommittedItem_IsNotDirty()
    {
        var draft = new QueryTemplateDraft("{title} {quality}", committed: true);

        draft.IsCommitted.Should().BeTrue();
        draft.IsDirty.Should().BeFalse();
        draft.CommittedPattern.Should().Be("{title} {quality}");
    }

    [Fact]
    public void Edit_MarksDirty_SaveClears_ResetRestores()
    {
        var draft = new QueryTemplateDraft("{title}", committed: true);
        draft.Pattern = "{title} {year}";

        draft.IsDirty.Should().BeTrue();

        draft.Save();
        draft.IsDirty.Should().BeFalse();
        draft.CommittedPattern.Should().Be("{title} {year}");

        draft.Pattern = "{title} {quality}";
        draft.Reset();
        draft.Pattern.Should().Be("{title} {year}");
        draft.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void UncommittedAdd_IsDirty_ResetUsesBaseline_OmittedUntilSave()
    {
        var draft = new QueryTemplateDraft("{title} {quality}", committed: false);

        draft.IsCommitted.Should().BeFalse();
        draft.IsDirty.Should().BeTrue();
        draft.CommittedPattern.Should().BeNull();

        draft.Pattern = "{title}";
        draft.Reset();
        draft.Pattern.Should().Be("{title} {quality}");
        draft.IsCommitted.Should().BeFalse();

        draft.Save();
        draft.IsCommitted.Should().BeTrue();
        draft.CommittedPattern.Should().Be("{title} {quality}");
        draft.IsDirty.Should().BeFalse();
    }
}
