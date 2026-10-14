using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    // The page and the result list receive finite, independent viewports. No outer scroll host.
    private sealed class CaptureViewport : IDisposable
    {
        public Grid Content { get; }=new() {RowSpacing=8};
        public WebView2 Web { get; }=new() {HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
        public ComboBox Zoom { get; }=new() {ItemsSource=new[] {"适合宽度","100%","90%","80%","70%","60%","50%"},SelectedIndex=0,Width=125};
        public TextBlock ScaleLabel { get; }=Text("网页缩放",11);
        public Button ResultsToggle { get; }=new() {Content="收起结果",Padding=new Thickness(12,5,12,5),FontFamily=ResumeTypography.InterfaceFont};
        private readonly XamlRoot root;
        private readonly DispatcherTimer resizeTimer=new() {Interval=TimeSpan.FromMilliseconds(180)};
        private readonly SemaphoreSlim scaleLock=new(1,1);
        private bool disposed;

        public CaptureViewport(XamlRoot root,FrameworkElement controls,TextBlock status,TextBlock address,ListView results)
        {
            this.root=root;
            foreach(var height in new[] {GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(3,GridUnitType.Star),GridLength.Auto,new GridLength(1,GridUnitType.Star)})Content.RowDefinitions.Add(new RowDefinition {Height=height});
            status.MaxLines=2;status.TextTrimming=TextTrimming.CharacterEllipsis;
            var summary=new Grid {ColumnSpacing=12};summary.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});summary.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});summary.Children.Add(status);
            var scaling=Row(8);ScaleLabel.VerticalAlignment=VerticalAlignment.Center;scaling.Children.Add(ScaleLabel);scaling.Children.Add(Zoom);Grid.SetColumn(scaling,1);summary.Children.Add(scaling);
            void Add(FrameworkElement element,int row){Grid.SetRow(element,row);Content.Children.Add(element);}
            var resultHeader=new Grid {ColumnSpacing=12};resultHeader.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});resultHeader.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});resultHeader.Children.Add(Text("本次手动收藏记录；完整正文、备注和投递跟进请到收藏页查看。",12));Grid.SetColumn(ResultsToggle,1);resultHeader.Children.Add(ResultsToggle);
            ButtonMotion(ResultsToggle);ResultsToggle.Click+=(_,_)=>{bool expanded=results.Visibility==Visibility.Visible;results.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;Content.RowDefinitions[5].Height=expanded?new GridLength(0):new GridLength(1,GridUnitType.Star);ResultsToggle.Content=expanded?"展开结果":"收起结果";};
            Add(controls,0);Add(summary,1);Add(address,2);Add(Web,3);Add(resultHeader,4);Add(results,5);
            results.MinHeight=0;results.MaxHeight=double.PositiveInfinity;results.HorizontalAlignment=HorizontalAlignment.Stretch;
            ScrollViewer.SetHorizontalScrollMode(results,ScrollMode.Disabled);ScrollViewer.SetVerticalScrollBarVisibility(results,ScrollBarVisibility.Auto);
            root.Changed+=RootChanged;Web.SizeChanged+=WebSizeChanged;Zoom.SelectionChanged+=ZoomChanged;resizeTimer.Tick+=ResizeTick;
            Resize();
        }
        private void RootChanged(XamlRoot sender,XamlRootChangedEventArgs args)=>Resize();
        private void Resize()
        {
            if(disposed)return;
            Content.Width=Math.Max(240,Math.Min(1380,root.Size.Width-80));
            Content.Height=Math.Max(180,Math.Min(880,root.Size.Height-190));
            QueueScale();
        }
        private void WebSizeChanged(object sender,SizeChangedEventArgs args)=>QueueScale();
        private void ZoomChanged(object sender,SelectionChangedEventArgs args)=>QueueScale();
        private void QueueScale(){if(disposed)return;resizeTimer.Stop();resizeTimer.Start();}
        private async void ResizeTick(object? sender,object args){resizeTimer.Stop();await FitAsync();}
        public async Task FitAsync()
        {
            if(disposed||Web.CoreWebView2 is null||Web.ActualWidth<1)return;
            await scaleLock.WaitAsync();
            try {
                if(disposed||Web.CoreWebView2 is null)return;
                double requested=Zoom.SelectedIndex<=0?0:1-(Zoom.SelectedIndex-1)*.1;
                // WinUI WebView2 does not expose controller zoom. CSS zoom reflows the desktop page,
                // preserving normal DOM input and hit testing; no viewport/device spoofing is used.
                string json=await Web.CoreWebView2.ExecuteScriptAsync($$"""
                (()=>{const html=document.documentElement;if(!html||!document.body)return null;
                  const requested={{JsonSerializer.Serialize(requested)}},previous=Number(html.style.zoom)||1,x=scrollX,y=scrollY;
                  html.style.zoom='1';
                  const viewport=html.clientWidth||innerWidth;
                  const width=Math.max(viewport,html.scrollWidth,document.body.scrollWidth);
                  const scale=requested||(width<=viewport+1?1:Math.max(.5,Math.min(1,(viewport-2)/width)));
                  html.style.zoom=String(scale);
                  html.getBoundingClientRect();scrollTo(x*scale/previous,y*scale/previous);return {scale,viewport,width};})()
                """);
                if(disposed||json=="null")return;
                using var result=JsonDocument.Parse(json);double scale=result.RootElement.GetProperty("scale").GetDouble();
                ScaleLabel.Text=$"网页 {scale:P0}";
            }
            catch(Exception) when(disposed) { }
            catch(Exception) {ScaleLabel.Text="网页缩放";}
            finally {scaleLock.Release();}
        }
        public void Dispose()
        {
            disposed=true;resizeTimer.Stop();root.Changed-=RootChanged;Web.SizeChanged-=WebSizeChanged;Zoom.SelectionChanged-=ZoomChanged;resizeTimer.Tick-=ResizeTick;
        }
    }
}
