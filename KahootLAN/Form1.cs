using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KahootLAN
{
    public partial class Form1 : Form
    {
        private TcpListener server;
        private List<TcpClient> clients = new List<TcpClient>();
        private TcpClient client;
        private NetworkStream stream;
        private const int port = 12345;
        private bool isHost = false;

        public Form1()
        {
            InitializeComponent();
        }

        private async void btnHost_Click_1(object sender, EventArgs e)
        {
            isHost = true;
            panel1.Visible = false;
            panel2.Visible = true;
            string ip = GetLocalIPAddress();
            MessageBox.Show($"Server started at {ip}:{port}");
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            await AcceptClientsAsync();

        }

        private async Task AcceptClientsAsync() 
        {
            while (true)
            {
                TcpClient newClient = await server.AcceptTcpClientAsync();
                clients.Add(newClient);
                _ = ReceiveFromClientAsync(newClient); // fire and forget
            }
        }

        private async Task ReceiveFromClientAsync(TcpClient tcpClient)
        {
            var buffer = new byte[1024];
            var stream = tcpClient.GetStream();
            while (tcpClient.Connected)
            {
                int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                string message = Encoding.UTF8.GetString(buffer, 0, byteCount);
                // Handle incoming messages here (e.g., quiz answers)
                Console.WriteLine($"Received from client: {message}");
            }
        }

        private async void btnJoin_Click_1(object sender, EventArgs e)
        {
            isHost = false;
            string ip = Prompt.ShowDialog("Enter Host IP:", "Join Game");
            client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(ip), port);
            stream = client.GetStream();
            _ = ReceiveFromServerAsync();
            MessageBox.Show("Connected to server!");
        }

        private async Task ReceiveFromServerAsync()
        {
            var buffer = new byte[1024];
            while (client.Connected)
            {
                int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                string message = Encoding.UTF8.GetString(buffer, 0, byteCount);
                // Handle server messages (e.g., new question, start signal)
                Console.WriteLine($"Server says: {message}");
            }
        }

        private async void btnStartQuiz_Click_1(object sender, EventArgs e)
        {
            if (!isHost) return;
            foreach (var cl in clients)
            {
                var msg = Encoding.UTF8.GetBytes("START_QUIZ");
                await cl.GetStream().WriteAsync(msg, 0, msg.Length);
            }
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
    }

    // Helper for input dialog
    public static class Prompt
    {
        public static string ShowDialog(string text, string caption)
        {
            Form prompt = new Form() { Width = 400, Height = 150, Text = caption };
            Label textLabel = new Label() { Left = 50, Top = 20, Text = text, Width = 300 };
            TextBox textBox = new TextBox() { Left = 50, Top = 50, Width = 300 };
            Button confirmation = new Button() { Text = "OK", Left = 250, Width = 100, Top = 80 };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;
            prompt.ShowDialog();
            return textBox.Text;
        }
    }
}
