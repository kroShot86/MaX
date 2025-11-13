using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Shapes;

namespace MaX
{
    public partial class Chat : Window
    {
        TcpClient client;
        NetworkStream stream;
        string tek_message = "";

        public Chat(string NikName)
        {
            InitializeComponent();
            CurrentUser.Text = NikName;
            Message.Focus();
            Message.PreviewKeyDown += (s, e) => SendStatus("TYPING");
            Message.KeyUp += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(Message.Text))
                    SendStatus("ONLINE");
            };

            InitializeSmilePanel();
            Smile.Click += (s, e) =>
            {
                SmilePanel.Visibility = SmilePanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            };

            ConnectToServer();
        }

        private void SendStatus(string status)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes($"STATUS:{CurrentUser.Text}|{status}");
                stream.Write(data, 0, data.Length);
            }
            catch { }
        }

        private void ConnectToServer()
        {
            try
            {
                client = new TcpClient("127.0.0.1", 5000);
                stream = client.GetStream();

                byte[] nameData = Encoding.UTF8.GetBytes(CurrentUser.Text);
                stream.Write(nameData, 0, nameData.Length);

                Thread receiveThread = new Thread(ReceiveMessages)
                {
                    IsBackground = true
                };
                receiveThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка подключения к серверу: " + ex.Message);
            }
        }

        private void ReceiveMessages()
        {
            var buffer = new byte[4096];

            while (true)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    Dispatcher.Invoke(() =>
                    {
                        if (message.StartsWith("USERS:"))
                        {
                            UpdateUsersList(message.Substring(6));
                        }
                        else if (message.StartsWith("FILE:"))
                        {
                            ReceiveFile(message);
                        }
                        else
                        {
                            int sep = message.IndexOf(": ");
                            if (sep > 0)
                            {
                                string sender = message.Substring(0, sep);
                                string text = message.Substring(sep + 2);
                                AddMessage(sender, text, sender == CurrentUser.Text);
                            }
                        }
                    });
                }
                catch
                {
                    break;
                }
            }
        }

        private void UpdateUsersList(string usersStr)
        {
            UsersList.Items.Clear();
            string[] users = usersStr.Split(',');

            foreach (var entry in users)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;

                string[] parts = entry.Split('|');
                string nameText = parts[0];
                string statusText = parts.Length > 1 ? parts[1].ToUpper() : "OFFLINE";

                if (nameText == CurrentUser.Text) continue;

                StackPanel userPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(5)
                };

                Brush statusBrush = Brushes.Gray;
                switch (statusText)
                {
                    case "ONLINE": statusBrush = Brushes.Green; break;
                    case "TYPING": statusBrush = Brushes.Yellow; break;
                    case "OFFLINE": statusBrush = Brushes.Gray; break;
                }

                Ellipse statusCircle = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = statusBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock name = new TextBlock
                {
                    Text = statusText == "TYPING" ? $"{nameText} (печатает...)" : nameText,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                userPanel.Children.Add(statusCircle);
                userPanel.Children.Add(name);
                UsersList.Items.Add(userPanel);
            }
        }

        private void ReceiveFile(string header)
        {
            int newline = header.IndexOf('\n');
            if (newline < 0) return;

            string fileHeader = header.Substring(5, newline - 5);
            string[] parts = fileHeader.Split('|');
            if (parts.Length != 3) return;

            string fileName = parts[0];
            string fileType = parts[1];
            int fileSize = int.Parse(parts[2]);

            byte[] fileBytes = new byte[fileSize];
            int totalRead = 0;

            while (totalRead < fileSize)
            {
                int read = stream.Read(fileBytes, totalRead, fileSize - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + "_" + fileName);
            System.IO.File.WriteAllBytes(tempPath, fileBytes);

            AddFileMessage("Система", fileName, tempPath, false);
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            tek_message = Message.Text;
            if (string.IsNullOrWhiteSpace(tek_message)) return;

            byte[] data = Encoding.UTF8.GetBytes(tek_message);
            try { stream.Write(data, 0, data.Length); }
            catch
            {
                MessageBox.Show("Не удалось отправить сообщение. Сервер недоступен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AddMessage(CurrentUser.Text, tek_message, true);
            Message.Text = "";
            Message.Focus();
        }

        private void AddMessage(string sender, string text, bool isOwn)
        {
            StackPanel messagePanel = new StackPanel
            {
                HorizontalAlignment = isOwn ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(5)
            };

            TextBlock userBlock = new TextBlock
            {
                Text = sender,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.LightGray
            };

            TextBlock textBlock = new TextBlock
            {
                Text = text,
                FontSize = 18,
                Foreground = Brushes.White,
                Background = isOwn
                    ? new SolidColorBrush(Color.FromRgb(0, 128, 64))
                    : new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                Padding = new Thickness(5),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400
            };

            TextBlock timeBlock = new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm"),
                FontSize = 12,
                Foreground = Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            messagePanel.Children.Add(userBlock);
            messagePanel.Children.Add(textBlock);
            messagePanel.Children.Add(timeBlock);

            MessagesPanel.Children.Add(messagePanel);
            ChatScroll.ScrollToEnd();
        }

        private void AddFileMessage(string sender, string fileName, string filePath, bool isOwn)
        {
            StackPanel messagePanel = new StackPanel
            {
                HorizontalAlignment = isOwn ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(5)
            };

            TextBlock userBlock = new TextBlock
            {
                Text = sender,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.LightGray
            };

            Button fileButton = new Button
            {
                Content = $"📎 {fileName}",
                Background = isOwn ? new SolidColorBrush(Color.FromRgb(0, 128, 64)) : new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                Foreground = Brushes.White,
                Padding = new Thickness(5),
                Cursor = Cursors.Hand
            };

            fileButton.Click += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            };

            TextBlock timeBlock = new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm"),
                FontSize = 12,
                Foreground = Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            messagePanel.Children.Add(userBlock);
            messagePanel.Children.Add(fileButton);
            messagePanel.Children.Add(timeBlock);

            MessagesPanel.Children.Add(messagePanel);
            ChatScroll.ScrollToEnd();
        }

        private void SendFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Выберите файл";
            dlg.Filter = "Все файлы|*.*";

            if (dlg.ShowDialog() != true) return;

            string filePath = dlg.FileName;
            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
            string fileName = System.IO.Path.GetFileName(filePath);
            string fileType = System.IO.Path.GetExtension(filePath);

            string header = $"FILE:{fileName}|{fileType}|{fileBytes.Length}\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);

            stream.Write(fileBytes, 0, fileBytes.Length);

            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + "_" + fileName);
            System.IO.File.WriteAllBytes(tempPath, fileBytes);
            AddFileMessage(CurrentUser.Text, fileName, tempPath, true);
        }

        private void Del_Chat_Click(object sender, RoutedEventArgs e)
        {
            MessagesPanel.Children.Clear();
        }

        private void InitializeSmilePanel()
        {
            string[] smiles = new string[]
            {
                "😀","😁","😂","🤣","😊","😍","😎","😢","😡","👍","👎","❤️","🔥","🎉","✨"
            };

            foreach (string smile in smiles)
            {
                Button btn = new Button
                {
                    Content = smile,
                    FontSize = 24,
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(2),
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };

                btn.Click += (s, e) =>
                {
                    Message.Text += smile;
                    Message.CaretIndex = Message.Text.Length;
                    SmilePanel.Visibility = Visibility.Collapsed;
                    Message.Focus();
                };

                SmilePanel.Children.Add(btn);
            }
        }
    }
}