using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Drawing;
using System.IO;
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
        string file = "C:\\Users\\PC\\Desktop\\otazky.txt";
        int anoNie = 0;
        int viacOtazok = 0;
        int pocetOtazok = 0;
        string otazka = null;
        private TcpListener server;
        private List<TcpClient> clients = new List<TcpClient>();
        private TcpClient client;
        private NetworkStream stream;
        private const int port = 55413;
        private bool isHost = false;
        private List<(string Question, string[] Options, int CorrectIndex)> questions = new List<(string, string[], int)>
        {
            ("Koľko je 2 + 2?", new[] { "3", "4", "5", "6" }, 1),
            ("What is the capital of France?", new[] { "Berlin", "Madrid", "Paris", "Rome" }, 2),
            ("Which planet is known as the Red Planet?", new[] { "Earth", "Mars", "Jupiter", "Venus" }, 1)
        };
        private int currentQuestionIndex = 0;
        private Dictionary<string, int> playerScores = new Dictionary<string, int>();
        private string nickname;
        private Dictionary<string, string> clientNicknames = new Dictionary<string, string>();

        public Form1()
        {
            InitializeComponent();
        }

        private async void btnHost_Click_1(object sender, EventArgs e)
        {
            isHost = true;
            panel1.Visible = false;
            panel2.Visible = true;

            btnLoadQuestions.Visible = true;
            btnStartQuiz.Visible = true;
            button1.Visible = true;

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
                try
                {
                    if (!server.Server.IsBound)
                    {
                        break; // Ak server už nebeží, tak koniec
                    }

                    TcpClient newClient = await server.AcceptTcpClientAsync();
                    clients.Add(newClient);

                    _ = ReceiveFromClientAsync(newClient); // spustí prijímanie správ od klienta
                }
                catch (ObjectDisposedException)
                {
                    break; // Ak je server vypnutý, tak koniec
                }
            }
        }

        private async Task ReceiveFromClientAsync(TcpClient tcpClient)
        {
            var buffer = new byte[1024];
            var stream = tcpClient.GetStream();
            string clientIP = tcpClient.Client.RemoteEndPoint.ToString();

            while (tcpClient.Connected)
            {
                int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                string message = Encoding.UTF8.GetString(buffer, 0, byteCount);

                if (message.StartsWith("NICKNAME"))
                {
                    string nickname = message.Split('|')[1];
                    clientNicknames[clientIP] = nickname;

                    // Pridá meno klienta do zoznamu
                    Invoke((Action)(() =>
                    {
                        listBox1.Items.Add($"{nickname} connected");
                    }));
                }
                else if (message.StartsWith("ANSWER"))
                {
                    int answer = int.Parse(message.Split('|')[1]);

                    if (!playerScores.ContainsKey(clientIP))
                        playerScores[clientIP] = 0;

                    if (answer == questions[currentQuestionIndex].CorrectIndex)
                        playerScores[clientIP] += 10; // Pridá body za správnu odpoveď
                }
            }
        }

        private async void btnJoin_Click_1(object sender, EventArgs e)
        {
            isHost = false;
            panel1.Visible = false;
            panel2.Visible = true;

            // Pýta si prezývku
            nickname = Prompt.ShowDialog("Enter your nickname:", "Set Nickname");
            if (string.IsNullOrWhiteSpace(nickname))
            {
                MessageBox.Show("Nickname cannot be empty!");
                panel1.Visible = true;
                panel2.Visible = false;
                return;
            }

            // Skryje tlačidlá pre klienta
            btnStartQuiz.Visible = false;
            btnLoadQuestions.Visible = false;
            button1.Visible = false;

            // Pýta si IP hosta
            string ip = Prompt.ShowDialog("Enter Host IP (X.X.X.X):", "Join Game");
            client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(ip), port);
            stream = client.GetStream();

            // Pošle prezývku hostovi
            string message = $"NICKNAME|{nickname}";
            var msg = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(msg, 0, msg.Length);

            _ = ReceiveFromServerAsync();
            MessageBox.Show("Connected to server!");

            // Pridá info o pripojení do zoznamu
            Invoke((Action)(() =>
            {
                listBox1.Items.Add($"You are connected as {nickname}.");
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
                    // Spustí kvíz pre klienta
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
                        DisplayQuestion((question, options, -1)); // -1 lebo klient nepozná správnu odpoveď
                    }));
                }
                else if (message.StartsWith("LEADERBOARD"))
                {
                    string leaderboard = message.Substring("LEADERBOARD|".Length);
                    MessageBox.Show($"Leaderboard:\n{leaderboard}");
                }
                else if (message == "RESET")
                {
                    // Resetuje stav klienta
                    Invoke((Action)(() =>
                    {
                        ResetQuiz();
                    }));
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

            // Povie klientom, že kvíz začína
            foreach (var cl in clients)
            {
                var msg = Encoding.UTF8.GetBytes("START_QUIZ");
                await cl.GetStream().WriteAsync(msg, 0, msg.Length);
            }

            // Spustí kvíz pre hosta
            StartQuiz();
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork &&
                    (ip.ToString().StartsWith("192.168.") || ip.ToString().StartsWith("10.") ||
                     ip.ToString().StartsWith("172.")))
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No local network adapters with an IPv4 address in the private(LAN) range were found.");
        }
        private void StartQuiz()
        {
            panel2.Visible = false;
            panel3.Visible = true;

            // Nastaví viditeľnosť tlačidiel podľa toho, či je host alebo klient
            btnNextQuestion.Visible = isHost;
            btnSubmit.Visible = !isHost;

            // Pošle prvú otázku klientom
            if (isHost)
            {
                SendQuestionToClients();
            }

            // Zobrazí prvú otázku pre hosta
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
            // Nastaví otázku a možnosti
            label3.Text = question.Question;
            checkBox1.Text = question.Options[0];
            checkBox2.Text = question.Options[1];
            checkBox3.Text = question.Options[2];
            checkBox4.Text = question.Options[3];

            // Resetuje checkboxy
            checkBox1.Checked = false;
            checkBox2.Checked = false;
            checkBox3.Checked = false;
            checkBox4.Checked = false;

            // Resetuje farbu tlačidla
            btnSubmit.BackColor = System.Drawing.Color.White;

            // labelik: na ktorej som otázke
            lblQuestionNumber.Text = $"{currentQuestionIndex + 1}/{questions.Count}";
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            btnSubmit.BackColor = System.Drawing.Color.Lime;
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
            // Odškrtne všetky checkboxy
            checkBox1.Checked = false;
            checkBox2.Checked = false;
            checkBox3.Checked = false;
            checkBox4.Checked = false;

            // Ukáže leaderboard
            var leaderboard = string.Join("\n", playerScores.OrderByDescending(p => p.Value)
                .Select(p =>
                {
                    string nickname = clientNicknames.ContainsKey(p.Key) ? clientNicknames[p.Key] : p.Key;
                    return $"{nickname}: {p.Value} points";
                }));

            foreach (var cl in clients)
            {
                var msg = Encoding.UTF8.GetBytes($"LEADERBOARD|{leaderboard}");
                cl.GetStream().WriteAsync(msg, 0, msg.Length);
            }

            MessageBox.Show($"Leaderboard:\n{leaderboard}");

            // Ide na ďalšiu otázku alebo končí kvíz
            currentQuestionIndex++;
            if (currentQuestionIndex < questions.Count)
            {
                SendQuestionToClients();
                DisplayQuestion(questions[currentQuestionIndex]);
            }
            else
            {
                MessageBox.Show("Quiz finished!");

                // Povie klientom, že sa resetuje
                foreach (var cl in clients)
                {
                    var msg = Encoding.UTF8.GetBytes("RESET");
                    cl.GetStream().WriteAsync(msg, 0, msg.Length);
                }

                // Resetuje stav hosta
                ResetQuiz();
            }
        }
        // Resetuje kvíz
        private void ResetQuiz()
        {
            // Resetuje panely
            panel1.Visible = true;
            panel2.Visible = false;
            panel3.Visible = false;

            // Vyčistí UI prvky
            listBox1.Items.Clear();
            label3.Text = string.Empty;
            checkBox1.Text = string.Empty;
            checkBox2.Text = string.Empty;
            checkBox3.Text = string.Empty;
            checkBox4.Text = string.Empty;
            checkBox1.Checked = false;
            checkBox2.Checked = false;
            checkBox3.Checked = false;
            checkBox4.Checked = false;

            // Resetuje stav kvízu
            currentQuestionIndex = 0;
            playerScores.Clear();
            clientNicknames.Clear();

            // Resetuje stav hosta a klienta
            if (isHost)
            {
                foreach (var cl in clients)
                {
                    cl.Close();
                }
                clients.Clear();
                server?.Stop();
            }
            else
            {
                client?.Close();
                stream = null;
            }

            isHost = false;
            nickname = null;

            lblQuestionNumber.Text = string.Empty;
        }

        // Načítanie otázok zo súboru
        private void btnLoadQuestions_Click(object sender, EventArgs e)
        {
            if (!isHost) return;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Textové súbory (*.txt)|*.txt|Všetky súbory (*.*)|*.*";
                openFileDialog.Title = "Vyber súbor s otázkami";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    MessageBox.Show($"Vybraný súbor: {filePath}");

                    // logika na čítanie otázok zo súboru príde neskôr
                }
            }
        }

        // Prepne na panel 4
        private void button1_Click(object sender, EventArgs e)
        {
            panel4.Visible = true;
            panel1.Visible = false;
            panel2.Visible = false;
            panel3.Visible = false;
            comboBox1.Visible = false;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            anoNie = 1;
            viacOtazok = 0;
            comboBox1.Visible = false;
            button2.BackColor = Color.Black;
            button2.ForeColor = Color.White;
            button3.BackColor = Color.White;
            button3.ForeColor = Color.Black;
            textBox2.Visible = false;
            textBox3.Visible = false;
            textBox4.Visible = false;
            textBox5.Visible = false;
            button7.Visible = true;
            button6.Visible = true;
        }

        private void panel4_Paint(object sender, PaintEventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {
            comboBox1.Visible = true;
            comboBox1.SelectedIndex = 0;
            button3.BackColor = Color.Black;
            button3.ForeColor = Color.White;
            button2.BackColor = Color.White;
            button2.ForeColor = Color.Black;
            button7.Visible = false;
            button6.Visible = false;
            anoNie = 0;
            viacOtazok = 1;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            pocetOtazok = int.Parse(comboBox1.SelectedItem.ToString());
            if (pocetOtazok == 2)
            {
                textBox2.Visible = true;
                textBox3.Visible = true;
                textBox4.Visible = false;
                textBox5.Visible = false;
            }
            else if (pocetOtazok == 3)
            {
                textBox2.Visible = true;
                textBox3.Visible = true;
                textBox4.Visible = true;
                textBox5.Visible = false;
            }
            else if (pocetOtazok == 4)
            {
                textBox2.Visible = true;
                textBox3.Visible = true;
                textBox4.Visible = true;
                textBox5.Visible = true;
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            button6.BackColor = Color.Black;
            button6.ForeColor = Color.White;
            button7.BackColor = Color.White;
            button7.ForeColor = Color.Black;
        }

        private void button7_Click(object sender, EventArgs e)
        {
            button7.BackColor = Color.Black;
            button7.ForeColor = Color.White;
            button6.BackColor = Color.White;
            button6.ForeColor = Color.Black;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            otazka = textBox1.Text;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (anoNie == 1)
            {
                File.AppendAllText(file, textBox1.Text + "\n");
            }
            else if (viacOtazok == 1)
            {
                File.AppendAllText(file, textBox1.Text + "\n");
            }
            else
            {
                MessageBox.Show("Vyber typ otázky");
            }
            textBox1.Clear();
        }


        // input dialóg
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
}
