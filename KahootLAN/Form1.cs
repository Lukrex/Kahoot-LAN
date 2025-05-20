using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
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
        private List<(string Question, string[] Options, int CorrectIndex)> questions = new List<(string, string[], int)>
        {
            ("What is 2 + 2?", new[] { "3", "4", "5", "6" }, 1),
            ("What is the capital of France?", new[] { "Berlin", "Madrid", "Paris", "Rome" }, 2),
            ("Which planet is known as the Red Planet?", new[] { "Earth", "Mars", "Jupiter", "Venus" }, 1)
        };
        private int currentQuestionIndex = 0;
        private Dictionary<string, int> playerScores = new Dictionary<string, int>();

        public Form1()
        {
            InitializeComponent();
        }

        private async void btnHost_Click_1(object sender, EventArgs e)
        {
            isHost = true;
            panel1.Visible = false;
            panel2.Visible = true;

            // Ensure the Start Quiz button is visible only for the host
            btnStartQuiz.Visible = true;

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

                // Update the listBox1 with the new client
                Invoke((Action)(() =>
                {
                    listBox1.Items.Add($"Client {clients.Count} connected");
                }));

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

                if (message.StartsWith("ANSWER"))
                {
                    string clientName = tcpClient.Client.RemoteEndPoint.ToString();
                    int answer = int.Parse(message.Split('|')[1]);

                    if (!playerScores.ContainsKey(clientName))
                        playerScores[clientName] = 0;

                    if (answer == questions[currentQuestionIndex].CorrectIndex)
                        playerScores[clientName] += 10; // Award points for correct answer
                }
            }
        }

        private async void btnJoin_Click_1(object sender, EventArgs e)
        {
            isHost = false;
            panel1.Visible = false;
            panel2.Visible = true;

            // Ensure the Start Quiz button is hidden for clients
            btnStartQuiz.Visible = false;

            string ip = Prompt.ShowDialog("Enter Host IP:", "Join Game");
            client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(ip), port);
            stream = client.GetStream();
            _ = ReceiveFromServerAsync();
            MessageBox.Show("Connected to server!");

            // Update the listBox1 for the client
            Invoke((Action)(() =>
            {
                listBox1.Items.Add("You are connected to the server.");
            }));
        }

        private async Task ReceiveFromServerAsync()
        {
            var buffer = new byte[1024];
            while (client.Connected)
            {
                int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                string message = Encoding.UTF8.GetString(buffer, 0, byteCount);

                if (message == "START_QUIZ")
                {
                    // Start the quiz for the client
                    Invoke((Action)(() =>
                    {
                        StartQuiz();
                    }));
                }
                else if (message.StartsWith("QUESTION"))
                {
                    string[] parts = message.Split('|');
                    string question = parts[1];
                    string[] options = parts.Skip(2).ToArray();

                    Invoke((Action)(() =>
                    {
                        DisplayQuestion((question, options, -1)); // -1 because clients don't know the correct answer
                    }));
                }
                else if (message.StartsWith("LEADERBOARD"))
                {
                    string leaderboard = message.Substring("LEADERBOARD|".Length);
                    MessageBox.Show($"Leaderboard:\n{leaderboard}");
                }
                else
                {
                    Console.WriteLine($"Server says: {message}");
                }
            }
        }

        private async void btnStartQuiz_Click_1(object sender, EventArgs e)
        {
            if (!isHost) return;

            // Notify all clients to start the quiz
            foreach (var cl in clients)
            {
                var msg = Encoding.UTF8.GetBytes("START_QUIZ");
                await cl.GetStream().WriteAsync(msg, 0, msg.Length);
            }

            // Start the quiz for the host
            StartQuiz();
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
        private void StartQuiz()
        {
            panel2.Visible = false;
            panel3.Visible = true;

            // Send the first question to all clients
            SendQuestionToClients();

            // Display the first question for the host
            DisplayQuestion(questions[currentQuestionIndex]);
        }

        private void SendQuestionToClients()
        {
            var question = questions[currentQuestionIndex];
            string message = $"QUESTION|{question.Question}|{string.Join("|", question.Options)}";
            foreach (var cl in clients)
            {
                var msg = Encoding.UTF8.GetBytes(message);
                cl.GetStream().WriteAsync(msg, 0, msg.Length);
            }
        }

        private void DisplayQuestion((string Question, string[] Options, int CorrectIndex) question)
        {
            label3.Text = question.Question;
            checkBox1.Text = question.Options[0];
            checkBox2.Text = question.Options[1];
            checkBox3.Text = question.Options[2];
            checkBox4.Text = question.Options[3];
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            int selectedAnswer = -1;
            if (checkBox1.Checked) selectedAnswer = 0;
            else if (checkBox2.Checked) selectedAnswer = 1;
            else if (checkBox3.Checked) selectedAnswer = 2;
            else if (checkBox4.Checked) selectedAnswer = 3;

            if (selectedAnswer == -1)
            {
                MessageBox.Show("Please select an answer!");
                return;
            }

            string message = $"ANSWER|{selectedAnswer}";
            var msg = Encoding.UTF8.GetBytes(message);
            stream.WriteAsync(msg, 0, msg.Length);
        }

        private void btnNextQuestion_Click(object sender, EventArgs e)
        {
            // Show leaderboard
            var leaderboard = string.Join("\n", playerScores.OrderByDescending(p => p.Value)
                .Select(p => $"{p.Key}: {p.Value} points"));

            foreach (var cl in clients)
            {
                var msg = Encoding.UTF8.GetBytes($"LEADERBOARD|{leaderboard}");
                cl.GetStream().WriteAsync(msg, 0, msg.Length);
            }

            MessageBox.Show($"Leaderboard:\n{leaderboard}");

            // Move to the next question
            currentQuestionIndex++;
            if (currentQuestionIndex < questions.Count)
            {
                SendQuestionToClients();
                DisplayQuestion(questions[currentQuestionIndex]);
            }
            else
            {
                MessageBox.Show("Quiz finished!");
                panel3.Visible = false;
                panel2.Visible = true;
            }
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
