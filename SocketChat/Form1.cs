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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace SocketChat
{
    public partial class Form1 : Form
    {
        private Socket listener = null;
        private bool started = false;
        private int _port = 11000;
        private static int _buff_size = 2048;
        private byte[] _buffer = new byte[_buff_size];
        private Thread serverThread = null;
        private delegate void SafeCallDelegate(string text, Control obj);
        private List<Socket> clientSockets = new List<Socket>();
        private Dictionary<Socket, string> clientNames = new Dictionary<Socket, string>();
        private List<string> connectedClients = new List<string>();

        public enum MessageType
        {
            Text,
            FileEof,
            FilePart,
        }
        public Form1()
        {
            InitializeComponent();
            listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                if (started)
                {
                    started = false;
                    button2.Text = "Listen";
                    serverThread = null;
                    listener.Close();
                }
                else
                {
                    serverThread = new Thread(() => this.listen());
                    serverThread.Start();
                   
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        private void listen()
        {
            listener.Bind(new IPEndPoint(IPAddress.Parse(textBox1.Text), _port));
            listener.Listen(10);
            started = true;
            UpdateTextThreadSafe("Stop", button2);
            UpdateTextThreadSafe("Start listening", richTextBox1);
            while (started)
            {
                Socket client = listener.Accept();
                clientSockets.Add(client);
                Thread clientThread = new Thread(() => this.readingClientSocket(client)); //Lambla expression
                clientThread.Start();
              // UpdateTextThreadSafe("Accepted connection from " + client.RemoteEndPoint.ToString(), this.richTextBox1);
                UpdateTextThreadSafe(client.RemoteEndPoint.ToString() + " has joined the chat!", richTextBox1);

                // Gửi danh sách client cho tất cả client
                BroadcastClientList();
            }
        }

        private void readingClientSocket(Socket client)
        {
            string username = string.Empty;
            byte[] buffer = new byte[_buff_size];
            try
            {
                // Nhận tin nhắn đầu tiên từ client, giả sử chứa tên người dùng
                int received = client.Receive(buffer);
                string initialMessage = Encoding.UTF8.GetString(buffer, 0, received);

                // Kiểm tra nếu tin nhắn chứa tên người dùng
                if (initialMessage.StartsWith("USERNAME|"))
                {
                    string userName = initialMessage.Substring("USERNAME|".Length);
                    clientNames[client] = userName;
                    UpdateTextThreadSafe($"{userName} has joined the chat!", richTextBox1);
                }
                else
                {
                    clientNames[client] = "Unknown Client";
                    UpdateTextThreadSafe("A client connected without username.", richTextBox1);
                }

                // Lắng nghe tin nhắn từ client
                while (client.Connected)
                {
                    if (client.Available > 0)
                    {
                        StringBuilder sb = new StringBuilder();
                        while (client.Available > 0)
                        {
                            int bRead = client.Receive(buffer, _buff_size, SocketFlags.None);
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, bRead));
                            Array.Clear(buffer, 0, buffer.Length);
                        }

                        string userName = clientNames[client];
                        string receivedStr = sb.ToString();
                        if (receivedStr.StartsWith("FILE|"))
                        {
                            string[] parts = receivedStr.Substring("FILE|".Length).Split('|');
                            string fileName = parts[0];
                            int fileSize = int.Parse(parts[1]);

                            // Read the file data
                            byte[] fileData = new byte[fileSize];
                            int totalBytesReceived = 0;

                            while (totalBytesReceived < fileSize)
                            {
                                int bytesReceived = client.Receive(fileData, totalBytesReceived, fileSize - totalBytesReceived, SocketFlags.None);
                                totalBytesReceived += bytesReceived;
                            }

                            // Save the file (you can change the path as needed)
                            string filePath = Path.Combine("ReceivedFiles", fileName);
                            File.WriteAllBytes(filePath, fileData);

                            UpdateTextThreadSafe($"{clientNames[client]} sent a file: {fileName}", richTextBox1);
                        }
                        // Kiểm tra nếu tin nhắn là tin nhắn riêng
                        if (receivedStr.StartsWith("PRIVATE_MESSAGE|"))
                        {
                            string[] parts = receivedStr.Substring("PRIVATE_MESSAGE|".Length).Split('|');
                            if (parts.Length == 2)
                            {
                                string recipient = parts[0]; // Tên người nhận
                                string message = parts[1];   // Nội dung tin nhắn

                                // Gửi tin nhắn riêng
                                SendPrivateMessage(recipient, $"{userName} (private): {message}");
                            }
                        }
                        else
                        {
                            // Gửi tin nhắn đến tất cả client nếu không phải tin nhắn riêng
                            receivedStr = $"{userName}: {receivedStr}";
                            UpdateTextThreadSafe(receivedStr, richTextBox1);
                            BroadcastMessage(receivedStr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                MessageBox.Show("Error: " + ex.Message);
            }

            finally
            {
                if (clientNames.ContainsKey(client))
                {
                    string userName = clientNames[client];
                    clientNames.Remove(client);

                    // Gửi thông báo đến tất cả client rằng client đã rời
                    string disconnectMessage = $"{userName} has left the chat!";
                    BroadcastMessage(disconnectMessage);

                    // Cập nhật danh sách client
                    connectedClients.Remove(userName); // Xóa client khỏi danh sách
                    UpdateTextThreadSafe(disconnectMessage, richTextBox1); // Cập nhật server UI

                    // Cập nhật lại danh sách client cho tất cả client
                    BroadcastClientList();
                }
                clientSockets.Remove(client);
                client.Close();
            }
        }
        private void BroadcastMessage(string message)
        {
            foreach (Socket s in clientSockets)
            {
                s.Send(Encoding.UTF8.GetBytes(message));
            }
        }

        private void UpdateTextThreadSafe(string text, Control control)
        {
            if (control.InvokeRequired)
            {
                var d = new SafeCallDelegate(UpdateTextThreadSafe);
                control.Invoke(d, new object[] { text, control});
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
        private void BroadcastClientList()
        {
            StringBuilder clientList = new StringBuilder("CLIENT_LIST|");
            foreach (Socket s in clientSockets)
            {
                if (clientNames.ContainsKey(s))
                {
                    clientList.Append(clientNames[s] + ";"); // Thêm tên người dùng vào danh sách
                }
            }

            // Gửi danh sách cho tất cả client
            foreach (Socket s in clientSockets)
            {
                s.Send(Encoding.UTF8.GetBytes(clientList.ToString()));
            }
        }
        private void SendPrivateMessage(string recipient, string message)
        {
            // Tìm kiếm socket tương ứng với recipient
            foreach (Socket s in clientSockets)
            {
                if (clientNames.ContainsKey(s) && clientNames[s] == recipient)
                {
                    s.Send(Encoding.UTF8.GetBytes(message));
                    return; // Ngừng tìm kiếm sau khi đã gửi
                }
            }

            // Nếu không tìm thấy client, có thể log hoặc xử lý lỗi ở đây
            UpdateTextThreadSafe($"User '{recipient}' not found.", richTextBox1);
        }
        private void ProcessClientMessage(string message, Socket clientSocket)
        {
            if (message.StartsWith("CLIENT_LEFT|"))
            {
                string clientName = message.Substring("CLIENT_LEFT|".Length);

                // Xóa client khỏi danh sách
                RemoveClient(clientName); // Xóa client ra khỏi danh sách

                // Cập nhật danh sách client cho tất cả client còn lại
                UpdateClientList();
            }
            else
            {
                // Xử lý các tin nhắn khác
            }
        }

        // Phương thức để xóa client khỏi danh sách và gửi lại danh sách cho tất cả client
        private void RemoveClient(string clientName)
        {
            connectedClients.Remove(clientName);
            UpdateClientList();
        }

        // Cập nhật danh sách client và gửi cho tất cả client
        private void UpdateClientList()
        {
            string clientListMessage = "CLIENT_LIST|" + string.Join(",", connectedClients);
            byte[] messageBytes = Encoding.UTF8.GetBytes(clientListMessage);

            foreach (Socket client in clientSockets)
            {
                if (client.Connected)
                {
                    client.Send(messageBytes);
                }
            }
        }

    }
}
