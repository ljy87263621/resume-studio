using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private ListView? visibleJobList;
    private string favoriteQuery="",favoriteSource="全部平台";
    private int favoritePage=1,favoritePageSize=20;
    private void ResetFavoriteView(){favoriteQuery="";favoriteSource="全部平台";favoritePage=1;}
    private async Task ShowFavorites()
    {
        int generation=navigationRevision;
        Heading("职位收藏","跨平台收藏到拾简，按平台与关键词整理。");
        var jobs=await api.Get<List<Job>>("favorites"); if(!PageIsCurrent("favorites",generation)) return;
        body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); body.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var controls=Stack(12); controls.Margin=new Thickness(0,0,0,14); controls.Children.Add(Button("＋ 从 BOSS 等平台链接收藏",ImportExternalFavorite,true));
        var browsing=Row();browsing.Children.Add(Button("BOSS 浏览并收藏",()=>OpenPlatformCaptureAsync("boss")));browsing.Children.Add(Button("牛客浏览并收藏",()=>OpenPlatformCaptureAsync("nowcoder")));controls.Children.Add(browsing);
        var filters=Row(); var query=new TextBox { PlaceholderText="职位 / 公司 / 备注",Width=280,Text=favoriteQuery };
        var sources=new[] {"全部平台"}.Concat(jobs.Select(j=>j.Source).Where(s=>s.Length>0).Distinct()).ToList();
        var source=new ComboBox { ItemsSource=sources,SelectedItem=sources.Contains(favoriteSource)?favoriteSource:sources[0],Width=180 }; filters.Children.Add(query); filters.Children.Add(source);filters.Children.Add(Button("清空筛选",()=>{query.Text="";source.SelectedIndex=0;return Task.CompletedTask;})); controls.Children.Add(filters);
        var count=Text("",12); controls.Children.Add(count); body.Children.Add(controls);
        var list=CreateJobList(true); Grid.SetRow(list,1); body.Children.Add(list);
        // Keep page controls outside the scrolling list, so old favorites remain reachable.
        var footer=Stack(6);footer.Margin=new Thickness(0,12,0,0);
        var pager=Row(8);pager.HorizontalAlignment=HorizontalAlignment.Center;
        var previous=new Button {Content="上一页",Padding=new Thickness(12,8,12,8),FontFamily=ResumeTypography.InterfaceFont};
        var next=new Button {Content="下一页",Padding=new Thickness(12,8,12,8),FontFamily=ResumeTypography.InterfaceFont};ButtonMotion(previous);ButtonMotion(next);
        var pages=new ComboBox {Width=90,Tag="favorite-pages"};ToolTipService.SetToolTip(pages,"跳转到指定页");
        int[] sizes=[10,20,50];var size=new ComboBox {ItemsSource=sizes.Select(n=>$"{n} 条/页").ToList(),SelectedIndex=Array.IndexOf(sizes,favoritePageSize),Width=115,Tag="favorite-page-size"};ToolTipService.SetToolTip(size,"每页显示的收藏数");
        pager.Children.Add(previous);pager.Children.Add(pages);pager.Children.Add(next);pager.Children.Add(size);footer.Children.Add(pager);
        var pageSummary=Text("",12);pageSummary.HorizontalAlignment=HorizontalAlignment.Center;footer.Children.Add(pageSummary);Grid.SetRow(footer,2);body.Children.Add(footer);
        bool refreshing=false;
        void Refresh(bool resetPage=false)
        {
            if(!PageIsCurrent("favorites",generation))return;
            string selected=source.SelectedItem as string??"全部平台";
            favoriteQuery=query.Text;favoriteSource=selected;
            var found=jobs.Where(j=>(j.Title+" "+j.Company+" "+j.Notes).Contains(query.Text,StringComparison.OrdinalIgnoreCase)&&(selected=="全部平台"||j.Source==selected)).ToList();
            int pageCount=(found.Count+favoritePageSize-1)/favoritePageSize;
            favoritePage=resetPage?1:Math.Clamp(favoritePage,1,Math.Max(1,pageCount));
            int offset=(favoritePage-1)*favoritePageSize;var pageJobs=found.Skip(offset).Take(favoritePageSize).ToList();
            list.ItemsSource=pageJobs;if(pageJobs.Count>0)list.ScrollIntoView(pageJobs[0]);
            count.Text=$"共 {jobs.Count} 条收藏 · 筛选结果 {found.Count} 条 · {selected}"+(found.Count==0?(jobs.Count==0?"。可浏览职位详情或粘贴链接添加。":"。可清空筛选查看全部收藏。") : "");
            refreshing=true;
            try {pages.ItemsSource=Enumerable.Range(1,pageCount).Select(n=>$"第 {n} 页").ToList();pages.SelectedIndex=pageCount==0?-1:favoritePage-1;pages.IsEnabled=pageCount>1;}
            finally {refreshing=false;}
            previous.IsEnabled=favoritePage>1;next.IsEnabled=favoritePage<pageCount;
            pageSummary.Text=pageCount==0?"没有匹配的收藏":$"第 {favoritePage} / {pageCount} 页 · 显示第 {offset+1}–{offset+pageJobs.Count} 条";
        }
        previous.Click+=(_,_)=>{if(favoritePage>1){favoritePage--;Refresh();}};
        next.Click+=(_,_)=>{favoritePage++;Refresh();};
        pages.SelectionChanged+=(_,_)=>{if(!refreshing&&pages.SelectedIndex>=0){favoritePage=pages.SelectedIndex+1;Refresh();}};
        size.SelectionChanged+=(_,_)=>{if(size.SelectedIndex>=0){favoritePageSize=sizes[size.SelectedIndex];Refresh(true);}};
        query.TextChanged+=(_,_)=>Refresh(true); source.SelectionChanged+=(_,_)=>Refresh(true); Refresh();
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
