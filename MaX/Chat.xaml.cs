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
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using NAudio.Wave;
using System.IO;


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
            LoadMessageHistory();
            this.PreviewKeyDown += Chat_PreviewKeyDown;
            Message.TextChanged += Message_TextChanged;

            RecordVoiceButton.PreviewMouseLeftButtonDown += (s, e) => StartRecording();
            RecordVoiceButton.PreviewMouseLeftButtonUp += (s, e) => StopRecording();

            ConnectToServer();
        }

        private WaveInEvent waveIn;
        private MemoryStream audioStream;
        private bool isRecording = false;

        class ChatMessageRecord
        {
            public string Sender { get; set; }
            public string Text { get; set; }
            public bool IsOwn { get; set; }
            public DateTime Time { get; set; }
        }
        private readonly string historyFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MaXChatHistory.json");

        class ChatMessage
        {
            public Guid Id { get; } = Guid.NewGuid();
            public string Sender { get; set; }
            public string Text { get; set; }
            public bool IsOwn { get; set; }
            public StackPanel Panel { get; set; }
        }

        List<ChatMessage> messages = new List<ChatMessage>();

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
                        else if (message.StartsWith("EDIT:"))
                        {
                            string[] parts = message.Substring(5).Split(new char[] { '|' }, 2);
                            if (parts.Length < 2) return;

                            Guid id = Guid.Parse(parts[0]);
                            string newText = parts[1];

                            var msg = messages.FirstOrDefault(m => m.Id == id);
                            if (msg != null)
                            {
                                msg.Text = newText;
                                (msg.Panel.Children[1] as TextBlock).Text = newText;
                            }
                        }
                        else
                        {
                            int sep = message.IndexOf(": ");
                            if (sep > 0)
                            {
                                string sender = message.Substring(0, sep);
                                string text = message.Substring(sep + 2);
                                AddMessage(sender, text, sender == CurrentUser.Text);
                                SaveMessageHistory();
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
            SaveMessageHistory();

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

            if (isOwn)
            {
                ContextMenu menu = new ContextMenu();

                MenuItem edit = new MenuItem { Header = "Редактировать" };
                edit.Click += (s, e) =>
                {
                    // Находим ChatMessage по панели TextBlock
                    var panel = textBlock.Parent as StackPanel;
                    var msg = messages.FirstOrDefault(m => m.Panel == panel);
                    if (msg != null)
                    {
                        EditMessage(msg);
                    }
                };

                MenuItem del = new MenuItem { Header = "Удалить" };
                del.Click += (s, e) => DeleteMessage(messagePanel);

                menu.Items.Add(edit);
                menu.Items.Add(del);

                messagePanel.ContextMenu = menu;
            }

            // Сохраняем сообщение
            messages.Add(new ChatMessage
            {
                Sender = sender,
                Text = text,
                IsOwn = isOwn,
                Panel = messagePanel
            });

            if (!isOwn)
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
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

            messages.Clear();

            if (System.IO.File.Exists(historyFile))
            {
                System.IO.File.Delete(historyFile);
            }
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

        private string ShowInputDialog(string prompt, string defaultText = "")
        {
            Window inputWindow = new Window
            {
                Width = 400,
                Height = 150,
                Title = "Редактирование",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = this
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(10) };

            TextBlock textBlock = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 10)
            };

            TextBox textBox = new TextBox
            {
                Text = defaultText,
                Margin = new Thickness(0, 0, 0, 10)
            };

            StackPanel buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Margin = new Thickness(5, 0, 0, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => inputWindow.DialogResult = true;

            Button cancelButton = new Button
            {
                Content = "Отмена",
                Width = 75,
                Margin = new Thickness(5, 0, 0, 0),
                IsCancel = true
            };
            cancelButton.Click += (s, e) => inputWindow.DialogResult = false;

            buttonsPanel.Children.Add(okButton);
            buttonsPanel.Children.Add(cancelButton);

            panel.Children.Add(textBlock);
            panel.Children.Add(textBox);
            panel.Children.Add(buttonsPanel);

            inputWindow.Content = panel;

            bool? result = inputWindow.ShowDialog();

            if (result == true)
                return textBox.Text;
            else
                return null;
        }

        private void EditMessage(ChatMessage msg)
        {
            string newText = ShowInputDialog("Редактировать сообщение:", msg.Text);
            if (string.IsNullOrWhiteSpace(newText)) return;

            // Обновляем локально
            msg.Text = newText;
            (msg.Panel.Children[1] as TextBlock).Text = newText;

            // Отправляем на сервер
            string editMessage = $"EDIT:{msg.Id}|{newText}";
            byte[] data = Encoding.UTF8.GetBytes(editMessage);
            stream.Write(data, 0, data.Length);
        }

        private void DeleteMessage(StackPanel panel)
        {
            MessagesPanel.Children.Remove(panel);

            var msg = messages.FirstOrDefault(m => m.Panel == panel);
            if (msg != null)
                messages.Remove(msg);

        }
        private void SaveMessageHistory()
        {
            var records = messages.Select(m => new ChatMessageRecord
            {
                Sender = m.Sender,
                Text = m.Text,
                IsOwn = m.IsOwn,
                Time = DateTime.Now
            }).ToList();

            // Используем Newtonsoft.Json
            string json = JsonConvert.SerializeObject(records, Formatting.Indented);
            System.IO.File.WriteAllText(historyFile, json);
        }
        private void LoadMessageHistory()
        {
            if (!System.IO.File.Exists(historyFile)) return;

            string json = System.IO.File.ReadAllText(historyFile);

            // Используем Newtonsoft.Json
            var records = JsonConvert.DeserializeObject<List<ChatMessageRecord>>(json);

            if (records == null) return;

            foreach (var r in records)
            {
                AddMessage(r.Sender, r.Text, r.IsOwn);
            }
        }
        private void Chat_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                OpenSearchWindow();
            }
        }

        private void OpenSearchWindow()
        {
            // Окно поиска
            Window searchWindow = new Window
            {
                Title = "Поиск сообщений",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(10) };

            TextBox searchBox = new TextBox
            {
                Width = 360,
                Height = 25,
                Margin = new Thickness(0, 0, 0, 10)
            };

            StackPanel buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button findButton = new Button
            {
                Content = "Найти",
                Width = 75,
                Margin = new Thickness(5, 0, 0, 0)
            };
            findButton.Click += (s, e) =>
            {
                SearchMessages(searchBox.Text);
            };

            Button resetButton = new Button
            {
                Content = "Сброс",
                Width = 75,
                Margin = new Thickness(5, 0, 0, 0)
            };
            resetButton.Click += (s, e) =>
            {
                SearchMessages("");
                searchBox.Text = "";
            };

            buttonsPanel.Children.Add(findButton);
            buttonsPanel.Children.Add(resetButton);

            panel.Children.Add(searchBox);
            panel.Children.Add(buttonsPanel);

            searchWindow.Content = panel;
            searchWindow.ShowDialog();
        }

        private void SearchMessages(string query)
        {
            query = query?.Trim().ToLower();
            if (string.IsNullOrEmpty(query))
            {
                foreach (var msg in messages)
                    msg.Panel.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var msg in messages)
                {
                    if (msg.Text.ToLower().Contains(query) || msg.Sender.ToLower().Contains(query))
                        msg.Panel.Visibility = Visibility.Visible;
                    else
                        msg.Panel.Visibility = Visibility.Collapsed;
                }
            }

            ChatScroll.ScrollToEnd();
        }

        private void Message_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Message.Text))
            {
                Send.Visibility = Visibility.Collapsed;
                RecordVoiceButton.Visibility = Visibility.Visible;
            }
            else
            {
                Send.Visibility = Visibility.Visible;
                RecordVoiceButton.Visibility = Visibility.Collapsed;
            }
        }

        private void StartRecording()
        {
            waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(44100, 1);
            audioStream = new MemoryStream();

            waveIn.DataAvailable += (s, a) =>
            {
                audioStream.Write(a.Buffer, 0, a.BytesRecorded);
            };

            waveIn.RecordingStopped += (s, a) =>
            {
                waveIn.Dispose();
                waveIn = null;

                byte[] wavBytes;
                string tempFile;
                using (var wavStream = new MemoryStream())
                {
                    using (var writer = new WaveFileWriter(wavStream, new WaveFormat(44100, 1)))
                    {
                        audioStream.Position = 0;
                        audioStream.CopyTo(writer);
                    }
                    wavBytes = wavStream.ToArray();
                }

                tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voice_{Guid.NewGuid()}.wav");
                File.WriteAllBytes(tempFile, wavBytes);

                AddFileMessage(CurrentUser.Text, "Голосовое сообщение.wav", tempFile, true);

                string header = $"FILE:{System.IO.Path.GetFileName(tempFile)}|.wav|{wavBytes.Length}\n";
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(wavBytes, 0, wavBytes.Length);

                audioStream.Dispose();
                isRecording = false;
            };

            waveIn.StartRecording();
            isRecording = true;
        }

        private void StopRecording()
        {
            if (isRecording && waveIn != null)
            {
                waveIn.StopRecording();
            }
        }

        private void RecordVoiceButton_Click(object sender, RoutedEventArgs e)
        {
            
        }
    }
}