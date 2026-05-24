using System.Windows;
using System.Windows.Controls;

namespace SukiMars.Views
{
    public partial class AdminUsersPage : Page
    {
        public AdminUsersPage()
        {
            InitializeComponent();
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var signUpWindow = new SignUpWindow();
            var owner = Window.GetWindow(this);
            if (owner != null)
            {
                signUpWindow.Owner = owner;
            }
            signUpWindow.ShowDialog();
        }
    }
}
