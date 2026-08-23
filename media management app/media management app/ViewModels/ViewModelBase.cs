using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public abstract partial class ViewModelBase : ObservableObject, INavigationAware
{
    public virtual void OnNavigatedTo()
    {
    }

    public virtual void OnNavigatedFrom()
    {
    }
}
