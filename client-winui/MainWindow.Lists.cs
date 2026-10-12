using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private ListView? visibleJobList;
    private async Task ShowFavorites()
    {
        Heading("职位收藏","跨平台收藏到拾简，按平台与关键词整理。");
        var jobs=await api.Get<List<Job>>("favorites"); if(currentPage!="favorites") return;
        body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); body.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
        var controls=Stack(12); controls.Margin=new Thickness(0,0,0,14); controls.Children.Add(Button("＋ 从 BOSS 等平台链接收藏",ImportExternalFavorite,true));
        var filters=Row(); var query=new TextBox { PlaceholderText="职位 / 公司 / 备注",Width=280 };
        var source=new ComboBox { ItemsSource=new[] {"全部平台"}.Concat(jobs.Select(j=>j.Source).Distinct()).ToList(),SelectedIndex=0,Width=180 }; filters.Children.Add(query); filters.Children.Add(source); controls.Children.Add(filters);
        var count=Text("",12); controls.Children.Add(count); body.Children.Add(controls);
        var list=CreateJobList(true); Grid.SetRow(list,1); body.Children.Add(list);
        void Refresh()
        {
            string selected=source.SelectedItem as string??"全部平台";
            var found=jobs.Where(j=>(j.Title+" "+j.Company+" "+j.Notes).Contains(query.Text,StringComparison.OrdinalIgnoreCase)&&(selected=="全部平台"||j.Source==selected)).ToList();
            list.ItemsSource=found; count.Text=found.Count==0?"没有匹配的收藏，可从招聘平台复制职位详情链接添加。":$"当前 {found.Count} 条收藏";
        }
        query.TextChanged+=(_,_)=>Refresh(); source.SelectionChanged+=(_,_)=>Refresh(); Refresh();
    }
    private ListView CreateJobList(bool favorite)
    {
        var style=new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,0,12)));
        var list=new ListView { SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=false,ItemContainerStyle=style,HorizontalContentAlignment=HorizontalAlignment.Stretch,
            ItemTemplate=(DataTemplate)Application.Current.Resources["JobCardTemplate"],Tag=new JobCardRenderer(job=>JobCard(job,favorite)) };
        // ItemsStackPanel inside ListView realizes cards only around the viewport.
        // Never wrap this list in another ScrollViewer: that would disable virtualization.
        visibleJobList=list;
        return list;
    }
}
