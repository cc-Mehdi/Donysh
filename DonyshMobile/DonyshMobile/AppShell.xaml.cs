namespace DonyshMobile
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
        }

        protected override void OnNavigated(ShellNavigatedEventArgs args)
        {
            base.OnNavigated(args);
            if (CurrentPage is not { } page || Shell.GetTitleView(page) is not null) return;
            var title = new Label
            {
                FontFamily = "YekanBakhBold", FontSize = 18,
                VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            title.SetBinding(Label.TextProperty, new Binding(nameof(Page.Title), source: page));
            title.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#15243C"), Color.FromArgb("#ECF2FF"));
            Shell.SetTitleView(page, title);
        }
    }
}
