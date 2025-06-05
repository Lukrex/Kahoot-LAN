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
        string testText = "";
        private TcpListener server;
        private List<TcpClient> clients = new List<TcpClient>();
        private TcpClient client;
        private NetworkStream stream;
        private const int port = 55413;
        private bool isHost = false;
        private List<(string Question, string[] Options, int CorrectIndex)> questions = new List<(string, string[], int)>{};
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
                    Invoke((Action)(() =>
                    {
                        Console.WriteLine("Quiz Start received on client.");
                        StartQuiz();
                    }));
                }
                else if (message.StartsWith("ALL_QUESTIONS"))
                {
                    string[] parts = message.Split('|');
                    if (parts.Length < 4)
                    {
                        MessageBox.Show("Invalid question format received from server.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue;
                    }

                    string question = parts[1];
                    string[] options = parts.Skip(2).Take(parts.Length - 3).ToArray();
                    int correctIndex = int.Parse(parts[parts.Length - 1]);

                    questions.Add((question, options, correctIndex));
                }
                else if (message.StartsWith("QUESTION"))
                {
                    string[] parts = message.Split('|');
                    if (parts.Length < 3)
                    {
                        MessageBox.Show("Invalid question format received from server.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    string question = parts[1];
                    string[] options = parts.Skip(2).ToArray();

                    Invoke((Action)(() =>
                    {
                        DisplayQuestion((question, options, -1));
                    }));
                }
                else if (message.StartsWith("LEADERBOARD"))
                {
                    string leaderboard = message.Substring("LEADERBOARD|".Length);
                    MessageBox.Show($"Leaderboard:\n{leaderboard}");
                }
                else if (message == "RESET")
                {
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

            // Send all questions to clients
            foreach (var cl in clients)
            {
                foreach (var question in questions)
                {
                    string message = $"ALL_QUESTIONS|{question.Question}|{string.Join("|", question.Options)}|{question.CorrectIndex}";
                    var msg = Encoding.UTF8.GetBytes(message);
                    await cl.GetStream().WriteAsync(msg, 0, msg.Length);
                }
            }

            Console.WriteLine($"Number of connected clients: {clients.Count}");

            // Notify clients that the quiz is starting
            foreach (var cl in clients.ToList()) // Use ToList to avoid modifying the collection during iteration
            {
                try
                {
                    var msg = Encoding.UTF8.GetBytes("START_QUIZ");
                    await cl.GetStream().WriteAsync(msg, 0, msg.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send message to a client: {ex.Message}");
                    clients.Remove(cl); // Remove disconnected clients
                }
            }


            // Start the quiz for the host
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

            if (currentQuestionIndex >= 0 && currentQuestionIndex < questions.Count)
            {
                DisplayQuestion(questions[currentQuestionIndex]);
            }
            else
            {
                foreach (var i in questions)
                {
                    Console.WriteLine(i);
                }
                MessageBox.Show("Invalid question index.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendQuestionToClients()
        {
            if (currentQuestionIndex >= questions.Count)
            {
                MessageBox.Show("No more questions to send!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var question = questions[currentQuestionIndex];
            string message = $"QUESTION|{question.Question}|{string.Join("|", question.Options)}";

            foreach (var cl in clients.ToList()) // Use ToList to avoid modifying the collection during iteration
            {
                try
                {
                    var msg = Encoding.UTF8.GetBytes(message);
                    cl.GetStream().WriteAsync(msg, 0, msg.Length);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to send question to a client: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    clients.Remove(cl); // Remove disconnected clients
                }
            }
        }

        private void DisplayQuestion((string Question, string[] Options, int CorrectIndex) question)
        {
            if (currentQuestionIndex < 0 || currentQuestionIndex >= questions.Count)
            {
                MessageBox.Show("Invalid question index!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Shuffle the options
            List<int> indices = new List<int> { 0, 1, 2, 3 };
            Random random = new Random();
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int temp = indices[i];
                indices[i] = indices[j];
                indices[j] = temp;
            }

            string[] shuffledOptions = new string[4];
            int newCorrectIndex = -1;

            for (int i = 0; i < indices.Count; i++)
            {
                shuffledOptions[i] = question.Options[indices[i]];
                if (indices[i] == question.CorrectIndex)
                {
                    newCorrectIndex = i;
                }
            }

            question = (question.Question, shuffledOptions, newCorrectIndex);

            // Set the question and options
            label3.Text = question.Question;

            // Update checkboxes
            CheckBox[] checkBoxes = { checkBox1, checkBox2, checkBox3, checkBox4 };
            for (int i = 0; i < checkBoxes.Length; i++)
            {
                if (i < question.Options.Length && !string.IsNullOrEmpty(question.Options[i]))
                {
                    checkBoxes[i].Text = question.Options[i];
                    checkBoxes[i].Visible = true;
                }
                else
                {
                    checkBoxes[i].Text = string.Empty;
                    checkBoxes[i].Visible = false;
                }
                checkBoxes[i].Checked = false;

                Console.WriteLine($"Checkbox {i}: Text='{checkBoxes[i].Text}', Visible={checkBoxes[i].Visible}");
            }


            // Reset button color
            btnSubmit.BackColor = System.Drawing.Color.White;

            // Update question number label
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
                    return $"{nickname}: {p.Value} points"; ;
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

                    LoadQuestionsFromFile(filePath);
                }
            }
        }

        private void SaveFile()
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "Text Files (.txt)|.txt|All Files (.)|.";
                saveFileDialog.Title = "Uložiť kvíz";
                saveFileDialog.DefaultExt = "txt";
                saveFileDialog.AddExtension = true;

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = saveFileDialog.FileName;

                    File.WriteAllText(filePath, testText);
                    MessageBox.Show($"File saved successfully at: {filePath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private void LoadQuestionsFromFile(string filePath)
        {
            try
            {
                var lines = System.IO.File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('/');
                    if (parts.Length < 3)
                    {
                        MessageBox.Show($"Invalid format in line: {line}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue;
                    }

                    string question = parts[0];
                    string[] options = parts[1].Split(',');
                    if (int.TryParse(parts[2], out int correctIndex) && correctIndex >= 0 && correctIndex < options.Length)
                    {
                        questions.Add((question, options, correctIndex));
                    }
                    else
                    {
                        MessageBox.Show($"Invalid correct index in line: {line}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading questions: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
         
        }

        private void button4_Click(object sender, EventArgs e)
        {
            testText += textBox1.Text + "/" + textBox2.Text + "," + textBox3.Text + "," + textBox4.Text + "," + textBox5.Text + "/" + 0 + "\n";
            textBox1.Clear();
            textBox2.Clear();
            textBox3.Clear();
            textBox4.Clear();
            textBox5.Clear();
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

        private void button5_Click(object sender, EventArgs e)
        {
            SaveFile();
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
