using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MD.BRIDGE.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        // 최소화 버튼
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // 최대화 버튼
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
                this.WindowState = WindowState.Maximized;
            else
                this.WindowState = WindowState.Normal;
        }

        // 닫기 버튼
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // 타이틀 바 드래그 이동
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 더블클릭: 최대화 <-> 복원
                if (this.WindowState == WindowState.Maximized)
                    this.WindowState = WindowState.Normal;
                else
                    this.WindowState = WindowState.Maximized;
            }
            else
            {
                // 단일 클릭: 창 이동
                if (e.ButtonState == MouseButtonState.Pressed)
                    this.DragMove();
            }
        }
        
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange;
            double newHeight = this.Height + e.VerticalChange;

            if (newWidth > this.MinWidth)
                this.Width = newWidth;

            if (newHeight > this.MinHeight)
                this.Height = newHeight;
        }

        private void ComboBoxArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // sender는 ControlTemplate의 최상위 Grid이므로, DataContext 또는 부모 체인에서 ComboBox를 가져와야 합니다.
            // 혹은 sender의 TemplatedParent로 접근 가능하면 이를 사용합니다.
            // 간단하게 VisualTreeHelper를 통해 ComboBox를 찾는 방법:
            DependencyObject dep = (DependencyObject)sender;
            while (dep != null && !(dep is ComboBox))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is ComboBox comboBox)
            {
                comboBox.IsDropDownOpen = true;
                e.Handled = true;
            }
        }

        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }
    }
}
