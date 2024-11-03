using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ClientForm
{
    public partial class ClientForm : Form
    {
        private Socket clientSocket = null;
        private static int _buff_size = 2048;
        public const int FileBufferSize = 3072;
        private delegate void SafeCallDelegate(string text, Control obj);
        private Thread recvThread = null;

        public enum MessageType
        {
            Text,
            FileEof,
            FilePart,
        }
        public ClientForm()
        {
            InitializeComponent();
            clientSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                IPAddress serverIp = IPAddress.Parse(textBox1.Text);
                int serverPort = int.Parse(textBox2.Text);
                IPEndPoint serverEp = new IPEndPoint(serverIp, serverPort);
                clientSocket.Connect(serverEp);
                string userName = textBox3.Text.Trim();
                if (!string.IsNullOrEmpty(userName))
                {
                    clientSocket.Send(Encoding.UTF8.GetBytes("USERNAME|" + userName));
                }
                richTextBox1.Text += "Connected to " + serverEp.ToString();
                this.Text = "Connected to " + serverEp.ToString();

     
                recvThread = new Thread(() => this.readingClientSocket());
                recvThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
 
            try
            {
                string message = richTextBox2.Text.Trim();
                string selectedUser = listBox1.SelectedItem?.ToString();

                if (!string.IsNullOrEmpty(message))
                {
                    if (!string.IsNullOrEmpty(selectedUser))
                    {
                        // Nếu đã chọn một người dùng, gửi tin nhắn riêng
                        string privateMessage = $"PRIVATE_MESSAGE|{selectedUser}|{message}";
                        clientSocket.Send(Encoding.UTF8.GetBytes(privateMessage));
                        UpdateTextThreadSafe($"You (private) to {selectedUser}: {message}", richTextBox1);
                    }
                    else
                    {
                        // Gửi tin nhắn như một tin nhắn nhóm
                        clientSocket.Send(Encoding.UTF8.GetBytes(message));
                        UpdateTextThreadSafe($"You (group): {message}", richTextBox1);
                    }

                    // Làm sạch richTextBox sau khi gửi
                    richTextBox2.Text = string.Empty;
                   
                    listBox1.ClearSelected(); 
                }
                else
                {
                    MessageBox.Show("Please enter a message.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        private void readingClientSocket()
        {
     
            byte[] buffer = new byte[_buff_size];
            while (clientSocket != null && clientSocket.Connected)
            {
                if (clientSocket.Available > 0)
                {
                    StringBuilder sb = new StringBuilder();

                    while (clientSocket.Available > 0)
                    {
                        int bRead = clientSocket.Receive(buffer, _buff_size, SocketFlags.None);
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, bRead));
                        Array.Clear(buffer, 0, buffer.Length); // Xóa buffer sau khi nhận xong
                    }

                    // Kiểm tra nếu tin nhắn chứa danh sách client
                    if (sb.ToString().StartsWith("CLIENT_LIST|"))
                    {
                        string clientListStr = sb.ToString().Substring("CLIENT_LIST|".Length);
                        ProcessClientList(clientListStr); // Gọi phương thức để xử lý danh sách client
                    }
                    else if (sb.ToString().StartsWith("PRIVATE_MESSAGE|"))
                    {
                        // Xử lý tin nhắn riêng
                        string privateMessage = sb.ToString().Substring("PRIVATE_MESSAGE|".Length);
                        string[] parts = privateMessage.Split(new[] { '|' }, 2);
                        if (parts.Length == 2)
                        {
                            string sender = parts[0]; // Người gửi
                            string message = parts[1]; // Nội dung tin nhắn
                            UpdateTextThreadSafe($"Private message from {sender}: {message}", richTextBox1);
                        }
                    }
                    else if (sb.ToString().StartsWith("USER_LEFT|"))
                    {
                        // Xử lý thông báo client đã rời
                        string userName = sb.ToString().Substring("USER_LEFT|".Length);
                        UpdateTextThreadSafe($"{userName} has left the chat!", richTextBox1);
                    }
                    else
                    {
                        string receivedStr = sb.ToString();
                        UpdateTextThreadSafe(receivedStr, richTextBox1); // Xử lý tin nhắn nhóm
                    }
                }
            }
        }


        private void UpdateTextThreadSafe(string text, Control control)
        {
            if (control.InvokeRequired)
            {
                var d = new SafeCallDelegate(UpdateTextThreadSafe);
                control.Invoke(d, new object[] { text, control });
            }
            else
            {
                if (control is RichTextBox)
                {
                    ((RichTextBox)control).AppendText("\r\n" + text);
                    ((RichTextBox)control).ScrollToCaret();
                }
                else
                {
                    control.Text = text;

                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            string filePath = string.Empty;
            byte[] sendingBuffer = null;

            try
            {
                // Kiểm tra nếu ClientSocket đã kết nối
                if (clientSocket != null && clientSocket.Connected)
                {
                    using (OpenFileDialog openFileDialog = new OpenFileDialog())
                    {
                        openFileDialog.InitialDirectory = "c:\\";
                        openFileDialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
                        openFileDialog.FilterIndex = 2;
                        openFileDialog.RestoreDirectory = true;

                        if (openFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            filePath = openFileDialog.FileName;

                            // Tạo một thread riêng để gửi file
                            Thread sendFileThread = new Thread(() =>
                            {
                                try
                                {
                                    using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                                    {
                                        int NoOfPackets = (int)Math.Ceiling((double)fileStream.Length / FileBufferSize);
                                        int TotalLength = (int)fileStream.Length;
                                        int CurrentPacketLength, counter = 0;

                                        for (int i = 0; i < NoOfPackets; i++)
                                        {
                                            if (TotalLength > FileBufferSize)
                                            {
                                                CurrentPacketLength = FileBufferSize;
                                                TotalLength -= CurrentPacketLength;
                                            }
                                            else
                                            {
                                                CurrentPacketLength = TotalLength;
                                            }

                                            byte[] fileBuffer = new byte[CurrentPacketLength];
                                            int bytesRead = fileStream.Read(fileBuffer, 0, CurrentPacketLength); // Đọc dữ liệu file

                                            MessageType msgType = (i == NoOfPackets - 1) ? MessageType.FileEof : MessageType.FilePart;
                                            string header = $"FILE|{textBox3.Text}|{msgType};";
                                            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                                            byte[] sendingBytes = headerBytes.Concat(fileBuffer).ToArray();

                                            clientSocket.Send(sendingBytes, 0, sendingBytes.Length, SocketFlags.None); // Gửi dữ liệu
                                        }
                                    }

                                    MessageBox.Show("File sent successfully.");
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Error sending file: {ex.Message}");
                                }
                            });

                            // Khởi động thread
                            sendFileThread.Start();
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Not connected to the server.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }

            /*OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filePath = openFileDialog.FileName;
                byte[] fileData = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                string header = $"FILE|{fileName}|{fileData.Length}";

                try
                {
                    // Gửi header trước
                    clientSocket.Send(Encoding.UTF8.GetBytes(header));

                    // Gửi dữ liệu file
                    clientSocket.Send(fileData);

                    // Nếu là file văn bản, lấy nội dung
                    string fileExtension = Path.GetExtension(filePath).ToLower();
                    string fileContent = "";
                    if (fileExtension == ".txt" || fileExtension == ".csv" || fileExtension == ".log")
                    {
                        try
                        {
                            fileContent = File.ReadAllText(filePath);
                        }
                        catch (Exception ex)
                        {
                            fileContent = "Error reading file content: " + ex.Message;
                        }
                    }
                    else
                    {
                        fileContent = "File is not a text file, content cannot be displayed.";
                    }

                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error sending file: " + ex.Message);
                }
            }
*/
        }
        private void ProcessClientList(string clientListStr)
        {
            // Chia tách danh sách địa chỉ client
            string[] clientAddresses = clientListStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            // Gọi phương thức để cập nhật ListBox an toàn
            UpdateListBoxThreadSafe(clientAddresses);
        }

        // Phương thức an toàn để cập nhật ListBox
        private void UpdateListBoxThreadSafe(string[] clientAddresses)
        {
            if (listBox1.InvokeRequired)
            {
                // Nếu không phải trên luồng UI, gọi lại trên luồng UI
                listBox1.Invoke(new Action(() => UpdateListBoxThreadSafe(clientAddresses)));
            }
            else
            {
                listBox1.Items.Clear(); // Xóa danh sách hiện tại

                // Thêm từng địa chỉ client vào ListBox
                foreach (string address in clientAddresses)
                {
                    listBox1.Items.Add(address);
                }
            }
        }


    }
}


