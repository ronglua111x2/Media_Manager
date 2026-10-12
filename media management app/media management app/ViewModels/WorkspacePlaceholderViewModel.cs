namespace media_management_app.ViewModels;

public abstract class WorkspacePlaceholderViewModel : ViewModelBase
{
    protected WorkspacePlaceholderViewModel(string title, string summary)
    {
        Title = title;
        Summary = summary;
    }

    public string Title { get; }

    public string Summary { get; }
}
