using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MaX
{
    /// <summary>
    /// Логика взаимодействия для Chat.xaml
    /// </summary>
    public partial class Chat : Window
    {
        string tek_message = "";
        public Chat(string NikName)
        {
            InitializeComponent();
            CurrentUser.Text = NikName;
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            tek_message = Message.Text;

            TextBlock message = new TextBlock();
            message.Foreground = Brushes.White;
            message.FontSize = 20;

            message.Text = tek_message;
            MessagesPanel.Children.Add(message);

            Message.Text = "";
        }
    }
}
